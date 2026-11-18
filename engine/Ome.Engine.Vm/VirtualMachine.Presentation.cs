using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // Under DRAWMODE=1 (both games), screen_transition_begin@0x439da0 creates scratch surface 0x20 for the black and
    // white fades (modes 0-3) and for modes 5-7 and 9, which sets that record's created flag. The crossfade (mode 4)
    // and the strip reveals 0x27/0x28 composite their surfaces directly. screen_transition_finalize@0x4399c0, the
    // fast-forward path, fills 0x20 only for a negative surface, the black or white endpoint (docs/engine-re.md,
    // "Legacy transitions and scratch surface 0x20").
    private void MarkLegacyTransitionScratchWritten() => Gfx.MarkSurfaceContentWritten(0x20);

    private void MarkScratchIfFinalizeFills(long surface)
    {
        if (surface < 0) MarkLegacyTransitionScratchWritten();
    }

    // 0x223: target surface crossfade over two object ranges
    private StepResult OpQueueSurfaceAlphaTransition(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        Gfx.RecordTransitionTargetCreated((int)Read(a[1]));   // gfx_queue_surface_alpha_transition
        Gfx.QueueSurfaceAlphaTransition(Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
            Read(a[4]), (int)Read(a[5]), Read(a[6]), Read(a[7])); return StepResult.Next;
    }

    // 0x24f: type-2 two-range wipe/strip command
    private StepResult OpQueuePatternedSurfaceTransition(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        Gfx.RecordTransitionTargetCreated((int)Read(a[1]));   // gfx_queue_patterned_surface_transition
        Gfx.QueuePatternedSurfaceTransition(
            Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
            Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
            Read(a[8]), Read(a[9]));
        return StepResult.Next;
    }

    private StepResult OpQueueDirectionalBlurRangeTransition(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.QueueDirectionalBlurRangeTransition(
            Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
            Read(a[4]), Read(a[5]), Read(a[6]), Read(a[7]), Read(a[8]), Read(a[9]));
        return StepResult.Next;
    }

    private StepResult OpQueueRadialBlurRangeTransition(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.QueueRadialBlurRangeTransition(
            Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
            Read(a[4]), Read(a[5]), Read(a[6]),
            Read(a[7]), Read(a[8]), Read(a[9]), Read(a[10]), Read(a[11]));
        return StepResult.Next;
    }

    // 0x20c: read/message-skip path snaps a queued transition to its endpoint
    private StepResult OpPresentFrame(Instruction ins, int pc)
    {
        _waits.PresentFrame(Gfx); return StepResult.Next;
    }

    // 0x21: blocking black -> captured full-frame surface
    private StepResult OpFadeSurfaceInFromBlack(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Under fast-forward the handler finishes on the surface, where a negative slot is the black endpoint.
        if ((AdvFastForwardActive ? FinishedTransitionSurfaceRefused(ins, Read(a[0]))
                                  : TransitionSurfaceSlotRefused(ins, Read(a[0]))) is { } refused) return refused;
        // Begin (mode 0) fills 0x20 with black; under fast-forward the handler finalizes on the surface instead.
        if (AdvFastForwardActive) MarkScratchIfFinalizeFills(Read(a[0]));
        else MarkLegacyTransitionScratchWritten();
        DiscardHeldTriggersUnlessFastForward();   // input_flush_action_triggers as the fade starts
        _waits.FadeSurfaceWithBlack(
            Gfx, (int)Read(a[0]), Read(a[1]), SurfaceBlackFadeDirection.FromBlack,
            AdvFastForwardActive);
        return StepResult.Next;
    }

    // 0x22: blocking captured full-frame surface -> black
    private StepResult OpFadeSurfaceOutToBlack(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Under fast-forward the handler finishes on black (finalize(-1)) and never reads the slot.
        if (!AdvFastForwardActive && TransitionSurfaceSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        MarkLegacyTransitionScratchWritten();   // begin (mode 1) or, under fast-forward, finalize(-1) fills 0x20
        DiscardHeldTriggersUnlessFastForward();   // input_flush_action_triggers as the fade starts
        _waits.FadeSurfaceWithBlack(
            Gfx, (int)Read(a[0]), Read(a[1]), SurfaceBlackFadeDirection.ToBlack,
            AdvFastForwardActive);
        return StepResult.Next;
    }

    // 0x24: blocking captured full-frame surface -> white
    private StepResult OpFadeSurfaceOutToWhite(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Under fast-forward the handler finishes on white (finalize(-1)) and never reads the slot.
        if (!AdvFastForwardActive && TransitionSurfaceSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        MarkLegacyTransitionScratchWritten();   // begin (mode 3) or, under fast-forward, finalize(-1) fills 0x20
        DiscardHeldTriggersUnlessFastForward();   // input_flush_action_triggers as the fade starts
        _waits.FadeSurfaceToWhite(
            Gfx, (int)Read(a[0]), Read(a[1]),
            AdvFastForwardActive);
        return StepResult.Next;
    }

    // 0x25: legacy full-frame surface alpha transition
    private StepResult OpCrossfadeSurfaces(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Under fast-forward the handler finishes on the target alone, where a negative slot is the black endpoint;
        // otherwise mode 4 draws both.
        if (AdvFastForwardActive)
        {
            if (FinishedTransitionSurfaceRefused(ins, Read(a[1])) is { } finishRefused) return finishRefused;
        }
        else if ((TransitionSurfaceSlotRefused(ins, Read(a[0])) ?? TransitionSurfaceSlotRefused(ins, Read(a[1])))
                 is { } refused)
            return refused;
        // Mode 4 never touches 0x20; under fast-forward the handler finalizes on the target.
        if (AdvFastForwardActive) MarkScratchIfFinalizeFills(Read(a[1]));
        DiscardHeldTriggersUnlessFastForward();   // input_flush_action_triggers as the fade starts
        _waits.CrossfadeSurfaces(
            Gfx, (int)Read(a[0]), (int)Read(a[1]), Read(a[2]),
            AdvFastForwardActive);
        return StepResult.Next;
    }

    /// <summary>
    /// Ops 0x27 and 0x28 (modes <paramref name="firstMode"/> + their type): native's tests in its order, before the
    /// surface is drawn. A type outside 0 to 4 is traced and skipped. With no divisions or under fast-forward the
    /// handler finishes on the surface at once. Otherwise <c>pattern_surface_transition_begin</c> sizes the surface
    /// (tracing a slot past 999) and then refuses a mode or a division count the screen does not take, with a trace
    /// and nothing started, so the surface is never drawn; when it begins, its tick draws the slot unchecked
    /// (docs/engine-re.md, "The 28 ops' native tests").
    /// </summary>
    private StepResult? PatternRevealRefused(Instruction ins, string command, int firstMode)
    {
        var a = ins.Args;
        long type = Read(a[3]), slot = Read(a[0]), divisions = Read(a[2]);
        if (type is < 0 or > 4)
        {
            ReportSkipped(ins, "transition type", type, "0..4", $"関数：{command} エラー：Typeは0から1の間にしてください");
            return StepResult.Next;
        }
        if (AdvFastForwardActive || divisions <= 0) return FinishedTransitionSurfaceRefused(ins, slot);
        Sys4LogicalCanvas screen = _graphics.Canvas;
        if (LegacyScreenTransitionTiming.PatternBeginRefusal(
                screen.Width, screen.Height, (int)divisions, (SurfacePatternTransitionMode)(firstMode + (int)type))
            is { } refusal)
        {
            if (TextureSizeRefuses(slot))
                _waits.ReportWarning($"{At(ins)}: surface slot {slot} is outside 0..999; AGE traces 「"
                                     + $"{TextureSizeTrace(slot)}」 as it sizes the transition");
            _waits.ReportWarning($"{At(ins)}: {refusal.Reason}; skipped, as AGE skips it with the trace "
                                 + $"「{refusal.Trace}」");
            return StepResult.Next;
        }
        return TransitionSurfaceSlotRefused(ins, slot);
    }

    private StepResult OpRevealSurfaceStriped(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (PatternRevealRefused(ins, "ComEfBl", 4) is { } refused) return refused;
        // pattern_surface_transition_begin/_tick draw the surface itself; with no divisions or under fast-forward,
        // the handler finalizes on the surface.
        if (AdvFastForwardActive || Read(a[2]) <= 0)
            MarkScratchIfFinalizeFills(Read(a[0]));
        _waits.RevealSurfaceWithPattern(
            Gfx,
            new SurfacePatternTransitionRequest(
                (int)Read(a[0]), Read(a[1]), (int)Read(a[2]),
                (SurfacePatternTransitionMode)(4 + (int)Read(a[3]))),
            AdvFastForwardActive || Read(a[2]) <= 0);
        return StepResult.Next;
    }

    private StepResult OpRevealSurfaceStaggeredStrips(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (PatternRevealRefused(ins, "ComEfBl2", 8) is { } refused) return refused;
        if (AdvFastForwardActive || Read(a[2]) <= 0)
            MarkScratchIfFinalizeFills(Read(a[0]));
        _waits.RevealSurfaceWithPattern(
            Gfx,
            new SurfacePatternTransitionRequest(
                (int)Read(a[0]), Read(a[1]), (int)Read(a[2]),
                (SurfacePatternTransitionMode)(8 + (int)Read(a[3]))),
            AdvFastForwardActive || Read(a[2]) <= 0);
        return StepResult.Next;
    }

    // 0x21c: normal foreground-transition scheduler/resume boundary
    private StepResult OpMarkFrameYield(Instruction ins, int pc)
    {
        _waits.WaitForForegroundTransition(Gfx); return StepResult.Next;
    }

    // 0x224: retained compositor does not use this native queue
    private StepResult OpClearGfxCommandQueue(Instruction ins, int pc)
    {
        return StepResult.Next;
    }

    // 0x222: publish pending retained changes in the selected range
    private StepResult OpPresentGfxObjectRange(Instruction ins, int pc)
    {
        var a = ins.Args;
        _waits.PresentObjectRange(Gfx, Read(a[0]), Read(a[1])); return StepResult.Next;
    }

    // 0x21b
    private StepResult OpSetTickPresentation(Instruction ins, int pc)
    {
        _scene.TickPresentationEnabled = Read(ins.Args[0]) != 0;
        return StepResult.Next;
    }

    // 0x247
    private StepResult OpQueryTickPresentation(Instruction ins, int pc)
    {
        Write(ins.Args[0], _scene.TickPresentationEnabled ? 1 : 0);
        return StepResult.Next;
    }
}
