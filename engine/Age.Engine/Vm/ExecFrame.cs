using Age.Engine.Model;
namespace Age.Engine.Vm;

/// <summary>One script activation: the running script, its instruction cursor, its local slots,
/// its intra-script call/ret stack, and its per-script loop-guard map. Globals live on the VM and
/// are shared across frames; everything here is per-call and discarded on return.</summary>
internal sealed class ExecFrame
{
    internal readonly record struct TimedCallback(long DeadlineMs, int PrimaryOffset, int CatchUpOffset);

    public readonly Script Script;
    public int Pc;                                       // entry instruction index
    public int ReadMessageOffset = -1;                   // latest op-0x71 code DWORD coordinate
    public readonly Frame Locals = new();
    public readonly List<int> CallStack = new();         // intra-script `call` (op 0x8f) returns
    public readonly Dictionary<int, int> EmitSeen = new();
    public int? CoroutineYieldHandlerA;                  // op 0x7b: native per-frame handler PCs
    public int? CoroutineYieldHandlerB;
    public int? CoroutineResumePc;                      // op 0x199 -> handler A/B -> op 0x7c
    public bool CoroutineYieldActive;
    public readonly Dictionary<int, int> CoroutineYieldVisits = new(); // instruction index -> visits
    public readonly int[] InputCallbackTargets = Enumerable.Repeat(-1, 32).ToArray(); // op 0xfb
    public int PendingInputCallbackMask;                 // op 0xff snapshot consumed by op 0x100
    public int InputCallbackScanIndex;
    public int MouseCallbackTarget = -1;                 // op 0xcc target dword offset
    public long MouseCallbackIntervalMs;
    public long MouseCallbackNextAtMs;
    public readonly List<TimedCallback> TimedCallbacks = new(); // ops 0xd3/0xd4/0xd5
    public int TimedCallbackCursor;
    public long? TimedCallbackStartedAtMs;
    public int TimedCallbackAbortOffset = -1;
    public readonly HotspotRegistry Hotspots = new();
    public ExecFrame(Script script, int pc) { Script = script; Pc = pc; }
}
