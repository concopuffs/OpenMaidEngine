using Age.Engine.Model;
using Age.Engine.Text;

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
