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
        byte[] RangeRecord) Capture(GfxState gfx)
    {
        GfxPersistenceSnapshot snapshot = gfx.CapturePersistenceSnapshot();
        byte[] surfaces = new byte[NativeNumberedSaveState.SurfaceRecordsSize];
        foreach (var (slot, resourceId, colorKey, created) in snapshot.Surfaces)
        {
            if ((uint)slot >= SurfaceCount || created || resourceId < 0) continue;
            int at = slot * SurfaceRecordSize;
            WriteInt(surfaces, at, unchecked((int)resourceId));
            WriteInt(surfaces, at + 4, PackNativeColorKey(colorKey));
            WriteInt(surfaces, at + 8, 1);
        }
        NativeSavedGfxObject[] objects = snapshot.Objects
            .Select(item => new NativeSavedGfxObject(item.Handle, EncodeObject(item.Object)))
            .ToArray();
        return (
            surfaces, objects, snapshot.RangeFirst, unchecked((int)snapshot.RangeCount),
            EncodeObject(snapshot.RangeTransform));
    }

    public static GfxPersistenceSnapshot Decode(NativeNumberedSaveState state)
    {
        var surfaces = new List<(int Slot, long ResourceId, long ColorKey, bool Created)>();
        for (int slot = 0; slot < SurfaceCount; slot++)
        {
            int at = slot * SurfaceRecordSize;
            int resourceId = ReadInt(state.SurfaceRecords, at);
            int present = ReadInt(state.SurfaceRecords, at + 8);
            if (present == 1 && resourceId >= 0)
                surfaces.Add((slot, unchecked((uint)resourceId),
                              UnpackNativeColorKey(ReadInt(state.SurfaceRecords, at + 4)), false));
        }
        var objects = state.GfxObjects
            .Select(item => (item.Handle, DecodeObject(item.Record)))
            .ToArray();
        return new GfxPersistenceSnapshot(
            surfaces, objects, state.RangeTransformFirst, state.RangeTransformCount,
            DecodeObject(state.RangeTransformRecord));
    }

    private static byte[] EncodeObject(GfxState.GfxObject value)
    {
        byte[] raw = new byte[NativeNumberedSaveState.GfxRecordSize];
        int flags = value.Visible ? 1 : 0;
        if (value.RotationEnabled || value.SrcAnim || value.ColorAnim) flags |= 4;
        WriteInt(raw, 0, flags);
        WriteInt(raw, 4, value.SourceSlot);
        WriteInt(raw, 8, value.SrcRect.X);
        WriteInt(raw, 0x0c, value.SrcRect.Y);
        WriteInt(raw, 0x10, checked(value.SrcRect.X + value.SrcRect.W));
        WriteInt(raw, 0x14, checked(value.SrcRect.Y + value.SrcRect.H));
        WriteVector(raw, 0x18, value.V18);
        WriteVector(raw, 0x24, value.V24);
        WriteInt(raw, 0x30, unchecked((int)value.StaticColorMode));
        WriteInt(raw, 0x34, unchecked((int)value.OneShotStartMs));
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
        WriteFloat(raw, 0x16c, value.TranslationCurrent.X);
        WriteFloat(raw, 0x170, value.TranslationCurrent.Y);
        WriteFloat(raw, 0x174, value.TranslationCurrent.Z);
        WriteFloat(raw, 0x1ac, value.TranslationTarget.X);
        WriteFloat(raw, 0x1b0, value.TranslationTarget.Y);
        WriteFloat(raw, 0x1b4, value.TranslationTarget.Z);
        WriteInt(raw, 0x20c, unchecked((int)value.ColorStart));
        WriteInt(raw, 0x214, unchecked((int)value.RotationStartMs));
        WriteInt(raw, 0x220, unchecked((int)value.ColorPeriod));
        WriteInt(raw, 0x228, unchecked((int)value.RotationPeriodMs));
        WriteInt(raw, 0x230, unchecked((int)value.SrcPeriod));
        WriteInt(raw, 0x234, unchecked((int)value.SrcCell));
        WriteInt(raw, 0x238, unchecked((int)value.SrcFrameCount));
        WriteInt(raw, 0x23c, unchecked((int)value.SrcColumns));
        WriteInt(raw, 0x240, unchecked((int)value.ColorTarget));
        WriteVector(raw, 0x244, value.RotationAxis);
        WriteInt(raw, 0x2d0, unchecked((int)value.OneShotAnimationControlFlags));
        return raw;
    }

    private static GfxState.GfxObject DecodeObject(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != NativeNumberedSaveState.GfxRecordSize)
            throw new InvalidDataException("Native retained-gfx record has the wrong size.");
        int left = ReadInt(raw, 8), top = ReadInt(raw, 0x0c);
        int right = ReadInt(raw, 0x10), bottom = ReadInt(raw, 0x14);
        int flags = ReadInt(raw, 0);
        return new GfxState.GfxObject
        {
            Visible = (flags & 1) != 0,
            SourceSlot = ReadInt(raw, 4),
            SrcRect = (left, top, right - left, bottom - top),
            V18 = ReadLongVector(raw, 0x18),
            V24 = ReadLongVector(raw, 0x24),
            StaticColorMode = ReadInt(raw, 0x30),
            OneShotStartMs = ReadInt(raw, 0x34),
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
            TranslationCurrent = ReadDoubleVector(raw, 0x16c),
            V16c = ReadLongFloatVector(raw, 0x16c),
            TranslationTarget = ReadDoubleVector(raw, 0x1ac),
            ColorStart = ReadInt(raw, 0x20c),
            RotationStartMs = ReadInt(raw, 0x214),
            ColorPeriod = ReadInt(raw, 0x220),
            RotationPeriodMs = ReadInt(raw, 0x228),
            SrcPeriod = ReadInt(raw, 0x230),
            SrcCell = ReadInt(raw, 0x234),
            SrcFrameCount = Math.Max(1, ReadInt(raw, 0x238)),
            SrcColumns = Math.Max(1, ReadInt(raw, 0x23c)),
            ColorTarget = unchecked((uint)ReadInt(raw, 0x240)),
            RotationAxis = ReadLongVector(raw, 0x244),
            OneShotAnimationControlFlags = unchecked((uint)ReadInt(raw, 0x2d0)),
            RotationEnabled = (flags & 4) != 0 && ReadInt(raw, 0x228) > 0,
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
        WriteFloat(raw, offset, scale.X);
        WriteFloat(raw, offset + 0x14, scale.Y);
        WriteFloat(raw, offset + 0x28, scale.Z);
        WriteFloat(raw, offset + 0x3c, 1);
    }

    private static (double X, double Y, double Z) ReadScale(ReadOnlySpan<byte> raw, int offset)
        => (ReadFloat(raw, offset), ReadFloat(raw, offset + 0x14), ReadFloat(raw, offset + 0x28));

    private static void WriteVector(Span<byte> raw, int offset, (long X, long Y, long Z) vector)
    {
        WriteInt(raw, offset, unchecked((int)vector.X));
        WriteInt(raw, offset + 4, unchecked((int)vector.Y));
        WriteInt(raw, offset + 8, unchecked((int)vector.Z));
    }

    private static (long X, long Y, long Z) ReadLongVector(ReadOnlySpan<byte> raw, int offset)
        => (ReadInt(raw, offset), ReadInt(raw, offset + 4), ReadInt(raw, offset + 8));

    private static (long X, long Y, long Z) ReadLongFloatVector(ReadOnlySpan<byte> raw, int offset)
        => ((long)ReadFloat(raw, offset), (long)ReadFloat(raw, offset + 4), (long)ReadFloat(raw, offset + 8));

    private static (double X, double Y, double Z) ReadDoubleVector(ReadOnlySpan<byte> raw, int offset)
        => (ReadFloat(raw, offset), ReadFloat(raw, offset + 4), ReadFloat(raw, offset + 8));

    private static void WriteFloat(Span<byte> raw, int offset, double value)
        => WriteInt(raw, offset, BitConverter.SingleToInt32Bits((float)value));

    private static float ReadFloat(ReadOnlySpan<byte> raw, int offset)
        => BitConverter.Int32BitsToSingle(ReadInt(raw, offset));

    private static int ReadInt(ReadOnlySpan<byte> raw, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(raw.Slice(offset, 4));

    private static void WriteInt(Span<byte> raw, int offset, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(raw.Slice(offset, 4), value);
}
