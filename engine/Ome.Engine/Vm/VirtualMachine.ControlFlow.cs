using Ome.Engine.Diagnostics;
using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // The op-0x199 yield is engine state (coroutine_runstate +0x6dbc8, resume offset +0x6dbcc, owner +0x5f6bc).
    // The resume is held as an instruction index: op 0x7c only takes it in a frame of the owner's script.
    private bool _coroutineYieldEntered;      // coroutine_runstate 0x4000000
    private int? _coroutineResumePc;          // coroutine_runstate 0x2000000 with its resume offset
    private Script? _coroutineYieldOwner;
    // The yield came from an ADV text wait (adv_input_service_poll's action-5 entry), so the saved run state holds
    // the wait (0x80000000): op 0x7c returns to the parked wait rather than to an instruction.
    private bool _coroutineResumesTextWait;
    private volatile bool _coroutineYieldActive;      // _coroutineResumePc is set; read by the input thread
    private volatile bool _hiddenWindowWaiting;       // run-state 0x4000000: the engine waits for any trigger
    private volatile bool _hiddenWindowWheelTriggers; // ...with the text wait's 0x80000000, so the wheel is one
    private int _advWaitCoroutineYieldRequested;      // logical action 5 reached a polling ADV text wait

    private void ResetCoroutineYield()
    {
        _coroutineYieldEntered = false;
        _coroutineResumePc = null;
        _coroutineYieldOwner = null;
        _coroutineResumesTextWait = false;
        _coroutineYieldActive = false;
    }

    /// <summary>Enter the frame's op-0x7b handler A with the yield saved (coroutine_runstate gets the run state plus
    /// 0x6000000; the owner is the running script). <paramref name="resumePc"/> is the instruction op 0x7c resumes;
    /// for a text wait it is the instruction after the wait, which is where native's frame pc stands.</summary>
    private int EnterCoroutineYieldHandlerA(int resumePc, bool resumesTextWait)
    {
        _coroutineResumePc = resumePc;
        _coroutineYieldOwner = _cur.Script;
        _coroutineYieldEntered = true;
        _coroutineResumesTextWait = resumesTextWait;
        _coroutineYieldActive = true;
        // The poll that led here took the input latch (the text wait's poll for Space, the flush before a control's
        // callback); the latch keeps what arrived since, such as a click an effect in the callback held.
        _host.SetAdvPagePresentationSuspended(Gfx, true);
        return _cur.Script.IndexByOffset.GetValueOrDefault(_cur.CoroutineYieldHandlerA, resumePc);
    }

    /// <summary>True while an ADV coroutine yield is under way (handler A entered, op 0x7c not yet back on the page).
    /// Native runs the handlers with no input service and parks in the hidden-window wait, so a triggered action
    /// only latches for that wait. Not while a nested script's raw input callbacks own input (HIDEWIN.BIN).</summary>
    public bool IsAdvCoroutineYieldInputLatched
        => (_coroutineYieldActive || _hiddenWindowWaiting) && !IsRawInputCallbackActive;

    /// <summary>Hold triggered logical actions (bit = 1 &lt;&lt; action) in the input latch while a yield is under way
    /// (<see cref="HoldTriggers"/>): the hidden-window wait takes them, or the text wait op 0x7c returns to. True means
    /// the input was consumed, as native's latch keeps it from every other consumer.</summary>
    public bool TryLatchAdvCoroutineYieldTrigger(int actionMask)
    {
        if (actionMask == 0 || !IsAdvCoroutineYieldInputLatched) return false;
        HoldTriggers(actionMask);
        return true;
    }

    /// <summary>adv_input_service_poll@0x411230: logical action 5 at a polling ADV text wait (with no click or
    /// fast-forward in the same poll and no op-0x97 binding) enters the frame's handler A and saves the wait itself
    /// as the resume. Both games map Space to action 5, so Space hides the text window. True when a text wait is
    /// polling; a frame without handler A then ignores the request, as native does.</summary>
    public bool TryRequestAdvWaitCoroutineYield()
    {
        if (!_advTextWaitPolling) return false;
        Volatile.Write(ref _advWaitCoroutineYieldRequested, 1);
        _host.WakeInputCallbackService();
        return true;
    }

    /// <summary>The run-state 0x4000000 wait (engine_main_tick_with_exception_policy@0x411ec8). Op 0x7c restored a
    /// run state that still holds op 0x199's bit 0x4000000, so the frame sits at the resume pc while the engine
    /// presents frames and polls the triggered actions. Any trigger (a mapped key press, a mapped mouse click, a
    /// mapped joystick button, or the persistent Skip state's injected action 6) ends it and enters the frame's
    /// handler B with a fresh yield (coroutine_runstate 0x2000000, resume = the frame pc, owner = the running script).
    /// Returns handler B's instruction index, or -1 when the frame has none.</summary>
    private int WaitInHiddenWindow(int resumePc, bool resumesTextWait)
    {
        _cur.Pc = resumePc - 1; // frame locations read the instruction after Pc: native shows the resume pc
        _hiddenWindowWheelTriggers = resumesTextWait;
        _hiddenWindowWaiting = true;
        try
        {
            _host.WaitForServiceTrigger(() =>
            {
                // input_poll_triggered_action_mask@0x460880 adds action 6 while persistent Skip (ctx+0x13dc) is set, so
                // op 0x88(1) or the native menu's Skip toggle ends this wait. Fast-forward alone (read-skip) does not.
                ApplyMenuToggles();
                return TakeHeldTriggers() != 0
                       || _messageSkipInjected || _stopRequested || _rootReloadRequested;
            });
        }
        finally
        {
            _hiddenWindowWaiting = false;
            _hiddenWindowWheelTriggers = false;
        }
        if (_cur.CoroutineYieldHandlerB == -1
            || !_cur.Script.IndexByOffset.TryGetValue(_cur.CoroutineYieldHandlerB, out int handlerB))
            return -1;
        _coroutineResumePc = resumePc;
        _coroutineYieldOwner = _cur.Script;
        _coroutineResumesTextWait = resumesTextWait;
        _coroutineYieldActive = true;
        return handlerB;
    }

    private int StepControlFlow(string label, Instruction ins, int pc)
    {
        int op = ins.Opcode;
        var a = ins.Args;
        switch (label)
        {
            case "jmp": return _cur.Script.IndexByOffset.GetValueOrDefault((int)a[0].Value, pc + 1);
            case "call":
            {
                // A direct call names its target; `call *v` calls the code offset the variable holds. A value that is
                // no instruction's offset calls nothing, and pushes no return address.
                long target = a[0].Type == T_IMM ? a[0].Value : Read(a[0]);
                if (target is < 0 or > int.MaxValue || !_cur.Script.IndexByOffset.TryGetValue((int)target, out int callee))
                    return pc + 1;
                _cur.CallStack.Add(pc + 1);
                return callee;
            }
            case "ret":
                if (_cur.CallStack.Count > 0) { int r = _cur.CallStack[^1]; _cur.CallStack.RemoveAt(_cur.CallStack.Count - 1); return r; }
                return FRAME_RETURN;    // empty intra-call stack => return from the script frame
            case "jcc":
            {
                long tgt = Read(a[0]) != 0 ? a[1].Value : a[2].Value;
                return tgt == NoJump ? pc + 1 : _cur.Script.IndexByOffset.GetValueOrDefault((int)tgt, pc + 1);
            }
            case "begin-value-switch":
                _valueSwitchTargets.Clear(); return pc + 1;
            case "add-value-switch-case":
                _valueSwitchTargets[ReadStr(a[0])] = checked((int)Read(a[1])); return pc + 1;
            case "value-switch-jump":
            {
                int target = _valueSwitchTargets.TryGetValue(ReadStr(a[0]), out int matched)
                    ? matched : checked((int)Read(a[1]));
                return _cur.Script.IndexByOffset.GetValueOrDefault(target, pc + 1);
            }
            case "u0041ADB0":
            case "coroutine-save-yield-handlers":   // 0x7b: retain native handler metadata
                _cur.CoroutineYieldHandlerA = (int)Read(a[0]);
                _cur.CoroutineYieldHandlerB = (int)Read(a[1]);
                return pc + 1;
            case "u00414D50":
            case "yield-adv-coroutine":             // 0x199: A -> nested service -> B -> 0x7c resume
            {
                // op_0x199_yield_adv_coroutine@0x416440. Only the 0x7b handlers belong to the frame; the yield state
                // and its owner (+0x5f6bc) are engine-wide. With no yield entered, it saves the resume pc and its
                // owner and enters handler A, or does nothing without one. A yield while one is entered leaves for
                // handler B, with the resume still armed. Either way it clears the service trigger mask (ctx+0xa0ce8).
                // The port keeps no such mask apart from the input latch, which op 0x199 leaves alone.
                if (!_coroutineYieldEntered)
                    return _cur.CoroutineYieldHandlerA == -1
                        ? pc + 1
                        : EnterCoroutineYieldHandlerA(pc + 1, resumesTextWait: false);
                if (_cur.CoroutineYieldHandlerB == -1) return pc + 1;
                _coroutineYieldEntered = false;
                return _cur.Script.IndexByOffset.GetValueOrDefault(_cur.CoroutineYieldHandlerB, pc + 1);
            }
            case "u00416A90":
            case "coroutine-resume":                // 0x7c: restore the PC saved by op 0x199
                if (_coroutineResumePc is int resumePc)
                {
                    // op_0x7c_handler@0x417cb0: a resume saved by another script raises Depth before anything changes,
                    // so 再試行 raises it again. Op 0x7c records no instruction length before the throw, so native's
                    // 無視 skips by the previous handler's; the port skips the 0x7c itself.
                    if (_coroutineYieldOwner is { } owner && !IsSameScript(owner, _cur.Script))
                        return RaiseInstructionScriptError(DepthError(owner), pc);
                    // Op 0x7c restores coroutine_runstate & ~0x2000000. Unless handler B came through a second op
                    // 0x199, that keeps op 0x199's 0x4000000: the hidden-window wait (Himegari's handler A takes this
                    // path, as adv_hide_window_enabled G[0x62425] is 0 in retail). The page stays hidden until
                    // handler B's own op 0x7c.
                    bool hiddenWindow = _coroutineYieldEntered;
                    bool resumesTextWait = _coroutineResumesTextWait;
                    ResetCoroutineYield();
                    // Op 0x7c also clears the current hotspot hit (ctx+0xc6f8) and keeps the hovered record: the
                    // next poll leaves it, and only a pointer move hits it again.
                    bool hitCleared;
                    lock (_interactiveLock) hitCleared = _interactiveFrame?.Hotspots.InvalidateHit() == true;
                    if (hitCleared) _host.WakeInputCallbackService();
                    if (hiddenWindow && WaitInHiddenWindow(resumePc, resumesTextWait) is >= 0 and int handlerB)
                        return handlerB;
                    _host.SetAdvPagePresentationSuspended(Gfx, false);
                    return resumesTextWait ? HOTSPOT_RETURN : resumePc;
                }
                return pc + 1;                       // cold bounded scene-entry path
            case "u0041F9C0":
            case "coroutine-label-yield":           // 0x140: bounded host model for LABEL/J only
            {
                if (!IsAdvLabeledYield(_cur.Script, ins))
                {
                    if (_sink.TracingSteps) _sink.Emit(TraceEvent.Stub(op, pc));
                    return pc + 1;
                }
                if (!TryGetAdvYieldTerminal(pc, a[0], out long terminal, out string? problem))
                {
                    HaltReason ??= problem ?? $"coroutine-yield-pattern@0x{ins.Offset:x}";
                    return HALT;
                }

                int visits = _cur.CoroutineYieldVisits.GetValueOrDefault(pc);
                _cur.CoroutineYieldVisits[pc] = visits + 1;
                // First visit must enter setup even if out retained this same terminal from a prior scene: it returns
                // -1, which no instruction has, so the setup's `call *out` calls nothing. Every later visit returns
                // the scene loop head and exits the bounded loop.
                Write(a[0], visits == 0 ? -1 : terminal);
                return pc + 1;
            }
            default:
                throw new InvalidOperationException($"Non-control-flow opcode routed to control-flow handler: {label}");
        }
    }
}
