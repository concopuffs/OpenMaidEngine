using Ome.Engine.Model;
using Ome.Engine.Persistence;
namespace Ome.Engine.Vm;

/// <summary>One script activation: the running script, its instruction cursor, its local slots,
/// its intra-script call/ret stack, and its per-script loop-guard map. Globals live on the VM and
/// are shared across frames; everything here is per-call and discarded on return.</summary>
internal sealed class ExecFrame
{
    public readonly Script Script;
    public int Pc;                                       // entry instruction index
    // While a restored descendant is running, this activation is parked at the synthetic op-0xae
    // rendezvous rather than its native T1/T2/T3 coordinate. Retain the serialized coordinates until
    // the descendant returns so an intervening save can reproduce the native parent frame.
    public NativeSavedScriptFrame? RestoredSaveFrame;
    public int ReadMessageOffset = -1;                   // latest op-0x71 code DWORD coordinate
    public readonly Frame Locals = new();
    public readonly List<int> CallStack = new();         // intra-script `call` (op 0x8f) returns
    public readonly Dictionary<int, int> EmitSeen = new();
    // Op 0x7b's handler dword offsets (-1: none) are native per-depth state. The yield they serve, the op-0xcc mouse
    // callback and the op-0xd3..0xd5 timed sequence are engine-wide and live on the VM.
    public int CoroutineYieldHandlerA = -1;
    public int CoroutineYieldHandlerB = -1;
    public readonly Dictionary<int, int> CoroutineYieldVisits = new(); // instruction index -> visits
    public readonly int[] InputCallbackTargets = Enumerable.Repeat(-1, 32).ToArray(); // op 0xfb
    public int PendingInputCallbackMask;                 // op 0xff snapshot consumed by op 0x100
    public int InputCallbackScanIndex;
    // AGE 4.38D's stepped callbacks (ops 0x102 to 0x105): a target for each action and step (native's 33 rows of
    // ten steps per frame; the idle row is the action count's, as op 0xfb's), the step this frame is at
    // (ctx+0x771b4), and how far op 0x104 scans, which op 0xff sets to the action count (ctx+0x772f4).
    public const int InputCallbackStepCount = 10;
    public readonly int[] InputCallbackStepTargets = Enumerable.Repeat(-1, 33 * InputCallbackStepCount).ToArray();
    public int InputCallbackStep;
    public int InputCallbackScanLimit;
    public readonly HotspotRegistry Hotspots = new();
    // 無視 after an interpreter error: native adds the frame's last decoded instruction length to its pc. A
    // waiting frame's native pc is already past the wait, so the port applies the dwords to the wait's successor.
    public int IgnoredDwords;
    // The script's hook table (Lua scripting spec §7.5), taken when the frame is entered; null for a script with none.
    public Ome.Engine.Modding.ScriptHooks? Hooks;
    public ExecFrame(Script script, int pc) { Script = script; Pc = pc; }
}
