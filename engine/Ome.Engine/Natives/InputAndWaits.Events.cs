using System;
using System.Threading;
using Ome.Engine.Model;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

// The input a front end's device delivers, routed as age_main_window_proc@0x486320 and the ADV services route it: a
// pointer move, a mouse button, a wheel notch, a key, a joystick button or axis. The front end passes each event as it
// arrives and keeps only its own controls (menus, dialogs, its hot keys). The events queue for the engine thread, which
// routes them where native's main loop pumps its window messages: at its next instruction boundary or wait
// (InputAndWaits.Turns.cs). What an input does in the game is decided there, against the waits and the interpreter
// (IInterpreterInput). While no engine thread runs, an event is routed at once.
public sealed partial class InputAndWaits
{
    private enum InputKind { Pointer, Wheel, Button, Key, JoystickButton, JoystickAxis, HoldKey, Advance }

    private readonly record struct InputEvent(InputKind Kind, int A, int B = 0, int C = 0, bool Pressed = false,
                                              double Value = 0);

    private readonly System.Collections.Concurrent.ConcurrentQueue<InputEvent> _input = new();
    private int _advanceQueued;   // SignalInput's advance waits in the queue: a second one adds nothing
    /// <summary>Native AGE's logical action 4: it advances, finishes a reveal, skips an effect or cancels a movie.</summary>
    private const int AdvanceAction = InputBindings.AdvanceAction;

    /// <summary>Logical action 5, Space in both games: at a polling text wait it enters the frame's handler A, which
    /// hides the window (adv_input_service_poll@0x411230).</summary>
    private const int HideWindowAction = 5;

    private IInterpreterInput _interpreter = NoInterpreter.Instance;
    private readonly Sys4RegIniStore? _settings;

    /// <summary>SYS4INI CLICKONUP: a mouse click triggers on its release (Himegari), not on its press.</summary>
    public bool ClickOnUp { get; init; }

    /// <summary>The interpreter whose hotspots, callbacks and coroutine yield the routed input reaches. The VM gives
    /// itself when it is constructed with this native.</summary>
    public void RouteInputTo(IInterpreterInput interpreter)
        => _interpreter = interpreter ?? throw new System.ArgumentNullException(nameof(interpreter));

    /// <summary>The pointer moved to native client pixel (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void PointerMoved(int x, int y) => Queue(new(InputKind.Pointer, x, y));

    /// <summary>
    /// A wheel turned: a WM_MOUSEWHEEL delta (signed multiples of WHEEL_DELTA, 120). At an ADV text wait its sign raises
    /// the WHEELKEYUP/WHEELKEYDOWN action (wheel-up opens History); otherwise AGE accumulates it until op 0x10d reads and
    /// clears it (HISTORY scrolls by its sign). VirtualMachine.ApplyMouseWheel has the rules; message:AdvanceMesOnWheel
    /// lets a wheel-down advance, as a click does.
    /// </summary>
    public void MouseWheel(int delta) => Queue(new(InputKind.Wheel, delta));

    /// <summary>A mouse button (<paramref name="physicalButton"/> 0 is left, 1 is right) changed at native client
    /// pixel (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void MouseButton(int physicalButton, bool pressed, int x, int y)
        => Queue(new(InputKind.Button, physicalButton, x, y, pressed));

    /// <summary>A key, as its Win32 virtual key (WM_KEYDOWN and WM_KEYUP), through the script's bindings.</summary>
    public void Key(int virtualKey, bool pressed) => Queue(new(InputKind.Key, virtualKey, Pressed: pressed));

    /// <summary>A joystick button, through the script's bindings (one button may raise several actions).</summary>
    public void JoystickButton(int physicalButton, bool pressed)
        => Queue(new(InputKind.JoystickButton, physicalButton, Pressed: pressed));

    /// <summary>A joystick axis moved; AGE reads the x and y axes (0 and 1) only.</summary>
    public void JoystickAxis(int axis, double value)
    {
        if (axis is 0 or 1) Queue(new(InputKind.JoystickAxis, axis, Value: value));
    }

    /// <summary>A key held from launch (the --hold-skip Ctrl): the bindings see it held, and no trigger is routed,
    /// since no press arrived.</summary>
    public void HoldKeyFromLaunch(int virtualKey) => Queue(new(InputKind.HoldKey, virtualKey));

    /// <summary>
    /// A click or an advance key that names no button, from the front end's thread or an auto-clicker: it cancels the
    /// modal movie, finishes the reveal, forces a transition that a click may skip, or ends the text wait, in that
    /// order (<see cref="Advance"/>). A second one queued before the engine thread routes the first adds nothing.
    /// </summary>
    public void SignalInput()
    {
        if (Interlocked.Exchange(ref _advanceQueued, 1) == 1) return;
        Queue(new(InputKind.Advance, 0));
    }

    // While an engine thread runs, the event waits for it; otherwise it is routed now, under the lock the engine thread
    // takes to start, so it cannot start in the middle of one.
    private void Queue(InputEvent input)
    {
        lock (_turnLock)
        {
            if (_engineThreadId == 0 || _engineThreadId == Environment.CurrentManagedThreadId)
            {
                RouteQueuedInput();   // what came before it first
                Route(input);
                return;
            }
            _input.Enqueue(input);
        }
        QueueWork();
    }

    /// <summary>The engine thread: route the input the front end queued, in the order it arrived.</summary>
    private void RouteQueuedInput()
    {
        while (_input.TryDequeue(out InputEvent input)) Route(input);
    }

    private void Route(InputEvent input)
    {
        switch (input.Kind)
        {
            case InputKind.Pointer: _interpreter.UpdatePointer(input.A, input.B); break;
            case InputKind.Wheel: RouteMouseWheel(input.A); break;
            case InputKind.Button: RouteMouseButton(input.A, input.Pressed, input.B, input.C); break;
            case InputKind.Key: RouteKey(input.A, input.Pressed); break;
            case InputKind.JoystickButton: RouteJoystickButton(input.A, input.Pressed); break;
            case InputKind.JoystickAxis: _interpreter.UpdateJoystickAxisState(input.A, input.Value); break;
            case InputKind.HoldKey: _interpreter.UpdateKeyboardVirtualKeyState(input.A, true); break;
            case InputKind.Advance:
                Volatile.Write(ref _advanceQueued, 0);
                Advance();
                break;
        }
    }

    // The routing, on the engine thread. Each returns true when the game took the input, which the tests read.
    private bool RouteMouseWheel(int delta)
    {
        bool advanceOnWheel = _settings?.LoadMessageInt(Sys4RegIniStore.AdvanceMesOnWheelKey, 0) == 1;
        MouseWheelOutcome outcome = _interpreter.ApplyMouseWheel(delta, advanceOnWheel, _text.IsRevealing);
        if (outcome == MouseWheelOutcome.Advance
            && !_text.IsAdvPagePresentationSuspended && !_interpreter.IsRawInputCallbackActive)
            Advance();
        return true;
    }

    private bool RouteMouseButton(int physicalButton, bool pressed, int x, int y)
    {
        bool advPageSuspended = _text.IsAdvPagePresentationSuspended;
        bool rawInputCallbackActive = _interpreter.IsRawInputCallbackActive;
        _interpreter.UpdatePointer(x, y);
        _interpreter.UpdateMouseButtonState(physicalButton == 0 ? 0x1 : 0x2, pressed);
        int action = _interpreter.UpdatePhysicalMouseButtonState(physicalButton, pressed);
        // Native AGE latches the click that every trigger below consumes on button-up under CLICKONUP (Himegari), on
        // button-down otherwise. Menus read the polled state above, so a menu opened by this click starts with the
        // button already up, as natively.
        if (pressed == ClickOnUp) return false;
        if (_modalMovieWaiting)
        {
            Advance();
            return true;
        }
        // While an effect service parks the VM, the trigger goes to the effect's poll or to the input latch, never to a
        // hotspot under the effect or to a page wait (RouteEffectTrigger).
        if (_transitionWaiting)
        {
            if (action >= 0) RouteEffectTrigger(1 << action);
            return true;
        }
        // An ADV coroutine yield (hide window) runs with no input service: the click only latches for the hidden-window
        // wait, which it ends. It reaches no hotspot and never advances the page.
        if (action >= 0 && _interpreter.TryLatchAdvCoroutineYieldTrigger(1 << action)) return true;
        // CANCELMESSKIPONCLICK: during active fast-forward native withholds hotspot dispatch and feeds the action-4
        // click to adv_interpreter_tick's cancel state machine instead.
        if (action == AdvanceAction && _interpreter.TryLatchMessageSkipCancelClick()) return true;
        // Only a polling text wait dispatches hotspots and op-0x97 bindings (adv_input_service_poll). Elsewhere the
        // click goes to a running reveal's poll or stays in native's input latch (RouteUnpolledTrigger).
        if (action >= 0 && !_interpreter.IsAdvTextWaitPolling)
        {
            RouteUnpolledTrigger(1 << action);
            return true;
        }
        // AGE exposes mouse buttons twice: op 0x108 reads the raw bitmask while op 0xff translates the held physical
        // button through the script-configured logical action map.
        if (action >= 0 && _interpreter.TryActivateInputActions(1 << action)) return true;
        if (physicalButton == 0 && _interpreter.TryActivatePointer(x, y)) return true;
        // Modal callback scripts return through their own bytecode. Signaling the enclosing ADV wait here would also
        // advance the restored dialogue page after HISTORY/HIDEWIN exits.
        if (physicalButton == 0 && !advPageSuspended && !rawInputCallbackActive) Advance();
        return false;
    }

    private bool RouteKey(int virtualKey, bool pressed)
    {
        int action = _interpreter.UpdateKeyboardVirtualKeyState(virtualKey, pressed);
        if (!pressed || action < 0) return false;
        if (_transitionWaiting)
        {
            RouteEffectTrigger(1 << action);
            return true;
        }
        if (_interpreter.TryLatchAdvCoroutineYieldTrigger(1 << action)) return true;
        if (action == AdvanceAction && _interpreter.TryLatchMessageSkipCancelClick()) return true;
        if (!_modalMovieWaiting && !_interpreter.IsAdvTextWaitPolling)
        {
            RouteUnpolledTrigger(1 << action);
            return true;
        }
        if (_interpreter.TryActivateInputActions(1 << action)) return true;
        if (action == HideWindowAction && _interpreter.TryRequestAdvWaitCoroutineYield()) return true;
        if (action != AdvanceAction) return false;
        if (_modalMovieWaiting)
        {
            Advance();
            return true;
        }
        if (!_text.IsAdvPagePresentationSuspended && !_interpreter.IsRawInputCallbackActive) Advance();
        return false;
    }

    private bool RouteJoystickButton(int physicalButton, bool pressed)
    {
        int actionMask = _interpreter.UpdateJoystickButtonState(physicalButton, pressed);
        if (!pressed) return false;
        if (actionMask != 0 && _transitionWaiting)
        {
            RouteEffectTrigger(actionMask);
            return true;
        }
        if (_interpreter.TryLatchAdvCoroutineYieldTrigger(actionMask)) return true;
        if (actionMask != 0 && !_modalMovieWaiting && !_interpreter.IsAdvTextWaitPolling)
        {
            RouteUnpolledTrigger(actionMask);
            return true;
        }
        if (_interpreter.TryActivateInputActions(actionMask)) return true;
        if (!HasAdvanceAction(actionMask) && (actionMask & (1 << HideWindowAction)) != 0
            && _interpreter.TryRequestAdvWaitCoroutineYield())
            return true;
        if (!HasAdvanceAction(actionMask)) return false;
        if (_modalMovieWaiting)
        {
            Advance();
            return true;
        }
        if (!_text.IsAdvPagePresentationSuspended && !_interpreter.IsRawInputCallbackActive) Advance();
        return false;
    }

    private static bool HasAdvanceAction(int mask) => (mask & (1 << AdvanceAction)) != 0;

    /// <summary>system:EffectSkipOnClick, read on each trigger: the options panel's その他 page writes it.</summary>
    private bool EffectSkipOnClick => _settings?.LoadInt("system", "EffectSkipOnClick", 0) == 1;

    /// <summary>A trigger while an effect service parks the VM. With EffectSkipOnClick 1 the effect polls it (action 4
    /// skips the effect, anything else is dropped); otherwise native's input latch holds it for the next service that
    /// polls, such as the next text reveal, which a held click finishes (docs/engine-re.md, "Effect skipping and the
    /// input latch").</summary>
    private void RouteEffectTrigger(int actionMask)
    {
        if (!TryPollEffectTriggers(actionMask, EffectSkipOnClick)) _interpreter.HoldTriggers(actionMask);
    }

    /// <summary>A trigger while no text wait polls (native run-state 0x80000000 clear), so no hotspot or op-0x97 binding
    /// can take it. A running reveal polls on every tick (adv_text_reveal_service_tick@0x406fa0): action 4 finishes it
    /// and the poll drops the rest. Otherwise nothing polls: a sleep (run-state 1), the ops between waits, a callback
    /// script or a timed callback sequence. age_main_window_proc latches every mapped click and key whatever the run
    /// state, so native's input latch keeps the trigger for the next service that polls: the next reveal's first tick,
    /// the timed sequence's tick, or op 0x72's entry, which discards it (docs/engine-re.md, "Effect skipping and the
    /// input latch").</summary>
    private void RouteUnpolledTrigger(int actionMask)
    {
        if (_text.IsRevealing)
        {
            if (HasAdvanceAction(actionMask)) Advance();
            return;
        }
        _interpreter.HoldTriggers(actionMask);
    }

    /// <summary>Until an interpreter is given: input changes nothing.</summary>
    private sealed class NoInterpreter : IInterpreterInput
    {
        public static readonly NoInterpreter Instance = new();
        public void UpdatePointer(int x, int y) { }
        public void UpdateMouseButtonState(int bit, bool pressed) { }
        public int UpdatePhysicalMouseButtonState(int physicalButton, bool pressed) => -1;
        public int UpdateKeyboardVirtualKeyState(int virtualKey, bool pressed) => -1;
        public int UpdateJoystickButtonState(int physicalButton, bool pressed) => 0;
        public void UpdateJoystickAxisState(int axis, double value) { }
        public MouseWheelOutcome ApplyMouseWheel(int delta, bool advanceMessageOnWheel, bool textRevealing)
            => MouseWheelOutcome.Dropped;
        public bool IsRawInputCallbackActive => false;
        public bool IsAdvTextWaitPolling => false;
        public bool TryLatchAdvCoroutineYieldTrigger(int actionMask) => false;
        public bool TryLatchMessageSkipCancelClick() => false;
        public bool TryActivateInputActions(int actionMask) => false;
        public bool TryActivatePointer(int x, int y) => false;
        public bool TryRequestAdvWaitCoroutineYield() => false;
        public void HoldTriggers(int actionMask) { }
    }
}
