using Age.Engine.Model;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Syntax;
using Age.Script.Text;

namespace Age.Script;

/// <summary>Gate-2 result: bytes → OMS source + sidecar → bytes, with sidecar use and raw residue counted.</summary>
public sealed record OmsSourceRoundTripReport(
    string Level,
    int Scripts,
    int Identical,
    int EmptySidecars,
    IReadOnlyDictionary<string, int> SidecarFieldUse,
    long SourceBytes,
    long Instructions,
    long RawStatements,
    IReadOnlyList<IrRoundTripFailure> Failures,
    long FoldedInstructions = 0,
    long FoldRejections = 0,
    long TempOverrides = 0)
{
    public bool Passed => Failures.Count == 0 && Identical == Scripts;
}

public static class OmsSourceRoundTrip
{
    public const string RawLevel = "raw", CoreLevel = "core";

    public static OmsSourceRoundTripReport Run(
        IEnumerable<(string Name, long PackedId, Func<byte[]> Read)> scripts,
        OpcodeTable table, OpcodeContracts contracts, string level = RawLevel)
    {
        if (level is not (RawLevel or CoreLevel)) throw new ArgumentException($"unknown level {level}", nameof(level));
        var intrinsics = new IntrinsicCatalog(table, contracts);
        int count = 0, identical = 0, empty = 0;
        long sourceBytes = 0, instructions = 0, rawStatements = 0, folded = 0, rejected = 0, overrides = 0;
        var fieldUse = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<IrRoundTripFailure>();
        foreach (var (name, packedId, read) in scripts)
        {
            count++;
            try
            {
                byte[] original = read();
                byte[] rebuilt = RoundTrip(original, name, table, contracts, intrinsics, level, out OmsDecompilation decompiled,
                                           out int instructionCount);
                instructions += instructionCount;
                rawStatements += CountRawStatements(decompiled.Source);
                folded += decompiled.FoldedInstructions;
                rejected += decompiled.FoldRejections;
                overrides += decompiled.Layout.Temps?.Count ?? 0;
                sourceBytes += System.Text.Encoding.UTF8.GetByteCount(decompiled.Source);
                OmsLayout layout = decompiled.Layout;
                if (layout.IsEmpty) empty++;
                void Note(string field, bool used) { if (used) fieldUse[field] = fieldUse.GetValueOrDefault(field) + 1; }
                Note("magic", layout.Magic != null);
                Note("field6", layout.Field6 != null);
                Note("local_counts", layout.LocalCounts != null);
                Note("data", layout.Data != null);
                Note("item_dwords", layout.ItemDwords != null);
                Note("tables", layout.Tables != null);
                Note("temp_base", layout.TempBase != null);
                Note("ref_swap", layout.RefSwap != null);
                Note("temps", layout.Temps != null);
                if (rebuilt.AsSpan().SequenceEqual(original)) identical++;
                else failures.Add(new IrRoundTripFailure(name, packedId, FirstDifference(original, rebuilt)));
            }
            catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException
                                              or OmsSyntaxException or OmsCompileException)
            {
                failures.Add(new IrRoundTripFailure(name, packedId, error.Message));
            }
        }
        return new OmsSourceRoundTripReport(level, count, identical, empty, fieldUse, sourceBytes, instructions,
            rawStatements, failures, folded, rejected, overrides);
    }

    /// <summary>The raw-level round trip for one script.</summary>
    public static byte[] RoundTrip(byte[] original, string name, OpcodeTable table, OpcodeContracts contracts,
                                   out OmsDecompilation decompiled)
        => RoundTrip(original, name, table, contracts, new IntrinsicCatalog(table, contracts), RawLevel,
                     out decompiled, out _);

    /// <summary>One script through a source level: decode, decompile, serialize the sidecar, parse, compile, encode.</summary>
    public static byte[] RoundTrip(byte[] original, string name, OpcodeTable table, OpcodeContracts contracts,
                                   IntrinsicCatalog intrinsics, string level, out OmsDecompilation decompiled,
                                   out int instructions)
    {
        IrScript ir = IrDecoder.Decode(original, table, contracts, name);
        instructions = ir.Code.Count;
        decompiled = level == CoreLevel
            ? OmsCoreDecompiler.Decompile(ir, table.AbiId, intrinsics)
            : OmsRawDecompiler.Decompile(ir, table.AbiId);
        OmsLayout layout = OmsLayout.FromJson(decompiled.Layout.ToJson());
        ScriptUnit unit = Parser.ParseScript(decompiled.Source, name);
        IrScript compiled = OmsCompiler.Compile(unit, table.AbiId, table, layout, intrinsics);
        return IrEncoder.Encode(compiled);
    }

    private static int CountRawStatements(string source)
    {
        int count = 0, index = 0;
        while ((index = source.IndexOf("@op(", index, StringComparison.Ordinal)) >= 0) { count++; index += 4; }
        return count;
    }

    private static string FirstDifference(byte[] original, byte[] rebuilt)
    {
        int limit = Math.Min(original.Length, rebuilt.Length);
        for (int k = 0; k < limit; k++)
            if (original[k] != rebuilt[k])
                return $"first difference at byte 0x{k:x} (dword 0x{(k - IrScript.HeaderSize) / 4:x} of the body; "
                       + $"original {original.Length} bytes, rebuilt {rebuilt.Length})";
        return $"length differs: original {original.Length} bytes, rebuilt {rebuilt.Length}";
    }
}
