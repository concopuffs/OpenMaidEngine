using Age.Engine.Model;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Modules;
using Age.Script.Syntax;
using Age.Script.Text;

namespace Age.Script.Patch;

/// <summary>
/// The base script a fragment compiles against (spec §10.1–10.2): its instruction offsets, the label and sub names of
/// its decompilation, the library subs it links and, per local bank in header order, the highest slot it may use
/// (−1 when it uses none).
/// </summary>
public sealed record OmsPatchBase(
    string Script,
    IReadOnlyList<int> Offsets,
    IReadOnlyDictionary<int, int> IndexByOffset,
    IReadOnlyDictionary<string, int> Labels,
    IReadOnlyDictionary<string, string> Library,
    int[] HighestSlots)
{
    /// <summary>The base's fingerprint, which selects a named patch point's site.</summary>
    public string Fingerprint { get; init; } = "";
    /// <summary>The profile's named patch points (mod loader spec §5), when a registry is loaded.</summary>
    public OmsPatchPoints? Points { get; init; }

    /// <summary>The environment of a decoded base with the labels and library subs of its decompilation.</summary>
    public static OmsPatchBase From(IrScript script, IReadOnlyDictionary<string, int>? labels = null,
                                    IReadOnlyDictionary<string, string>? library = null)
    {
        var offsets = new int[script.Code.Count];
        for (int k = 1; k < offsets.Length; k++) offsets[k] = offsets[k - 1] + script.Code[k - 1].SizeInDwords;
        var indexByOffset = new Dictionary<int, int>(offsets.Length);
        for (int k = 0; k < offsets.Length; k++) indexByOffset[offsets[k]] = k;
        // Header counts are "highest slot + 2, or 1 when unused" (spec §7.3); a slot the code names counts too.
        var highest = script.LocalCounts.Select(count => (long)count - 2).ToArray();
        foreach (IrInstruction instruction in script.Code)
            foreach (IrOperand operand in instruction.Operands)
            {
                int bank = Array.IndexOf(StorageBanks.LocalBankTypes, operand.Type);
                if (operand.Kind == IrOperandKind.Literal && bank >= 0) highest[bank] = Math.Max(highest[bank], operand.Value);
            }
        return new OmsPatchBase(script.Name, offsets, indexByOffset,
            labels ?? new Dictionary<string, int>(), library ?? new Dictionary<string, string>(),
            highest.Select(slot => (int)Math.Max(-1, slot)).ToArray());
    }
}

/// <summary>
/// Compiles fragments (spec §10): checks the base's fingerprint, builds the base environment from its decompilation,
/// compiles the fragment into a plan and validates the plan by applying it to the base.
/// </summary>
public static class OmsPatchCompiler
{
    /// <param name="modules">The profile's modules: they resolve the fragment's <c>use</c> statements and name the
    /// base's labels and library subs (with <paramref name="idioms"/>, at the idiomatic level).</param>
    /// <param name="notes">Receives the applier's notes, such as base table sites a region removes.</param>
    /// <param name="points">The profile's named patch points, which <c>site a.b.c</c> resolves through.</param>
    /// <param name="resources">The mod's names, when the fragment belongs to a mod: references to them stay symbolic
    /// in the plan (mod loader spec §6.2).</param>
    public static OmsPatchPlan Compile(ScriptUnit unit, byte[] baseBytes, OpcodeTable table, OpcodeContracts contracts,
                                       IntrinsicCatalog intrinsics, IReadOnlyList<GameModule> modules, OmsIdioms? idioms = null,
                                       ICollection<OmsWarning>? warnings = null, ICollection<string>? notes = null,
                                       OmsPatchPoints? points = null, OmsResourceScope? resources = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!unit.IsPatch) throw new OmsCompileException(unit.Span, $"{unit.Name}: not a patch unit");
        string fingerprint = OmsPatchPlan.FingerprintOf(baseBytes);
        if (!string.Equals(unit.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new OmsCompileException(unit.Span, $"this patch targets {unit.Fingerprint}, but {unit.Name} is {fingerprint}");
        if (unit.Abi != null && unit.Abi != table.AbiId)
            throw new OmsCompileException(unit.Span, $"this patch targets ABI {unit.Abi}, not {table.AbiId}");
        IrScript script = IrDecoder.Decode(baseBytes, table, contracts, unit.Name);
        OmsPatchBase environment = Environment(script, baseBytes, table, contracts, intrinsics, modules, idioms)
            with { Fingerprint = fingerprint, Points = points };
        OmsPatchPlan plan = OmsCompiler.CompilePatch(unit, table.AbiId, table, intrinsics,
            name => modules.FirstOrDefault(module => module.Name == name), environment, warnings, resources);
        CheckOperandAccess(plan, script, environment, intrinsics, unit);
        try
        {
            // Resource references are bound at link time; any packed id validates the plan's shape.
            OmsPatchResult applied = OmsPatchApplier.Apply(script, fingerprint, [plan.ResolveResources(_ => 0)], table);
            foreach (string note in applied.Notes) notes?.Add(note);
        }
        catch (OmsPatchException error)
        {
            throw new OmsCompileException(unit.Span, error.Message);
        }
        return plan;
    }

    /// <summary>The base environment: the decompiled base (idiomatic when <paramref name="idioms"/> is given),
    /// compiled back with its sidecar, which must reproduce the base, gives its label names and linked library subs.</summary>
    public static OmsPatchBase Environment(IrScript script, byte[] baseBytes, OpcodeTable table, OpcodeContracts contracts,
                                           IntrinsicCatalog intrinsics, IReadOnlyList<GameModule> modules, OmsIdioms? idioms)
    {
        OmsDecompilation decompiled = OmsCoreDecompiler.Decompile(script, table.AbiId, intrinsics, modules: modules, idioms: idioms);
        ScriptUnit unit = Parser.ParseScript(decompiled.Source, script.Name);
        OmsImage image = OmsCompiler.CompileImage(unit, table.AbiId, table, OmsLayout.FromJson(decompiled.Layout.ToJson()),
            intrinsics, name => modules.FirstOrDefault(module => module.Name == name), linkAll: false);
        if (!IrEncoder.Encode(image.Script).AsSpan().SequenceEqual(baseBytes))
            throw new InvalidDataException($"{script.Name}: its decompilation does not reproduce it, so its labels cannot be named");
        return OmsPatchBase.From(script, image.Labels,
            image.Library.GroupBy(sub => sub.Name).ToDictionary(group => group.Key, group => group.First().Module));
    }

    /// <summary>An operand the instruction writes, or whose cell it binds, takes a cell (spec §10.2).</summary>
    private static void CheckOperandAccess(OmsPatchPlan plan, IrScript script, OmsPatchBase environment,
                                           IntrinsicCatalog intrinsics, ScriptUnit unit)
    {
        foreach (OmsPatchOperation operation in plan.Operations)
        {
            if (operation.Kind != OmsPatchOperationKind.Operand || operation.Value is not { } value
                || operation.Operand is not int number || !environment.IndexByOffset.TryGetValue(operation.Site, out int index))
                continue;
            int opcode = script.Code[index].Opcode;
            bool cell = value.Kind == OmsPatchOperandKind.Literal && value.Type is not (0 or 1 or 2);
            if (intrinsics.TryGet(opcode, out Intrinsic intrinsic) && intrinsic.OutOperands.Contains(number) && !cell)
                throw new OmsCompileException(unit.Span,
                    $"{operation.Source}: operand {number} of {intrinsic.Name} at 0x{operation.Site:x} is written, so it takes a cell");
        }
    }
}
