using Ome.Engine.Hosting;
using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    private StepResult OpSetDisplayBackgroundColor(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetDisplayBackgroundColor(Read(a[0]));
        return StepResult.Next;
    }

    private StepResult OpScriptEntry(Instruction ins, int pc)
    {
        Gfx.ClearSurfaceReloadPolicies();
        return StepResult.Next;
    }

    // 0x258 (slot)(flags): bit 0 = numbered-load reload
    private StepResult OpSetSurfacePersistenceFlags(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        Gfx.SetSurfacePersistenceFlags(unchecked((int)Read(a[0])), Read(a[1]));
        return StepResult.Next;
    }

    // 0x1f8 (slot)(w)(h) — allocate a blank surface at the slot
    private StepResult OpCreateTexture(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        _graphics.CreateSurface((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
        return StepResult.Next;
    }

    // 0x1f9 (resId)(slot)(colorkey) — load a file into the slot's surface
    private StepResult OpSetTexture(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        long resourceId = Read(a[0]);
        if (_diagSetTexture)   // OME_DIAG_SETTEX: log the SLOT operand source (literal vs which global) — grey-BG slot dig
            System.Console.Error.WriteLine($"[settex] resId=0x{resourceId:x} slot={(int)Read(a[1])} " +
                $"slotOp=(type={a[1].Type} val=0x{a[1].Value:x}){(a[1].Type == 3 ? $" G[0x{a[1].Value:x}]" : "")}");
        long colorKey = a.Count > 2 ? Read(a[2]) : -1;
        _graphics.LoadSurface((int)Read(a[1]), resourceId, colorKey);
        return StepResult.Next;
    }

    // 0x248 (edge pixels)
    private StepResult OpSetTiledSurfaceEdgeLength(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SetTiledSurfaceEdgeLength(Read(a[0]));
        return StepResult.Next;
    }

    // 0x249 (packed resource id)(slot)(colorkey)
    private StepResult OpLoadRawTextureSurface(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        // Native shares 0x1f9's release/load/colorkey path, but constructs its mode-1
        // surface subclass. Both texture opcodes receive the same universal packed id;
        // the CPU compositor does not need the D3D subclass distinction.
        long resourceId = Read(a[0]);
        int surfaceSlot = (int)Read(a[1]);
        _graphics.LoadSurface(surfaceSlot, resourceId, Read(a[2]), rawTextureMode: true);
        return StepResult.Next;
    }

    // 0x1fb (handle)(slot)(srcX)(srcY)(w)(h)(dstX)(dstY) — bind object -> surface + rect + pos
    private StepResult OpDrawTexture(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (UnclassifiedSurfaceSlot(ins, 2) is { } error) return YesNoScriptError(error, pc);
        _graphics.DrawTexture(Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                              (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]));
        return StepResult.Next;
    }

    // 0x208 (slot) (out_w) (out_h)
    private StepResult OpGetTextureSize(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Slot -1, op 0x215's "no such object", reads 0 x 0. HIDEWIN.BIN passes op 0x215's answer for object 0xcb2a
        // without testing it (both games), Kamidori runs it on every hide, and 4.33G answers -1 once a scene has
        // erased that object. What native's test makes of -1 is not read (docs/backlog.md, "Native index tests for 28
        // ops").
        if (Read(a[0]) != -1 && UnclassifiedSurfaceSlot(ins, 1) is { } error) return YesNoScriptError(error, pc);
        var (gw, gh) = _graphics.SurfaceSize((int)Read(a[0]));
        Write(a[1], gw); Write(a[2], gh);
        return StepResult.Next;
    }

    // 0x20b: clipped alpha/RGB fill of a mutable surface
    private StepResult OpFillSurfaceRect(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        _graphics.FillSurfaceRect(new SurfaceRectFill(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
            (int)System.Math.Min(Read(a[5]), 255), Read(a[6]) & 0x00ff_ffff));
        return StepResult.Next;
    }

    // 0x207: paired-clipped source-to-destination surface copy
    private StepResult OpCopySurfaceRect(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        if (SurfaceSlotRefused(ins, Read(a[1])) is { } refused2) return refused2;
        _graphics.CopySurfaceRect(new SurfaceRectCopy(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
            (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])));
        return StepResult.Next;
    }

    // 0x32: independent source/destination rectangle sizes
    private StepResult OpScaleCopySurfaceRect(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        if (SurfaceSlotRefused(ins, Read(a[1])) is { } refused2) return refused2;
        _graphics.ScaleCopySurfaceRect(new SurfaceRectCopy(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
            (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
            (int)Read(a[8]), (int)Read(a[9])));
        return StepResult.Next;
    }

    // 0x20d: slot <1000 selects a surface; >=1000 restores backbuffer
    private StepResult OpSelectRenderTarget(Instruction ins, int pc)
    {
        var a = ins.Args;
        // The port selects the backbuffer for any slot outside 0..999: 1000 and up, as native does
        // (vm-map/opcodes.toml), and -1, which both games' retail scripts pass (a Himegari save load and a Kamidori new
        // game reach one). What native's test makes of -1 is not read (docs/backlog.md, "Native index tests for 28
        // ops").
        Gfx.SelectRenderTarget(Read(a[0])); return StepResult.Next;
    }

    // 0x20e: clear color to black and depth to one
    private StepResult OpClearRenderTarget(Instruction ins, int pc)
    {
        _graphics.ClearRenderTarget(); return StepResult.Next;
    }

    // 0x1fa (surface slot)
    private StepResult OpGfxElemRelease(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        _graphics.ReleaseSurface((int)Read(a[0]));
        return StepResult.Next;
    }

    // 0x23d: native fixed range [42,1000)
    private StepResult OpReleaseTransientSurfaces(Instruction ins, int pc)
    {
        _graphics.ReleaseSurfaceRange(42, 1000 - 42); return StepResult.Next;
    }
}
