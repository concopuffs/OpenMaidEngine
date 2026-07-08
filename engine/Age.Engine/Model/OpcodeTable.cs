namespace Age.Engine.Model;
public sealed class OpcodeTable
{
    private readonly IReadOnlyDictionary<int, (string Label, int Argc)> _t;
    public OpcodeTable(IReadOnlyDictionary<int, (string, int)> t) => _t = t;
    public int Count => _t.Count;
    public bool TryGet(int op, out string label, out int argc)
    {
        if (_t.TryGetValue(op, out var e)) { label = e.Label; argc = e.Argc; return true; }
        label = ""; argc = -1; return false;
    }
    public string Label(int op) => _t.TryGetValue(op, out var e) ? e.Label : "";
    public int Argc(int op) => _t.TryGetValue(op, out var e) ? e.Argc : -1;

    /// <summary>Reverse lookup: opcode by mnemonic label (e.g. "sleep" -> 0xc8), or null if none.
    /// Used by --trace-ops to let a diagnostic filter name ops by mnemonic instead of raw hex.</summary>
    public int? ByLabel(string label)
    {
        foreach (var kv in _t) if (kv.Value.Label == label) return kv.Key;
        return null;
    }
}
