using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0x239 one-shot spritesheet: (handle)(delay)(duration)(frame count)(columns)(flags)
    private StepResult OpAnimateGfxSrcrectTarget(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetSrcRectOneShot(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3]), Read(a[4]), Read(a[5]));
        return StepResult.Next;
    }

    // 0x230: stop all five retained looping channels
    private StepResult OpResetGfxCyclicAnimations(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.ResetCyclicAnimationChannels(Read(a[0])); return StepResult.Next;
    }

    // 0x231 looping spritesheet: (handle)(ms per frame)(frame count)(columns)
    private StepResult OpAnimateGfxSrcrectLoop(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetSrcRect(Read(a[0]), Read(a[2]), Read(a[3]), Read(a[1])); return StepResult.Next;
    }

    // 0x232 cyclic packed ARGB; negative alpha/RGB preserve static obj color
    private StepResult OpAnimateGfxColorLoop(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetColorAnimResolved(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3])); return StepResult.Next;
    }

    // 0x233 (handle)(period ms)(target scale x/y/z percent)
    private StepResult OpSetScaleCycle(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetScaleCycle(Read(a[0]), Read(a[1]), (ReadNumber(ins, 2), ReadNumber(ins, 3), ReadNumber(ins, 4)));
        return StepResult.Next;
    }

    // 0x235 (handle)(period ms)(target translation x/y/z)
    private StepResult OpSetTranslationCycle(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetTranslationCycle(Read(a[0]), Read(a[1]), (ReadNumber(ins, 2), ReadNumber(ins, 3), ReadNumber(ins, 4)));
        return StepResult.Next;
    }

    // 0x23c: previous <- current; current <- monotonic time
    private StepResult OpSampleFrameTime(Instruction ins, int pc)
    {
        Gfx.SampleFrameTime(_waits.InputClockMilliseconds); return StepResult.Next;
    }

    // 0x202 (handle)(delay)(duration)(alpha)(color) — one-shot color
    private StepResult OpGfxBlitColor(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetAnimatedObjectColorResolved(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3]), Read(a[4]));
        return StepResult.Next;
    }

    // 0x203 (handle)(v)(alpha)(color) — static alpha/tint
    private StepResult OpGfxDrawColor(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetStaticObjectColorResolved(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3])); return StepResult.Next;
    }

    // ---- sprite transform / animation cluster (docs/engine-re.md "0x21c-0x243 ... ANIMATION") ----
    // 0x220 (handle)(delay)(duration)(tx)(ty)(tz)
    private StepResult OpSetAnimTransformAbs(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetTranslationChannel(Read(a[0]), Read(a[1]), Read(a[2]),
            (ReadNumber(ins, 3), ReadNumber(ins, 4), ReadNumber(ins, 5))); return StepResult.Next;
    }

    // 0x21e (handle)(delay)(duration)(sx%)(sy%)(sz%)
    private StepResult OpSetAnimTransformNorm(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetScaleChannel(Read(a[0]), Read(a[1]), Read(a[2]),
            (ReadNumber(ins, 3), ReadNumber(ins, 4), ReadNumber(ins, 5))); return StepResult.Next;
    }

    // 0x21f (handle)(delay)(duration)(axis x/y/z)(angle deg)
    private StepResult OpSetAnimRotationAxisAngle(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetRotationChannel(Read(a[0]), Read(a[1]), Read(a[2]),
            (ReadNumber(ins, 3), ReadNumber(ins, 4), ReadNumber(ins, 5)), ReadNumber(ins, 6)); return StepResult.Next;
    }

    private StepResult OpAnimStart(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetRotationCycle(Read(a[0]), Read(a[1]), (ReadNumber(ins, 2), ReadNumber(ins, 3), ReadNumber(ins, 4))); return StepResult.Next;
    }

    // 0x238 (duration) — global, non-blocking (host advances it per-frame)
    private StepResult OpSetAnimClock(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetAnimClock(Read(a[0])); return StepResult.Next;
    }

    // 0x242 (handle)(flags): bit 0 is nonblocking/force-proof
    private StepResult OpSetObjectAnimationDetached(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetOneShotAnimationControl(Read(a[0]), Read(a[1])); return StepResult.Next;
    }

    // 0x243: force unprotected one-shots and reset the global service clock
    private StepResult OpResetAnimClock(Instruction ins, int pc)
    {
        Gfx.ResetAnimClock(); return StepResult.Next;
    }

    // 0x24e: bit 1 suppresses op 0x243
    private StepResult OpSetGfxAnimationServiceFlags(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetAnimationServiceFlags(Read(a[0])); return StepResult.Next;
    }
}
