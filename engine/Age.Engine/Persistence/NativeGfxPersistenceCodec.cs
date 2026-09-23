using System.Buffers.Binary;
using Age.Engine.Model;

namespace Age.Engine.Persistence;

internal static class NativeGfxPersistenceCodec
{
    private const int SurfaceCount = 1000;
    private const int SurfaceRecordSize = 20;

    public static (
        byte[] SurfaceRecords,
        IReadOnlyList<NativeSavedGfxObject> Objects,
        long RangeFirst,
        int RangeCount,
        byte[] RangeRecord) Capture(GfxState gfx,
            int gfxRecordSize = NativeGfxSaveLayout.BaseRecordSize)
    {
        NativeGfxSaveLayout layout = NativeGfxSaveLayout.FromRecordSize(gfxRecordSize);
        GfxPersistenceSnapshot snapshot = gfx.CapturePersistenceSnapshot();
        byte[] surfaces = new byte[NativeNumberedSaveState.SurfaceRecordsSize];
        for (int slot = 0; slot < SurfaceCount; slot++)
            WriteInt(surfaces, slot * SurfaceRecordSize, -1);
        foreach (GfxSurfacePersistenceState surface in snapshot.Surfaces)
        {
            if ((uint)surface.Slot >= SurfaceCount) continue;
            int at = surface.Slot * SurfaceRecordSize;
            WriteInt(surfaces, at,
                surface.Created ? -1 : unchecked((int)surface.ResourceId));
            WriteInt(surfaces, at + 4, PackNativeColorKey(surface.ColorKey));
            WriteInt(surfaces, at + 8, surface.ReloadOnRestore ? 1 : 0);
            WriteInt(surfaces, at + 0x10, surface.Created ? 1 : 0);
        }
        NativeSavedGfxObject[] objects = snapshot.Objects
            .Select(item => new NativeSavedGfxObject(item.Handle, EncodeObject(item.Object, layout)))
            .ToArray();
        return (
            surfaces, objects, snapshot.RangeFirst, unchecked((int)snapshot.RangeCount),
            EncodeObject(snapshot.RangeTransform, layout));
    }

    public static GfxPersistenceSnapshot Decode(NativeNumberedSaveState state)
    {
        var surfaces = new List<GfxSurfacePersistenceState>();
        for (int slot = 0; slot < SurfaceCount; slot++)
        {
            int at = slot * SurfaceRecordSize;
            int resourceId = ReadInt(state.SurfaceRecords, at);
            bool reload = ReadInt(state.SurfaceRecords, at + 8) == 1;
            bool created = ReadInt(state.SurfaceRecords, at + 0x10) == 1;
            if (resourceId >= 0 || reload || created)
                surfaces.Add(new GfxSurfacePersistenceState(
                    slot,
                    resourceId < 0 ? resourceId : unchecked((uint)resourceId),
                    UnpackNativeColorKey(ReadInt(state.SurfaceRecords, at + 4)),
                    created,
                    reload));
        }
        var objects = state.GfxObjects
            .Select(item => (item.Handle, DecodeObject(item.Record, state.LegacyTightGfxLayout)))
            .ToArray();
        return new GfxPersistenceSnapshot(
            surfaces, objects, state.RangeTransformFirst, state.RangeTransformCount,
            DecodeObject(state.RangeTransformRecord, state.LegacyTightGfxLayout));
    }

    private static byte[] EncodeObject(GfxState.GfxObject value, NativeGfxSaveLayout layout)
    {
        byte[] raw = value.NativePersistenceRecord?.Length == layout.RecordSize
            ? value.NativePersistenceRecord.ToArray()
            : CreateDefaultObjectRecord(layout.RecordSize);
        int flags = ReadInt(raw, 0);
        flags = value.Visible ? flags | 1 : flags & ~1;
        flags = value.OneShotColorEnabled || value.ScaleEnabled
            || value.RotationChannelEnabled || value.TranslationEnabled
            ? flags | 2
            : flags & ~2;
        flags = value.ScaleCycleEnabled || value.RotationEnabled || value.TranslationCycleEnabled
            || value.SrcAnim || value.ColorAnim
            ? flags | 4
            : flags & ~4;
        WriteInt(raw, 0, flags);
        WriteInt(raw, 4, value.SourceSlot);
        WriteInt(raw, 8, value.SrcRect.X);
        WriteInt(raw, 0x0c, value.SrcRect.Y);
        WriteInt(raw, 0x10, checked(value.SrcRect.X + value.SrcRect.W));
        WriteInt(raw, 0x14, checked(value.SrcRect.Y + value.SrcRect.H));
        WriteVector(raw, 0x18, value.V18);
        WriteVector(raw, 0x24, value.V24);
        WriteInt(raw, 0x30, unchecked((int)value.StaticColorMode));
        WriteInt(raw, 0x34, EncodeNativeStart(value.OneShotStartMs));
        WriteInt(raw, 0x38, unchecked((int)value.ColorDelayMs));
        WriteInt(raw, 0x3c, unchecked((int)value.ScaleDelayMs));
        WriteInt(raw, 0x40, unchecked((int)value.RotationDelayMs));
        WriteInt(raw, 0x44, unchecked((int)value.TranslationDelayMs));
        WriteInt(raw, 0x4c, unchecked((int)value.ColorDurationMs));
        WriteInt(raw, 0x50, unchecked((int)value.ScaleDurationMs));
        WriteInt(raw, 0x54, unchecked((int)value.RotationDurationMs));
        WriteInt(raw, 0x58, unchecked((int)value.TranslationDurationMs));
        WriteInt(raw, 0x60, unchecked((int)value.Color));
        WriteInt(raw, 0x64, unchecked((int)value.OneShotColorTarget));
        WriteScaleMatrix(raw, 0x6c, value.ScaleCurrent);
        WriteScaleMatrix(raw, 0xac, value.ScaleTarget);
        WriteRotationMatrix(raw, 0xec, value.RotationCurrent);
        WriteRotationMatrix(raw, 0x12c, value.RotationTarget);
        WriteTranslationMatrix(raw, 0x16c, value.TranslationCurrent);
        WriteTranslationMatrix(raw, 0x1ac, value.TranslationTarget);
        WriteFloat(raw, 0x1ec, value.RotationCurrent.X);
        WriteFloat(raw, 0x1f0, value.RotationCurrent.Y);
        WriteFloat(raw, 0x1f4, value.RotationCurrent.Z);
        WriteFloat(raw, 0x1f8, value.RotationTarget.X);
        WriteFloat(raw, 0x1fc, value.RotationTarget.Y);
        WriteFloat(raw, 0x200, value.RotationTarget.Z);
        WriteFloat(raw, 0x204, value.RotationCurrent.Angle);
        WriteFloat(raw, 0x208, value.RotationTarget.Angle);
        WriteInt(raw, 0x20c, EncodeNativeStart(value.ColorStart));
        WriteInt(raw, 0x210, EncodeNativeStart(value.ScaleCycleStartMs));
        WriteInt(raw, 0x214, EncodeNativeStart(value.RotationStartMs));
        WriteInt(raw, 0x218, EncodeNativeStart(value.TranslationCycleStartMs));
        WriteInt(raw, 0x21c, EncodeNativeStart(value.SrcStart));
        WriteInt(raw, 0x220, unchecked((int)value.ColorPeriod));
        WriteInt(raw, 0x224, unchecked((int)value.ScaleCyclePeriodMs));
        WriteInt(raw, 0x228, unchecked((int)value.RotationPeriodMs));
        WriteInt(raw, 0x22c, unchecked((int)value.TranslationCyclePeriodMs));
        WriteInt(raw, 0x230, unchecked((int)value.SrcPeriod));
        WriteInt(raw, 0x234, unchecked((int)value.SrcCell));
        WriteInt(raw, 0x238, value.SrcAnim ? unchecked((int)value.SrcFrameCount) : 0);
        WriteInt(raw, 0x23c, value.SrcAnim ? unchecked((int)value.SrcColumns) : 0);
        if (value.ColorAnim)
            WriteInt(raw, 0x240, unchecked((int)value.ColorTarget));
        WriteVector(raw, 0x244, value.RotationAxis);
        WriteScaleMatrix(raw, 0x250, value.ScaleCycleTarget);
        WriteTranslationMatrix(raw, 0x290, value.TranslationCycleTarget);
        WriteInt(raw, 0x2d0, unchecked((int)value.OneShotAnimationControlFlags));
        if (layout.HasRotationCache)
        {
            var rotation = value.EvaluatedRotationCache
                ?? (value.RotationCurrent.X, value.RotationCurrent.Y, value.RotationCurrent.Z,
                    value.RotationCurrent.Angle * Math.PI / 180.0);
            WriteFloat(raw, 0x2d4, rotation.Item1);
            WriteFloat(raw, 0x2d8, rotation.Item2);
            WriteFloat(raw, 0x2dc, rotation.Item3);
            WriteFloat(raw, 0x2e0, rotation.Item4);
        }
        return raw;
    }

    private static GfxState.GfxObject DecodeObject(ReadOnlySpan<byte> raw, bool legacyTightLayout)
    {
        NativeGfxSaveLayout layout = NativeGfxSaveLayout.FromRecordSize(raw.Length);
        int left = ReadInt(raw, 8), top = ReadInt(raw, 0x0c);
        int right = ReadInt(raw, 0x10), bottom = ReadInt(raw, 0x14);
        int flags = ReadInt(raw, 0);
        return new GfxState.GfxObject
        {
            NativePersistenceRecord = legacyTightLayout
                ? CreateDefaultObjectRecord(raw.Length)
                : raw.ToArray(),
            Visible = (flags & 1) != 0,
            SourceSlot = ReadInt(raw, 4),
            SrcRect = (left, top, right - left, bottom - top),
            V18 = ReadLongVector(raw, 0x18),
            V24 = ReadLongVector(raw, 0x24),
            StaticColorMode = ReadInt(raw, 0x30),
            OneShotStartMs = DecodeNativeStart(ReadInt(raw, 0x34)),
            ColorDelayMs = ReadInt(raw, 0x38),
            ScaleDelayMs = ReadInt(raw, 0x3c),
            RotationDelayMs = ReadInt(raw, 0x40),
            TranslationDelayMs = ReadInt(raw, 0x44),
            ColorDurationMs = ReadInt(raw, 0x4c),
            ScaleDurationMs = ReadInt(raw, 0x50),
            RotationDurationMs = ReadInt(raw, 0x54),
            TranslationDurationMs = ReadInt(raw, 0x58),
            Color = unchecked((uint)ReadInt(raw, 0x60)),
            HasColor = ReadInt(raw, 0x60) != -1,
            OneShotColorTarget = unchecked((uint)ReadInt(raw, 0x64)),
            ScaleCurrent = ReadScale(raw, 0x6c),
            ScaleTarget = ReadScale(raw, 0xac),
            TranslationCurrent = ReadMatrixTranslation(raw, 0x16c),
            V16c = ReadLongMatrixTranslation(raw, 0x16c),
            TranslationTarget = ReadMatrixTranslation(raw, 0x1ac),
            RotationCurrent = (
                ReadFloat(raw, 0x1ec), ReadFloat(raw, 0x1f0), ReadFloat(raw, 0x1f4),
                ReadFloat(raw, 0x204)),
            RotationTarget = (
                ReadFloat(raw, 0x1f8), ReadFloat(raw, 0x1fc), ReadFloat(raw, 0x200),
                ReadFloat(raw, 0x208)),
            EvaluatedRotationCache = layout.HasRotationCache
                ? (ReadFloat(raw, 0x2d4), ReadFloat(raw, 0x2d8), ReadFloat(raw, 0x2dc), ReadFloat(raw, 0x2e0))
                : null,
            ColorStart = DecodeNativeStart(ReadInt(raw, 0x20c)),
            ScaleCycleStartMs = DecodeNativeStart(ReadInt(raw, 0x210)),
            RotationStartMs = DecodeNativeStart(ReadInt(raw, 0x214)),
            TranslationCycleStartMs = DecodeNativeStart(ReadInt(raw, 0x218)),
            SrcStart = DecodeNativeStart(ReadInt(raw, 0x21c)),
            ColorPeriod = ReadInt(raw, 0x220),
            ScaleCyclePeriodMs = ReadInt(raw, 0x224),
            RotationPeriodMs = ReadInt(raw, 0x228),
            TranslationCyclePeriodMs = ReadInt(raw, 0x22c),
            SrcPeriod = ReadInt(raw, 0x230),
            SrcCell = ReadInt(raw, 0x234),
            SrcFrameCount = Math.Max(1, ReadInt(raw, 0x238)),
            SrcColumns = Math.Max(1, ReadInt(raw, 0x23c)),
            ColorTarget = unchecked((uint)ReadInt(raw, 0x240)),
            RotationAxis = ReadLongVector(raw, 0x244),
            ScaleCycleTarget = ReadScale(raw, 0x250),
            TranslationCycleTarget = ReadMatrixTranslation(raw, 0x290),
            OneShotAnimationControlFlags = unchecked((uint)ReadInt(raw, 0x2d0)),
            ScaleCycleEnabled = (flags & 4) != 0 && ReadInt(raw, 0x224) > 0,
            RotationEnabled = (flags & 4) != 0 && ReadInt(raw, 0x228) > 0,
            TranslationCycleEnabled = (flags & 4) != 0 && ReadInt(raw, 0x22c) > 0,
            SrcAnim = (flags & 4) != 0 && ReadInt(raw, 0x238) > 1,
            ColorAnim = (flags & 4) != 0 && ReadInt(raw, 0x220) > 0,
            OneShotColorEnabled = ReadInt(raw, 0x4c) > 0,
            ScaleEnabled = ReadInt(raw, 0x50) > 0,
            RotationChannelEnabled = ReadInt(raw, 0x54) > 0,
            TranslationEnabled = ReadInt(raw, 0x58) > 0,
        };
    }

    private static int PackNativeColorKey(long colorKey)
        => colorKey < 0 ? 0 : unchecked((int)(0xff000000 | (uint)colorKey & 0xffffff));

    private static long UnpackNativeColorKey(int colorKey)
        => colorKey == 0 ? -1 : unchecked((uint)colorKey) & 0xffffff;

    private static void WriteScaleMatrix(Span<byte> raw, int offset, (double X, double Y, double Z) scale)
    {
        ClearMatrix(raw, offset);
        WriteFloat(raw, offset, scale.X);
        WriteFloat(raw, offset + 0x14, scale.Y);
        WriteFloat(raw, offset + 0x28, scale.Z);
        WriteFloat(raw, offset + 0x3c, 1);
    }

    private static (double X, double Y, double Z) ReadScale(ReadOnlySpan<byte> raw, int offset)
        => (ReadFloat(raw, offset), ReadFloat(raw, offset + 0x14), ReadFloat(raw, offset + 0x28));

    private static void WriteRotationMatrix(
        Span<byte> raw, int offset, (double X, double Y, double Z, double Angle) rotation)
    {
        ClearMatrix(raw, offset);
        double length = Math.Sqrt(
            rotation.X * rotation.X + rotation.Y * rotation.Y + rotation.Z * rotation.Z);
        if (length <= double.Epsilon || rotation.Angle == 0)
        {
            WriteFloat(raw, offset, 1);
            WriteFloat(raw, offset + 0x14, 1);
            WriteFloat(raw, offset + 0x28, 1);
            WriteFloat(raw, offset + 0x3c, 1);
            return;
        }

        double x = rotation.X / length;
        double y = rotation.Y / length;
        double z = rotation.Z / length;
        double radians = rotation.Angle * Math.PI / 180.0;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        double complement = 1.0 - cosine;
        WriteFloat(raw, offset, x * x * complement + cosine);
        WriteFloat(raw, offset + 0x04, x * y * complement + z * sine);
        WriteFloat(raw, offset + 0x08, x * z * complement - y * sine);
        WriteFloat(raw, offset + 0x10, x * y * complement - z * sine);
        WriteFloat(raw, offset + 0x14, y * y * complement + cosine);
        WriteFloat(raw, offset + 0x18, x * sine + y * z * complement);
        WriteFloat(raw, offset + 0x20, y * sine + x * z * complement);
        WriteFloat(raw, offset + 0x24, y * z * complement - x * sine);
        WriteFloat(raw, offset + 0x28, z * z * complement + cosine);
        WriteFloat(raw, offset + 0x3c, 1);
    }

    private static void WriteVector(Span<byte> raw, int offset, (long X, long Y, long Z) vector)
    {
        WriteVectorComponent(raw, offset, vector.X);
        WriteVectorComponent(raw, offset + 4, vector.Y);
        WriteVectorComponent(raw, offset + 8, vector.Z);
    }

    private static (long X, long Y, long Z) ReadLongVector(ReadOnlySpan<byte> raw, int offset)
        => (ReadVectorComponent(raw, offset), ReadVectorComponent(raw, offset + 4),
            ReadVectorComponent(raw, offset + 8));

    // Native anchor, position, and cyclic axis fields are float32, although the VM exposes
    // integer vector queries. Early Himegari port saves wrote int32 here; their nonzero small
    // coordinates decode as subnormals (negative ones as NaNs). Accept those old records,
    // then normalize on write. Never apply that compatibility interpretation to SYS4433.
    private static bool IsLegacyIntegerVector(ReadOnlySpan<byte> raw, int offset)
    {
        float value = ReadFloat(raw, offset);
        return raw.Length == NativeGfxSaveLayout.BaseRecordSize
            && (!float.IsFinite(value) || float.IsSubnormal(value));
    }

    private static long ReadVectorComponent(ReadOnlySpan<byte> raw, int offset)
        => IsLegacyIntegerVector(raw, offset) ? ReadInt(raw, offset) : (long)ReadFloat(raw, offset);

    private static void WriteVectorComponent(Span<byte> raw, int offset, long value)
    {
        // Keep imported fractional bits while the integer-facing semantic value is unchanged.
        if (!IsLegacyIntegerVector(raw, offset) && ReadVectorComponent(raw, offset) == value) return;
        WriteFloat(raw, offset, value);
    }

    private static void WriteTranslationMatrix(
        Span<byte> raw, int offset, (double X, double Y, double Z) translation)
    {
        ClearMatrix(raw, offset);
        WriteFloat(raw, offset, 1);
        WriteFloat(raw, offset + 0x14, 1);
        WriteFloat(raw, offset + 0x28, 1);
        WriteFloat(raw, offset + 0x30, translation.X);
        WriteFloat(raw, offset + 0x34, translation.Y);
        WriteFloat(raw, offset + 0x38, translation.Z);
        WriteFloat(raw, offset + 0x3c, 1);
    }

    private static (long X, long Y, long Z) ReadLongMatrixTranslation(
        ReadOnlySpan<byte> raw, int offset)
        => ((long)ReadFloat(raw, offset + 0x30),
            (long)ReadFloat(raw, offset + 0x34),
            (long)ReadFloat(raw, offset + 0x38));

    private static (double X, double Y, double Z) ReadMatrixTranslation(
        ReadOnlySpan<byte> raw, int offset)
        => (ReadFloat(raw, offset + 0x30),
            ReadFloat(raw, offset + 0x34),
            ReadFloat(raw, offset + 0x38));

    private static void ClearMatrix(Span<byte> raw, int offset)
        => raw.Slice(offset, 0x40).Clear();

    private static int EncodeNativeStart(long value)
        => value < 0 ? 0 : unchecked((int)value);

    private static long DecodeNativeStart(int value)
        => value == 0 ? -1 : value;

    private static byte[] CreateDefaultObjectRecord(int recordSize)
    {
        byte[] raw = new byte[recordSize];
        WriteInt(raw, 0x60, -1);
        WriteInt(raw, 0x64, -1);
        foreach (int matrixOffset in new[] { 0x6c, 0xac, 0xec, 0x12c, 0x16c, 0x1ac, 0x250, 0x290 })
        {
            WriteFloat(raw, matrixOffset, 1);
            WriteFloat(raw, matrixOffset + 0x14, 1);
            WriteFloat(raw, matrixOffset + 0x28, 1);
            WriteFloat(raw, matrixOffset + 0x3c, 1);
        }
        WriteInt(raw, 0x240, -1);
        return raw;
    }

    private static void WriteFloat(Span<byte> raw, int offset, double value)
        => WriteInt(raw, offset, BitConverter.SingleToInt32Bits((float)value));

    private static float ReadFloat(ReadOnlySpan<byte> raw, int offset)
        => BitConverter.Int32BitsToSingle(ReadInt(raw, offset));

    private static int ReadInt(ReadOnlySpan<byte> raw, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(raw.Slice(offset, 4));

    private static void WriteInt(Span<byte> raw, int offset, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(raw.Slice(offset, 4), value);
}
