namespace Age.Engine.Model;

public sealed record OpcodeDefinition(
    int Opcode,
    string Label,
    int Argc,
    bool ObservedInHimegari = false,
    string? SemanticName = null)
{
    public string CanonicalLabel => string.IsNullOrWhiteSpace(SemanticName) ? Label : SemanticName;
}

public sealed class OpcodeTable
{
    private readonly IReadOnlyDictionary<int, OpcodeDefinition> _t;

    public OpcodeTable(IReadOnlyDictionary<int, (string Label, int Argc)> entries,
                       string abiId = "AGE-catalog")
        : this(entries.ToDictionary(
            pair => pair.Key,
            pair => new OpcodeDefinition(pair.Key, pair.Value.Label, pair.Value.Argc)), abiId)
    {
    }

    public OpcodeTable(IReadOnlyDictionary<int, OpcodeDefinition> entries,
                       string abiId = "AGE-catalog")
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(abiId);
        _t = entries;
        AbiId = abiId;
    }

    public string AbiId { get; }
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
