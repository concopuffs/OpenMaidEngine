namespace Ome.Engine.Natives;

/// <summary>
/// What the input and waits native asks of the interpreter when it routes a device's input (engine layering spec §5.4,
/// rule 2: the native defines the interface, the interpreter implements it). The hotspot records, the callback frames
/// and the coroutine yield are the interpreter's; the native decides, as age_main_window_proc and the ADV services do,
/// which of them an input reaches.
/// </summary>
public interface IInterpreterInput
{
    /// <summary>The WM_MOUSEMOVE path: the native cursor coordinate, and the hover it changes.</summary>
    void UpdatePointer(int x, int y);
    /// <summary>One native mouse-button bit (left 0x1, right 0x2), which op 0x108 reads.</summary>
    void UpdateMouseButtonState(int bit, bool pressed);
    /// <summary>A physical mouse button through the script's bindings: the logical action, or -1.</summary>
    int UpdatePhysicalMouseButtonState(int physicalButton, bool pressed);
    /// <summary>A Win32 virtual key through the script's bindings: the logical action, or -1.</summary>
    int UpdateKeyboardVirtualKeyState(int virtualKey, bool pressed);
    /// <summary>A joystick button through the script's bindings: the mask of logical actions.</summary>
    int UpdateJoystickButtonState(int physicalButton, bool pressed);
    /// <summary>A joystick axis (0 is x, 1 is y).</summary>
    void UpdateJoystickAxisState(int axis, double value);
    /// <summary>Route one WM_MOUSEWHEEL delta as age_main_window_proc and the ADV services do.</summary>
    MouseWheelOutcome ApplyMouseWheel(int delta, bool advanceMessageOnWheel, bool textRevealing);

    /// <summary>A timed raw input callback (HISTORY, HIDEWIN, FIELD) belongs to the frame that runs.</summary>
    bool IsRawInputCallbackActive { get; }
    /// <summary>An ADV text wait polls input with no hotspot callback running (native run-state 0x80000000).</summary>
    bool IsAdvTextWaitPolling { get; }

    /// <summary>Latch triggers for an ADV coroutine yield (hide window), which no input service polls.</summary>
    bool TryLatchAdvCoroutineYieldTrigger(int actionMask);
    /// <summary>Latch an action-4 click for the fast-forward click cancel.</summary>
    bool TryLatchMessageSkipCancelClick();
    /// <summary>Queue the callback of the first armed hotspot bound to an action in the mask.</summary>
    bool TryActivateInputActions(int actionMask);
    /// <summary>Queue the activation callback of the armed hotspot under the pointer.</summary>
    bool TryActivatePointer(int x, int y);
    /// <summary>At a polling text wait, ask for the frame's coroutine yield (action 5: hide window).</summary>
    bool TryRequestAdvWaitCoroutineYield();
    /// <summary>Hold triggers in native's input latch while nothing polls it.</summary>
    void HoldTriggers(int actionMask);
}

/// <summary>Where <see cref="IInterpreterInput.ApplyMouseWheel"/> sent one wheel delta.</summary>
public enum MouseWheelOutcome
{
    /// <summary>Accumulated for op 0x10d.</summary>
    Accumulated,
    /// <summary>Queued the hotspot callback bound to the wheel's logical action.</summary>
    Activated,
    /// <summary>The wheel advances the page or finishes the text reveal, as a click does.</summary>
    Advance,
    /// <summary>Consumed with no effect.</summary>
    Dropped,
}
