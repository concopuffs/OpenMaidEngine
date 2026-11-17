using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0x1f6: erase object records, but preserve surfaces
    private StepResult OpClearRetainedGfxObjects(Instruction ins, int pc)
    {
        Gfx.ClearRetainedObjects(); return StepResult.Next;
    }

    // 0x215 (out)(handle) -> slot | -1
    private StepResult OpQueryGfxObject(Instruction ins, int pc)
    {
        var a = ins.Args;
        long handle = Read(a[1]);
        int slot = Gfx.QuerySlot(handle, unboundReadsAbsent: _rules.Gfx.UnboundObjectReadsAbsent);
        if (_diagSetTexture)   // reuse the flag: show what the slot query returns (grey-BG slot dig)
            System.Console.Error.WriteLine($"[query] handle=0x{handle:x} handleOp=(type={a[1].Type} val=0x{a[1].Value:x}) " +
                $"-> QuerySlot={slot} objectPresent={Gfx.TryGet(handle) != null}");
        Write(a[0], slot); return StepResult.Next;
    }

    // 0x216 (out)(surface slot) -> packed resource id or -1
    private StepResult OpQueryGfxField(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Native reads the live records, then the page snapshot after them, unchecked. Slot -1, which scripts pass
        // for op 0x215's "no such object", reads a word nothing writes (GfxState.QueryField); others outside the two
        // tables read unrelated memory.
        long record = Read(a[1]);
        if (record != -1 && IndexRefused(ins, "surface record", record, 2 * GfxState.SurfaceRecordCount,
                                         IndexTest.Unchecked) is { } refused)
            return refused;
        Write(a[0], Gfx.QueryField(record)); return StepResult.Next;
    }

    // 0x218 (handle)(outA)(outB)(outC) <- V18
    private StepResult OpGetGfxGeom3(Instruction ins, int pc)
    {
        var a = ins.Args;
        var v = Gfx.TryGet(Read(a[0]))?.V18 ?? default;
        WriteNumber(ins, 1, v.X); WriteNumber(ins, 2, v.Y); WriteNumber(ins, 3, v.Z); return StepResult.Next;
    }

    // 0x21a (handle)(outA)(outB)(outC) <- V24
    private StepResult OpGetGfxGeom3B(Instruction ins, int pc)
    {
        var a = ins.Args;
        var v = Gfx.TryGet(Read(a[0]))?.V24 ?? default;
        WriteNumber(ins, 1, v.X); WriteNumber(ins, 2, v.Y); WriteNumber(ins, 3, v.Z); return StepResult.Next;
    }

    // 0x217 (handle)(a)(b)(c) -> V18
    private StepResult OpSetGfxGeom3(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetObjectAnchor(Read(a[0]), (ReadNumber(ins, 1), ReadNumber(ins, 2), ReadNumber(ins, 3))); return StepResult.Next;
    }

    // 0x219 (handle)(a)(b)(c) -> V24
    private StepResult OpSetGfxGeom3B(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetObjectPosition(Read(a[0]), (ReadNumber(ins, 1), ReadNumber(ins, 2), ReadNumber(ins, 3))); return StepResult.Next;
    }

    // 0x80: select the text manager's current layout (text_manager+0x4c8, EngineCtx+0x14e08), which slot 0 selects in
    // the layout ops and op 0x1d9's object slot 0 substitutes
    private StepResult OpSetDefaultGfxObjectSlot(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (LayoutSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        _text.History.SelectCurrentLayout((int)Read(a[0])); return StepResult.Next;
    }

    // 0x214 (first handle)(second handle)
    private StepResult OpSwapGfxObjects(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SwapObjects(Read(a[0]), Read(a[1])); return StepResult.Next;
    }

    // ---- SC0000 anim/transform/spritesheet cluster (docs/engine-re.md §"SC0000 anim ... cluster") ----
    // 0x22f (delay)(duration)(x)(y)(z)
    private StepResult OpSetGfxRangeTranslationTarget(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetRangeTranslationChannel(Read(a[0]), Read(a[1]),
            (ReadNumber(ins, 2), ReadNumber(ins, 3), ReadNumber(ins, 4))); return StepResult.Next;
    }

    // 0x229 (first)(count)(anchor x/y/z)
    private StepResult OpSetGfxRangeTransform(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetRangeTransform(Read(a[0]), Read(a[1]), (ReadNumber(ins, 2), ReadNumber(ins, 3), ReadNumber(ins, 4)));
        return StepResult.Next;
    }

    // 0x22a (sx%)(sy%)(sz%)
    private StepResult OpSetGfxRangeScaleCurrent(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetRangeScaleCurrent((ReadNumber(ins, 0), ReadNumber(ins, 1), ReadNumber(ins, 2))); return StepResult.Next;
    }

    // 0x22c (tx)(ty)(tz)
    private StepResult OpSetGfxRangeTranslationCurrent(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetRangeTranslationCurrent((ReadNumber(ins, 0), ReadNumber(ins, 1), ReadNumber(ins, 2))); return StepResult.Next;
    }

    // 0x22d (delay)(duration)(sx%)(sy%)(sz%)
    private StepResult OpSetGfxRangeScaleTarget(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetRangeScaleChannel(Read(a[0]), Read(a[1]), (ReadNumber(ins, 2), ReadNumber(ins, 3), ReadNumber(ins, 4)));
        return StepResult.Next;
    }

    // 0x22e (delay)(duration)(axis x/y/z)(angle degrees)
    private StepResult OpSetGfxRangeRotationTarget(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetRangeRotationChannel(Read(a[0]), Read(a[1]),
            (ReadNumber(ins, 2), ReadNumber(ins, 3), ReadNumber(ins, 4)), ReadNumber(ins, 5));
        return StepResult.Next;
    }

    // 0x228: (succ)(handle)(outX)(outY)(outZ) <- current matrix
    private StepResult OpQueryGfxTranslationCurrent(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (Gfx.TryQueryTranslationCurrent(Read(a[1]), out var v))
        {
            WriteNumber(ins, 2, v.X); WriteNumber(ins, 3, v.Y); WriteNumber(ins, 4, v.Z);
            Write(a[0], 0);
        }
        else Write(a[0], 1);   // native missing-object path leaves output operands untouched
        return StepResult.Next;
    }

    // 0x227: (failure)(handle)(out axis x/y/z)(out angle)
    private StepResult OpQueryGfxRotationCurrent(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (Gfx.TryQueryRotationCurrent(Read(a[1]), out var v))
        {
            // SYS4433 writes floats (vm_operand_write_float), SYS4422 integers. Both truncate toward zero
            // into integer cells, which every corpus site passes.
            WriteNumber(ins, 2, v.X); WriteNumber(ins, 3, v.Y); WriteNumber(ins, 4, v.Z);
            WriteNumber(ins, 5, v.Angle); Write(a[0], 0);
        }
        else Write(a[0], 1);   // native missing-object path leaves output operands untouched
        return StepResult.Next;
    }

    // 0x1ff: set current translation matrix
    private StepResult OpSetCurrentTranslation(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetCurrentTranslation(Read(a[0]), (ReadNumber(ins, 1), ReadNumber(ins, 2), ReadNumber(ins, 3))); return StepResult.Next;
    }

    // 0x1fd (handle)(sx%)(sy%)(sz%) -> current scale matrix
    private StepResult OpGfxSetScaleCurrent(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetCurrentScale(Read(a[0]), (ReadNumber(ins, 1), ReadNumber(ins, 2), ReadNumber(ins, 3))); return StepResult.Next;
    }

    // 0x1fe (handle)(axis x/y/z)(angle degrees)
    private StepResult OpSetCurrentRotationAxisAngle(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetCurrentRotation(Read(a[0]), (ReadNumber(ins, 1), ReadNumber(ins, 2), ReadNumber(ins, 3)), ReadNumber(ins, 4));
        return StepResult.Next;
    }

    // 0x1f7 (handle)(count) — erase retained-object range
    private StepResult OpGfxElemErase(Instruction ins, int pc)
    {
        var a = ins.Args;
        long first = Read(a[0]), count = Read(a[1]);
        Gfx.EraseRange(first, count);
        foreach (int layoutSlot in _text.History.LayoutsCoveredByTextObjectErase(first, count))
            _text.ClearRenderedAdvTextLayout(layoutSlot);
        return StepResult.Next;
    }

    // 0x21d (source handle)(destination handle)
    private StepResult OpCloneGfxObject(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.CloneObject(Read(a[0]), Read(a[1])); return StepResult.Next;
    }
}
