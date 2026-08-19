using Age.Engine.Hosting;
using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepSurface(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "u00423120": // opcode 0x25a
            case "set-display-background-color":
                Gfx.SetDisplayBackgroundColor(Read(a[0]));
                return pc + 1;
            case "script-entry":
                Gfx.ClearSurfaceReloadPolicies();
                return pc + 1;
            case "set-surface-persistence-flags": // 0x258 (slot)(flags): bit 0 = numbered-load reload
                Gfx.SetSurfaceReloadOnRestore(unchecked((int)Read(a[0])), (Read(a[1]) & 1) != 0);
                return pc + 1;
            case "create-texture":   // 0x1f8 (slot)(w)(h) — allocate a blank surface at the slot
                _host.ReleaseSurface((int)Read(a[0]));
                Gfx.CreateSurface((int)Read(a[0]));
                _host.CreateTexture((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "set-texture":   // 0x1f9 (resId)(slot)(colorkey) — load a file into the slot's surface
            {
                long resourceId = Read(a[0]);
                if (_diagSetTexture)   // AGE_DIAG_SETTEX: log the SLOT operand source (literal vs which global) — grey-BG slot dig
                    System.Console.Error.WriteLine($"[settex] resId=0x{resourceId:x} slot={(int)Read(a[1])} " +
                        $"slotOp=(type={a[1].Type} val=0x{a[1].Value:x}){(a[1].Type == 3 ? $" G[0x{a[1].Value:x}]" : "")}");
                _host.ReleaseSurface((int)Read(a[1]));
                long colorKey = a.Count > 2 ? Read(a[2]) : -1;
                Gfx.SetSurface((int)Read(a[1]), resourceId, colorKey);
                _host.SetTexture(resourceId, (int)Read(a[1]), colorKey);
                return pc + 1;   // host still tracks dims for get-texture-size
            }
            case "u00422E80":                         // pre-reference compatibility
            case "set-tiled-surface-edge-length":    // 0x248 (edge pixels)
                Gfx.SetTiledSurfaceEdgeLength(Read(a[0]));
                return pc + 1;
            case "u00422EB0":                 // pre-reference compatibility
            case "load-raw-texture-surface":  // 0x249 (packed resource id)(slot)(colorkey)
            {
                // Native shares 0x1f9's release/load/colorkey path, but constructs its mode-1
                // surface subclass. Both texture opcodes receive the same universal packed id;
                // the CPU compositor does not need the D3D subclass distinction.
                long resourceId = Read(a[0]);
                int surfaceSlot = (int)Read(a[1]);
                _host.ReleaseSurface(surfaceSlot);
                Gfx.SetSurface(surfaceSlot, resourceId, Read(a[2]));
                _host.SetTexture(resourceId, surfaceSlot, Read(a[2]));
                return pc + 1;
            }
            case "draw-texture":   // 0x1fb (handle)(slot)(srcX)(srcY)(w)(h)(dstX)(dstY) — bind object -> surface + rect + pos
                Gfx.BindDraw(Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                             (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]));
                _host.DrawTexture((int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
                                  (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])); return pc + 1;   // IHost seam (oracle log; Godot no-ops)
            case "get-texture-size":   // 0x208 (slot) (out_w) (out_h)
            {
                var (gw, gh) = _host.GetTextureSize((int)Read(a[0]));
                Write(a[1], gw); Write(a[2], gh);
                return pc + 1;
            }
            case "fill-surface-rect": // 0x20b: clipped alpha/RGB fill of a mutable surface
            case "u00420D50":
                _host.FillSurfaceRect(new SurfaceRectFill(
                    (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
                    (int)System.Math.Min(Read(a[5]), 255), Read(a[6]) & 0x00ff_ffff));
                return pc + 1;
            case "copy-surface-rect": // 0x207: paired-clipped source-to-destination surface copy
                _host.CopySurfaceRect(new SurfaceRectCopy(
                    (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                    (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])));
                return pc + 1;
            case "select-render-target": // 0x20d: slot <1000 selects a surface; >=1000 restores backbuffer
                Gfx.SelectRenderTarget(Read(a[0])); return pc + 1;
            case "clear-render-target": // 0x20e: clear color to black and depth to one
                _host.ClearRenderTarget(Gfx.CurrentRenderTargetSlot); return pc + 1;
            case "gfx-elem-release":    // 0x1fa (surface slot)
                _host.ReleaseSurface((int)Read(a[0]));
                Gfx.ClearSurface((int)Read(a[0]));
                return pc + 1;
            case "release-transient-surfaces": // 0x23d: native fixed range [42,1000)
                Gfx.ReleaseSurfaceRange(42, 1000 - 42);
                _host.ReleaseSurfaceRange(42, 1000 - 42); return pc + 1;
            default:
                throw new InvalidOperationException($"Non-surface opcode routed to surface handler: {label}");
        }
    }
}
