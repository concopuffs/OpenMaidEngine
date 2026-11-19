using Ome.Engine.Model;

namespace Ome.Engine.Vm;

// AGE 4.38D's polygon objects, ops 0x320 to 0x323 (docs/superpowers/specs/2026-11-19-polygon-objects-design.md).
// Each handler reads its operands as native's does and calls the graphics state, which keeps the polygon map.
public sealed partial class VirtualMachine
{
    // 0x320 (handle)(xs)(ys)(zs)(alphas)(colors)(us)(vs)(count)(flags): build a polygon from seven arrays.
    // op_0x320_create_polygon@0x42e4a0 resolves each array operand's address and reads count cells from it: float
    // cells for the positions and texture coordinates, integer cells for each vertex's alpha and RGB.
    private StepResult OpCreatePolygon(Instruction ins, int pc)
    {
        var a = ins.Args;
        int count = unchecked((int)Read(a[8]));
        if (count < 1)
        {
            _waits.ReportWarning($"{At(ins)}: vertex count {count} is below 1; skipped, as AGE skips it with the "
                                 + $"trace 「頂点数{count}は不正です．」");
            return StepResult.Next;
        }
        if (count > MaxScriptRunLength)
        {
            HaltReason ??= $"create-polygon-count-too-large:{count}";
            return StepResult.Halt;
        }
        VmAddress alphas = BaseAddr(a[4]), colors = BaseAddr(a[5]), us = BaseAddr(a[6]), vs = BaseAddr(a[7]);
        VmAddress zs = BaseAddr(a[3]), ys = BaseAddr(a[2]), xs = BaseAddr(a[1]);
        var vertices = new GfxState.PolygonVertex[count];
        for (int index = 0; index < count; index++)
        {
            uint alpha = unchecked((uint)ReadIntCell(alphas.Offset(index)));
            uint color = unchecked((uint)ReadIntCell(colors.Offset(index)));
            vertices[index] = new GfxState.PolygonVertex(
                FloatAt(xs, index), FloatAt(ys, index), FloatAt(zs, index),
                (alpha << 24) | (color & 0xffffff), FloatAt(us, index), FloatAt(vs, index));
        }
        Gfx.CreatePolygon(Read(a[0]), vertices, Read(a[9]));
        return StepResult.Next;

        float FloatAt(VmAddress array, int index)
            => BitConverter.Int32BitsToSingle(unchecked((int)ReadFloatCell(array.Offset(index))));
    }

    // 0x321 (handle)(stage)(surface slot): the surface of texture stage 0 or 1; a negative slot is none. Native
    // writes at record +0x1c + stage * 4 with no test, and the draw indexes the surface table with the slot.
    private StepResult OpSetPolygonTexture(Instruction ins, int pc)
    {
        var a = ins.Args;
        long stage = Read(a[1]);
        if (IndexRefused(ins, "texture stage", stage, 2, IndexTest.Unchecked) is { } refusedStage) return refusedStage;
        int slot = unchecked((int)Read(a[2]));
        if (slot >= 0 && SurfaceSlotRefused(ins, slot) is { } refusedSlot) return refusedSlot;
        Gfx.SetPolygonTexture(Read(a[0]), (int)stage, slot);
        return StepResult.Next;
    }

    // 0x322 (handle)(blend mode)(alpha)(rgb): the polygon's colour and blend mode
    private StepResult OpSetPolygonColor(Instruction ins, int pc)
    {
        var a = ins.Args;
        long handle = Read(a[0]);
        uint color = Gfx.ResolvePolygonColor(handle, Read(a[2]), Read(a[3]));
        Gfx.SetPolygonColor(handle, unchecked((int)Read(a[1])), color);
        return StepResult.Next;
    }

    // 0x323 (handle)(delay ms)(duration ms)(alpha)(rgb): fade the polygon's colour to a target
    private StepResult OpFadePolygonColor(Instruction ins, int pc)
    {
        var a = ins.Args;
        long handle = Read(a[0]);
        uint target = Gfx.ResolvePolygonColor(handle, Read(a[3]), Read(a[4]));
        Gfx.FadePolygonColor(handle, Read(a[1]), Read(a[2]), target);
        return StepResult.Next;
    }
}
