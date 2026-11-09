using Ome.Engine.Model;
using Ome.Script.Abi;
using Ome.Script.Compile;
using Ome.Script.Ir;
using Ome.Script.Layout;
using Ome.Script.Modules;
using Ome.Script.Patch;
using Ome.Script.Syntax;
using Ome.Script.Text;

namespace Ome.Script;

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
    long TempOverrides = 0,
    long LibrarySubs = 0,
    long TemplateLifts = 0,
    IReadOnlyList<IrRoundTripFailure>? LiftFailures = null,
    IReadOnlyDictionary<string, long>? LiftedInstructions = null)
{
    public bool Passed => Failures.Count == 0 && Identical == Scripts;
}

/// <summary>
/// An idiomatic canonical rebuild with each instruction's relocation key on both sides. Linking without the sidecar
/// moves library subs, so positions are compared by key: a library sub's name and offset inside it, or otherwise the
/// instruction's ordinal among the script's own (non-library) instructions, which keep their order. A patch probe
/// (gate 7) also marks the instructions its plan inserted (<see cref="RebuiltInserted"/>); traces skip them.
/// </summary>
public sealed record OmsRelocation(byte[] Rebuilt, long[] OriginalKeys, long[] RebuiltKeys,
                                   IReadOnlyDictionary<int, int> OriginalIndexByOffset,
                                   IReadOnlyDictionary<int, int> RebuiltIndexByOffset,
                                   bool[]? RebuiltInserted = null)
{
    /// <summary>Whether two code offsets, one in the original and one in the rebuild, name the same instruction.</summary>
    public bool SamePosition(long original, long rebuilt)
        => OriginalIndexByOffset.TryGetValue((int)original, out int a) && RebuiltIndexByOffset.TryGetValue((int)rebuilt, out int b)
           && OriginalKeys[a] == RebuiltKeys[b];
}

public static class OmsSourceRoundTrip
{
    public const string RawLevel = "raw", CoreLevel = "core", IdiomaticLevel = "idiomatic";

    /// <param name="level"><c>raw</c>, <c>core</c>, or <c>idiomatic</c> (core plus the library subs and templates of
    /// the source modules among <paramref name="modules"/>).</param>
    public static OmsSourceRoundTripReport Run(
        IEnumerable<(string Name, long PackedId, Func<byte[]> Read)> scripts,
        OpcodeTable table, OpcodeContracts contracts, string level = RawLevel, IReadOnlyList<GameModule>? modules = null,
        bool fixedPoint = false)
    {
        if (level is not (RawLevel or CoreLevel or IdiomaticLevel))
            throw new ArgumentException($"unknown level {level}", nameof(level));
        var intrinsics = new IntrinsicCatalog(table, contracts);
        OmsIdioms? idioms = level == IdiomaticLevel ? OmsIdioms.Build(modules ?? [], table.AbiId, intrinsics) : null;
        int count = 0, identical = 0, empty = 0;
        long sourceBytes = 0, instructions = 0, rawStatements = 0, folded = 0, rejected = 0, overrides = 0;
        long librarySubs = 0, templateLifts = 0;
        var lifted = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var fieldUse = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<IrRoundTripFailure>();
        var liftFailures = new List<IrRoundTripFailure>();
        foreach (var (name, packedId, read) in scripts)
        {
            count++;
            try
            {
                byte[] original = read();
                // Gate 4 (spec §12): start from compiler-generated code, the canonical rebuild without a sidecar.
                if (fixedPoint) original = CanonicalRebuild(original, name, table, contracts, intrinsics, modules, idioms);
                byte[] rebuilt = RoundTrip(original, name, table, contracts, intrinsics, level, out OmsDecompilation decompiled,
                                           out int instructionCount, modules, idioms);
                librarySubs += decompiled.LibrarySubs;
                templateLifts += decompiled.TemplateLifts;
                foreach (var (module, liftedCount) in decompiled.LiftedInstructions ?? new Dictionary<string, int>())
                    lifted[module] = lifted.GetValueOrDefault(module) + liftedCount;
                if (decompiled.LiftFailure != null) liftFailures.Add(new IrRoundTripFailure(name, packedId, decompiled.LiftFailure));
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
                Note("library", layout.Library != null);
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
            rawStatements, failures, folded, rejected, overrides, librarySubs, templateLifts, liftFailures, lifted);
    }

    /// <summary>The raw-level round trip for one script.</summary>
    public static byte[] RoundTrip(byte[] original, string name, OpcodeTable table, OpcodeContracts contracts,
                                   out OmsDecompilation decompiled)
        => RoundTrip(original, name, table, contracts, new IntrinsicCatalog(table, contracts), RawLevel,
                     out decompiled, out _);

    /// <summary>Gate 3's canonical rebuild of one script: core source (idiomatic source when
    /// <paramref name="idioms"/> is given) compiled without its sidecar.</summary>
    public static byte[] CanonicalRebuild(byte[] original, string name, OpcodeTable table, OpcodeContracts contracts,
                                          IntrinsicCatalog intrinsics, IReadOnlyList<GameModule>? modules = null,
                                          OmsIdioms? idioms = null)
    {
        IrScript ir = IrDecoder.Decode(original, table, contracts, name);
        OmsDecompilation source = OmsCoreDecompiler.Decompile(ir, table.AbiId, intrinsics, modules: modules, idioms: idioms);
        IrScript compiled = OmsCompiler.Compile(Parser.ParseScript(source.Source, name), table.AbiId, table, layout: null,
            intrinsics, module => modules?.FirstOrDefault(candidate => candidate.Name == module));
        return IrEncoder.Encode(compiled);
    }

    /// <summary>Gate 3 at the idiomatic level: idiomatic source compiled without its sidecar, with the relocation keys
    /// of the original (the same source compiled with its sidecar, which must reproduce the original) and the
    /// rebuild.</summary>
    public static OmsRelocation IdiomaticRebuild(byte[] original, string name, OpcodeTable table, OpcodeContracts contracts,
                                                 IntrinsicCatalog intrinsics, IReadOnlyList<GameModule> modules,
                                                 OmsIdioms idioms)
    {
        IrScript ir = IrDecoder.Decode(original, table, contracts, name);
        OmsDecompilation source = OmsCoreDecompiler.Decompile(ir, table.AbiId, intrinsics, modules: modules, idioms: idioms);
        ScriptUnit unit = Parser.ParseScript(source.Source, name);
        GameModule? Resolve(string module) => modules.FirstOrDefault(candidate => candidate.Name == module);
        OmsImage placed = OmsCompiler.CompileImage(unit, table.AbiId, table, OmsLayout.FromJson(source.Layout.ToJson()),
            intrinsics, Resolve, linkAll: false);
        if (!IrEncoder.Encode(placed.Script).AsSpan().SequenceEqual(original))
            throw new InvalidDataException($"{name}: idiomatic source with its sidecar does not reproduce the original");
        OmsImage canonical = OmsCompiler.CompileImage(unit, table.AbiId, table, null, intrinsics, Resolve, linkAll: false);
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        return new OmsRelocation(IrEncoder.Encode(canonical.Script), Keys(placed, ids), Keys(canonical, ids),
            IndexByOffset(placed.Script), IndexByOffset(canonical.Script));
    }

    private static long[] Keys(OmsImage image, Dictionary<string, long> ids)
    {
        var keys = new long[image.Script.Code.Count];
        var inLibrary = new bool[keys.Length];
        foreach (OmsLinkedSub sub in image.Library)
        {
            if (!ids.TryGetValue(sub.Name, out long id)) ids[sub.Name] = id = ids.Count + 1;
            for (int k = sub.Start; k < sub.End; k++)
            {
                keys[k] = id << 32 | (uint)(k - sub.Start);
                inLibrary[k] = true;
            }
        }
        for (int k = 0, ordinal = 0; k < keys.Length; k++)
            if (!inLibrary[k]) keys[k] = ordinal++;
        return keys;
    }

    /// <summary>Gate 7: <paramref name="original"/> patched with its probe plan (<see cref="OmsPatchProbe"/>), with
    /// relocation keys. A base instruction keys as its base index, and so does a region copy of it or a <c>before</c>
    /// body at it, because transfers and code positions that named it land there. Inserted notes are marked.
    /// <paramref name="emptyIdentical"/> reports whether an empty plan reproduces the original byte for byte.</summary>
    public static OmsRelocation PatchProbe(byte[] original, string name, OpcodeTable table, OpcodeContracts contracts,
                                           int stride, out bool emptyIdentical)
    {
        string fingerprint = OmsPatchPlan.FingerprintOf(original);
        var empty = new OmsPatchPlan { Script = name, Fingerprint = fingerprint, Abi = table.AbiId };
        emptyIdentical = OmsPatchApplier.Apply(original, name, [empty], table, contracts).Bytes.AsSpan().SequenceEqual(original);
        IrScript ir = IrDecoder.Decode(original, table, contracts, name);
        OmsPatchResult patched = OmsPatchApplier.Apply(original, name,
            [OmsPatchProbe.Build(ir, fingerprint, table.AbiId, stride)], table, contracts);
        var originalKeys = Enumerable.Range(0, ir.Code.Count).Select(k => (long)k).ToArray();
        var rebuiltKeys = new long[patched.Provenance.Count];
        var inserted = new bool[rebuiltKeys.Length];
        for (int j = 0; j < rebuiltKeys.Length; j++)
        {
            OmsPatchProvenance origin = patched.Provenance[j];
            inserted[j] = origin.Origin is OmsPatchOrigin.Before or OmsPatchOrigin.After or OmsPatchOrigin.Append;
            rebuiltKeys[j] = origin.Origin is OmsPatchOrigin.Base or OmsPatchOrigin.Region or OmsPatchOrigin.Before
                ? origin.BaseIndex : -1 - j;
        }
        return new OmsRelocation(patched.Bytes, originalKeys, rebuiltKeys, IndexByOffset(ir), IndexByOffset(patched.Script),
            inserted);
    }

    private static Dictionary<int, int> IndexByOffset(IrScript script)
    {
        var map = new Dictionary<int, int>(script.Code.Count);
        for (int k = 0, offset = 0; k < script.Code.Count; offset += script.Code[k].SizeInDwords, k++) map[offset] = k;
        return map;
    }

    /// <summary>One script through a source level: decode, decompile, serialize the sidecar, parse, compile, encode.</summary>
    public static byte[] RoundTrip(byte[] original, string name, OpcodeTable table, OpcodeContracts contracts,
                                   IntrinsicCatalog intrinsics, string level, out OmsDecompilation decompiled,
                                   out int instructions, IReadOnlyList<GameModule>? modules = null, OmsIdioms? idioms = null)
    {
        IrScript ir = IrDecoder.Decode(original, table, contracts, name);
        instructions = ir.Code.Count;
        decompiled = level switch
        {
            CoreLevel => OmsCoreDecompiler.Decompile(ir, table.AbiId, intrinsics, modules: modules),
            IdiomaticLevel => OmsCoreDecompiler.Decompile(ir, table.AbiId, intrinsics, modules: modules,
                idioms: idioms ?? OmsIdioms.Build(modules ?? [], table.AbiId, intrinsics)),
            _ => OmsRawDecompiler.Decompile(ir, table.AbiId),
        };
        OmsLayout layout = OmsLayout.FromJson(decompiled.Layout.ToJson());
        ScriptUnit unit = Parser.ParseScript(decompiled.Source, name);
        IrScript compiled = OmsCompiler.Compile(unit, table.AbiId, table, layout, intrinsics,
            name => modules?.FirstOrDefault(module => module.Name == name));
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
