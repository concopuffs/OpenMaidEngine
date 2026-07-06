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
}
