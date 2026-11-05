using Age.Engine.Diagnostics;
using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    // The op-0x199 yield is engine state (coroutine_runstate +0x6dbc8, resume offset +0x6dbcc, owner +0x5f6bc).
    // The resume is held as an instruction index: op 0x7c only takes it in a frame of the owner's script.
    private bool _coroutineYieldEntered;      // coroutine_runstate 0x4000000
    private int? _coroutineResumePc;          // coroutine_runstate 0x2000000 with its resume offset
    private Script? _coroutineYieldOwner;

    private void ResetCoroutineYield()
    {
        _coroutineYieldEntered = false;
        _coroutineResumePc = null;
        _coroutineYieldOwner = null;
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
                _valueSwitchTargets[FormatSwitchValue(a[0])] = checked((int)Read(a[1])); return pc + 1;
            case "value-switch-jump":
            {
                int target = _valueSwitchTargets.TryGetValue(FormatSwitchValue(a[0]), out int matched)
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
                // and its owner (+0x5f6bc) are engine-wide. The first yield saves the resume pc and its owner and
                // enters handler A. A yield while one is entered (or with no handler A) leaves for handler B, with
                // the resume still armed.
                if (!_coroutineYieldEntered && _cur.CoroutineYieldHandlerA != -1)
                {
                    _coroutineResumePc = pc + 1;
                    _coroutineYieldOwner = _cur.Script;
                    _coroutineYieldEntered = true;
                    _host.SetAdvPagePresentationSuspended(Gfx, true);
                    return _cur.Script.IndexByOffset.GetValueOrDefault(_cur.CoroutineYieldHandlerA, pc + 1);
                }
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
                        return RaiseInstructionScriptError(DepthErrorText(owner), pc);
                    ResetCoroutineYield();
                    _host.SetAdvPagePresentationSuspended(Gfx, false);
                    return resumePc;
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
