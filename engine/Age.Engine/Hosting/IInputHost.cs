using Age.Engine.Model;

namespace Age.Engine.Hosting;

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

    void WakeInputCallbackService() { }
    void InputCallbackCompleted(GfxState gfx) { }

    // Generic AGE input-callback services (ops 0xcc/0xcd, 0xfb/0xff/0x100, 0x108).
    // Interactive hosts expose the same monotonic clock used by their frame scheduler.
    long InputClockMilliseconds => Environment.TickCount64;

    void SetCursorResource(long resourceId) { }
    void ClearCursorResource() { }
    void WarpCursor(int virtualX, int virtualY) { }

    // Native 0x1c7/0x1cc query two distinct ADV skip channels. Headless and non-interactive
    // hosts default to normal playback; the Godot host supplies the live interactive values.
    void SetMessageSkipActive(bool active) { }

    // Logical action 6 is the native hold-to-fast-forward channel. Keep it separate from the
    // persistent op-0x88 channel so releasing the key cannot turn off the user's Skip toggle.
    void SetPhysicalMessageSkipActive(bool active) { }

    bool IsMessageSkipActive => false;

    // Optional diagnostic override. Native ReadTextDB state is VM/profile-owned; interactive hosts
    // normally leave this false.
    bool IsAdvReadSkipActive => false;
}
