namespace Age.Engine.Model;

public sealed partial class GfxState
{
    public sealed class GfxObject
    {
        // The native numbered-save record contains several full transform matrices and reserved fields
        // beyond the port's semantic model. Preserve the decoded bytes so a native load/save round trip
        // does not erase state that the port has not modeled yet; known fields are patched by the codec.
        public byte[]? NativePersistenceRecord;
        public (long X, long Y, long Z) V18, V24, V16c;
        public long Color = 0xffffffff; // native gfx_object_init_default obj+0x60: identity packed ARGB
        public bool HasColor;   // true once op 0x202/0x203 set a color/alpha modulation on this object
        public long StaticColorMode; // op 0x203 operand 2 -> obj+0x30; mode 2 is transition alpha/identity
        // Op 0x202 one-shot packed-color channel: current +0x60, target +0x64, delay +0x38,
        // duration +0x4c. It shares obj+0x34's start timestamp with the one-shot matrix channels.
        public long OneShotColorTarget = -1, ColorDelayMs, ColorDurationMs;
        public bool OneShotColorEnabled;
        // Once op 0x202 arms this object's color channel, its sampled/current ARGB is consumed as D3D
        // opacity + multiplicative modulation even after the target commits. This distinguishes ADV chrome
        // fades from a static mode-0 0x203 such as a CG initialized with 0x00ffffff (opaque identity).
        public bool OneShotColorBlend;
        // A mode-0 static color written after draw-texture binds an object consumes packed alpha as
        // opacity. FIELD uses this to suppress a bound idle unit, and CALLBACK_SETTING uses it when
        // rebuilding the ADV backing after CONFIG. A later draw-texture rebind clears the latch.
        public bool PostBindStaticColorBlend;

        // ---- src-rect / spritesheet-cell channel (ops 0x239 one-shot cell, 0x231 looping animation).
        // Native obj+0x238 is the TOTAL FRAME COUNT and +0x23c is the COLUMN COUNT. Each frame keeps
        // draw-texture's original source-rect size; those operands are not a grid to divide it by. ----
        public long SrcFrameCount = 1, SrcColumns = 1, SrcCell, SrcPeriod, SrcStart = -1;
        public bool SrcAnim;
        // ---- animated color/glow channel (op 0x232). Interpolator COLOR channel: period obj+0x220,
        // start obj+0x20c, target obj+0x240 — PING-PONG (distinct from static 0x202/0x203). ----
        public long ColorPeriod, ColorStart = -1, ColorTarget;
        public bool ColorAnim;
        // draw-texture bind (gfx_object_bind_draw): the surface to draw + its source rect + the visible flag.
        // Native gfx_object_init_default zeroes obj+4. Querying an object created by a geometry/animation
        // setter therefore returns slot 0 even before draw-texture binds it; only an absent object returns -1.
        public int SourceSlot;
        public (int X, int Y, int W, int H) SrcRect;
        public bool Visible;

        // ---- independent one-shot matrix channels (gfx_object_apply_transform_channels@0x472f00). ----
        // Scale: current obj+0x6c, target obj+0xac, delay obj+0x3c, duration obj+0x50.
        public (double X, double Y, double Z) ScaleCurrent = (1, 1, 1), ScaleTarget = (1, 1, 1);
        public long ScaleDelayMs, ScaleDurationMs;
        public bool ScaleEnabled;
        // Translation: current obj+0x16c, target obj+0x1ac, delay obj+0x44, duration obj+0x58.
        public (double X, double Y, double Z) TranslationCurrent, TranslationTarget;
        public long TranslationDelayMs, TranslationDurationMs;
        public bool TranslationEnabled;
        public (double X, double Y, double Z, double Angle) RotationCurrent;
        public (double X, double Y, double Z, double Angle) RotationTarget;
        public long RotationDelayMs, RotationDurationMs;
        public bool RotationChannelEnabled;
        // Op 0x242 writes obj+0x2d0. Bit 0 detaches the finite one-shot group from the blocking-dirty
        // service and protects it from op 0x243's global force-completion request until natural completion.
        public long OneShotAnimationControlFlags;
        // Shared matrix-channel start timestamp obj+0x34, seeded from retained-gfx owner+0xb550
        // (EngineCtx+0x51b64).
        public long OneShotStartMs = -1;

        // Op 0x234 is a separate cyclic rotation channel (period obj+0x228, axis obj+0x244..0x24c).
        public long RotationPeriodMs;
        public (long X, long Y, long Z) RotationAxis;
        public bool RotationEnabled;
        public long RotationStartMs = -1;

        // Op 0x233 is a separate cyclic scale channel (period obj+0x224, target matrix obj+0x250).
        public long ScaleCyclePeriodMs;
        public (double X, double Y, double Z) ScaleCycleTarget = (1, 1, 1);
        public bool ScaleCycleEnabled;
        public long ScaleCycleStartMs = -1;

        // Op 0x235 is a separate cyclic translation channel (period obj+0x22c, target matrix obj+0x290).
        public long TranslationCyclePeriodMs;
        public (double X, double Y, double Z) TranslationCycleTarget;
        public bool TranslationCycleEnabled;
        public long TranslationCycleStartMs = -1;
    }

    // ---- geometry/draw object store (V18/V24/draw bind, the compositor's input) ----
    // Populated lazily by the geometry SET ops and draw-texture. Op 0x215 queries this same native map and
    // returns the object's live source slot (obj+4), or -1 when the handle has not been drawn/bound yet.
    private readonly Dictionary<long, GfxObject> _objects = new();
    // Native composition is handle-ascending z order. Mutations maintain this small index so snapshots do
    // not rebuild/sort a dictionary-sized LINQ buffer, while hot handle lookup remains O(1).
    private readonly List<long> _orderedObjectHandles = new();
    private readonly NumericGlyphStyle[] _numericGlyphStyles = new NumericGlyphStyle[11];

    // Ops 0x229-0x22e address one embedded gfx-object record outside the ordinary object map. Its sampled
    // matrix is post-multiplied onto only the selected handle range during native composition. FIELD uses
    // this as its map camera while the surrounding dungeon UI remains screen-fixed.
    private long _rangeTransformFirst, _rangeTransformCount;
    private GfxObject _rangeTransform = new();

    private readonly Dictionary<long, long> _fieldTable = new();   // ctx+0x46d14 (0x216); no family writer -> default 0
    public long CurrentObject { get; private set; }
    /// <summary>EngineCtx+0x14e08, selected by op 0x80 and used by op 0x1d9 when its slot is zero.</summary>
    public int DefaultObjectSlot { get; private set; }

    /// <summary>Live geometry objects and the surface slot they draw from — for the CLI gfx oracle.</summary>
    public IEnumerable<(long Handle, int Slot)> Objects
    {
        get { foreach (var kv in _objects) yield return (kv.Key, kv.Value.SourceSlot); }
    }

    public GfxObject GetOrCreate(long handle)
    {
        // Locked: called from the VM thread (directly by geometry/animation ops and inside BindDraw/anim
        // ops) while the main-thread compositor enumerates _objects in SnapshotVisibleObjects. _lock is re-entrant
        // (Monitor) so the callers that already hold it are fine.
        lock (_lock)
        {
            if (!_objects.TryGetValue(handle, out var o))
            {
                o = new GfxObject();
                _objects[handle] = o;
                InsertOrderedHandle(handle);
            }
            CurrentObject = handle;
            MarkRetainedMutation();
            return o;
        }
    }

    public void SetDefaultObjectSlot(int slot)
    {
        lock (_lock) DefaultObjectSlot = slot;
    }


    public void SetObjectAnchor(long handle, (long X, long Y, long Z) anchor)
    {
        lock (_lock) GetOrCreate(handle).V18 = anchor;
    }

    public void SetObjectPosition(long handle, (long X, long Y, long Z) position)
    {
        lock (_lock) GetOrCreate(handle).V24 = position;
    }

    /// <summary>Op 0x229: reset the embedded range transform, select [first, first+count), and set its
    /// anchor/pivot. This does not create or mutate an ordinary retained object.</summary>
    public void SetRangeTransform(long first, long count, (long X, long Y, long Z) anchor)
    {
        lock (_lock)
        {
            _rangeTransformFirst = first;
            _rangeTransformCount = System.Math.Max(0, count);
            _rangeTransform = new GfxObject { V18 = anchor };
            MarkRetainedMutation();
        }
    }

    /// <summary>Op 0x22a: immediately replace the embedded range transform's current scale.</summary>
    public void SetRangeScaleCurrent((long X, long Y, long Z) percent)
    {
        lock (_lock)
        {
            _rangeTransform.ScaleCurrent = (percent.X / 100.0, percent.Y / 100.0, percent.Z / 100.0);
            MarkRetainedMutation();
        }
    }

    /// <summary>Op 0x22c: immediately replace the embedded range transform's current translation.</summary>
    public void SetRangeTranslationCurrent((long X, long Y, long Z) translation)
    {
        lock (_lock)
        {
            _rangeTransform.TranslationCurrent = translation;
            MarkRetainedMutation();
        }
    }

    /// <summary>Op 0x22d: arm the range transform's delayed one-shot scale target.</summary>
    public void SetRangeScaleChannel(long delayMs, long durationMs, (long X, long Y, long Z) percent)
    {
        lock (_lock)
        {
            _rangeTransform.ScaleDelayMs = delayMs;
            _rangeTransform.ScaleDurationMs = durationMs;
            _rangeTransform.ScaleTarget = (percent.X / 100.0, percent.Y / 100.0, percent.Z / 100.0);
            _rangeTransform.ScaleEnabled = durationMs > 0;
            _rangeTransform.OneShotStartMs = -1;
            MarkRetainedMutation();
        }
    }

    /// <summary>Op 0x22f: arm the selected retained-gfx range transform's delayed one-shot
    /// translation target. FIELD uses this channel to pan the world while its ordinary minimap object
    /// runs a matching op-0x220 translation.</summary>
    public void SetRangeTranslationChannel(long delayMs, long durationMs,
                                           (long X, long Y, long Z) target)
    {
        lock (_lock)
        {
            _rangeTransform.TranslationDelayMs = delayMs;
            _rangeTransform.TranslationDurationMs = durationMs;
            _rangeTransform.TranslationTarget = target;
            _rangeTransform.TranslationEnabled = durationMs > 0;
            _rangeTransform.OneShotStartMs = -1;
            MarkRetainedMutation();
        }
    }

    /// <summary>Op 0x21d: clone the native 0x2d4-byte retained-object record from source to destination.</summary>
    public bool CloneObject(long sourceHandle, long destinationHandle)
    {
        lock (_lock)
        {
            if (!_objects.TryGetValue(sourceHandle, out var s)) return false;
            bool destinationIsNew = !_objects.ContainsKey(destinationHandle);
            _objects[destinationHandle] = CloneState(s);
            if (destinationIsNew) InsertOrderedHandle(destinationHandle);
            CurrentObject = destinationHandle;
            MarkRetainedMutation();
            return true;
        }
    }

    /// <summary>Op 0x214: exchange the complete retained-object records at two handles. If exactly
    /// one handle exists, move its record to the missing handle; if neither exists, retain no records.</summary>
    public void SwapObjects(long firstHandle, long secondHandle)
    {
        lock (_lock)
        {
            if (firstHandle == secondHandle)
            {
                MarkRetainedMutation();
                return;
            }

            bool hasFirst = _objects.TryGetValue(firstHandle, out var first);
            bool hasSecond = _objects.TryGetValue(secondHandle, out var second);
            if (hasFirst && hasSecond)
            {
                _objects[firstHandle] = CloneState(second!);
                _objects[secondHandle] = CloneState(first!);
            }
            else if (hasFirst)
            {
                _objects.Remove(firstHandle);
                _objects[secondHandle] = CloneState(first!);
                RemoveOrderedHandle(firstHandle);
                InsertOrderedHandle(secondHandle);
            }
            else if (hasSecond)
            {
                _objects.Remove(secondHandle);
                _objects[firstHandle] = CloneState(second!);
                RemoveOrderedHandle(secondHandle);
                InsertOrderedHandle(firstHandle);
            }
            MarkRetainedMutation();
        }
    }

    public GfxObject? TryGet(long handle) => _objects.TryGetValue(handle, out var o) ? o : null;

    /// <summary>Op 0x215: look up <paramref name="handle"/> in the retained gfx-object map and return obj+4,
    /// the live source-surface slot written by draw-texture, or -1 when absent/unbound.</summary>
    public int QuerySlot(long handle)
    {
        lock (_lock)
            return _objects.TryGetValue(handle, out var o) ? o.SourceSlot : -1;
    }

    /// <summary>Rebind one retained object from one source surface to another. Used by SC0000's bounded
    /// movie site to reproduce the native warm-engine slot assignment before the following static loaders
    /// reuse the port's cold-bootstrap slot.</summary>
    public void RemapObjectSurface(long handle, int fromSlot, int toSlot)
    {
        lock (_lock)
            if (_objects.TryGetValue(handle, out var obj) && obj.SourceSlot == fromSlot)
            {
                obj.SourceSlot = toSlot;
                MarkRetainedMutation();
            }
    }
    public long QueryField(long idx) => _fieldTable.TryGetValue(idx, out var v) ? v : 0;

    public void Release(long handle)
    {
        lock (_lock)   // re-entrant: EraseRange already holds _lock
        {
            bool removedTransition = _radialBlurTransitions.Remove(handle)
                                     | _directionalBlurTransitions.Remove(handle);
            if (_objects.Remove(handle))
            {
                int index = _orderedObjectHandles.BinarySearch(handle);
                if (index >= 0) _orderedObjectHandles.RemoveAt(index);
                MarkRetainedMutation();
            }
            else if (removedTransition) MarkRetainedMutation();
        }
    }

    /// <summary>Op 0x1f7 semantics (native gfx_object_erase_range @0x47d8b0): erase handles in
    /// [handle, handle+count) when count>1, else just <paramref name="handle"/>. It is a teardown/erase,
    /// NOT a create — objects are created lazily by the geometry SET ops (gfx_object_get_or_create).</summary>
    public void EraseRange(long handle, long count)
    {
        // Retained-object cleanup (native gfx_object_erase): removes the object from the map, so it stops
        // compositing next frame. Faithful to the engine (the render loop iterates the retained-object map).
        lock (_lock)
        {
            if (count > 1) for (long i = handle; i < handle + count; i++) Release(i);
            else Release(handle);
        }
    }

    /// <summary>Op 0x1f6: clear every retained gfx-object record without releasing surface resources.</summary>
    public void ClearRetainedObjects()
    {
        lock (_lock)
        {
            _objects.Clear();
            _orderedObjectHandles.Clear();
            _radialBlurTransitions.Clear();
            _directionalBlurTransitions.Clear();
            CurrentObject = 0;
            MarkRetainedMutation();
        }
    }

    private static GfxObject CloneState(GfxObject s)
        => new()
        {
            NativePersistenceRecord = s.NativePersistenceRecord?.ToArray(),
            V18 = s.V18, V24 = s.V24, V16c = s.V16c,
            Color = s.Color, HasColor = s.HasColor, StaticColorMode = s.StaticColorMode,
            OneShotColorTarget = s.OneShotColorTarget, ColorDelayMs = s.ColorDelayMs,
            ColorDurationMs = s.ColorDurationMs, OneShotColorEnabled = s.OneShotColorEnabled,
            OneShotColorBlend = s.OneShotColorBlend, PostBindStaticColorBlend = s.PostBindStaticColorBlend,
            SrcFrameCount = s.SrcFrameCount, SrcColumns = s.SrcColumns, SrcCell = s.SrcCell,
            SrcPeriod = s.SrcPeriod, SrcStart = s.SrcStart, SrcAnim = s.SrcAnim,
            ColorPeriod = s.ColorPeriod, ColorStart = s.ColorStart, ColorTarget = s.ColorTarget,
            ColorAnim = s.ColorAnim, SourceSlot = s.SourceSlot, SrcRect = s.SrcRect, Visible = s.Visible,
            ScaleCurrent = s.ScaleCurrent, ScaleTarget = s.ScaleTarget,
            ScaleDelayMs = s.ScaleDelayMs, ScaleDurationMs = s.ScaleDurationMs, ScaleEnabled = s.ScaleEnabled,
            TranslationCurrent = s.TranslationCurrent, TranslationTarget = s.TranslationTarget,
            TranslationDelayMs = s.TranslationDelayMs, TranslationDurationMs = s.TranslationDurationMs,
            TranslationEnabled = s.TranslationEnabled,
            RotationCurrent = s.RotationCurrent, RotationTarget = s.RotationTarget,
            RotationDelayMs = s.RotationDelayMs, RotationDurationMs = s.RotationDurationMs,
            RotationChannelEnabled = s.RotationChannelEnabled,
            OneShotAnimationControlFlags = s.OneShotAnimationControlFlags,
            OneShotStartMs = s.OneShotStartMs,
            RotationPeriodMs = s.RotationPeriodMs, RotationAxis = s.RotationAxis,
            RotationEnabled = s.RotationEnabled, RotationStartMs = s.RotationStartMs,
            ScaleCyclePeriodMs = s.ScaleCyclePeriodMs, ScaleCycleTarget = s.ScaleCycleTarget,
            ScaleCycleEnabled = s.ScaleCycleEnabled, ScaleCycleStartMs = s.ScaleCycleStartMs,
            TranslationCyclePeriodMs = s.TranslationCyclePeriodMs,
            TranslationCycleTarget = s.TranslationCycleTarget,
            TranslationCycleEnabled = s.TranslationCycleEnabled,
            TranslationCycleStartMs = s.TranslationCycleStartMs,
        };

    /// <summary>draw-texture bind (gfx_object_bind_draw): object <paramref name="handle"/> draws surface
    /// <paramref name="slot"/>'s rect at (dstX,dstY) and becomes visible.</summary>
    public void BindDraw(long handle, int slot, int sx, int sy, int w, int h, int dstX, int dstY)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.SourceSlot = slot; o.SrcRect = (sx, sy, w, h); o.V24 = (dstX, dstY, 0); o.Visible = true;
            o.PostBindStaticColorBlend = false;
        }
    }

    /// <summary>Ops 0x13a/0x2da: replace one of eleven safely isolated handler-addressable styles.
    /// Native physically clears ten records; its admitted index 10 aliases return-stack storage.</summary>
    public bool RegisterNumericGlyphStyle(int styleIndex, int surfaceSlot, int atlasX, int atlasY,
                                          int digitWidth, int digitHeight,
                                          int sourceCellSpacing = 0,
                                          int destinationCellSpacing = 0)
    {
        lock (_lock)
        {
            if ((uint)styleIndex >= (uint)_numericGlyphStyles.Length) return false;
            _numericGlyphStyles[styleIndex] =
                new NumericGlyphStyle(surfaceSlot, atlasX, atlasY, digitWidth, digitHeight,
                                      sourceCellSpacing, destinationCellSpacing);
            return true;
        }
    }

    /// <summary>Op 0x23b: erase a destination handle range, split a signed 32-bit value into decimal
    /// digits, and bind one retained object per displayed atlas cell. Returns false for an invalid or
    /// unregistered style, matching the native handler's script-error path.</summary>
    public bool DrawDecimalGlyphs(long baseHandle, int styleIndex, int value, int x, int y,
                                  int digitCapacity, int flags)
    {
        lock (_lock)
        {
            if ((uint)styleIndex >= (uint)_numericGlyphStyles.Length
                || !_numericGlyphStyles[styleIndex].Registered) return false;

            NumericGlyphStyle style = _numericGlyphStyles[styleIndex];
            EraseRange(baseHandle, digitCapacity);
            int sourceCellAdvance = unchecked(style.DigitWidth + style.SourceCellSpacing);
            int destinationCellAdvance =
                unchecked(style.DigitWidth + style.DestinationCellSpacing);

            int remaining = value;
            int handleOffset = 0;
            int digitCount = 1;
            for (int n = remaining / 10; n != 0; n /= 10) digitCount++;

            for (int slot = digitCapacity - 1; slot >= 0; slot--)
            {
                int digit = remaining % 10;
                int drawX;
                bool draw;

                if ((flags & 0x2) != 0)
                {
                    draw = slot == digitCapacity - 1 || remaining != 0;
                    drawX = unchecked(x + destinationCellAdvance * slot
                        - (digitCapacity - digitCount) * destinationCellAdvance / 2);
                }
                else if ((flags & 0x4) != 0)
                {
                    digitCount--;
                    draw = slot == digitCapacity - 1 || remaining != 0;
                    drawX = unchecked(x + destinationCellAdvance * digitCount);
                }
                else
                {
                    draw = (flags & 0x1) != 0 || slot == digitCapacity - 1 || remaining != 0;
                    drawX = unchecked(x + destinationCellAdvance * slot);
                }

                if (draw)
                {
                    int srcX = unchecked(style.AtlasX + digit * sourceCellAdvance);
                    BindDraw(baseHandle + handleOffset, style.SurfaceSlot,
                             srcX, style.AtlasY, style.DigitWidth, style.DigitHeight, drawX, y);
                    handleOffset++;
                }
                remaining /= 10;
            }
            return true;
        }
    }

    private void InsertOrderedHandle(long handle)
    {
        int index = _orderedObjectHandles.BinarySearch(handle);
        if (index < 0) _orderedObjectHandles.Insert(~index, handle);
    }

    private void RemoveOrderedHandle(long handle)
    {
        int index = _orderedObjectHandles.BinarySearch(handle);
        if (index >= 0) _orderedObjectHandles.RemoveAt(index);
    }
}
