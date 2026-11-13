using System.Threading;

namespace Ome.Engine.Hosting;

// The one place the host blocks the engine thread (engine layering spec §5.3, part 1). A wait that is not modal runs
// the engine tick each time it wakes, so a BGM fade, a delayed start or a duck release goes on while the VM waits, as
// native's main loop runs them while its interpreter is parked. A modal wait runs nothing: native's modal loops (a
// message box, AGERC's dialogs, a menu's loop) freeze its main loop (docs/engine-re.md, "A fade under the 終了 dialog
// stops, then catches up"). EngineHostWaitTests fails on a blocking call anywhere else in the host; a zero timeout
// only polls and is allowed.
public sealed partial class EngineHost
{
    /// <summary>Block until <paramref name="handle"/> is set or <paramref name="timeoutMs"/> passes, then run the
    /// tick.</summary>
    private bool WaitAndTick(WaitHandle handle, int timeoutMs)
    {
        bool signalled = handle.WaitOne(timeoutMs);
        _tick.Service();
        return signalled;
    }

    /// <summary>Block until one of <paramref name="handles"/> is set or <paramref name="timeoutMs"/> passes, then run
    /// the tick.</summary>
    private int WaitAnyAndTick(WaitHandle[] handles, int timeoutMs = Timeout.Infinite)
    {
        int index = WaitHandle.WaitAny(handles, timeoutMs);
        _tick.Service();
        return index;
    }

    /// <summary>A modal wait: block without running the tick.</summary>
    private static bool WaitModal(WaitHandle handle, int timeoutMs) => handle.WaitOne(timeoutMs);

    /// <summary>A modal wait: block without running the tick.</summary>
    private static bool WaitModal(ManualResetEventSlim released, int timeoutMs) => released.Wait(timeoutMs);
}
