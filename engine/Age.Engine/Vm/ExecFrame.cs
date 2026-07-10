using Age.Engine.Model;
namespace Age.Engine.Vm;

/// <summary>One script activation: the running script, its instruction cursor, its local slots,
/// its intra-script call/ret stack, and its per-script loop-guard map. Globals live on the VM and
/// are shared across frames; everything here is per-call and discarded on return.</summary>
internal sealed class ExecFrame
{
    public readonly Script Script;
    public int Pc;                                       // entry instruction index
    public readonly Frame Locals = new();
    public readonly List<int> CallStack = new();         // intra-script `call` (op 0x8f) returns
    public readonly Dictionary<int, int> EmitSeen = new();
    public int? CoroutineYieldHandlerA;                  // op 0x7b: native per-frame handler PCs
    public int? CoroutineYieldHandlerB;
    public readonly Dictionary<int, int> CoroutineYieldVisits = new(); // instruction index -> visits
    public ExecFrame(Script script, int pc) { Script = script; Pc = pc; }
}
