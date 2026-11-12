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
    public void RejectsBytesCp932DoesNotRoundTrip()
    {
        // 0xFD is no CP932 lead byte: refused rather than decoded to a character that encodes back otherwise.
        byte[] raw = [0x41, 0xFD, 0x42, 0x00];
        uint[] dw = [BitConverter.ToUInt32(raw, 0) ^ 0xFFFFFFFFu];
        Assert.Null(Sys4StringCodec.Decode(dw, 0).Text);
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
