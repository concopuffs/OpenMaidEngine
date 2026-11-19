using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Ome.Engine.Sys4;

/// <summary>
/// Ordered per-game key/value pairs stored after the base SYS4INI directory and VM metadata.
/// Unknown keys are retained so newer AGE profiles remain inspectable before they have consumers.
/// </summary>
public sealed class Sys4StartupSettings
{
    private readonly IReadOnlyList<KeyValuePair<string, string>> _pairs;
    private readonly IReadOnlyDictionary<string, string> _values;

    public static Sys4StartupSettings Empty { get; } = new([]);

    public IReadOnlyList<KeyValuePair<string, string>> Pairs => _pairs;
    public int Count => _pairs.Count;

    private Sys4StartupSettings(List<KeyValuePair<string, string>> pairs)
    {
        _pairs = new ReadOnlyCollection<KeyValuePair<string, string>>(pairs);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
            values[pair.Key] = pair.Value;
        _values = new ReadOnlyDictionary<string, string>(values);
    }

    /// <summary>Settings from explicit ordered pairs (tests and tools; games read them from SYS4INI).</summary>
    public static Sys4StartupSettings FromPairs(IEnumerable<KeyValuePair<string, string>> pairs)
        => new(pairs.ToList());

    public bool TryGetValue(string key, out string value)
        => _values.TryGetValue(key, out value!);

    public string? GetValueOrDefault(string key)
        => _values.GetValueOrDefault(key);

    /// <summary>CLICKONUP: native AGE latches a mouse click, the trigger behind page advance, hotspot clicks, bound
    /// actions (right-click -> MENU) and movie/transition skips, on button release instead of press
    /// (g_dwClickOnUp@0x5c6030, input_latch_mouse_click@0x485e20). Missing or non-numeric means press, AGE's
    /// registered default. Polled button state, which menus read, follows the hardware either way.</summary>
    public bool ClickOnUp
        => int.TryParse(GetValueOrDefault("CLICKONUP")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
               out int value) && value != 0;

    /// <summary>CANCELMESSKIPONCLICK (set:CancelMesSkipOnClick, registered default 0). Nonzero runs
    /// adv_interpreter_tick's press/release state machine: while ADV fast-forward is active, a completed action-4
    /// click cancels all-message Skip and the ReadTextSkip setting; value 2 then launches CALLBACK_SETTING.BIN.
    /// Missing or non-numeric means 0.</summary>
    public int CancelMessageSkipOnClick
        => int.TryParse(GetValueOrDefault("CANCELMESSKIPONCLICK")?.Trim(), NumberStyles.Integer,
               CultureInfo.InvariantCulture, out int value) ? value : 0;

    /// <summary>WHEELKEYUP (set:WheelKeyUp, registered default 3): the logical action a wheel-up raises while an ADV
    /// text wait polls (age_main_window_proc@0x486320 WM_MOUSEWHEEL under run-state 0x90100000). Both games use 8,
    /// which every scene binds to its History record with op 0x97. Missing or non-numeric means 3.</summary>
    public int WheelKeyUpAction => ReadInt("WHEELKEYUP", 3);

    /// <summary>NOSETMUSIC (set:NoSetMusic, registered default 0). Nonzero gives AGERC's 設定 dialog the ｻｳﾝﾄﾞ page
    /// without sound:Music's radio buttons (dialog 102 instead of 101); Himegari's 3 also starts sound:Music at 2
    /// (<see cref="Persistence.Sys4RegIniStore.ForPath"/>). Missing or non-numeric means 0.</summary>
    public int NoSetMusic => ReadInt("NOSETMUSIC", 0);

    /// <summary>WHEELKEYDOWN (set:WheelKeyDown, registered default 1): the logical action a wheel-down raises in the
    /// same mode. Both games use 9, which no script binds. Missing or non-numeric means 1.</summary>
    public int WheelKeyDownAction => ReadInt("WHEELKEYDOWN", 1);

    /// <summary>COEXISTMESSKIP (set:CoexistMesSkip, registered default 0). Nonzero lets the native menu's Skip and
    /// Auto toggles stay on together (adv_toggle_skip_mode@0x406c20, adv_toggle_auto_mode@0x406b70); with zero,
    /// enabling one clears the other. Both shipped games set 1. Missing or non-numeric means 0.</summary>
    public bool CoexistMessageSkip => ReadInt("COEXISTMESSKIP", 0) != 0;

    /// <summary>DRAWMODE (set:DrawMode, registered default 0). Both shipped games set 1, under which ADV text draws
    /// into each layout's blank surface (slot layout + 0x14) that sprites then publish. SYS4433's ruby placement also
    /// reads it (<see cref="Text.RubyLayoutRules"/>). Missing or non-numeric means 0.</summary>
    public int DrawMode => ReadInt("DRAWMODE", 0);

    /// <summary>CREATEOBJECT (set:CreateObject, registered default 1): the renderer backends AGE creates, bit n for
    /// DrawMode n. Both shipped games set 2, the object backend their DrawMode 1 draws with. Startup replaces a mask
    /// that lacks DrawMode's bit with 3 (sys4_read_startup_settings@0x415a20). Missing or non-numeric means 1.</summary>
    public int CreateObject
    {
        get
        {
            int value = ReadInt("CREATEOBJECT", 1);
            int drawModeBit = 1 << (DrawMode & 31);
            return (value & drawModeBit) == drawModeBit ? value : 3;
        }
    }

    /// <summary>DEPENDMOVIESOUND (set:DependMovieSound, registered default 1): the mixer category a movie without a
    /// route flag follows (<see cref="Model.MovieSoundRoute"/>). Himegari sets 1 (music), Kamidori 2 (SFX). Missing or
    /// non-numeric means 1.</summary>
    public int DependMovieSound => ReadInt("DEPENDMOVIESOUND", 1);

    /// <summary>AUTOFREETEX (set:AutoFreeTex, registered default 0): under CreateObject bit 1, bit 1 releases every
    /// surface before a numbered load and bit 0 at op 0x9 (the VM's <c>VmOptions</c>). Kamidori sets 3; Himegari has
    /// none. Missing or non-numeric means 0.</summary>
    public int AutoFreeTex => ReadInt("AUTOFREETEX", 0);

    /// <summary>BLANKEXTENTMODE (set:BlankExtentMode; SYS4433). With 1, a blank in aspect-mode text (a space or 0x8140)
    /// advances by its own extent in the reference ＭＳ ゴシック (text_measure_blank_extent@0x404da0) rather than by the
    /// reference cell, halved for one byte (<see cref="Text.ImmediateSurfaceTextRenderer"/>). Kamidori sets 1. Missing
    /// or non-numeric means 0.</summary>
    public int BlankExtentMode => ReadInt("BLANKEXTENTMODE", 0);

    /// <summary>USEPROPORTIONALFONT (set:UseProportionalFont, registered default 0; SYS4433). Zero removes IPA
    /// Pゴシック, IPA P明朝 and every family that is not FIXED_PITCH from the installed-font list that ops
    /// 0x2dc-0x2de read (<see cref="Text.FontFamilyCatalog"/>). Kamidori sets 1. Missing or non-numeric means 0.</summary>
    public bool UseProportionalFont => ReadInt("USEPROPORTIONALFONT", 0) != 0;

    /// <summary>VALIDFONTCHECKER (SYS4433), one character per pair; the key repeats, and each pair adds to the list in
    /// file order. A family that lacks any of these characters leaves the installed-font list
    /// (<see cref="Text.FontFamilyCatalog"/>). Kamidori lists 悪, ｱ and 洒.</summary>
    public IReadOnlyList<string> ValidFontCheckers
        => _pairs.Where(pair => string.Equals(pair.Key, "VALIDFONTCHECKER", StringComparison.OrdinalIgnoreCase))
                 .Select(pair => pair.Value).ToArray();

    /// <summary>RUBYSHIFTMODE (message:RubyShiftMode, registered default 0; SYS4433). Nonzero drops the furigana's
    /// one-pixel drop and its mode-3 raise by the effect offset, in live text and History
    /// (<see cref="Text.RubyLayoutRules"/>). Kamidori sets 1. Missing or non-numeric means 0.</summary>
    public int RubyShiftMode => ReadInt("RUBYSHIFTMODE", 0);

    private int ReadInt(string key, int fallback)
        => int.TryParse(GetValueOrDefault(key)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
               out int value) ? value : fallback;

    /// <summary>The catalog's trailer: the VM metadata, the settings pairs and, since AGE 4.38D, the font list
    /// (docs/sys4-format-notes.md, "SYS4INI trailer"). Anything after the last block the reader knows is refused, so
    /// no game runs past bytes nobody has reversed.</summary>
    internal static Sys4StartupSettings ParseTrailer(
        byte[] blob, ref int offset, string source, out IReadOnlyList<Sys4CatalogFont> fonts)
    {
        Sys4StartupSettings settings = ReadSettings(blob, ref offset, source);
        fonts = offset == blob.Length ? [] : ReadFonts(blob, ref offset, source);
        if (offset != blob.Length)
            throw new InvalidDataException($"{source}: the catalog trailer has {blob.Length - offset} trailing bytes");
        return settings;
    }

    /// <summary>
    /// As <see cref="ParseTrailer"/>, for a first look at an install the engine has no profile for
    /// (<see cref="InstallIntake"/>): bytes the reader does not know come back in <paramref name="unread"/> instead of
    /// being refused, with the reason in <paramref name="problem"/>. Settings that do not parse leave all of the
    /// trailer unread; a font list that does not parse leaves itself and what follows it. A game is never run from
    /// this reading.
    /// </summary>
    internal static Sys4StartupSettings ParseTrailerLeniently(
        byte[] blob, ref int offset, string source, out IReadOnlyList<Sys4CatalogFont> fonts, out byte[] unread,
        out string? problem)
    {
        int start = offset;
        fonts = [];
        Sys4StartupSettings settings;
        try
        {
            settings = ReadSettings(blob, ref offset, source);
        }
        catch (InvalidDataException error)
        {
            unread = blob.AsSpan(start).ToArray();
            problem = error.Message;
            offset = blob.Length;
            return Empty;
        }

        int afterSettings = offset;
        problem = null;
        if (offset != blob.Length)
        {
            try
            {
                fonts = ReadFonts(blob, ref offset, source);
                if (offset != blob.Length) problem = $"{blob.Length - offset} bytes follow the font list";
            }
            catch (InvalidDataException error)
            {
                fonts = [];
                offset = afterSettings;
                problem = $"{blob.Length - offset} bytes follow the settings pairs and are no font list ({error.Message})";
            }
        }
        unread = blob.AsSpan(offset).ToArray();
        offset = blob.Length;
        return settings;
    }

    /// <summary>Reads the VM metadata and the settings pairs, and leaves <paramref name="offset"/> after them. A
    /// catalog may end before either (an append catalog's has neither).</summary>
    private static Sys4StartupSettings ReadSettings(byte[] blob, ref int offset, string source)
    {
        if (offset == blob.Length) return Empty;

        uint vmMetadataBytes = ReadU32(blob, ref offset, source, "VM metadata length");
        if (vmMetadataBytes > blob.Length - offset)
            throw new InvalidDataException(
                $"{source}: VM metadata length {vmMetadataBytes} exceeds {blob.Length - offset} remaining bytes");
        offset += checked((int)vmMetadataBytes);
        if (offset == blob.Length) return Empty;

        uint stringBytes = ReadU32(blob, ref offset, source, "settings string length");
        if (stringBytes == 0) return Empty;
        if (offset > blob.Length - 4)
            throw new InvalidDataException($"{source}: settings pair count is truncated");
        if (stringBytes > blob.Length - offset - 4)
            throw new InvalidDataException(
                $"{source}: settings string length {stringBytes} exceeds the remaining block");

        uint pairCount = ReadU32(blob, ref offset, source, "settings pair count");
        if (pairCount > stringBytes / 2)
            throw new InvalidDataException(
                $"{source}: settings pair count {pairCount} cannot fit in {stringBytes} string bytes");

        int end = checked(offset + (int)stringBytes);
        var pairs = new List<KeyValuePair<string, string>>(checked((int)pairCount));
        for (int i = 0; i < pairCount; i++)
        {
            string key = ReadCString(blob, ref offset, end, source, $"settings key {i}");
            string value = ReadCString(blob, ref offset, end, source, $"settings value {i}");
            pairs.Add(new KeyValuePair<string, string>(key, value));
        }
        if (offset != end)
            throw new InvalidDataException(
                $"{source}: settings pair count consumed {offset} bytes through the blob, expected {end}");
        return new Sys4StartupSettings(pairs);
    }

    /// <summary>
    /// Reads the font list that follows the settings pairs since AGE 4.38D (engine_load_sys4ini_catalog@0x414290 of
    /// 創刻のアテリアル's build): a byte count, then records of a catalog file id and a NUL-terminated face name until
    /// that many bytes are read. A count below 1 means no fonts, as native reads it.
    /// </summary>
    private static IReadOnlyList<Sys4CatalogFont> ReadFonts(byte[] blob, ref int offset, string source)
    {
        int listBytes = unchecked((int)ReadU32(blob, ref offset, source, "font list length"));
        if (listBytes < 1) return [];
        if (listBytes > blob.Length - offset)
            throw new InvalidDataException(
                $"{source}: font list length {listBytes} exceeds {blob.Length - offset} remaining bytes");

        int end = offset + listBytes;
        var fonts = new List<Sys4CatalogFont>();
        while (offset < end)
        {
            if (offset > end - 4)
                throw new InvalidDataException($"{source}: font record {fonts.Count} is truncated");
            int fileId = unchecked((int)ReadU32(blob, ref offset, source, $"font {fonts.Count} file id"));
            string faceName = ReadCString(blob, ref offset, end, source, $"font {fonts.Count} face name");
            fonts.Add(new Sys4CatalogFont(fileId, faceName));
        }
        return fonts;
    }

    private static uint ReadU32(byte[] blob, ref int offset, string source, string field)
    {
        if (offset > blob.Length - 4)
            throw new InvalidDataException($"{source}: {field} is truncated");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    private static string ReadCString(
        byte[] blob, ref int offset, int end, string source, string field)
    {
        if (offset >= end)
            throw new InvalidDataException($"{source}: {field} is missing");
        int zero = blob.AsSpan(offset, end - offset).IndexOf((byte)0);
        if (zero < 0)
            throw new InvalidDataException($"{source}: {field} is not NUL-terminated");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string value = Encoding.GetEncoding(932).GetString(blob, offset, zero);
        offset += zero + 1;
        return value;
    }
}

/// <summary>A font file a game ships in its catalog and the face it provides: one record of the trailer's font list
/// (AGE 4.38D). <see cref="FileId"/> is the catalog's raw index of the font file.</summary>
public sealed record Sys4CatalogFont(int FileId, string FaceName);

/// <summary>The validated per-game logical canvas selected before presentation allocation.</summary>
public readonly record struct Sys4LogicalCanvas(int Width, int Height)
{
    public const int DefaultWidth = 640;
    public const int DefaultHeight = 480;
    public const int MaximumDimension = 16_384;
    public const int MaximumPixels = 67_108_864;

    public int PixelCount => checked(Width * Height);
    public int RgbaByteCount => checked(PixelCount * 4);

    public static Sys4LogicalCanvas FromSettings(Sys4StartupSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int width = ReadDimension(settings, "SCREENX", DefaultWidth);
        int height = ReadDimension(settings, "SCREENY", DefaultHeight);
        if (width > MaximumDimension || height > MaximumDimension)
            throw new InvalidDataException(
                $"SYS4 logical canvas {width}x{height} exceeds maximum dimension {MaximumDimension}");
        long pixels = (long)width * height;
        if (pixels > MaximumPixels)
            throw new InvalidDataException(
                $"SYS4 logical canvas {width}x{height} exceeds maximum pixel count {MaximumPixels}");
        return new Sys4LogicalCanvas(width, height);
    }

    private static int ReadDimension(
        Sys4StartupSettings settings, string key, int fallback)
    {
        string? raw = settings.GetValueOrDefault(key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
               && value > 0
            ? value
            : fallback;
    }
}
