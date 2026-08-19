using Age.Engine.Hosting;
using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepPresentation(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "queue-surface-alpha-transition": // 0x223: target surface crossfade over two object ranges
                Gfx.QueueSurfaceAlphaTransition(Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
                    Read(a[4]), (int)Read(a[5]), Read(a[6]), Read(a[7])); return pc + 1;
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
                _host.FadeSurfaceWithBlack(
                    Gfx, (int)Read(a[0]), Read(a[1]), SurfaceBlackFadeDirection.FromBlack,
                    _messageSkipServiceActive || _host.IsMessageSkipActive);
                return pc + 1;
            case "fade-surface-out-to-black": // 0x22: blocking captured full-frame surface -> black
            case "u00418920":
                _host.FadeSurfaceWithBlack(
                    Gfx, (int)Read(a[0]), Read(a[1]), SurfaceBlackFadeDirection.ToBlack,
                    _messageSkipServiceActive || _host.IsMessageSkipActive);
                return pc + 1;
            case "fade-surface-out-to-white": // 0x24: blocking captured full-frame surface -> white
            case "u00418A90":
                _host.FadeSurfaceToWhite(
                    Gfx, (int)Read(a[0]), Read(a[1]),
                    _messageSkipServiceActive || _host.IsMessageSkipActive);
                return pc + 1;
            case "crossfade-surfaces": // 0x25: legacy full-frame surface alpha transition
            case "u00418B40":
                _host.CrossfadeSurfaces(
                    Gfx, (int)Read(a[0]), (int)Read(a[1]), Read(a[2]),
                    _messageSkipServiceActive || _host.IsMessageSkipActive);
                return pc + 1;
            case "u00418CC0": // SYS4433 0x27 upstream ABI label
            case "reveal-surface-striped":
                _host.RevealSurfaceWithPattern(
                    Gfx,
                    new SurfacePatternTransitionRequest(
                        (int)Read(a[0]), Read(a[1]), (int)Read(a[2]),
                        (SurfacePatternTransitionMode)(4 + (int)Read(a[3]))),
                    _messageSkipServiceActive || _host.IsMessageSkipActive || Read(a[2]) <= 0);
                return pc + 1;
            case "u00418D90": // SYS4433 0x28 upstream ABI label
            case "reveal-surface-staggered-strips":
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
