using Age.Engine.Model;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Syntax;
using Age.Script.Text;

namespace Age.Script;

/// <summary>Gate-2 result: bytes → OMS source + sidecar → bytes, with sidecar usage counted per field.</summary>
public sealed record OmsSourceRoundTripReport(
    string Level,
    int Scripts,
    int Identical,
    int EmptySidecars,
    IReadOnlyDictionary<string, int> SidecarFieldUse,
    long SourceBytes,
    IReadOnlyList<IrRoundTripFailure> Failures)
{
    public bool Passed => Failures.Count == 0 && Identical == Scripts;
}

public static class OmsSourceRoundTrip
{
    public static OmsSourceRoundTripReport Run(
        IEnumerable<(string Name, long PackedId, Func<byte[]> Read)> scripts,
        OpcodeTable table, OpcodeContracts contracts)
    {
        int count = 0, identical = 0, empty = 0;
        long sourceBytes = 0;
        var fieldUse = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<IrRoundTripFailure>();
        foreach (var (name, packedId, read) in scripts)
        {
            count++;
            try
            {
                byte[] original = read();
                byte[] rebuilt = RoundTrip(original, name, table, contracts, out OmsDecompilation decompiled);
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
                if (rebuilt.AsSpan().SequenceEqual(original)) identical++;
                else failures.Add(new IrRoundTripFailure(name, packedId,
                    $"rebuilt {rebuilt.Length} bytes differ from original {original.Length} bytes"));
            }
            catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException
                                              or OmsSyntaxException or OmsCompileException)
            {
                failures.Add(new IrRoundTripFailure(name, packedId, error.Message));
            }
        }
        return new OmsSourceRoundTripReport("raw", count, identical, empty, fieldUse, sourceBytes, failures);
    }

    /// <summary>One script through the raw level: decode, decompile, serialize the sidecar, parse, compile, encode.</summary>
    public static byte[] RoundTrip(byte[] original, string name, OpcodeTable table, OpcodeContracts contracts,
                                   out OmsDecompilation decompiled)
    {
        IrScript ir = IrDecoder.Decode(original, table, contracts, name);
        decompiled = OmsRawDecompiler.Decompile(ir, table.AbiId);
        OmsLayout layout = OmsLayout.FromJson(decompiled.Layout.ToJson());
        ScriptUnit unit = Parser.ParseScript(decompiled.Source, name);
        IrScript compiled = OmsCompiler.Compile(unit, table.AbiId, table, layout);
        return IrEncoder.Encode(compiled);
    }
}
