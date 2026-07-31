using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Text;

public class GlyphMaskCompositorTests
{
    private sealed class CountingRasterizer(Func<GlyphRasterRequest, GlyphMask> factory)
        : IGlyphMaskRasterizer
    {
        public int Calls { get; private set; }

        public GlyphMask Rasterize(GlyphRasterRequest request)
        {
            Calls++;
            return factory(request);
        }
    }

    [Fact]
    public void NativeRequestRequiresCp932IdentityAndValidScalar()
    {
        Assert.Throws<ArgumentException>(() => new GlyphRasterRequest(
            "face", 24, -12, 700, 0x3042, null, GlyphRasterPolicy.NativeCp932Gray4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphRasterRequest(
            "face", 24, -12, 700, 0x110000, 0x82a0, GlyphRasterPolicy.NativeCp932Gray4));

        var request = new GlyphRasterRequest(
            "face", 24, -12, 700, 0x3042, 0x82a0, GlyphRasterPolicy.NativeCp932Gray4);

        Assert.Equal(("face", 24, -12, 700, 0x3042, (ushort)0x82a0),
            (request.FontFace, request.PixelHeight, request.RequestedWidth, request.Weight,
             request.UnicodeScalar, request.Cp932Code!.Value));
    }

    [Fact]
    public void MaskOwnsValidatedNormalizedCoverage()
    {
        byte[] source = [0, 8, 16, 0];
        var mask = new GlyphMask(2, 1, 4, 0, 1, 2, 0, 2, 3, source);
        source[1] = 0;

        Assert.Equal(new byte[] { 0, 8, 16, 0 }, mask.Coverage.ToArray());
        Assert.Throws<ArgumentException>(
            () => new GlyphMask(1, 1, 1, 0, 0, 1, 0, 1, 1, [17]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GlyphMask(2, 1, 1, 0, 0, 1, 0, 1, 1, [0]));
    }

    [Fact]
    public void Gray4CoverageUsesNativeAlphaAndTransparentDestinationRule()
    {
        var destination = Image(1, 1);
        GlyphMask mask = Mask(coverage: 8);

        Assert.True(AgeGlyphMaskCompositor.DrawMask(
            destination, mask, baselineX: 0, baselineY: 1, rgb: 0x123456));

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 127 }, destination.Pixels);
    }

    [Fact]
    public void ExistingDestinationUsesIntegerRgbBlendAndMaximumAlpha()
    {
        var destination = new RgbaImage(1, 1, [10, 20, 30, 128]);

        AgeGlyphMaskCompositor.DrawMask(
            destination, Mask(coverage: 8), baselineX: 0, baselineY: 1, rgb: 0x6edc0a);

        Assert.Equal(new byte[] { 59, 119, 20, 128 }, destination.Pixels);
    }

    [Fact]
    public void BearingsAndDestinationBoundsClipTheMask()
    {
        var destination = Image(2, 1);
        var mask = new GlyphMask(
            3, 1, 4, originX: -1, originY: 1,
            cellAdvanceX: 3, cellAdvanceY: 0, cellWidth: 3, cellHeight: 1,
            [16, 16, 16, 0]);

        AgeGlyphMaskCompositor.DrawMask(
            destination, mask, baselineX: 0, baselineY: 1, rgb: 0x010203);

        Assert.Equal(new byte[]
        {
            1, 2, 3, 255,
            1, 2, 3, 255,
        }, destination.Pixels);
    }

    [Fact]
    public void ModeOneDrawsDisplacedEffectThenPrimaryMask()
    {
        var destination = Image(3, 1);
        AdvTextStyle style = Style(renderMode: 1) with
        {
            TextColor = 0xff0000,
            EffectColor = 0x0000ff,
            EffectOffsetX = 1,
        };

        AgeGlyphMaskCompositor.DrawGlyph(
            destination, Mask(), cellX: 0, cellY: 0, topToBaseline: 1, style);

        Assert.Equal(new byte[]
        {
            255, 0, 0, 255,
            0, 0, 255, 255,
            0, 0, 0, 0,
        }, destination.Pixels);
    }

    [Fact]
    public void ModeTwoAddsQuarterCoveragePrimaryPass()
    {
        var destination = new RgbaImage(1, 1, [0, 0, 0, 255]);

        AgeGlyphMaskCompositor.DrawGlyph(
            destination, Mask(coverage: 8), 0, 0, 1,
            Style(renderMode: 2) with { TextColor = 0xffffff });

        Assert.Equal(new byte[] { 142, 142, 142, 255 }, destination.Pixels);
    }

    [Fact]
    public void ModeThreeRadiusOneMatchesNativeTwelveSampleEllipse()
    {
        IReadOnlyList<(int X, int Y)> offsets =
            AgeGlyphMaskCompositor.GetMode3OutlineOffsets(1, 1);

        Assert.Equal(12, offsets.Count);
        Assert.Equal(8, offsets.Distinct().Count());
        Assert.Equal(2, offsets.Count(offset => offset == (1, 0)));
        Assert.Equal(2, offsets.Count(offset => offset == (0, 1)));
        Assert.Equal(2, offsets.Count(offset => offset == (-1, 0)));
        Assert.Equal(2, offsets.Count(offset => offset == (0, -1)));
        Assert.Equal(1, offsets.Count(offset => offset == (1, 1)));
        Assert.Equal(1, offsets.Count(offset => offset == (-1, 1)));
        Assert.Equal(1, offsets.Count(offset => offset == (-1, -1)));
        Assert.Equal(1, offsets.Count(offset => offset == (1, -1)));
        Assert.DoesNotContain((0, 0), offsets);
    }

    [Fact]
    public void ModeThreeOverlappingMasksUseTheSameIntegerCompositorBeforePrimary()
    {
        var destination = Image(5, 3);
        var mask = new GlyphMask(
            2, 1, 2, originX: 0, originY: 1,
            cellAdvanceX: 2, cellAdvanceY: 0, cellWidth: 2, cellHeight: 1,
            [8, 16]);
        AdvTextStyle style = Style(renderMode: 3) with
        {
            TextColor = 0xff0000,
            EffectColor = 0x0000ff,
            EffectOffsetX = 1,
            EffectOffsetY = 1,
        };

        AgeGlyphMaskCompositor.DrawGlyph(
            destination, mask, cellX: 2, cellY: 1, topToBaseline: 1, style);

        // The left horizontal outline copy puts opaque blue under the half-coverage primary texel.
        // AGE retains max alpha and blends RGB with integer truncation.
        Assert.Equal((127, 0, 128, 255), Pixel(destination, 2, 1));
        Assert.Equal((255, 0, 0, 255), Pixel(destination, 3, 1));
        Assert.Equal((0, 0, 255, 127), Pixel(destination, 1, 1));
        Assert.Equal((0, 0, 255, 255), Pixel(destination, 4, 1));
    }

    [Fact]
    public void LayoutWrapsBeforeOrdinaryGlyphAndReturnsNativeEdgeRecords()
    {
        var destination = Image(16, 16);
        var renderer = new CountingRasterizer(_ => CellMask());
        var engine = new RetainedGlyphLayoutEngine(renderer);

        GlyphTextLayoutResult result = engine.Render(
            destination,
            new GlyphTextLayoutOptions(
                CursorX: 2, CursorY: 1, LineOriginX: 2,
                RightBound: 8, BottomBound: 16, WrapHorizontally: true, Style()),
            [NativeRequest(0x82a0, 0x3042), NativeRequest(0x82a2, 0x3044)]);

        Assert.Equal(2, result.ConsumedGlyphs);
        Assert.Equal(1, result.WrappedLines);
        Assert.Equal((6, 7), (result.CursorX, result.CursorY));
        Assert.Equal(AdvTextOverflowFlags.Horizontal, result.ObservedOverflow);
        Assert.Collection(result.Records,
            first => Assert.Equal(new AdvRetainedGlyphRecord(0, 2, 1, 6, 6), first),
            second => Assert.Equal(new AdvRetainedGlyphRecord(0, 2, 7, 6, 12), second));
        Assert.Equal(255, Pixel(destination, 2, 1).A);
        Assert.Equal(255, Pixel(destination, 2, 7).A);
    }

    [Fact]
    public void RetainedPresentationCropIncludesInkBelowTheNativeCell()
    {
        var mask = new GlyphMask(
            width: 1, height: 4, stride: 1,
            originX: 0, originY: 3,
            cellAdvanceX: 4, cellAdvanceY: 0,
            cellWidth: 4, cellHeight: 5,
            coverage: [16, 16, 16, 16]);
        var destination = Image(16, 16);
        var engine = new RetainedGlyphLayoutEngine(
            new CountingRasterizer(_ => mask));

        GlyphTextLayoutResult result = engine.Render(
            destination,
            new GlyphTextLayoutOptions(
                CursorX: 2, CursorY: 1, LineOriginX: 2,
                RightBound: 16, BottomBound: 16, WrapHorizontally: true,
                Style(renderMode: 3) with
                {
                    EffectOffsetX = 1,
                    EffectOffsetY = 1,
                }),
            [NativeRequest(0x82a0, 0x3042)]);

        Assert.Equal(
            new AdvRetainedGlyphRecord(0, 2, 1, 6, 6),
            Assert.Single(result.Records));
        Assert.Equal(
            new AdvRetainedGlyphPresentationRect(2, 1, 6, 8),
            Assert.Single(result.PresentationRects));
        Assert.NotEqual(0, Pixel(destination, 2, 7).A);
    }

    [Fact]
    public void ClosingPunctuationStaysOnOverflowingPrecedingLine()
    {
        var engine = new RetainedGlyphLayoutEngine(new CountingRasterizer(_ => CellMask()));

        GlyphTextLayoutResult result = engine.Render(
            Image(16, 16),
            new GlyphTextLayoutOptions(2, 1, 2, 8, 16, true, Style()),
            [NativeRequest(0x82a0, 0x3042), NativeRequest(0x8141, 0x3001)]);

        Assert.Equal(0, result.WrappedLines);
        Assert.Equal((10, 1), (result.CursorX, result.CursorY));
        Assert.Equal(new AdvRetainedGlyphRecord(0, 6, 1, 10, 6), result.Records[1]);
    }

    [Fact]
    public void VerticalOnlyOverflowStopsBeforeCompositingGlyph()
    {
        var rasterizer = new CountingRasterizer(_ => CellMask());
        var engine = new RetainedGlyphLayoutEngine(rasterizer);

        GlyphTextLayoutResult result = engine.Render(
            Image(16, 16),
            new GlyphTextLayoutOptions(2, 12, 2, 8, 16, true, Style()),
            [NativeRequest(0x82a0, 0x3042)]);

        Assert.True(result.StoppedOnVerticalOverflow);
        Assert.Equal(0, result.ConsumedGlyphs);
        Assert.Empty(result.Records);
        Assert.Equal(1, rasterizer.Calls); // metrics are required before the overflow decision
    }

    [Fact]
    public void SimultaneousHorizontalAndVerticalOverflowTakesNativeWrapPath()
    {
        var engine = new RetainedGlyphLayoutEngine(new CountingRasterizer(_ => CellMask()));

        GlyphTextLayoutResult result = engine.Render(
            Image(16, 24),
            new GlyphTextLayoutOptions(6, 12, 2, 8, 16, true, Style()),
            [NativeRequest(0x82a0, 0x3042)]);

        Assert.False(result.StoppedOnVerticalOverflow);
        Assert.Equal(1, result.ConsumedGlyphs);
        Assert.Equal(1, result.WrappedLines);
        Assert.Equal(
            AdvTextOverflowFlags.Horizontal | AdvTextOverflowFlags.Vertical,
            result.ObservedOverflow);
        Assert.Equal(new AdvRetainedGlyphRecord(0, 2, 18, 6, 23), Assert.Single(result.Records));
    }

    [Fact]
    public void GlyphCacheIsBoundedAndUsesLruRecency()
    {
        var inner = new CountingRasterizer(_ => Mask());
        var cached = new CachedGlyphMaskRasterizer(inner, capacity: 2);
        GlyphRasterRequest a = NativeRequest(0x41, 0x41);
        GlyphRasterRequest b = NativeRequest(0x42, 0x42);
        GlyphRasterRequest c = NativeRequest(0x43, 0x43);

        GlyphMask firstA = cached.Rasterize(a);
        cached.Rasterize(b);
        Assert.Same(firstA, cached.Rasterize(a));
        cached.Rasterize(c);
        cached.Rasterize(b);

        Assert.Equal(2, cached.Count);
        Assert.Equal(1, cached.Hits);
        Assert.Equal(4, cached.Misses);
        Assert.Equal(4, inner.Calls);
    }

    [Fact]
    public void GenericCacheEvictionCanReleaseBackendFontResources()
    {
        var evicted = new List<string>();
        var cache = new BoundedLruCache<int, string>(2, evicted.Add);
        cache.Set(1, "font-1");
        cache.Set(2, "font-2");
        Assert.True(cache.TryGetValue(1, out _));

        cache.Set(3, "font-3");

        Assert.Equal(["font-2"], evicted);
        Assert.False(cache.TryGetValue(2, out _));
        Assert.True(cache.TryGetValue(1, out _));
        Assert.True(cache.TryGetValue(3, out _));
    }

    private static GlyphRasterRequest NativeRequest(ushort cp932, int scalar)
        => new("face", 5, -2, 700, scalar, cp932, GlyphRasterPolicy.NativeCp932Gray4);

    private static GlyphMask Mask(byte coverage = 16)
        => new(1, 1, 1, originX: 0, originY: 1,
            cellAdvanceX: 1, cellAdvanceY: 0, cellWidth: 1, cellHeight: 1, [coverage]);

    private static GlyphMask CellMask()
        => new(1, 1, 1, originX: 0, originY: 5,
            cellAdvanceX: 4, cellAdvanceY: 0, cellWidth: 4, cellHeight: 5, [16]);

    private static AdvTextStyle Style(int renderMode = 0)
        => new(
            PrimaryFontSize: 5,
            RubyFontSize: 0,
            Bold: true,
            TextColor: 0xffffff,
            EffectColor: 0,
            RenderMode: renderMode,
            EffectOffsetX: 0,
            EffectOffsetY: 0,
            LineSpacing: 1,
            FontFace: "face");

    private static RgbaImage Image(int width, int height)
        => new(width, height, new byte[checked(width * height * 4)]);

    private static (byte R, byte G, byte B, byte A) Pixel(RgbaImage image, int x, int y)
    {
        int offset = (y * image.Width + x) * 4;
        return (
            image.Pixels[offset],
            image.Pixels[offset + 1],
            image.Pixels[offset + 2],
            image.Pixels[offset + 3]);
    }
}
