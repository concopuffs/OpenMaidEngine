using Ome.Engine.Diagnostics;
using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0xd9 / semantics: clear-run-state-0x1000
    private StepResult OpClearRunState0x1000(Instruction ins, int pc)
    {
        return StepResult.Next;
    }

    // 0x130 (out)
    private StepResult OpGetInitialRootRun(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _scene.InitialRootRun ? 1 : 0);
        return StepResult.Next;
    }

    private StepResult OpThrowExitRequest(Instruction ins, int pc)
    {
        if (_o.IgnoreExitRequests) return StepResult.Next;
        // Native op 0x1 throws Command_Exit_Exception through callbacks and nested script
        // frames. The outer engine loop catches it and exits without advancing frame_pc.
        throw new ProcessExitRequestedException();
    }

    private StepResult OpExit(Instruction ins, int pc)
    {
        return StepResult.FrameReturn;
    }

    private StepResult OpExitScript(Instruction ins, int pc)
    {
        // Native op 0x9 clears the process-initial flag, disposes every active script frame,
        // zeroes the global banks, resets scene-owned services, and loads raw script resource 0
        // as the new root (ResetSceneContextForRootReload, shared with the 終了 dialog's return
        // to title).
        return StepResult.RootReload;
    }

    private StepResult OpCallScript(Instruction ins, int pc)
    {
        var a = ins.Args;
        long id = a.Count > 0 ? Read(a[0]) : 0;
        CallScriptDispatches++;
        if (_provider == null)
        {
            _sink.Emit(TraceEvent.CallScript(id, null));   // stub mode: notify only, no child pushed
            return StepResult.Next;
        }
        if (_depth >= _o.CallDepthCap) { HaltReason ??= "call-depth-exceeded"; return StepResult.Halt; }
        var child = _provider.GetById(id);
        _sink.Emit(TraceEvent.CallScript(id, child?.Name));
        if (child == null) { HaltReason ??= $"callscript-unresolved:0x{id:x}"; return StepResult.Halt; }
        var entry = child.IndexByOffset.TryGetValue(0, out var ci) ? ci : 0;
        var outcome = RunFrame(new ExecFrame(child, entry), FrameCause.CallScript, id);
        if (outcome == FrameOutcome.Halted) return StepResult.Halt;   // propagate whole-VM halt up
        if (outcome == FrameOutcome.RootReload) return StepResult.RootReload; // discard every caller frame
        if (outcome == FrameOutcome.ExitRequested) throw new ProcessExitRequestedException();
        return StepResult.Next;                                      // Returned / RanOff: resume caller
    }

    // 0x143: selector slots 1..255, packed record zero
    private StepResult OpRunMountedAppendAutoruns(Instruction ins, int pc)
    {
        if (_provider == null) return StepResult.Next;

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
                return StepResult.Halt;
            }

            long id = (long)selector << 24;
            CallScriptDispatches++;
            var child = _provider.GetById(id);
            _sink.Emit(TraceEvent.CallScript(id, child?.Name));
            if (child == null)
            {
                HaltReason ??= $"append-autorun-unresolved:0x{id:x}";
                return StepResult.Halt;
            }

            int entry = child.IndexByOffset.TryGetValue(0, out int childEntry) ? childEntry : 0;
            var outcome = RunFrame(new ExecFrame(child, entry), FrameCause.CallScript, id);
            if (outcome == FrameOutcome.Halted) return StepResult.Halt;
            if (outcome == FrameOutcome.RootReload) return StepResult.RootReload;
            if (outcome == FrameOutcome.ExitRequested) throw new ProcessExitRequestedException();
        }
        return StepResult.Next;
    }

    /// <summary>Native's script frames, 0 to 39 (EngineCtx's frame table, 0x1e dwords each).</summary>
    private const int ScriptFrameSlots = 40;

    // 0x06 (script_id, frame_slot), slots 0..39. op_0x6_preload_script_slot@0x41bdb0 makes the slot the current frame
    // before its signed test: a slot above 39 throws a fatal box (flags 0x10001) whose context is read from the frame
    // outside the table, and a negative slot passes and loads below it.
    private StepResult OpPreloadScriptSlot(Instruction ins, int pc)
    {
        var a = ins.Args;
        long id = Read(a[0]);
        if (IndexRefused(ins, "frame slot", Read(a[1]), ScriptFrameSlots, IndexTest.Unchecked) is { } refused)
            return refused;
        int slot = (int)Read(a[1]);
        if (_provider == null)
        {
            HaltReason ??= $"preloaded-script-provider-unavailable:0x{id:x}";
            return StepResult.Halt;
        }
        var script = _provider.GetById(id);
        if (script == null)
        {
            HaltReason ??= $"preloaded-script-unresolved:0x{id:x}";
            return StepResult.Halt;
        }
        int entry = script.IndexByOffset.TryGetValue(0, out int loadedEntry) ? loadedEntry : 0;
        _scene.PreloadedScriptSlots[slot] = new PreloadedScriptSlot(id, new ExecFrame(script, entry));
        return StepResult.Next;
    }

    // 0x08 (frame_slot). op_0x8_call_preloaded_script_slot@0x41bf00 reads the slot's frame unchecked, and throws
    // 「この階層にはファイルが読み込まれていません．Depth=n」 with flags 0x10005 for one no script was loaded into.
    private StepResult OpCallPreloadedScriptSlot(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (IndexRefused(ins, "frame slot", Read(a[0]), ScriptFrameSlots, IndexTest.Unchecked) is { } refused)
            return refused;
        int slot = (int)Read(a[0]);
        if (!_scene.PreloadedScriptSlots.TryGetValue(slot, out var loaded))
            return YesNoScriptError(AgeBoxMessage.Keyed(AgeBoxText.EmptyScriptSlot,
                slot.ToString(System.Globalization.CultureInfo.InvariantCulture)), pc);
        if (_depth >= _o.CallDepthCap) { HaltReason ??= "call-depth-exceeded"; return StepResult.Halt; }

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
        if (outcome == FrameOutcome.Halted) return StepResult.Halt;
        if (outcome == FrameOutcome.RootReload) return StepResult.RootReload;
        if (outcome == FrameOutcome.ExitRequested) throw new ProcessExitRequestedException();
        return StepResult.Next;
    }

    // 0x1f4 stmt-begin: the outermost open withholds delayed audio starts and queued callback scripts
    private StepResult OpStmtBegin(Instruction ins, int pc)
    {
        _tick.OpenStatementBlock();
        return StepResult.Next;
    }

    // 0x1f5 stmt-end: the outermost close releases them
    private StepResult OpStmtEnd(Instruction ins, int pc)
    {
        _tick.CloseStatementBlock();
        return StepResult.Next;
    }
}
