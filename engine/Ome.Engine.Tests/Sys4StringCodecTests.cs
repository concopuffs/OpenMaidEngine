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
    public void RejectsNonString()
    {
        // 0x00000000 XOR-decodes to bytes 0xFF,0xFF,0xFF,0xFF -> no NUL, invalid
        uint[] dw = { 0x00000000u };
        var (text, _) = Sys4StringCodec.Decode(dw, 0);
        Assert.Null(text);
    }
}
