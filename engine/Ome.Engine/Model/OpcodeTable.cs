namespace Ome.Engine.Model;

public sealed record OpcodeCorpusObservation(
    string ProfileId,
    string CatalogRevision,
    string ScriptRevision,
    string EngineAbiId,
    string Artifact,
    int ScriptCount,
    long InstructionCount,
    IReadOnlySet<int> Opcodes);

/// <summary>
/// The evidence-scoped ABI snapshot/layer that supplied an effective operand contract. Layers are
/// resolved independently from the shared C# implementation that may execute the instruction.
/// </summary>
public sealed record OpcodeResolutionProvenance(
    string LayerId,
    string LayerKind,
    IReadOnlyList<string> ComposedFrom,
    string EvidenceMethod,
    string EvidenceArtifact,
    string EvidenceScope);

/// <summary>Whether an implemented opcode's handler waits (Lua scripting spec §7.1): parks the engine thread in the
/// wait native before it returns. <see cref="May"/> covers a wait that Skip, Auto or fast-forward pass.</summary>
public enum OpcodeWaits { Never, May, Always }

/// <summary>How an implemented opcode's handler can leave (Lua scripting spec §7.1), from the registry's
/// <c>leaves</c> (vm-map/opcodes.toml, derived by tools/opcode_flow.py).</summary>
[Flags]
public enum OpcodeLeaves
{
    None = 0,
    /// <summary>It goes on at the next instruction.</summary>
    Falls = 1 << 0,
    /// <summary>It goes on at another instruction of its frame.</summary>
    Jumps = 1 << 1,
    /// <summary>It runs another frame or a callback before it returns.</summary>
    Calls = 1 << 2,
    /// <summary>It ends its frame, the scene or the run, itself or because a frame it ran did.</summary>
    Ends = 1 << 3,
    /// <summary>It writes a numbered save of the game state.</summary>
    Saves = 1 << 4,
    /// <summary>It replaces the game state from a save.</summary>
    Loads = 1 << 5,
    /// <summary>It continues a load's restore of the saved frames (op 0xae).</summary>
    Restores = 1 << 6,
}

public sealed record OpcodeDefinition(
    int Opcode,
    string Label,
    int Argc,
    IReadOnlySet<string> ObservedBy,
    string? SemanticName = null,
    IReadOnlySet<string>? SemanticEvidenceRevisions = null,
    OpcodeResolutionProvenance? Resolution = null)
{
    /// <summary>Whether the port's handler waits; null for an opcode the port does not implement.</summary>
    public OpcodeWaits? Waits { get; init; }

    /// <summary>How the port's handler can leave; <see cref="OpcodeLeaves.None"/> with <see cref="Waits"/> null.</summary>
    public OpcodeLeaves Leaves { get; init; }

    /// <summary>The operands (0-based) the table's ABI reads or writes as floats: those whose registry contract is
    /// <c>read-float</c> or <c>write-float</c> for it (vm-map/opcodes.toml <c>access_by_abi</c>; SYS4433's transform
    /// family). The others go through the integer accessors.</summary>
    public IReadOnlySet<int> FloatOperands { get; init; } = NoOperands;

    private static readonly IReadOnlySet<int> NoOperands = new HashSet<int>();

    public string CanonicalLabel => string.IsNullOrWhiteSpace(SemanticName) ? Label : SemanticName;
    public bool IsObservedBy(string profileId) => ObservedBy.Contains(profileId);
    public IReadOnlySet<string> EvidenceRevisions { get; } = SemanticEvidenceRevisions
        ?? new HashSet<string>(StringComparer.Ordinal);
    public OpcodeResolutionProvenance ContractProvenance => Resolution
        ?? throw new InvalidOperationException("opcode definition has not been resolved through an ABI layer");
}

public sealed class OpcodeTable
{
    private readonly IReadOnlyDictionary<int, OpcodeDefinition> _t;

    public OpcodeTable(IReadOnlyDictionary<int, (string Label, int Argc)> entries,
                       string abiId = "AGE-catalog")
        : this(entries.ToDictionary(
            pair => pair.Key,
            pair => new OpcodeDefinition(
                pair.Key, pair.Value.Label, pair.Value.Argc,
                new HashSet<string>(StringComparer.Ordinal))), abiId, null)
    {
    }

    public OpcodeTable(IReadOnlyDictionary<int, OpcodeDefinition> entries,
                       string abiId = "AGE-catalog",
                       IReadOnlyDictionary<string, OpcodeCorpusObservation>? observations = null,
                       IReadOnlyList<string>? resolvedLayers = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(abiId);
        var directProvenance = new OpcodeResolutionProvenance(
            abiId, "direct-table", [], "caller-supplied", "in-memory OpcodeTable",
            "caller-supplied table; revision applicability unspecified");
        _t = entries.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Resolution == null
                ? pair.Value with { Resolution = directProvenance }
                : pair.Value);
        AbiId = abiId;
        Observations = observations
            ?? new Dictionary<string, OpcodeCorpusObservation>(StringComparer.Ordinal);
        ResolvedLayers = resolvedLayers ?? [abiId];
    }

    public string AbiId { get; }
    public IReadOnlyDictionary<string, OpcodeCorpusObservation> Observations { get; }
    public IReadOnlyList<string> ResolvedLayers { get; }
    public int Count => _t.Count;
    public IEnumerable<OpcodeDefinition> Entries => _t.Values;

    public bool TryGet(int op, out string label, out int argc)
    {
        if (_t.TryGetValue(op, out var e)) { label = e.Label; argc = e.Argc; return true; }
        label = ""; argc = -1; return false;
    }

    public bool TryGetDefinition(int op, out OpcodeDefinition definition)
        => _t.TryGetValue(op, out definition!);

    public string Label(int op) => _t.TryGetValue(op, out var e) ? e.Label : "";
    public int Argc(int op) => _t.TryGetValue(op, out var e) ? e.Argc : -1;

    /// <summary>Reverse lookup: opcode by mnemonic label (e.g. "sleep" -> 0xc8), or null if none.
    /// Used by --trace-ops to let a diagnostic filter name ops by mnemonic instead of raw hex.</summary>
    public int? ByLabel(string label)
    {
        foreach (var kv in _t)
            if (kv.Value.Label == label || kv.Value.SemanticName == label) return kv.Key;
        return null;
    }
}
