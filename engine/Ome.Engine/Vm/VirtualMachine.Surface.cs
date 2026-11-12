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
        Gfx.SetSurfacePersistenceFlags(unchecked((int)Read(a[0])), Read(a[1]));
        return StepResult.Next;
    }

    // 0x1f8 (slot)(w)(h) — allocate a blank surface at the slot
    private StepResult OpCreateTexture(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.ReleaseSurface((int)Read(a[0]));
        Gfx.CreateSurface((int)Read(a[0]));
        _host.CreateTexture((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return StepResult.Next;
    }

    // 0x1f9 (resId)(slot)(colorkey) — load a file into the slot's surface
    private StepResult OpSetTexture(Instruction ins, int pc)
    {
        var a = ins.Args;
        long resourceId = Read(a[0]);
        if (_diagSetTexture)   // OME_DIAG_SETTEX: log the SLOT operand source (literal vs which global) — grey-BG slot dig
            System.Console.Error.WriteLine($"[settex] resId=0x{resourceId:x} slot={(int)Read(a[1])} " +
                $"slotOp=(type={a[1].Type} val=0x{a[1].Value:x}){(a[1].Type == 3 ? $" G[0x{a[1].Value:x}]" : "")}");
        _host.ReleaseSurface((int)Read(a[1]));
        long colorKey = a.Count > 2 ? Read(a[2]) : -1;
        Gfx.SetSurface((int)Read(a[1]), resourceId, colorKey);
        _host.SetTexture(resourceId, (int)Read(a[1]), colorKey);
        return StepResult.Next;   // host still tracks dims for get-texture-size
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
        // Native shares 0x1f9's release/load/colorkey path, but constructs its mode-1
        // surface subclass. Both texture opcodes receive the same universal packed id;
        // the CPU compositor does not need the D3D subclass distinction.
        long resourceId = Read(a[0]);
        int surfaceSlot = (int)Read(a[1]);
        _host.ReleaseSurface(surfaceSlot);
        Gfx.SetSurface(surfaceSlot, resourceId, Read(a[2]), rawTextureMode: true);
        _host.SetTexture(resourceId, surfaceSlot, Read(a[2]));
        return StepResult.Next;
    }

    // 0x1fb (handle)(slot)(srcX)(srcY)(w)(h)(dstX)(dstY) — bind object -> surface + rect + pos
    private StepResult OpDrawTexture(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.BindDraw(Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                     (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]));
        _host.DrawTexture((int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
                          (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])); return StepResult.Next;   // IHost seam (oracle log; Godot no-ops)
    }

    // 0x208 (slot) (out_w) (out_h)
    private StepResult OpGetTextureSize(Instruction ins, int pc)
    {
        var a = ins.Args;
        var (gw, gh) = _host.GetTextureSize((int)Read(a[0]));
        Write(a[1], gw); Write(a[2], gh);
        return StepResult.Next;
    }

    // 0x20b: clipped alpha/RGB fill of a mutable surface
    private StepResult OpFillSurfaceRect(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.MarkSurfaceContentWritten((int)Read(a[0]));
        _host.FillSurfaceRect(new SurfaceRectFill(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
            (int)System.Math.Min(Read(a[5]), 255), Read(a[6]) & 0x00ff_ffff));
        return StepResult.Next;
    }

    // 0x207: paired-clipped source-to-destination surface copy
    private StepResult OpCopySurfaceRect(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.MarkSurfaceContentWritten((int)Read(a[1]));
        _host.CopySurfaceRect(new SurfaceRectCopy(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
            (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])));
        return StepResult.Next;
    }

    // 0x32: independent source/destination rectangle sizes
    private StepResult OpScaleCopySurfaceRect(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.MarkSurfaceContentWritten((int)Read(a[1]));
        _host.CopySurfaceRect(new SurfaceRectCopy(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
            (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
            (int)Read(a[8]), (int)Read(a[9])));
        return StepResult.Next;
    }

    // 0x20d: slot <1000 selects a surface; >=1000 restores backbuffer
    private StepResult OpSelectRenderTarget(Instruction ins, int pc)
    {
        var a = ins.Args;
        Gfx.SelectRenderTarget(Read(a[0])); return StepResult.Next;
    }

    // 0x20e: clear color to black and depth to one
    private StepResult OpClearRenderTarget(Instruction ins, int pc)
    {
        _host.ClearRenderTarget(Gfx.CurrentRenderTargetSlot); return StepResult.Next;
    }

    // 0x1fa (surface slot)
    private StepResult OpGfxElemRelease(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.ReleaseSurface((int)Read(a[0]));
        Gfx.ClearSurface((int)Read(a[0]));
        return StepResult.Next;
    }

    // 0x23d: native fixed range [42,1000)
    private StepResult OpReleaseTransientSurfaces(Instruction ins, int pc)
    {
        Gfx.ReleaseSurfaceRange(42, 1000 - 42);
        _host.ReleaseSurfaceRange(42, 1000 - 42); return StepResult.Next;
    }
}
