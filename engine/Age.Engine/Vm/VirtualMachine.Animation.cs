using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepAnimation(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "u004223C0":   // 0x239 spritesheet cell: (handle)(delay)(duration)(frame count)(columns)(cell)
                Gfx.SetSrcRect(Read(a[0]), Read(a[3]), Read(a[4]), Read(a[5]), 0); return pc + 1;
            case "reset-gfx-cyclic-animations": // 0x230: stop all five retained looping channels
            case "u00421E70":
                Gfx.ResetCyclicAnimationChannels(Read(a[0])); return pc + 1;
            case "u00421EA0":   // 0x231 looping spritesheet: (handle)(ms per frame)(frame count)(columns)
                Gfx.SetSrcRect(Read(a[0]), Read(a[2]), Read(a[3]), 0, Read(a[1])); return pc + 1;
            case "u00421EF0":   // 0x232 cyclic packed ARGB; negative alpha/RGB preserve static obj color
                Gfx.SetColorAnimResolved(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            case "set-scale-cycle": // 0x233 (handle)(period ms)(target scale x/y/z percent)
                Gfx.SetScaleCycle(Read(a[0]), Read(a[1]), (Read(a[2]), Read(a[3]), Read(a[4])));
                return pc + 1;
            case "sample-frame-time": // 0x23c: previous <- current; current <- monotonic time
                Gfx.SampleFrameTime(_host.InputClockMilliseconds); return pc + 1;
            case "gfx-blit-color":      // 0x202 (handle)(delay)(duration)(alpha)(color) — one-shot color
                Gfx.SetAnimatedObjectColorResolved(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3]), Read(a[4]));
                return pc + 1;
            case "gfx-draw-color":      // 0x203 (handle)(v)(alpha)(color) — static alpha/tint
                Gfx.SetStaticObjectColorResolved(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            // ---- sprite transform / animation cluster (docs/engine-re.md "0x21c-0x243 ... ANIMATION") ----
            case "set-anim-transform-abs":   // 0x220 (handle)(delay)(duration)(tx)(ty)(tz)
                Gfx.SetTranslationChannel(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5]))); return pc + 1;
            case "set-anim-transform-norm":  // 0x21e (handle)(delay)(duration)(sx%)(sy%)(sz%)
                Gfx.SetScaleChannel(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5]))); return pc + 1;
            case "set-anim-rotation-axis-angle": // 0x21f (handle)(delay)(duration)(axis x/y/z)(angle deg)
                Gfx.SetRotationChannel(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5])), Read(a[6])); return pc + 1;
            case "anim-start":   // 0x234 legacy name: (handle)(period)(axis x/y/z), cyclic rotation channel
                Gfx.SetRotationCycle(Read(a[0]), Read(a[1]), (Read(a[2]), Read(a[3]), Read(a[4]))); return pc + 1;
            case "set-anim-clock":   // 0x238 (duration) — global, non-blocking (host advances it per-frame)
                Gfx.SetAnimClock(Read(a[0])); return pc + 1;
            case "set-object-animation-detached": // 0x242 (handle)(flags): bit 0 is nonblocking/force-proof
                Gfx.SetOneShotAnimationControl(Read(a[0]), Read(a[1])); return pc + 1;
            case "reset-anim-clock": // 0x243: force unprotected one-shots and reset the global service clock
                Gfx.ResetAnimClock(); return pc + 1;
            case "set-gfx-animation-service-flags": // 0x24e: bit 1 suppresses op 0x243
                Gfx.SetAnimationServiceFlags(Read(a[0])); return pc + 1;
            default:
                throw new InvalidOperationException($"Non-animation opcode routed to animation handler: {label}");
        }
    }
}
