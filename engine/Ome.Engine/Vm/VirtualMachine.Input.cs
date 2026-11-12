using Ome.Engine.Hosting;
using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // The op-0xcc mouse callback is engine state, not frame state (op 0xcd checks the registrant).
    private int _mouseCallbackTarget = -1;
    private Script? _mouseCallbackOwner;
    private long _mouseCallbackIntervalMs;
    private long _mouseCallbackArmedAtMs;

    /// <summary>Native run-state 0x80000000: the engine runs adv_input_service_poll@0x411230 in place of the interpreter
    /// until a click, fast-forward, Auto or a callback ends the wait. Op 0x72 enters it for a text page. Op 0xfa enters
    /// it with no page: no wait indicator, no page break, and the text stays. Either way the end queues the frame's
    /// current message as read.</summary>
    private void WaitAtAdvInputPoll(int layoutSlot, bool textPage)
    {
        Volatile.Write(ref _advWaitCoroutineYieldRequested, 0); // a request belongs to the wait it reached
        bool outerWaitPolling = _advTextWaitPolling;
        bool outerPageless = _advInputPollPageless;
        _advTextWaitPolling = true;
        _advInputPollPageless = !textPage;
        try
        {
            if (textPage) _host.WaitForInput(layoutSlot, ServiceHotspotCallback, CurrentAutoWaitState);
            else _host.WaitForAdvInputPoll(ServiceHotspotCallback, CurrentAutoWaitState);
        }
        finally
        {
            _advTextWaitPolling = outerWaitPolling;
            _advInputPollPageless = outerPageless;
        }
        _sharedProfile.ReadText.QueueMessage(
            _cur.Script.PackedId, CurrentReadMessageIndex(),
            _cur.Script.ReadMessageOffsets.Count);
        _fastForwardPagePassed = true;   // PaceFastForwardOp charges the page if fast-forward passed it
    }

    private AdvAutoWaitState CurrentAutoWaitState()
        => new(_autoMessageEnabled, _autoVoicePending, _autoMessageTime0Ms, _autoMessageTime1Ms);

    private StepResult OpWaitForInput(Instruction ins, int pc)
    {
        var a = ins.Args;
        EnterTextWait();
        // Faithful headless: no player => halt here rather than plow past every prompt (see VmOptions).
        if (_o.HaltAtWaitForInput) { HaltReason ??= "wait-for-input"; return StepResult.Halt; }
        // Re-arm definitions that an earlier 0x93 left as a template (HotspotRegistry.Reset). Only
        // direct-scene runs reach this: they force the scene's developer-loop 0x93 at 0x622. On retail
        // paths every 0x93 is re-registered before a text wait (2026-11-04; docs/backlog.md).
        bool wakeAtWait = false;
        lock (_interactiveLock)
        {
            if (_cur.Hotspots.HasDefinitions && !_cur.Hotspots.Armed)
            {
                _interactiveFrame = _cur;
                wakeAtWait = _cur.Hotspots.Arm(_pointerX, _pointerY);
            }
        }
        if (wakeAtWait) _host.WakeInputCallbackService();
        WaitAtAdvInputPoll((int)Read(a[0]), textPage: true);
        return StepResult.Next;
    }

    // 0x90: inclusive rect + enter/leave/activate local callbacks
    private StepResult OpRegisterHotspotCallbacks(Instruction ins, int pc)
    {
        var a = ins.Args;
        lock (_interactiveLock)
            _cur.Hotspots.Register((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                                   (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]));
        return StepResult.Next;
    }

    // 0x93
    private StepResult OpCancelHotspotWait(Instruction ins, int pc)
    {
        lock (_interactiveLock)
        {
            _cur.Hotspots.Reset();
            if (ReferenceEquals(_interactiveFrame, _cur)) _interactiveFrame = null;
        }
        return StepResult.Next;
    }

    // 0x94
    private StepResult OpArmHotspotWait(Instruction ins, int pc)
    {
        bool wake;
        lock (_interactiveLock)
        {
            _interactiveFrame = _cur;
            wake = _cur.Hotspots.Arm(_pointerX, _pointerY);
        }
        if (wake) _host.WakeInputCallbackService();
        return StepResult.Next;
    }

    // 0x97: bind a configured logical action to this record
    private StepResult OpBindHotspotKey(Instruction ins, int pc)
    {
        var a = ins.Args;
        lock (_interactiveLock)
            _cur.Hotspots.BindKey((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                                  (int)Read(a[3]), (int)Read(a[4]));
        return StepResult.Next;
    }

    // 0x86: raw indexed .CUR resource
    private StepResult OpSetCursorResource(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.SetCursorResource(Read(a[0])); return StepResult.Next;
    }

    // 0x87
    private StepResult OpClearCursorResource(Instruction ins, int pc)
    {
        _host.ClearCursorResource(); return StepResult.Next;
    }

    // 0xcc (poll interval ms, local target dword offset)
    private StepResult OpRegisterMouseCallback(Instruction ins, int pc)
    {
        var a = ins.Args;
        // op_0xcc_register_mouse_callback@0x420f70: one engine-wide registration (target +0x5f684, owner
        // +0x5f6ac, interval timer +0x5f320). A registration from any frame replaces the earlier one.
        _mouseCallbackTarget = (int)Read(a[1]);
        _mouseCallbackOwner = _cur.Script;
        _mouseCallbackIntervalMs = System.Math.Max(0, Read(a[0]));
        _mouseCallbackArmedAtMs = _host.InputClockMilliseconds;
        lock (_interactiveLock) _rawInputFrame = _cur;
        return StepResult.Next;
    }

    // 0xcd
    private StepResult OpDispatchMouseCallback(Instruction ins, int pc)
    {
        // op_0xcd_dispatch_mouse_callback@0x417e10: once the interval has passed (at once under fast-forward),
        // restart it, push the return pc and jump to the target. A target registered by another script raises
        // Depth with the return pc still pushed, so 再試行 and 無視 resume with it on the stack.
        long now = _host.InputClockMilliseconds;
        if (now - _mouseCallbackArmedAtMs < _mouseCallbackIntervalMs
            && !(_messageSkipServiceActive || _host.IsMessageSkipActive))
            return StepResult.Next;
        _mouseCallbackArmedAtMs = now;
        if (_mouseCallbackTarget == -1) return StepResult.Next;
        _cur.CallStack.Add(pc + 1);
        if (_mouseCallbackOwner is { } owner && !IsSameScript(owner, _cur.Script))
            return StepResult.Jump(RaiseInstructionScriptError(DepthError(owner), pc));
        if (_cur.Script.IndexByOffset.TryGetValue(_mouseCallbackTarget, out int target)) return StepResult.Jump(target);
        _cur.CallStack.RemoveAt(_cur.CallStack.Count - 1);
        return StepResult.Next;
    }

    // 0xfb (input index, local target dword offset)
    private StepResult OpRegisterJoyCallback(Instruction ins, int pc)
    {
        var a = ins.Args;
        int index = (int)Read(a[0]);
        if ((uint)index < 32) _cur.InputCallbackTargets[index] = (int)Read(a[1]);
        return StepResult.Next;
    }

    // 0xfe: actions [0,count), no-input callback at count
    private StepResult OpSetInputActionCount(Instruction ins, int pc)
    {
        var a = ins.Args;
        int count = unchecked((int)Read(a[0]));
        if (!InputBindings.SetActionCount(count))
        {
            HaltReason ??= $"input-action-count-out-of-range:{count}";
            return StepResult.Halt;
        }
        return StepResult.Next;
    }

    // 0xff
    private StepResult OpPollJoyCallbackInput(Instruction ins, int pc)
    {
        _cur.PendingInputCallbackMask = InputBindings.PollActionMask()
            | Volatile.Read(ref _heldInputCallbackMask)
            | Interlocked.Exchange(ref _queuedInputCallbackMask, 0);
        _cur.InputCallbackScanIndex = 0;
        return StepResult.Next;
    }

    // 0x100
    private StepResult OpDispatchJoyCallbacks(Instruction ins, int pc)
    {
        int actionCount = InputBindings.ActionCount;
        if (_cur.PendingInputCallbackMask == 0)
        {
            int idleTarget = _cur.InputCallbackTargets[actionCount];
            if (idleTarget < 0 || !_cur.Script.IndexByOffset.TryGetValue(idleTarget, out int target))
                return StepResult.Next;
            _cur.CallStack.Add(pc + 1);
            return StepResult.Jump(target);
        }
        while (_cur.InputCallbackScanIndex < actionCount)
        {
            int index = _cur.InputCallbackScanIndex++;
            if ((_cur.PendingInputCallbackMask & (1 << index)) == 0) continue;
            int targetOffset = _cur.InputCallbackTargets[index];
            if (targetOffset < 0 || !_cur.Script.IndexByOffset.TryGetValue(targetOffset, out int target))
                continue;
            // Resume on op 0x100 so another simultaneously active input can dispatch.
            _cur.CallStack.Add(pc);
            return StepResult.Jump(target);
        }
        return StepResult.Next;
    }

    // 0x107: button slot N emits action N+4
    private StepResult OpMapJoystickButton(Instruction ins, int pc)
    {
        var a = ins.Args;
        InputBindings.MapJoystickButton(unchecked((int)Read(a[0])), unchecked((int)Read(a[1])));
        return StepResult.Next;
    }

    // 0x108
    private StepResult OpGetMouseButtonState(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], Volatile.Read(ref _mouseButtonState)); return StepResult.Next;
    }

    // 0x106: active-device capability, zero when absent
    private StepResult OpGetJoystickButtonCount(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], System.Math.Max(0, _host.JoystickButtonCount)); return StepResult.Next;
    }

    // 0x10d
    private StepResult OpConsumeMouseWheelDelta(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], Interlocked.Exchange(ref _mouseWheelDelta, 0)); return StepResult.Next;
    }

    // 0x109
    private StepResult OpGetCursorVirtual(Instruction ins, int pc)
    {
        var a = ins.Args;
        int x, y;
        lock (_interactiveLock) { x = _pointerX; y = _pointerY; }
        Write(a[0], x == int.MinValue ? 0 : x);
        Write(a[1], y == int.MinValue ? 0 : y);
        return StepResult.Next;
    }

    // 0x10a: update AGE state immediately, then request the native warp
    private StepResult OpSetCursorVirtual(Instruction ins, int pc)
    {
        var a = ins.Args;
        int x = (int)Read(a[0]);
        int y = (int)Read(a[1]);
        UpdatePointer(x, y);
        _host.WarpCursor(x, y);
        return StepResult.Next;
    }

    // 0x10b: physical button -> slot, polled action is slot+4
    private StepResult OpMapMouseButton(Instruction ins, int pc)
    {
        var a = ins.Args;
        InputBindings.MapMouseButton(unchecked((int)Read(a[0])), unchecked((int)Read(a[1])));
        return StepResult.Next;
    }

    // 0x10c: logical action <- DIK translated through native VK table
    private StepResult OpMapKeyboardScancode(Instruction ins, int pc)
    {
        var a = ins.Args;
        int action = unchecked((int)Read(a[0]));
        int dik = unchecked((int)Read(a[1]));
        if (!InputBindings.MapKeyboardScanCode(action, dik))
        {
            HaltReason ??= $"keyboard-action-out-of-range:{action}";
            return StepResult.Halt;
        }
        return StepResult.Next;
    }
}

/// <summary>Where <see cref="VirtualMachine.ApplyMouseWheel"/> sent one wheel delta.</summary>
public enum MouseWheelOutcome
{
    /// <summary>Accumulated for op 0x10d.</summary>
    Accumulated,
    /// <summary>Queued the hotspot callback bound to the wheel's logical action.</summary>
    Activated,
    /// <summary>The frontend should advance the page or finish the text reveal, as a click does.</summary>
    Advance,
    /// <summary>Consumed with no effect.</summary>
    Dropped,
}
