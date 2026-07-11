namespace Age.Engine.Vm;
public sealed class Frame
{
    public Dictionary<int, long> I = new();   // local-int
    public Dictionary<int, long> F = new();   // local-float (raw)
    public Dictionary<int, string> S = new(); // local-string
    public Dictionary<int, long> P = new();   // local-ptr (holds a global address)
    public Dictionary<int, long> SP = new();  // local-string-ptr (holds a global-string address)
}
