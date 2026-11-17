using System.Text;
using Ome.Engine.Model;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Xunit;

/// <summary>Sys4TextEncoding: a script string's bytes decode to text that encodes back to the same bytes, and a unit
/// CP932 does not round-trip keeps its code where it is counted, saved and drawn.</summary>
public class Sys4TextEncodingTests
{
    private static int Raw(int code) => Sys4TextEncoding.RawBase + code;

    private static string Text(params int[] scalars) => string.Concat(scalars.Select(char.ConvertFromUtf32));

    [Fact]
    public void EveryCharacterUnitDecodesToOneScalarAndEncodesBack()
    {
        var units = new List<byte[]>();
        for (int b = 1; b < 0x100; b++)
            if (Sys4TextEncoding.IsSingleByteUnit((byte)b)) units.Add([(byte)b]);
        for (int lead = 0x80; lead < 0x100; lead++)
        {
            if (Sys4TextEncoding.IsSingleByteUnit((byte)lead)) continue;
            for (int trail = 1; trail < 0x100; trail++) units.Add([(byte)lead, (byte)trail]);
        }

        foreach (byte[] unit in units)
        {
            string text = Sys4TextEncoding.Strict.GetString(unit);
            Assert.Single(text.EnumerateRunes());
            Assert.Equal(unit, Sys4TextEncoding.Strict.GetBytes(text));
        }
    }

    [Fact]
    public void ALeadWithNothingAfterItStaysRaw()
    {
        string text = Sys4TextEncoding.Strict.GetString([0x41, 0x82]);

        Assert.Equal(Text('A', Raw(0x82)), text);
        Assert.Equal([0x41, 0x82], Sys4TextEncoding.Strict.GetBytes(text));
    }

    [Fact]
    public void ACharacterWithTwoCodesTakesTheOneWindowsEncodesTo()
    {
        // 纊 is NEC-selected IBM 0xED40 and IBM 0xFA5C; Windows' code page encodes it to 0xFA5C, so that unit decodes
        // to the character and 0xED40 stays raw.
        Assert.Equal("纊", Sys4TextEncoding.Strict.GetString([0xFA, 0x5C]));
        Assert.Equal(Text(Raw(0xED40)), Sys4TextEncoding.Strict.GetString([0xED, 0x40]));
        Assert.Equal([0xFA, 0x5C], Sys4TextEncoding.Strict.GetBytes("纊"));
    }

    [Fact]
    public void TextFromElsewhereEncodesThroughCp932AndItsFallback()
    {
        Assert.Throws<EncoderFallbackException>(() => Sys4TextEncoding.Strict.GetBytes("é"));
        Assert.Equal("?"u8.ToArray(), Sys4TextEncoding.Replacing.GetBytes("é"));
        Assert.Equal([0x82, 0xA0, 0xEB, 0x40, 0x41], Sys4TextEncoding.Replacing.GetBytes(Text('あ', Raw(0xEB40), 'A')));
    }

    [Fact]
    public void ARawUnitTakesTheCellsOfItsBytes()
    {
        Assert.Equal(2, CharacterCells.HalfCells(Raw(0xEB40)));
        Assert.Equal(1, CharacterCells.HalfCells(Raw(0x82)));
        Assert.Equal(5, CharacterCells.HalfCells(Text('A', Raw(0xEB40), Raw(0x8120))));
    }

    [Fact]
    public void ARawUnitAsksGdiForItsOwnCode()
    {
        var style = new AdvTextStyle(
            PrimaryFontSize: 24, RubyFontSize: 0, Bold: false, TextColor: 0xffffff, EffectColor: 0, RenderMode: 0,
            EffectOffsetX: 0, EffectOffsetY: 0, LineSpacing: 0, FontFace: "");

        IReadOnlyList<GlyphRasterRequest> requests = ImmediateSurfaceTextRenderer.CreateRequests(
            Text(Raw(0x8041), Raw(0xED40), 0x2460), style, GlyphRasterPolicy.NativeCp932Gray4);

        Assert.Equal([(ushort)0x8041, (ushort)0xED40, (ushort)0x8740], requests.Select(request => request.Cp932Code));
    }

    [Fact]
    public void ALuaWriteMayCarryARawUnitBack()
    {
        string text = Sys4TextEncoding.Strict.GetString([0xED, 0x40, 0x41]);

        Assert.Equal(text, Ome.Engine.Modding.CellRules.Text(text, 932));
        Assert.Throws<Ome.Engine.Modding.ScriptAccessException>(() => Ome.Engine.Modding.CellRules.Text("é", 932));
    }

    [Fact]
    public void ANumberedSaveKeepsAStringsRawUnits()
    {
        string text = Sys4TextEncoding.Strict.GetString([0xED, 0x40, 0x80, 0x41, 0xFD, 0x43]);
        NativeNumberedSaveState state = NativeNumberedSaveCodec.Empty(
            [new NativeSavedScriptFrame(-1, 0, Array.Empty<int>(), -1, -1)]) with { StringGlobals = [text] };

        Assert.Equal([text], NativeNumberedSaveCodec.Decode(NativeNumberedSaveCodec.Encode(state)).StringGlobals);
    }
}
