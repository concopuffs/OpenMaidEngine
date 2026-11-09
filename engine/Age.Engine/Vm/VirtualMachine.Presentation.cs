using Age.Engine.Hosting;
using Age.Engine.Model;

namespace Age.Engine.Vm;

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

    private int StepPresentation(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "queue-surface-alpha-transition": // 0x223: target surface crossfade over two object ranges
                Gfx.RecordTransitionTargetCreated((int)Read(a[1]));   // gfx_queue_surface_alpha_transition
                Gfx.QueueSurfaceAlphaTransition(Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
                    Read(a[4]), (int)Read(a[5]), Read(a[6]), Read(a[7])); return pc + 1;
            case "u00422ED0": // shared SYS4422/SYS4433 upstream ABI label
            case "queue-patterned-surface-transition": // 0x24f: type-2 two-range wipe/strip command
                Gfx.RecordTransitionTargetCreated((int)Read(a[1]));   // gfx_queue_patterned_surface_transition
                Gfx.QueuePatternedSurfaceTransition(
                    Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
                    Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
                    Read(a[8]), Read(a[9]));
                return pc + 1;
            case "u00422F60": // SYS4433 0x250 upstream ABI label
            case "queue-directional-blur-range-transition":
                Gfx.QueueDirectionalBlurRangeTransition(
                    Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
                    Read(a[4]), Read(a[5]), Read(a[6]), Read(a[7]), Read(a[8]), Read(a[9]));
                return pc + 1;
            case "u00422FF0": // SYS4433 0x251 upstream ABI label
            case "queue-radial-blur-range-transition":
                Gfx.QueueRadialBlurRangeTransition(
                    Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
                    Read(a[4]), Read(a[5]), Read(a[6]),
                    Read(a[7]), Read(a[8]), Read(a[9]), Read(a[10]), Read(a[11]));
                return pc + 1;
            case "present-frame": // 0x20c: read/message-skip path snaps a queued transition to its endpoint
                _host.PresentFrame(Gfx); return pc + 1;
            case "fade-surface-in-from-black": // 0x21: blocking black -> captured full-frame surface
            case "u00418860":
                // Begin (mode 0) fills 0x20 with black; under fast-forward the handler finalizes on the surface instead.
                if (AdvFastForwardActive) MarkScratchIfFinalizeFills(Read(a[0]));
                else MarkLegacyTransitionScratchWritten();
                DiscardHeldTriggersUnlessFastForward();   // input_flush_action_triggers as the fade starts
                _host.FadeSurfaceWithBlack(
                    Gfx, (int)Read(a[0]), Read(a[1]), SurfaceBlackFadeDirection.FromBlack,
                    _messageSkipServiceActive || _host.IsMessageSkipActive);
                return pc + 1;
            case "fade-surface-out-to-black": // 0x22: blocking captured full-frame surface -> black
            case "u00418920":
                MarkLegacyTransitionScratchWritten();   // begin (mode 1) or, under fast-forward, finalize(-1) fills 0x20
                DiscardHeldTriggersUnlessFastForward();   // input_flush_action_triggers as the fade starts
                _host.FadeSurfaceWithBlack(
                    Gfx, (int)Read(a[0]), Read(a[1]), SurfaceBlackFadeDirection.ToBlack,
                    _messageSkipServiceActive || _host.IsMessageSkipActive);
                return pc + 1;
            case "fade-surface-out-to-white": // 0x24: blocking captured full-frame surface -> white
            case "u00418A90":
                MarkLegacyTransitionScratchWritten();   // begin (mode 3) or, under fast-forward, finalize(-1) fills 0x20
                DiscardHeldTriggersUnlessFastForward();   // input_flush_action_triggers as the fade starts
                _host.FadeSurfaceToWhite(
                    Gfx, (int)Read(a[0]), Read(a[1]),
                    _messageSkipServiceActive || _host.IsMessageSkipActive);
                return pc + 1;
            case "crossfade-surfaces": // 0x25: legacy full-frame surface alpha transition
            case "u00418B40":
                // Mode 4 never touches 0x20; under fast-forward the handler finalizes on the target.
                if (AdvFastForwardActive) MarkScratchIfFinalizeFills(Read(a[1]));
                DiscardHeldTriggersUnlessFastForward();   // input_flush_action_triggers as the fade starts
                _host.CrossfadeSurfaces(
                    Gfx, (int)Read(a[0]), (int)Read(a[1]), Read(a[2]),
                    _messageSkipServiceActive || _host.IsMessageSkipActive);
                return pc + 1;
            case "u00418CC0": // SYS4433 0x27 upstream ABI label
            case "reveal-surface-striped":
                // pattern_surface_transition_begin/_tick draw the surface itself; with a valid selector but no divisions
                // or under fast-forward, the handler finalizes on the surface.
                if (Read(a[3]) is >= 0 and <= 4 && (AdvFastForwardActive || Read(a[2]) <= 0))
                    MarkScratchIfFinalizeFills(Read(a[0]));
                _host.RevealSurfaceWithPattern(
                    Gfx,
                    new SurfacePatternTransitionRequest(
                        (int)Read(a[0]), Read(a[1]), (int)Read(a[2]),
                        (SurfacePatternTransitionMode)(4 + (int)Read(a[3]))),
                    _messageSkipServiceActive || _host.IsMessageSkipActive || Read(a[2]) <= 0);
                return pc + 1;
            case "u00418D90": // SYS4433 0x28 upstream ABI label
            case "reveal-surface-staggered-strips":
                if (Read(a[3]) is >= 0 and <= 4 && (AdvFastForwardActive || Read(a[2]) <= 0))
                    MarkScratchIfFinalizeFills(Read(a[0]));
                _host.RevealSurfaceWithPattern(
                    Gfx,
                    new SurfacePatternTransitionRequest(
                        (int)Read(a[0]), Read(a[1]), (int)Read(a[2]),
                        (SurfacePatternTransitionMode)(8 + (int)Read(a[3]))),
                    _messageSkipServiceActive || _host.IsMessageSkipActive || Read(a[2]) <= 0);
                return pc + 1;
            case "mark-frame-yield": // 0x21c: normal foreground-transition scheduler/resume boundary
                _host.WaitForForegroundTransition(Gfx); return pc + 1;
            case "clear-gfx-command-queue": // 0x224: retained compositor does not use this native queue
                return pc + 1;
            case "present-gfx-object-range": // 0x222: publish pending retained changes in the selected range
            case "u004216C0":
                _host.PresentObjectRange(Gfx, Read(a[0]), Read(a[1])); return pc + 1;
            default:
                throw new InvalidOperationException($"Non-presentation opcode routed to presentation handler: {label}");
        }
    }
}
