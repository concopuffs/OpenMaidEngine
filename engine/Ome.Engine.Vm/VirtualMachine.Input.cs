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
    // The action op 0x104 dispatched last, or the action count for the idle row: a module global in native
    // (0x5e73fc, -1 in the image), which no reset touches.
    private int _lastSteppedInputAction = -1;

    /// <summary>Native run-state 0x80000000: the engine runs adv_input_service_poll@0x411230 in place of the interpreter
    /// until a click, fast-forward, Auto or a callback ends the wait. Op 0x72 enters it for a text page. Op 0xfa enters
    /// it with no page: no wait indicator, no page break, and the text stays. Either way the end queues the frame's
    /// current message as read.</summary>
    private void WaitAtAdvInputPoll(int layoutSlot, bool textPage)
    {
        _advWaitCoroutineYieldRequested = 0; // a request belongs to the wait it reached
        bool outerWaitPolling = _advTextWaitPolling;
        bool outerPageless = _advInputPollPageless;
        _advTextWaitPolling = true;
        _advInputPollPageless = !textPage;
        // A text page starts the wait indicator from its first frame, and the page's later reveals animate again
        // (op_0x72_handler clears ctx+0x55108).
        if (textPage) _text.BeginPageWait(layoutSlot);
        try
        {
            if (textPage) _waits.WaitForInput(ServiceHotspotCallback, CurrentAutoWaitState);
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

    private AdvAutoWaitState CurrentAutoWaitState() => _skip.AutoWaitStateFor(_text.WaitLayoutCharacterCount);

    // 0x72: wait for input, publishing the wait indicator on the layout
    private StepResult OpWaitForInput(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (LayoutSlotRefused(ins, Read(a[0])) is { } refused) return refused;
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
        long index = Read(a[0]);
        // op_0xfb_register_joy_callback@0x421270 stores slots 0..31 and throws SetKeyJump's argument error with flags
        // 0x10005 for the rest.
        if (index is < 0 or >= InputBindings.MaximumActions)
            return YesNoScriptError(BadArgument("SetKeyJump"), pc);
        _cur.InputCallbackTargets[(int)index] = (int)Read(a[1]);
        return StepResult.Next;
    }

    // 0xfe: actions [0,count), no-input callback at count
    private StepResult OpSetInputActionCount(Instruction ins, int pc)
    {
        var a = ins.Args;
        int count = unchecked((int)Read(a[0]));
        // op_0xfe_set_input_action_count@0x421390 throws SetKeyTotal's argument error with flags 0x10005 for a count
        // of 32 or more, unsigned.
        if (!InputBindings.SetActionCount(count))
            return YesNoScriptError(BadArgument("SetKeyTotal"), pc);
        return StepResult.Next;
    }

    // 0xff
    private StepResult OpPollJoyCallbackInput(Instruction ins, int pc)
    {
        _cur.PendingInputCallbackMask = InputBindings.PollActionMask()
            | _scene.HeldInputCallbackMask
            | Take(ref _scene.QueuedInputCallbackMask);
        _cur.InputCallbackScanIndex = 0;
        _cur.InputCallbackScanLimit = InputBindings.ActionCount;   // AGE 4.38D: op 0x104's scan starts below it
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

    // 0x102 (AGE's SetKeyJumpEx): (action)(step)(target) — the stepped callback table's entry
    private StepResult OpRegisterSteppedJoyCallback(Instruction ins, int pc)
    {
        var a = ins.Args;
        long action = Read(a[0]), step = Read(a[1]);
        // op_0x102_handler@0x420b40 throws one error with flags 0x10005 for an action outside 0..31 and for a step
        // outside 0..9.
        if (action is < 0 or >= InputBindings.MaximumActions || step is < 0 or >= ExecFrame.InputCallbackStepCount)
            return YesNoScriptError(AgeBoxMessage.Keyed(AgeBoxText.BadFirstArgument, "SetKeyJumpEx"), pc);
        _cur.InputCallbackStepTargets[(int)action * ExecFrame.InputCallbackStepCount + (int)step] = (int)Read(a[2]);
        return StepResult.Next;
    }

    // 0x103: (steps) — how many steps op 0x104 cycles through; this frame starts again at step 0
    private StepResult OpSetJoyCallbackSteps(Instruction ins, int pc)
    {
        _scene.InputCallbackSteps = unchecked((int)Read(ins.Args[0]));
        _cur.InputCallbackStep = 0;
        return StepResult.Next;
    }

    // 0x105: (mask) — the actions whose callback returns to op 0x104 itself
    private StepResult OpSetJoyCallbackRepeatMask(Instruction ins, int pc)
    {
        _scene.InputCallbackRepeatMask = unchecked((int)Read(ins.Args[0])); return StepResult.Next;
    }

    // 0x104: dispatch the stepped callback of the highest action op 0xff polled below this frame's scan limit, or
    // of the idle row. op_0x104_handler@0x418f40:
    //  - the step starts again at 0 when the action is not the one dispatched last;
    //  - an action of the repeat mask (op 0x105) returns to op 0x104 itself, with the scan limit lowered to it, so
    //    the next pass takes the actions below; any other action, and the idle row, returns to the next instruction;
    //  - a target of -1 calls nothing;
    //  - the step then advances, modulo op 0x103's count.
    private StepResult OpDispatchSteppedJoyCallbacks(Instruction ins, int pc)
    {
        int steps = _scene.InputCallbackSteps;
        if (steps == 0)
        {
            // Native divides by the count: with none set (op 0x103 not run since the scene reset) the process dies.
            HaltReason ??= $"division-by-zero: {At(ins)}: the stepped callbacks have no step count (op 0x103), where "
                           + "AGE divides by it and the process ends";
            return StepResult.Halt;
        }
        int action = -1;
        for (int index = _cur.InputCallbackScanLimit - 1; index >= 0; index--)
            if (index < 32 && (_cur.PendingInputCallbackMask & (1 << index)) != 0)
            {
                action = index;
                break;
            }
        bool repeats = action >= 0 && (_scene.InputCallbackRepeatMask & (1 << action)) != 0;
        int row = action >= 0 ? action : InputBindings.ActionCount;
        if (_lastSteppedInputAction != row) _cur.InputCallbackStep = 0;
        _lastSteppedInputAction = row;
        if (repeats) _cur.InputCallbackScanLimit = action;
        // Native indexes the frame's block flat, so a step of ten or more reads the next row; past the frame's block
        // it reads another frame's, where the port finds no target.
        int step = _cur.InputCallbackStep;
        long entry = (long)row * ExecFrame.InputCallbackStepCount + step;
        int targetOffset = entry >= 0 && entry < _cur.InputCallbackStepTargets.Length
            ? _cur.InputCallbackStepTargets[entry]
            : -1;
        _cur.InputCallbackStep = unchecked(step + 1) % steps;
        if (targetOffset < 0 || !_cur.Script.IndexByOffset.TryGetValue(targetOffset, out int target))
            return StepResult.Next;
        _cur.CallStack.Add(repeats ? pc : pc + 1);
        return StepResult.Jump(target);
    }

    // 0x107: button slot N emits action N+4
    private StepResult OpMapJoystickButton(Instruction ins, int pc)
    {
        var a = ins.Args;
        // op_0x107_map_joystick_button@0x421550 skips a slot of 32 or more, unsigned, without a word.
        if (IndexRefused(ins, "button slot", Read(a[0]), InputBindings.MaximumActions,
                         IndexTest.Silent(InputBindings.MaximumActions)) is { } skipped)
            return skipped;
        InputBindings.MapJoystickButton(unchecked((int)Read(a[0])), unchecked((int)Read(a[1])));
        return StepResult.Next;
    }

    // 0x108
    private StepResult OpGetMouseButtonState(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _scene.MouseButtonState); return StepResult.Next;
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
        Write(a[0], Take(ref _scene.MouseWheelDelta)); return StepResult.Next;
    }

    // 0x2e5 (AGE 4.38D): (out) — the accumulated horizontal wheel delta, read and cleared
    private StepResult OpConsumeMouseHorizontalWheelDelta(Instruction ins, int pc)
    {
        Write(ins.Args[0], Take(ref _scene.MouseHorizontalWheelDelta)); return StepResult.Next;
    }

    // 0x10e: (out)(DirectInput scan code) — GetAsyncKeyState of the code's virtual key
    private StepResult OpGetKeyState(Instruction ins, int pc)
    {
        var a = ins.Args;
        long scanCode = Read(a[1]);
        // The worker indexes its 256-entry DIK-to-VK table with the operand as it comes.
        if (IndexRefused(ins, "scan code", scanCode, InputBindings.ScanCodeCount, IndexTest.Unchecked) is { } refused)
            return refused;
        Write(a[0], InputBindings.KeyState((int)scanCode));
        return StepResult.Next;
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
        // op_0x10b_map_mouse_button@0x4216f0 skips an action of 32 or more, unsigned, without a word, then stores the
        // action at any physical button: its table holds 32, of which polling reads buttons 0 and 1.
        if (IndexRefused(ins, "action", Read(a[0]), InputBindings.MaximumActions,
                         IndexTest.Silent(InputBindings.MaximumActions)) is { } skipped)
            return skipped;
        if (IndexRefused(ins, "physical button", Read(a[1]), InputBindings.MaximumActions, IndexTest.Unchecked)
            is { } refused)
            return refused;
        InputBindings.MapMouseButton(unchecked((int)Read(a[0])), unchecked((int)Read(a[1])));
        return StepResult.Next;
    }

    // 0x10c: logical action <- DIK translated through native VK table
    private StepResult OpMapKeyboardScancode(Instruction ins, int pc)
    {
        var a = ins.Args;
        int action = unchecked((int)Read(a[0]));
        int dik = unchecked((int)Read(a[1]));
        // op_0x10c_map_keyboard_scancode@0x421730 throws SetKeyMulti's argument error with flags 0x10005 for an action
        // of 32 or more, unsigned; it then translates the scan code through its 256-entry table unchecked.
        if ((uint)action >= InputBindings.MaximumActions)
            return YesNoScriptError(BadArgument("SetKeyMulti"), pc);
        if (IndexRefused(ins, "scan code", dik, 256, IndexTest.Unchecked) is { } refused) return refused;
        InputBindings.MapKeyboardScanCode(action, dik);
        return StepResult.Next;
    }
}
