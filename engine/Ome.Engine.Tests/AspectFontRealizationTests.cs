using Ome.Engine.Model;
using Ome.Engine.Text;

/// <summary>SYS4433's aspect-mode primary font width (docs/engine-re.md, "Aspect-mode font calibration"), from
/// synthesized GDI metrics.</summary>
public class AspectFontRealizationTests
{
    private const byte Proportional = 34;   // VARIABLE_PITCH | FF_SWISS
    private const byte Fixed = 49;          // FIXED_PITCH | FF_MODERN

    /// <summary>Kamidori's measurements at 22 pixels bold: ＭＳ ゴシック 激 at half width has a 19-pixel black box and a
    /// 23-pixel advance; メイリオ 激 at its natural width has 21 and 22, and tmAveCharWidth 21.</summary>
    private sealed class FakeProbe : IFontMetricsProbe
    {
        public List<(string Face, int Height, int Width, int Weight, ushort Code)> Calls { get; } = new();
        public Dictionary<string, FontProbeMetrics> ByFace { get; } = new()
        {
            [AspectFontRealization.ReferenceFace] = new(11, 19, 23),
            ["メイリオ"] = new(21, 21, 22),
        };

        public FontProbeMetrics Measure(string faceName, int pixelHeight, int width, int weight, ushort code)
        {
            Calls.Add((faceName, pixelHeight, width, weight, code));
            return ByFace[faceName];
        }
    }

    private static readonly EnumeratedFontFamily[] Installed =
        [new("ＭＳ ゴシック", Fixed), new("メイリオ", Proportional), new("IPA Pゴシック", Fixed)];

    [Fact]
    public void AProportionalFaceInAspectModeOneIsCalibratedFromTheReferenceGlyph()
    {
        var probe = new FakeProbe();
        var realization = new AspectFontRealization(Installed, probe);

        // The reference black box (19) is no wider than メイリオ's (21): 19 / 22 * -21 = -18.1, truncated.
        Assert.Equal(-18, realization.RequestedWidth("メイリオ", 22, 700, aspectMode: 1));
        Assert.Equal(new (string, int, int, int, ushort)[]
        {
            ("ＭＳ ゴシック", 22, -11, 700, 0x8c83),
            ("メイリオ", 22, 0, 700, 0x8c83),
        }, probe.Calls);

        // Calibrated once per face, size and weight.
        realization.RequestedWidth("メイリオ", 22, 700, aspectMode: 1);
        Assert.Equal(2, probe.Calls.Count);
    }

    [Fact]
    public void AWiderReferenceBlackBoxDividesTheFacesByTheReferenceAdvance()
    {
        var probe = new FakeProbe();
        probe.ByFace["メイリオ"] = new FontProbeMetrics(21, 17, 22);

        // 17 / 23 * -21 = -15.5, truncated.
        Assert.Equal(-15, new AspectFontRealization(Installed, probe).RequestedWidth("メイリオ", 22, 700, 1));
    }

    [Theory]
    [InlineData("メイリオ", 0)]          // the mode is off
    [InlineData("ＭＳ ゴシック", 1)]      // fixed pitch
    [InlineData("Not Installed", 1)]    // missing from the installed-font list
    [InlineData("メイリオ", 2)]          // mode 2 is not modelled
    public void OtherwiseTheWidthIsHalfTheHeight(string face, int aspectMode)
    {
        var probe = new FakeProbe();

        Assert.Equal(-11, new AspectFontRealization(Installed, probe).RequestedWidth(face, 22, 700, aspectMode));
        Assert.Empty(probe.Calls);
    }

    [Fact]
    public void TheProportionalIpaFacesAreCalibratedWhateverTheirPitch()
    {
        var probe = new FakeProbe();
        probe.ByFace["IPA Pゴシック"] = new FontProbeMetrics(10, 20, 22);

        // 19 / 22 * -10 = -8.6, truncated.
        Assert.Equal(-8, new AspectFontRealization(Installed, probe).RequestedWidth("IPA Pゴシック", 22, 0, 1));
    }

    /// <summary>The single-byte calibration's measurements: Kamidori's メイリオ "6" at 14 pixels regular, as native's
    /// GetGlyphOutlineA calls returned them. At its natural width the face is 21 pixels tall, so font2 is 9 pixels
    /// high, with tmAveCharWidth 9 and a 5-pixel black box; ＭＳ ゴシック 激 at half width has a 13-pixel one.</summary>
    private sealed class SingleByteProbe : IFontMetricsProbe
    {
        public List<(string Face, int Height, int Width, int Weight, ushort Code)> Calls { get; } = new();
        public uint SmallBlackBox { get; set; } = 5;

        public FontProbeMetrics Measure(string faceName, int pixelHeight, int width, int weight, ushort code)
        {
            Calls.Add((faceName, pixelHeight, width, weight, code));
            if (faceName == AspectFontRealization.ReferenceFace) return new(7, 13, 14, Height: 14);
            return pixelHeight == 14 ? new(13, 7, 9, Height: 21) : new(9, SmallBlackBox, 6, Height: 14);
        }
    }

    [Fact]
    public void ASingleByteGlyphWithinHalfTheReferenceBlackBoxTakesTheSmallFontsAverageWidth()
    {
        var probe = new SingleByteProbe();
        var realization = new AspectFontRealization(Installed, probe);

        // font2 = trunc(14 / 21 * 14) = 9 pixels; its black box (5) is within 13 >> 1, so the width is -9.
        Assert.Equal(-9, realization.SingleByteWidth("メイリオ", 14, 0, (ushort)'6', aspectMode: 1));
        Assert.Equal(new (string, int, int, int, ushort)[]
        {
            ("メイリオ", 14, 0, 0, 0x36),
            ("メイリオ", 9, 0, 0, 0x36),
            ("ＭＳ ゴシック", 14, -7, 0, 0x8c83),
        }, probe.Calls);

        // Calibrated once per face, size, weight and character.
        realization.SingleByteWidth("メイリオ", 14, 0, (ushort)'6', 1);
        Assert.Equal(3, probe.Calls.Count);
        realization.SingleByteWidth("メイリオ", 14, 0, (ushort)'1', 1);
        Assert.Equal(6, probe.Calls.Count);
    }

    [Fact]
    public void ASingleByteGlyphWiderThanHalfTheReferenceBlackBoxIsScaledToIt()
    {
        var probe = new SingleByteProbe { SmallBlackBox = 7 };

        // (float)(13 * 0.5) / 7 * -9 = -8.36, truncated.
        Assert.Equal(-8, new AspectFontRealization(Installed, probe).SingleByteWidth("メイリオ", 14, 0, 0x36, 1));
    }

    [Theory]
    [InlineData("メイリオ", 0)]
    [InlineData("ＭＳ ゴシック", 1)]
    [InlineData("Not Installed", 1)]
    [InlineData("メイリオ", 2)]
    public void OtherwiseASingleByteGlyphIsHalfTheHeightWide(string face, int aspectMode)
    {
        var probe = new SingleByteProbe();

        Assert.Equal(-7, new AspectFontRealization(Installed, probe).SingleByteWidth(face, 14, 0, 0x36, aspectMode));
        Assert.Empty(probe.Calls);
    }

    [Fact]
    public void OnlySingleByteCodesHaveASingleByteWidth()
    {
        var realization = new AspectFontRealization(Installed, new SingleByteProbe());

        Assert.Throws<ArgumentOutOfRangeException>(() => realization.SingleByteWidth("メイリオ", 14, 0, 0x8c83, 1));
    }

    [Fact]
    public void SingleByteRequestsOutsideBlanksUseTheirOwnCalibration()
    {
        var realization = new AspectFontRealization(Installed, new SingleByteProbe());
        AdvTextStyle style = AdvTextStyle.Default with { PrimaryFontSize = 14, FontFace = "メイリオ", AspectMode = 1 };

        IReadOnlyList<GlyphRasterRequest> requests =
            ImmediateSurfaceTextRenderer.CreateRequests("6 あ", style, realization: realization);

        int primary = realization.RequestedWidth("メイリオ", 14, 0, 1);
        Assert.Equal(new[] { -9, primary, primary }, requests.Select(request => request.RequestedWidth));
    }

    [Fact]
    public void GlyphRequestsUseTheRealizedWidth()
    {
        var realization = new AspectFontRealization(Installed, new FakeProbe());
        AdvTextStyle style = AdvTextStyle.Default with
        {
            PrimaryFontSize = 22, Bold = true, FontFace = "メイリオ", AspectMode = 1,
        };

        Assert.Equal(-18, ImmediateSurfaceTextRenderer.CreateRequests("叛", style, realization: realization)[0]
            .RequestedWidth);
        Assert.Equal(-11, ImmediateSurfaceTextRenderer.CreateRequests("叛", style)[0].RequestedWidth);
    }
}
