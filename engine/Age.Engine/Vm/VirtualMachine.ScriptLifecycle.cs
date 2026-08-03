using Age.Engine.Diagnostics;
using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepScriptLifecycle(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "u00415880":           // 0xd9 / semantics: clear-run-state-0x1000
                return pc + 1;
            case "get-initial-root-run": // 0x130 (out)
                Write(a[0], _initialRootRun ? 1 : 0);
                return pc + 1;
            case "throw-exit-request":
                if (_o.IgnoreExitRequests) return pc + 1;
                // Native op 0x1 throws Command_Exit_Exception through callbacks and nested script
                // frames. The outer engine loop catches it and exits without advancing frame_pc.
                throw new ProcessExitRequestedException();
            case "exit": return FRAME_RETURN;
            case "exit-script":
                // Native op 0x9 clears the process-initial flag, disposes every active script frame,
                // resets scene-owned services, and loads raw script resource 0 as the new root.
                _initialRootRun = false;
                return ROOT_RELOAD;
            case "call-script":
            {
                long id = a.Count > 0 ? Read(a[0]) : 0;
                CallScriptDispatches++;
                if (_provider == null)
                {
                    _sink.Emit(TraceEvent.CallScript(id, null));   // stub mode: notify only, no child pushed
                    return pc + 1;
                }
                if (_depth >= _o.CallDepthCap) { HaltReason ??= "call-depth-exceeded"; return HALT; }
                var child = _provider.GetById(id);
                _sink.Emit(TraceEvent.CallScript(id, child?.Name));
                if (child == null) { HaltReason ??= $"callscript-unresolved:0x{id:x}"; return HALT; }
                var entry = child.IndexByOffset.TryGetValue(0, out var ci) ? ci : 0;
                var outcome = RunFrame(new ExecFrame(child, entry), FrameCause.CallScript, id);
                if (outcome == FrameOutcome.Halted) return HALT;   // propagate whole-VM halt up
                if (outcome == FrameOutcome.RootReload) return ROOT_RELOAD; // discard every caller frame
                if (outcome == FrameOutcome.ExitRequested) throw new ProcessExitRequestedException();
                return pc + 1;                                      // Returned / RanOff: resume caller
            }
            case "u00415FB0":
            case "run-mounted-append-autoruns": // 0x143: selector slots 1..255, packed record zero
            {
                if (_provider == null) return pc + 1;

                // Native first scans every mounted selector into its launch queue, then dispatches
                // those packed scripts serially. Snapshot before running any child so script-side
                // effects cannot change the current batch.
                int[] selectors = _provider.MountedAppendSelectors
                    .Where(selector => selector is > 0 and <= 0xff)
                    .Distinct()
                    .Order()
                    .ToArray();
                foreach (int selector in selectors)
                {
                    if (_depth >= _o.CallDepthCap)
                    {
                        HaltReason ??= "call-depth-exceeded";
                        return HALT;
                    }

                    long id = (long)selector << 24;
                    CallScriptDispatches++;
                    var child = _provider.GetById(id);
                    _sink.Emit(TraceEvent.CallScript(id, child?.Name));
                    if (child == null)
                    {
                        HaltReason ??= $"append-autorun-unresolved:0x{id:x}";
                        return HALT;
                    }

                    int entry = child.IndexByOffset.TryGetValue(0, out int childEntry) ? childEntry : 0;
                    var outcome = RunFrame(new ExecFrame(child, entry), FrameCause.CallScript, id);
                    if (outcome == FrameOutcome.Halted) return HALT;
                    if (outcome == FrameOutcome.RootReload) return ROOT_RELOAD;
                    if (outcome == FrameOutcome.ExitRequested) throw new ProcessExitRequestedException();
                }
                return pc + 1;
            }
            case "u00417E80":
            case "preload-script-slot": // 0x06 (script_id, frame_slot), valid slots 0..39
            {
                long id = Read(a[0]);
                int slot = unchecked((int)Read(a[1]));
                if ((uint)slot >= 40)
                {
                    HaltReason ??= $"preloaded-script-slot-out-of-range:{slot}";
                    return HALT;
                }
                if (_provider == null)
                {
                    HaltReason ??= $"preloaded-script-provider-unavailable:0x{id:x}";
                    return HALT;
                }
                var script = _provider.GetById(id);
                if (script == null)
                {
                    HaltReason ??= $"preloaded-script-unresolved:0x{id:x}";
                    return HALT;
                }
                int entry = script.IndexByOffset.TryGetValue(0, out int loadedEntry) ? loadedEntry : 0;
                _preloadedScriptSlots[slot] = new PreloadedScriptSlot(id, new ExecFrame(script, entry));
                return pc + 1;
            }
            case "u00417FC0":
            case "call-preloaded-script-slot": // 0x08 (frame_slot)
            {
                int slot = unchecked((int)Read(a[0]));
                if ((uint)slot >= 40)
                {
                    HaltReason ??= $"preloaded-script-slot-out-of-range:{slot}";
                    return HALT;
                }
                if (!_preloadedScriptSlots.TryGetValue(slot, out var loaded))
                {
                    HaltReason ??= $"preloaded-script-slot-empty:{slot}";
                    return HALT;
                }
                if (_depth >= _o.CallDepthCap) { HaltReason ??= "call-depth-exceeded"; return HALT; }

                CallScriptDispatches++;
                _sink.Emit(TraceEvent.CallScript(loaded.ScriptId, loaded.Frame.Script.Name));
                // PC restarts at codebase while the native slot's local banks remain allocated.
                // Balanced local calls leave this empty; clearing the port-only emission guard makes
                // each invocation an independent diagnostic activation.
                loaded.Frame.CallStack.Clear();
                loaded.Frame.EmitSeen.Clear();
                loaded.Frame.Pc = loaded.Frame.Script.IndexByOffset.TryGetValue(0, out int loadedEntry)
                    ? loadedEntry : 0;
                var outcome = RunFrame(loaded.Frame, FrameCause.CallScript, loaded.ScriptId);
                if (outcome == FrameOutcome.Halted) return HALT;
                if (outcome == FrameOutcome.RootReload) return ROOT_RELOAD;
                if (outcome == FrameOutcome.ExitRequested) throw new ProcessExitRequestedException();
                return pc + 1;
            }
            default:
                throw new InvalidOperationException($"Non-script-lifecycle opcode routed to script-lifecycle handler: {label}");
        }
    }
}
