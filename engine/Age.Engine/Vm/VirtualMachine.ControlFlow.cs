using Age.Engine.Diagnostics;
using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepControlFlow(string label, Instruction ins, int pc)
    {
        int op = ins.Opcode;
        var a = ins.Args;
        switch (label)
        {
            case "jmp": return _cur.Script.IndexByOffset.GetValueOrDefault((int)a[0].Value, pc + 1);
            case "call": _cur.CallStack.Add(pc + 1); return _cur.Script.IndexByOffset.GetValueOrDefault((int)a[0].Value, pc + 1);
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
                int? targetOffset;
                if (!_cur.CoroutineYieldActive)
                {
                    _cur.CoroutineResumePc = pc + 1;
                    _cur.CoroutineYieldActive = true;
                    _host.SetAdvPagePresentationSuspended(Gfx, true);
                    targetOffset = _cur.CoroutineYieldHandlerA;
                }
                else targetOffset = _cur.CoroutineYieldHandlerB;

                return targetOffset is int offset
                    ? _cur.Script.IndexByOffset.GetValueOrDefault(offset, pc + 1)
                    : pc + 1;
            }
            case "u00416A90":
            case "coroutine-resume":                // 0x7c: restore the PC saved by op 0x199
                if (_cur.CoroutineResumePc is int resumePc)
                {
                    _cur.CoroutineResumePc = null;
                    _cur.CoroutineYieldActive = false;
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
                if (!TryGetAdvYieldTerminal(pc, a[0], out long terminal))
                {
                    HaltReason ??= $"coroutine-yield-pattern@0x{ins.Offset:x}";
                    return HALT;
                }

                int visits = _cur.CoroutineYieldVisits.GetValueOrDefault(pc);
                _cur.CoroutineYieldVisits[pc] = visits + 1;
                // First visit must enter setup even if out retained this same terminal from a prior scene.
                // Every later visit returns the script-encoded terminal and exits the bounded loop.
                Write(a[0], visits == 0 ? (terminal == 0 ? 1 : 0) : terminal);
                return pc + 1;
            }
            default:
                throw new InvalidOperationException($"Non-control-flow opcode routed to control-flow handler: {label}");
        }
    }
}
