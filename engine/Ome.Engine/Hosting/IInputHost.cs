using Ome.Engine.Model;

namespace Ome.Engine.Hosting;

public interface IInputHost
{
    /// <summary>Present AGERc's modal full-width editor. Cancel preserves CurrentText.</summary>
    FullwidthTextEditResult EditFullwidthString(FullwidthTextEditRequest request)
        => new(false, request.CurrentText);

    void WaitForInput();
    void WaitForInput(int layoutSlot) => WaitForInput();

    // Interactive hosts service script callbacks on the VM thread while the enclosing ADV page remains
    // parked. The callback returns true while another queued input callback is ready to run.
    void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
    {
        while (serviceInputCallback()) { }
        WaitForInput(layoutSlot);
    }

    void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                      Func<AdvAutoWaitState> autoWaitState)
        => WaitForInput(layoutSlot, serviceInputCallback);

    // Op 0xfa's wait: native run-state 0x80000000, the input poll of a text wait, with no page. There is no wait
    // indicator or page break, the text stays, and Auto has no timer of its own: native polls the shared timer that the
    // last page or sleep armed, which has normally expired, so an unvoiced Auto ends the wait at once. Hosts without a
    // page model treat it as a text wait.
    void WaitForAdvInputPoll(Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
        => WaitForInput(0, serviceInputCallback, autoWaitState);

    void WakeInputCallbackService() { }
    // Native clearing run-state 0x80000000 from a service call: the polling text wait returns as if the page were
    // advanced. Called on the VM thread from the wait's input callback.
    void EndInputWait() { }
    void InputCallbackCompleted(GfxState gfx) { }

    // Native run-state 0x4000000, the hidden-window wait: the engine keeps presenting frames and polls the triggered
    // actions until any arrives. Return once triggered() is true (it consumes the trigger) or the host stops;
    // WakeInputCallbackService signals new input. Headless hosts have no input, so they return at once, as if a
    // trigger had arrived.
    void WaitForServiceTrigger(Func<bool> triggered) { }

    // Generic AGE input-callback services (ops 0xcc/0xcd, 0xfb/0xff/0x100, 0x108).
    // Interactive hosts expose the same monotonic clock used by their frame scheduler.
    long InputClockMilliseconds => Environment.TickCount64;
    int JoystickButtonCount => 0;

    void SetCursorResource(long resourceId) { }
    void ClearCursorResource() { }
    void WarpCursor(int virtualX, int virtualY) { }
}
