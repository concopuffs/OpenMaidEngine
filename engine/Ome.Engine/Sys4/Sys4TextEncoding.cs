using System.Text;

namespace Ome.Engine.Sys4;

/// <summary>
/// The text form of a SYS4 script's strings, which decodes any byte string and encodes it back byte for byte.
/// Native AGE keeps a string as bytes: it reads every byte before the NUL and lays out a byte below 0x80, or from
/// 0xA0 to 0xDF, as a character by itself, and any other byte as the lead of a two-byte character with the byte after
/// it (adv_text_append_horizontal_glyph_records@0x45b710; docs/engine-re.md, "Strings with control characters"). GDI
/// draws each character by its code.
/// <para>Each such character unit decodes through CP932 when CP932 decodes it to one character and encodes that
/// character back to the same bytes: ASCII and control characters, half-width katakana, JIS X 0208, NEC row 13, the
/// IBM extensions (0xFA-0xFC), the user-defined area (0xF0-0xF9, to U+E000-U+E757) and the single byte 0xA0 (to
/// U+F8F0). Any other unit decodes to one private-use scalar, <see cref="RawBase"/> plus its code (U+F0000 plus the
/// byte, or plus lead &lt;&lt; 8 | trail): the NEC-selected IBM extensions (0xED, 0xEE), whose characters CP932
/// encodes as their IBM codes; the IBM codes whose characters it encodes to NEC row 13 or JIS X 0208 (0xFA4A-0xFA54,
/// 0xFA58-0xFA5B); the unused leads 0xEB, 0xEC and 0xEF; a unit led by 0x80 or 0xFD-0xFF, which CP932 reads as two
/// characters; a bad trail byte; and a lead with no byte after it. That scalar encodes back to its bytes, and its code
/// is what the glyph request carries.</para>
/// <para>Characters from other text (Lua, language packages) encode through CP932; the encoder fallback decides what
/// happens to one it lacks.</para>
/// </summary>
public sealed class Sys4TextEncoding : Encoding
{
    /// <summary>The first raw scalar: plane 15's private-use area. A raw unit's scalar is this plus its code.</summary>
    public const int RawBase = 0xF0000;

    /// <summary>Throws on a character CP932 lacks.</summary>
    public static Sys4TextEncoding Strict { get; }

    /// <summary>Writes '?' for a character CP932 lacks, as <c>Encoding.GetEncoding(932)</c> does.</summary>
    public static Sys4TextEncoding Replacing { get; }

    // Marks a unit CP932 does not decode, or a character it does not encode, without the cost of an exception.
    private const char NotDecoded = '\uffff';
    private static readonly Encoding ProbeCp932;
    // The scalar each unit decodes to: by byte for a single-byte unit, by (lead - 0x80) << 8 | trail for a two-byte
    // one (leads 0x80-0xFF; the single-byte leads 0xA0-0xDF stay unused).
    private static readonly int[] SingleByteUnits = new int[0x100];
    private static readonly int[] DoubleByteUnits = new int[0x80 << 8];

    private readonly Encoding _cp932;

    static Sys4TextEncoding()
    {
        RegisterProvider(CodePagesEncodingProvider.Instance);
        ProbeCp932 = GetEncoding(932, new EncoderReplacementFallback(""),
                                 new DecoderReplacementFallback(NotDecoded.ToString()));
        for (int b = 0; b < 0x100; b++) SingleByteUnits[b] = DecodeUnit([(byte)b]);
        for (int lead = 0x80; lead < 0x100; lead++)
        {
            if (IsSingleByteUnit((byte)lead)) continue;
            for (int trail = 0; trail < 0x100; trail++)
                DoubleByteUnits[(lead - 0x80) << 8 | trail] = DecodeUnit([(byte)lead, (byte)trail]);
        }
        // After the provider is registered: each instance wraps the code page.
        Strict = new Sys4TextEncoding(EncoderFallback.ExceptionFallback);
        Replacing = new Sys4TextEncoding(EncoderFallback.ReplacementFallback);
    }

    private Sys4TextEncoding(EncoderFallback encoderFallback)
        : base(932, encoderFallback, DecoderFallback.ExceptionFallback)
    {
        _cp932 = GetEncoding(932, encoderFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>Whether native lays a byte out as a character by itself: below 0x80, or from 0xA0 to 0xDF.</summary>
    public static bool IsSingleByteUnit(byte value) => value < 0x80 || value is >= 0xA0 and <= 0xDF;

    /// <summary>Whether a scalar is a raw unit's, and its code (one byte, or lead &lt;&lt; 8 | trail).</summary>
    public static bool TryGetRawCode(int scalar, out int code)
    {
        code = scalar - RawBase;
        return code is >= 0 and <= 0xFFFF;
    }

    private static int DecodeUnit(byte[] unit)
    {
        int code = unit.Length == 1 ? unit[0] : unit[0] << 8 | unit[1];
        string text = ProbeCp932.GetString(unit);
        if (text.Contains(NotDecoded) || !Rune.TryGetRuneAt(text, 0, out Rune rune)
            || rune.Utf16SequenceLength != text.Length)
            return RawBase + code;
        return ProbeCp932.GetBytes(text).AsSpan().SequenceEqual(unit) ? rune.Value : RawBase + code;
    }

    // ---- decoding: every unit to one scalar ----

    private static int NextUnit(ReadOnlySpan<byte> bytes, ref int i)
    {
        byte b = bytes[i];
        if (IsSingleByteUnit(b) || i + 1 >= bytes.Length)
        {
            i++;
            return IsSingleByteUnit(b) ? SingleByteUnits[b] : RawBase + b;   // a lead with nothing after it stays raw
        }
        int scalar = DoubleByteUnits[(b - 0x80) << 8 | bytes[i + 1]];
        i += 2;
        return scalar;
    }

    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ReadOnlySpan<byte> span = bytes.AsSpan(index, count);
        int chars = 0;
        for (int i = 0; i < span.Length;) chars += NextUnit(span, ref i) > 0xFFFF ? 2 : 1;
        return chars;
    }

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ReadOnlySpan<byte> span = bytes.AsSpan(byteIndex, byteCount);
        int at = charIndex;
        for (int i = 0; i < span.Length;)
        {
            int scalar = NextUnit(span, ref i);
            if (scalar > 0xFFFF)
            {
                if (at + 2 > chars.Length) throw new ArgumentException("The output buffer is too small.", nameof(chars));
                new Rune(scalar).EncodeToUtf16(chars.AsSpan(at));
                at += 2;
            }
            else
            {
                if (at >= chars.Length) throw new ArgumentException("The output buffer is too small.", nameof(chars));
                chars[at++] = (char)scalar;
            }
        }
        return at - charIndex;
    }

    // ---- encoding: raw scalars to their bytes, the rest through CP932 ----

    public override int GetByteCount(char[] chars, int index, int count)
    {
        int bytes = 0;
        Walk(chars.AsSpan(index, count), text => bytes += _cp932.GetByteCount(text), code => bytes += code > 0xFF ? 2 : 1);
        return bytes;
    }

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        int at = byteIndex;
        Walk(chars.AsSpan(charIndex, charCount),
            text => at += _cp932.GetBytes(text, bytes.AsSpan(at)),
            code =>
            {
                if (code > 0xFF) bytes[at++] = (byte)(code >> 8);
                bytes[at++] = (byte)code;
            });
        return at - byteIndex;
    }

    private delegate void TextRun(ReadOnlySpan<char> text);

    // Hands each run of ordinary text to the code page and each raw scalar to its own writer.
    private static void Walk(ReadOnlySpan<char> chars, TextRun text, Action<int> raw)
    {
        int start = 0;
        for (int i = 0; i + 1 < chars.Length; i++)
        {
            if (!char.IsSurrogatePair(chars[i], chars[i + 1])) continue;
            if (!TryGetRawCode(char.ConvertToUtf32(chars[i], chars[i + 1]), out int code)) continue;
            if (i > start) text(chars[start..i]);
            raw(code);
            start = i + 2;
            i++;
        }
        if (start < chars.Length) text(chars[start..]);
    }

    public override int GetMaxByteCount(int charCount) => checked((charCount + 1) * 2);

    // A unit is one or two bytes and decodes to one scalar, at most two chars.
    public override int GetMaxCharCount(int byteCount) => checked((byteCount + 1) * 2);
}
