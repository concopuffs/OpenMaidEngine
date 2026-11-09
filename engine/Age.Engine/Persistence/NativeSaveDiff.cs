using System.Buffers.Binary;

namespace Age.Engine.Persistence;

/// <summary>One differing value between two saves. <see cref="Key"/> is the slot, index, frame or handle within the
/// section, and <see cref="Field"/> names the value inside it (a record field or a byte offset).</summary>
public sealed record NativeSaveDifference(string Section, long Key, string Field, string A, string B);

/// <summary>The differences between two decoded saves. <see cref="Numbered"/> is false when either payload is not a
/// numbered save (layout 3); the payloads are then compared dword by dword in the section "payload", and
/// <see cref="NumberedDecodeError"/> says why.</summary>
public sealed record NativeSaveComparison(
    bool Numbered,
    string? NumberedDecodeError,
    IReadOnlyList<NativeSaveDifference> Differences);

/// <summary>
/// Compares two native save containers by their decoded payloads. Files cannot be compared directly: the reversible
/// transform's seed is random and the LZSS encoders choose different matches. Numbered saves are compared section by
/// section, so a difference reads as "surface 0x20 created" or "int-global 0x320a" rather than a payload offset.
/// </summary>
public static class NativeSaveDiff
{
    public const string Metadata = "metadata";
    public const string Header = "header";
    public const string SoundEffects = "sfx";
    public const string ResourceRecords = "resource";
    public const string SurfaceRecords = "surface";
    public const string Frames = "frame";
    public const string IntegerGlobals = "int-global";
    public const string FloatGlobals = "float-global";
    public const string StringGlobals = "string-global";
    public const string PointerGlobals = "pointer-global";
    public const string PointerStrings = "pointer-string";
    public const string LocalPointerScratch = "local-scratch";
    public const string GfxObjects = "gfx";
    public const string RangeTransform = "range-transform";
    public const string Payload = "payload";

    /// <summary>Every section, in report order.</summary>
    public static readonly IReadOnlyList<string> Sections =
    [
        Metadata, Header, SoundEffects, ResourceRecords, SurfaceRecords, Frames, IntegerGlobals, FloatGlobals,
        StringGlobals, PointerGlobals, PointerStrings, LocalPointerScratch, GfxObjects, RangeTransform, Payload,
    ];

    // The 20-byte surface record (docs/sys4-format-notes.md, "Surface-record bookkeeping").
    private static readonly string[] SurfaceFields = ["id", "key", "reload", "lifecycle", "created"];
    private const int SurfaceRecordSize = 20;
    private const int ResourceRecordSize = 12;

    public static NativeSaveComparison Compare(NativeSaveDocument a, NativeSaveDocument b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var differences = new List<NativeSaveDifference>();
        CompareMetadata(a.Metadata, b.Metadata, differences);
        NativeNumberedSaveState? left = null, right = null;
        string? error = null;
        try
        {
            left = NativeNumberedSaveCodec.Decode(a.Payload);
            right = NativeNumberedSaveCodec.Decode(b.Payload);
        }
        catch (InvalidDataException exception)
        {
            error = exception.Message;
        }
        if (left is null || right is null)
        {
            CompareDwords(Payload, 0, a.Payload, b.Payload, differences);
            return new NativeSaveComparison(false, error, differences);
        }
        CompareNumbered(left, right, differences);
        return new NativeSaveComparison(true, null, differences);
    }

    private static void CompareMetadata(NativeSaveMetadata a, NativeSaveMetadata b, List<NativeSaveDifference> d)
    {
        Add(d, Metadata, 0, "magic", a.Magic.ToString(), b.Magic.ToString());
        Add(d, Metadata, 0, "compatibility-id", Hex(a.CompatibilityId), Hex(b.CompatibilityId));
        Add(d, Metadata, 0, "game-id", a.GameId, b.GameId);
        Add(d, Metadata, 0, "timestamp", Timestamp(a.Timestamp), Timestamp(b.Timestamp));
        Add(d, Metadata, 0, "play-seconds", a.AccumulatedPlaySeconds.ToString(), b.AccumulatedPlaySeconds.ToString());
        Add(d, Metadata, 0, "save-version-1", a.SaveVersion1.ToString(), b.SaveVersion1.ToString());
        Add(d, Metadata, 0, "save-version-2", a.SaveVersion2.ToString(), b.SaveVersion2.ToString());
    }

    private static void CompareNumbered(NativeNumberedSaveState a, NativeNumberedSaveState b,
                                        List<NativeSaveDifference> d)
    {
        Add(d, Header, 0, "frame-cutoff", (a.Frames.Count - 1).ToString(), (b.Frames.Count - 1).ToString());
        Add(d, Header, 0, "saved-frame-owner", a.SavedFrameOwner.ToString(), b.SavedFrameOwner.ToString());
        Add(d, Header, 0, "bgm", Value(a.BgmTrackId), Value(b.BgmTrackId));
        Add(d, Header, 0, "gfx-record-size", Hex(a.GraphicsRecordSize), Hex(b.GraphicsRecordSize));
        CompareInts(SoundEffects, a.SoundEffectResourceIds, b.SoundEffectResourceIds, d);
        CompareRecords(ResourceRecords, a.ResourceRecords, b.ResourceRecords, ResourceRecordSize, null, d);
        CompareRecords(SurfaceRecords, a.SurfaceRecords, b.SurfaceRecords, SurfaceRecordSize, SurfaceFields, d);
        CompareFrames(a.Frames, b.Frames, d);
        CompareInts(IntegerGlobals, a.IntegerGlobals, b.IntegerGlobals, d);
        CompareBank(FloatGlobals, a.FloatGlobals, b.FloatGlobals,
                    bits => BitConverter.Int32BitsToSingle(bits).ToString("R"), d);
        CompareBank(StringGlobals, a.StringGlobals, b.StringGlobals, text => Quote(text), d);
        CompareInts(PointerGlobals, a.PointerGlobals, b.PointerGlobals, d);
        CompareInts(PointerStrings, a.PointerStrings, b.PointerStrings, d);
        CompareInts(LocalPointerScratch, a.LocalPointerScratch, b.LocalPointerScratch, d);
        CompareGfx(a.GfxObjects, b.GfxObjects, d);
        Add(d, RangeTransform, 0, "first", Hex(a.RangeTransformFirst), Hex(b.RangeTransformFirst));
        Add(d, RangeTransform, 0, "count", a.RangeTransformCount.ToString(), b.RangeTransformCount.ToString());
        CompareDwords(RangeTransform, 0, a.RangeTransformRecord, b.RangeTransformRecord, d);
    }

    private static void CompareFrames(IReadOnlyList<NativeSavedScriptFrame> a, IReadOnlyList<NativeSavedScriptFrame> b,
                                      List<NativeSaveDifference> d)
    {
        int common = Math.Min(a.Count, b.Count);
        for (int i = 0; i < common; i++)
        {
            NativeSavedScriptFrame x = a[i], y = b[i];
            Add(d, Frames, i, "parent", x.ParentContext.ToString(), y.ParentContext.ToString());
            Add(d, Frames, i, "script", Hex(x.ScriptId), Hex(y.ScriptId));
            Add(d, Frames, i, "resume", Hex(x.ResumeIndex), Hex(y.ResumeIndex));
            Add(d, Frames, i, "call-target", Value(x.CallTargetIndex), Value(y.CallTargetIndex));
            Add(d, Frames, i, "returns", Returns(x), Returns(y));
        }
        for (int i = common; i < a.Count; i++) Add(d, Frames, i, "present", "yes", "no");
        for (int i = common; i < b.Count; i++) Add(d, Frames, i, "present", "no", "yes");
    }

    private static string Returns(NativeSavedScriptFrame frame)
        => "[" + string.Join(",", frame.ReturnIndices.Select(index => Hex(index)))
           + (frame.StoppedAtUnmatchedReturn ? ",-1" : "") + "]";

    private static void CompareGfx(IReadOnlyList<NativeSavedGfxObject> a, IReadOnlyList<NativeSavedGfxObject> b,
                                   List<NativeSaveDifference> d)
    {
        var left = a.ToDictionary(item => item.Handle);
        var right = b.ToDictionary(item => item.Handle);
        foreach (long handle in left.Keys.Union(right.Keys).Order())
        {
            bool inA = left.TryGetValue(handle, out NativeSavedGfxObject? x);
            bool inB = right.TryGetValue(handle, out NativeSavedGfxObject? y);
            if (!inA || !inB)
            {
                Add(d, GfxObjects, handle, "present", inA ? "yes" : "no", inB ? "yes" : "no");
                continue;
            }
            CompareDwords(GfxObjects, handle, x!.Record, y!.Record, d);
        }
    }

    private static void CompareRecords(string section, byte[] a, byte[] b, int recordSize, string[]? fields,
                                       List<NativeSaveDifference> d)
    {
        int length = Math.Min(a.Length, b.Length);
        for (int at = 0; at + 4 <= length; at += 4)
        {
            int x = BinaryPrimitives.ReadInt32LittleEndian(a.AsSpan(at));
            int y = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
            if (x == y) continue;
            int field = at % recordSize / 4;
            string name = fields is not null && field < fields.Length ? fields[field] : $"+0x{at % recordSize:x}";
            d.Add(new NativeSaveDifference(section, at / recordSize, name, Value(x), Value(y)));
        }
    }

    private static void CompareInts(string section, IReadOnlyList<int> a, IReadOnlyList<int> b,
                                    List<NativeSaveDifference> d)
        => CompareBank(section, a, b, Value, d);

    private static void CompareBank<T>(string section, IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, string> format,
                                       List<NativeSaveDifference> d)
    {
        if (a.Count != b.Count) Add(d, section, -1, "count", a.Count.ToString(), b.Count.ToString());
        int common = Math.Min(a.Count, b.Count);
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < common; i++)
            if (!comparer.Equals(a[i], b[i]))
                d.Add(new NativeSaveDifference(section, i, "", format(a[i]), format(b[i])));
    }

    private static void CompareDwords(string section, long key, byte[] a, byte[] b, List<NativeSaveDifference> d)
    {
        if (a.Length != b.Length) Add(d, section, key, "length", Hex(a.Length), Hex(b.Length));
        int length = Math.Min(a.Length, b.Length) & ~3;
        for (int at = 0; at < length; at += 4)
        {
            int x = BinaryPrimitives.ReadInt32LittleEndian(a.AsSpan(at));
            int y = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
            if (x != y) d.Add(new NativeSaveDifference(section, key, $"+0x{at:x}", Value(x), Value(y)));
        }
    }

    private static void Add(List<NativeSaveDifference> d, string section, long key, string field, string a, string b)
    {
        if (a != b) d.Add(new NativeSaveDifference(section, key, field, a, b));
    }

    /// <summary>Decimal, with the dword's bits in hex for values whose bits may matter (handles, ids, flags).</summary>
    private static string Value(int value)
        => value is > -10 and < 10 ? value.ToString() : $"{value} (0x{unchecked((uint)value):x})";

    private static string Hex(long value) => value < 0 ? $"-0x{-value:x}" : $"0x{value:x}";

    private static string Hex(uint value) => $"0x{value:x}";

    private static string Quote(string text) => "\"" + text.Replace("\"", "\\\"") + "\"";

    private static string Timestamp(NativeSystemTime t)
        => $"{t.Year:0000}-{t.Month:00}-{t.Day:00} {t.Hour:00}:{t.Minute:00}:{t.Second:00}.{t.Milliseconds:000}";
}
