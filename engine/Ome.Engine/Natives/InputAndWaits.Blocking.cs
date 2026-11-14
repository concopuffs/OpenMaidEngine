using System.Threading;

namespace Ome.Engine.Natives;

// The one place the waits block the engine thread (engine layering spec §5.3). Each wait is a turn of native's main
// loop: before it blocks it answers the frame turn the front end asked for (InputAndWaits.Turns.cs), and each time it
// wakes it pumps the front end's messages (its requests, its input, its movie frames), runs the engine tick unless it is
// modal, and answers the frame turn that woke it, so the frame shows what the tick did. A BGM fade, a delayed start or
// a duck release goes on while the VM waits, as native's main loop runs them while its interpreter is parked. A modal
// wait runs no tick: native's modal loops (a message box, AGERC's dialogs, a menu's loop) freeze its main loop
// (docs/engine-re.md, "A fade under the 終了 dialog stops, then catches up"). It still pumps the front end's messages
// and answers its frame turns, so the front end that holds it never waits on it. Every wait wakes on the frame signal,
// which is what the front end sets for each of them. InputAndWaitsTests fails on a blocking call anywhere else in the
// native; a zero timeout only polls and is allowed.
public sealed partial class InputAndWaits
{
    /// <summary>Block until <paramref name="handle"/> is set or <paramref name="timeoutMs"/> passes, then run the
    /// tick. A frame turn answered first returns at once, so the wait looks at its state before it blocks.</summary>
    private bool WaitAndTick(WaitHandle handle, int timeoutMs)
    {
        if (ServeFrameTurn()) return handle.WaitOne(0);
        bool signalled = Block(handle, timeoutMs);
        PumpMessages();
        _tick.Service(ParkedOnEffectWait);
        ServeFrameTurn();
        return signalled;
    }

    /// <summary>Block until one of <paramref name="handles"/> (the frame signal among them) is set or
    /// <paramref name="timeoutMs"/> passes, then run the tick.</summary>
    private int WaitAnyAndTick(WaitHandle[] handles, int timeoutMs = Timeout.Infinite)
    {
        if (ServeFrameTurn()) return WaitHandle.WaitTimeout;
        int index = WaitHandle.WaitAny(handles, timeoutMs);
        PumpMessages();
        _tick.Service(ParkedOnEffectWait);
        ServeFrameTurn();
        return index;
    }

    /// <summary>A modal wait: block without running the tick.</summary>
    private bool WaitModal(WaitHandle handle, int timeoutMs)
    {
        if (ServeFrameTurn()) return handle.WaitOne(0);
        bool signalled = Block(handle, timeoutMs);
        PumpMessages();
        ServeFrameTurn();
        return signalled;
    }

    // Native run-state 0x400: op 0x21c's effect wait parks the interpreter (WaitForForegroundTransition holds its
    // graphics state for exactly that park). The tick runs the delayed starts under a statement block during it.
    private bool ParkedOnEffectWait => _scene.ForegroundGfx != null;

    // The frame signal wakes every wait: the front end sets it for a frame turn, its input, a request or a movie frame.
    private bool Block(WaitHandle handle, int timeoutMs)
    {
        if (ReferenceEquals(handle, _frameSignal)) return handle.WaitOne(timeoutMs);
        _blockHandles[0] = handle;   // [1] is the frame signal
        return WaitHandle.WaitAny(_blockHandles, timeoutMs) == 0;
    }
}
