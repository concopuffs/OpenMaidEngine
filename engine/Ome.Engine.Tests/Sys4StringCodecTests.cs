using Ome.Engine.Sys4;
using Xunit;

public class Sys4StringCodecTests
{
    [Fact]
    public void DecodesXorFfCp932Ascii()
    {
        // "AB\0\0" little-endian = 0x00004241, stored XOR 0xFFFFFFFF
        uint[] dw = { 0x00004241u ^ 0xFFFFFFFFu };
        var (text, nd) = Sys4StringCodec.Decode(dw, 0);
        Assert.Equal("AB", text);
        Assert.Equal(1, nd);
    }

    [Fact]
    public void DecodesHalfWidthKatakanaBetweenFullWidthCharacters()
    {
        // Kamidori ITMES "ぐろｰぶ": a single-byte half-width prolonged mark (0xB0) between full-width kana.
        byte[] raw = [0x82, 0xAE, 0x82, 0xEB, 0xB0, 0x82, 0xD4, 0x00];
        uint[] dw = Enumerable.Range(0, raw.Length / 4)
            .Select(k => BitConverter.ToUInt32(raw, k * 4) ^ 0xFFFFFFFFu).ToArray();
        var (text, nd) = Sys4StringCodec.Decode(dw, 0);
        Assert.Equal("ぐろｰぶ", text);
        Assert.Equal(2, nd);
    }

    [Fact]
    public void DecodesControlCharactersAsNativeLaysThemOut()
    {
        // The translated Kamidori install's SC2310@0x1232: a tab, a space, then the text. Native draws the tab as a
        // single-byte glyph, so the string decodes whole and encodes back to the same bytes.
        byte[] raw = [0x09, 0x20, 0x81, 0x5C, 0x54, 0x68, 0x65, 0x00];
        uint[] dw = Enumerable.Range(0, raw.Length / 4)
            .Select(k => BitConverter.ToUInt32(raw, k * 4) ^ 0xFFFFFFFFu).ToArray();
        var (text, nd) = Sys4StringCodec.Decode(dw, 0);
        Assert.Equal("\u0009 ―The", text);
        Assert.Equal(2, nd);
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        Assert.Equal(raw[..^1], System.Text.Encoding.GetEncoding(932).GetBytes(text!));
    }

    [Fact]
    public void KeepsBytesOutsideJisX0208ByteForByte()
    {
        // Native reads every byte before the NUL and checks none. Each character unit below decodes to one character
        // and the string encodes back to the same bytes: NEC row 13, IBM, NEC-selected IBM, user-defined, an unused
        // lead, a 0x80 lead, the single byte 0xA0, a 0xFD lead and a bad trail.
        byte[] raw =
        [
            0x87, 0x40, 0xFA, 0x40, 0xED, 0x40, 0xF0, 0x40, 0xEB, 0x40, 0x80, 0x41, 0xA0, 0x42, 0xFD, 0x43,
            0x81, 0x20, 0x00,
        ];
        byte[] padded = [.. raw, .. new byte[(4 - raw.Length % 4) % 4]];
        uint[] dw = Enumerable.Range(0, padded.Length / 4)
            .Select(k => BitConverter.ToUInt32(padded, k * 4) ^ 0xFFFFFFFFu).ToArray();
        var (text, nd) = Sys4StringCodec.Decode(dw, 0);
        int R(int code) => Sys4TextEncoding.RawBase + code;
        Assert.Equal(
            [0x2460, 0x2170, R(0xED40), 0xE000, R(0xEB40), R(0x8041), 0xF8F0, 0x42, R(0xFD43), R(0x8120)],
            text!.EnumerateRunes().Select(rune => rune.Value));
        Assert.Equal(5, nd);
        Assert.Equal(raw[..^1], Sys4TextEncoding.Strict.GetBytes(text));
    }

    [Fact]
    public void RejectsNonString()
    {
        // 0x00000000 XOR-decodes to bytes 0xFF,0xFF,0xFF,0xFF -> no NUL, invalid
        uint[] dw = { 0x00000000u };
        var (text, _) = Sys4StringCodec.Decode(dw, 0);
        Assert.Null(text);
    }
}
