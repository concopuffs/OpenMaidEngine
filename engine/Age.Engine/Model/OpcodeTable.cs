namespace Age.Engine.Model;

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

public sealed record OpcodeDefinition(
    int Opcode,
    string Label,
    int Argc,
    IReadOnlySet<string> ObservedBy,
    string? SemanticName = null,
    IReadOnlySet<string>? SemanticEvidenceRevisions = null,
    string? HandlerImplementationId = null,
    OpcodeResolutionProvenance? Resolution = null)
{
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
