using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepRetainedObject(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "clear-retained-gfx-objects": // 0x1f6: erase object records, but preserve surfaces
                Gfx.ClearRetainedObjects(); return pc + 1;
            case "query-gfx-object?":   // 0x215 (out)(handle) -> slot | -1
                if (_diagSetTexture)   // reuse the flag: show what the slot query returns (grey-BG slot dig)
                {
                    long h = Read(a[1]);
                    System.Console.Error.WriteLine($"[query] handle=0x{h:x} handleOp=(type={a[1].Type} val=0x{a[1].Value:x}) " +
                        $"-> QuerySlot={Gfx.QuerySlot(h)} objectPresent={Gfx.TryGet(h) != null}");
                }
                Write(a[0], Gfx.QuerySlot(Read(a[1]))); return pc + 1;
            case "query-gfx-field?":    // 0x216 (out)(idx)
                Write(a[0], Gfx.QueryField(Read(a[1]))); return pc + 1;
            case "get-gfx-geom3?":      // 0x218 (handle)(outA)(outB)(outC) <- V18
            {
                var v = Gfx.TryGet(Read(a[0]))?.V18 ?? default;
                Write(a[1], v.X); Write(a[2], v.Y); Write(a[3], v.Z); return pc + 1;
            }
            case "get-gfx-geom3-b?":    // 0x21a (handle)(outA)(outB)(outC) <- V24
            {
                var v = Gfx.TryGet(Read(a[0]))?.V24 ?? default;
                Write(a[1], v.X); Write(a[2], v.Y); Write(a[3], v.Z); return pc + 1;
            }
            case "set-gfx-geom3":       // 0x217 (handle)(a)(b)(c) -> V18
                Gfx.SetObjectAnchor(Read(a[0]), (Read(a[1]), Read(a[2]), Read(a[3]))); return pc + 1;
            case "set-gfx-geom3-b":     // 0x219 (handle)(a)(b)(c) -> V24
                Gfx.SetObjectPosition(Read(a[0]), (Read(a[1]), Read(a[2]), Read(a[3]))); return pc + 1;
            case "u0041AF00":           // 0x80: default object slot substituted by native op 0x1d9
            case "set-default-gfx-object-slot":
                Gfx.SetDefaultObjectSlot((int)Read(a[0])); return pc + 1;

            // ---- SC0000 anim/transform/spritesheet cluster (docs/engine-re.md §"SC0000 anim ... cluster") ----
            case "u00421DD0":                         // pre-reference compatibility
            case "set-gfx-range-translation-target": // 0x22f (delay)(duration)(x)(y)(z)
                Gfx.SetRangeTranslationChannel(Read(a[0]), Read(a[1]),
                    (Read(a[2]), Read(a[3]), Read(a[4]))); return pc + 1;
            case "u004219E0":                  // pre-reference compatibility
            case "set-gfx-range-transform":   // 0x229 (first)(count)(anchor x/y/z)
                Gfx.SetRangeTransform(Read(a[0]), Read(a[1]), (Read(a[2]), Read(a[3]), Read(a[4])));
                return pc + 1;
            case "u00421A90":                  // pre-reference compatibility
            case "set-gfx-range-scale-current": // 0x22a (sx%)(sy%)(sz%)
                Gfx.SetRangeScaleCurrent((Read(a[0]), Read(a[1]), Read(a[2]))); return pc + 1;
            case "u00421BD0":                  // pre-reference compatibility
            case "set-gfx-range-translation-current": // 0x22c (tx)(ty)(tz)
                Gfx.SetRangeTranslationCurrent((Read(a[0]), Read(a[1]), Read(a[2]))); return pc + 1;
            case "u00421C60":                  // pre-reference compatibility
            case "set-gfx-range-scale-target": // 0x22d (delay)(duration)(sx%)(sy%)(sz%)
                Gfx.SetRangeScaleChannel(Read(a[0]), Read(a[1]), (Read(a[2]), Read(a[3]), Read(a[4])));
                return pc + 1;
            case "query-gfx-translation-target":  // pre-correction generated-name compatibility
            case "u00421940":
            case "query-gfx-translation-current": // 0x228: (succ)(handle)(outX)(outY)(outZ) <- current matrix
            {
                if (Gfx.TryQueryTranslationCurrent(Read(a[1]), out var v))
                {
                    Write(a[2], (long)v.X); Write(a[3], (long)v.Y); Write(a[4], (long)v.Z);
                    Write(a[0], 0);
                }
                else Write(a[0], 1);   // native missing-object path leaves output operands untouched
                return pc + 1;
            }
            case "u00421880":                    // upstream ABI label
            case "query-gfx-rotation-current":  // 0x227: (failure)(handle)(out axis x/y/z)(out angle)
            {
                if (Gfx.TryQueryRotationCurrent(Read(a[1]), out var v))
                {
                    // Native writes floats through the revision's destination helper. Both revisions
                    // truncate toward zero when these outputs target integer cells, as all corpus sites do.
                    Write(a[2], (long)v.X); Write(a[3], (long)v.Y); Write(a[4], (long)v.Z);
                    Write(a[5], (long)v.Angle); Write(a[0], 0);
                }
                else Write(a[0], 1);   // native missing-object path leaves output operands untouched
                return pc + 1;
            }
            case "set-gfx-geom3-c":     // 0x1ff: set current translation matrix
                Gfx.SetCurrentTranslation(Read(a[0]), (Read(a[1]), Read(a[2]), Read(a[3]))); return pc + 1;
            case "u00420620":             // upstream ABI label
            case "gfx-set-scale-current": // 0x1fd (handle)(sx%)(sy%)(sz%) -> current scale matrix
                Gfx.SetCurrentScale(Read(a[0]), (Read(a[1]), Read(a[2]), Read(a[3]))); return pc + 1;
            case "set-current-rotation-axis-angle": // 0x1fe (handle)(axis x/y/z)(angle degrees)
                Gfx.SetCurrentRotation(Read(a[0]), (Read(a[1]), Read(a[2]), Read(a[3])), Read(a[4]));
                return pc + 1;
            case "gfx-elem-erase":      // 0x1f7 (handle)(count) — erase retained-object range
            {
                long first = Read(a[0]), count = Read(a[1]);
                Gfx.EraseRange(first, count);
                foreach (int layoutSlot in TextHistory.LayoutsCoveredByTextObjectErase(first, count))
                    _host.ClearRenderedAdvTextLayout(layoutSlot);
                return pc + 1;
            }
            case "clone-gfx-object":    // 0x21d (source handle)(destination handle)
                Gfx.CloneObject(Read(a[0]), Read(a[1])); return pc + 1;
            default:
                throw new InvalidOperationException($"Non-retained-object opcode routed to retained-object handler: {label}");
        }
    }
}
