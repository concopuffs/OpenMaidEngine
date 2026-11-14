using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Vm;

// The interpreter's side of the input routing (IInterpreterInput): the input and waits native routes each device input
// to these members.
public sealed partial class VirtualMachine : IInterpreterInput
{
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
        // A text page starts the wait indicator from its first frame, and the page's later reveals animate again
        // (op_0x72_handler clears ctx+0x55108).
        if (textPage) _text.BeginPageWait(layoutSlot);
        try
        {
            if (textPage) _waits.WaitForInput(layoutSlot, ServiceHotspotCallback, CurrentAutoWaitState);
            else _waits.WaitForAdvInputPoll(ServiceHotspotCallback, CurrentAutoWaitState);
        }
        finally
        {
            // Also when a callback ends the run with a native script error (the exception leaves this wait).
            _text.StopWaitIndicator();
            _advTextWaitPolling = outerWaitPolling;
            _advInputPollPageless = outerPageless;
        }
        _saves.Profile.ReadText.QueueMessage(
            _cur.Script.PackedId, CurrentReadMessageIndex(),
            _cur.Script.ReadMessageOffsets.Count);
        _skip.PagePassed();   // pacing charges the page if fast-forward passed it
    }

    private AdvAutoWaitState CurrentAutoWaitState() => _skip.AutoWaitState;

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
        if (_cur.Hotspots.HasDefinitions && !_cur.Hotspots.Armed)
        {
            _scene.InteractiveFrame = _cur;
            wakeAtWait = _cur.Hotspots.Arm(_pointerX, _pointerY);
        }
        if (wakeAtWait) _waits.WakeInputCallbackService();
        WaitAtAdvInputPoll((int)Read(a[0]), textPage: true);
        return StepResult.Next;
    }

    // 0x90: inclusive rect + enter/leave/activate local callbacks
    private StepResult OpRegisterHotspotCallbacks(Instruction ins, int pc)
    {
        var a = ins.Args;
        _cur.Hotspots.Register((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                               (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]));
        return StepResult.Next;
    }

    // 0x93
    private StepResult OpCancelHotspotWait(Instruction ins, int pc)
    {
        _cur.Hotspots.Reset();
        if (ReferenceEquals(_scene.InteractiveFrame, _cur)) _scene.InteractiveFrame = null;
        return StepResult.Next;
    }

    // 0x94
    private StepResult OpArmHotspotWait(Instruction ins, int pc)
    {
        bool wake;
        _scene.InteractiveFrame = _cur;
        wake = _cur.Hotspots.Arm(_pointerX, _pointerY);
        if (wake) _waits.WakeInputCallbackService();
        return StepResult.Next;
    }

    // 0x97: bind a configured logical action to this record
    private StepResult OpBindHotspotKey(Instruction ins, int pc)
    {
        var a = ins.Args;
        _cur.Hotspots.BindKey((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                              (int)Read(a[3]), (int)Read(a[4]));
        return StepResult.Next;
    }

    // 0x86: raw indexed .CUR resource
    private StepResult OpSetCursorResource(Instruction ins, int pc)
    {
        var a = ins.Args;
        _waits.SetCursorResource(Read(a[0])); return StepResult.Next;
    }

    // 0x87
    private StepResult OpClearCursorResource(Instruction ins, int pc)
    {
        _waits.ClearCursorResource(); return StepResult.Next;
    }

    // 0xcc (poll interval ms, local target dword offset)
    private StepResult OpRegisterMouseCallback(Instruction ins, int pc)
    {
        var a = ins.Args;
        // op_0xcc_register_mouse_callback@0x420f70: one engine-wide registration (target +0x5f684, owner
        // +0x5f6ac, interval timer +0x5f320). A registration from any frame replaces the earlier one.
        _scene.MouseCallbackTarget = (int)Read(a[1]);
        _scene.MouseCallbackOwner = _cur.Script;
        _mouseCallbackIntervalMs = System.Math.Max(0, Read(a[0]));
        _mouseCallbackArmedAtMs = _waits.InputClockMilliseconds;
        _scene.RawInputFrame = _cur;
        return StepResult.Next;
    }

    // 0xcd
    private StepResult OpDispatchMouseCallback(Instruction ins, int pc)
    {
        // op_0xcd_dispatch_mouse_callback@0x417e10: once the interval has passed (at once under fast-forward),
        // restart it, push the return pc and jump to the target. A target registered by another script raises
        // Depth with the return pc still pushed, so 再試行 and 無視 resume with it on the stack.
        long now = _waits.InputClockMilliseconds;
        if (now - _mouseCallbackArmedAtMs < _mouseCallbackIntervalMs
            && !(AdvFastForwardActive))
            return StepResult.Next;
        _mouseCallbackArmedAtMs = now;
        if (_scene.MouseCallbackTarget == -1) return StepResult.Next;
        _cur.CallStack.Add(pc + 1);
        if (_scene.MouseCallbackOwner is { } owner && !IsSameScript(owner, _cur.Script))
            return StepResult.Jump(RaiseInstructionScriptError(DepthError(owner), pc));
        if (_cur.Script.IndexByOffset.TryGetValue(_scene.MouseCallbackTarget, out int target)) return StepResult.Jump(target);
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
            | Volatile.Read(ref _scene.HeldInputCallbackMask)
            | Interlocked.Exchange(ref _scene.QueuedInputCallbackMask, 0);
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
        Write(a[0], Volatile.Read(ref _scene.MouseButtonState)); return StepResult.Next;
    }

    // 0x106: active-device capability, zero when absent
    private StepResult OpGetJoystickButtonCount(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], System.Math.Max(0, _waits.JoystickButtonCount)); return StepResult.Next;
    }

    // 0x10d
    private StepResult OpConsumeMouseWheelDelta(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], Interlocked.Exchange(ref _scene.MouseWheelDelta, 0)); return StepResult.Next;
    }

    // 0x109
    private StepResult OpGetCursorVirtual(Instruction ins, int pc)
    {
        var a = ins.Args;
        int x, y;
        x = _pointerX; y = _pointerY;
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
        _waits.WarpCursor(x, y);
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
