using Ome.Engine.Model;
using Ome.Engine.Text;

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
    public void RequestsDifferByAntialias()
    {
        var gray = new GlyphRasterRequest(
            "face", 24, -12, 700, 0x3042, 0x82a0, GlyphRasterPolicy.NativeCp932Gray4);
        var aliased = new GlyphRasterRequest(
            "face", 24, -12, 700, 0x3042, 0x82a0, GlyphRasterPolicy.NativeCp932Gray4, antialias: false);

        Assert.True(gray.Antialias);
        Assert.False(aliased.Antialias);
        Assert.NotEqual(gray, aliased);
        Assert.Equal(aliased, aliased with { });
    }

    [Fact]
    public void MonochromeMaskHoldsOnlyEmptyOrFullCoverage()
    {
        Assert.True(new GlyphMask(2, 1, 2, 0, 1, 2, 0, 2, 1, [0, 16], monochrome: true).Monochrome);
        Assert.Throws<ArgumentException>(
            () => new GlyphMask(2, 1, 2, 0, 1, 2, 0, 2, 1, [0, 8], monochrome: true));
    }

    [Fact]
    public void AliasedGlyphReplacesTheDestinationPixel()
    {
        // text_blit_glyph_bitmap: a set bit is alpha 255, which writes source RGB and alpha directly.
        var destination = new RgbaImage(1, 1, [10, 20, 30, 128]);
        var mask = new GlyphMask(1, 1, 1, 0, 1, 1, 0, 1, 1, [16], monochrome: true);

        AgeGlyphMaskCompositor.DrawMask(destination, mask, 0, 1, 0x123456);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 255 }, destination.Pixels);
    }

    [Fact]
    public void ModeTwoSecondPassOfAnAliasedGlyphShiftsEachBytesPixels()
    {
        // (signed char)byte >> 2 over GGO_BITMAP rows: within each 8-pixel byte the pixels move two to the right and
        // the first pixel fills the first two.
        byte[] row = [16, 0, 16, 0, 0, 0, 0, 16, 16, 0, 0, 0];
        var mask = new GlyphMask(10, 1, 12, 0, 1, 10, 0, 10, 1, row, monochrome: true);

        GlyphMask pass = AgeGlyphMaskCompositor.ModeTwoBitmapPass(mask);

        Assert.True(pass.Monochrome);
        Assert.Equal(new byte[] { 16, 16, 16, 0, 16, 0, 0, 0, 16, 16, 0, 0 }, pass.Coverage.ToArray());
        Assert.Throws<ArgumentException>(() => AgeGlyphMaskCompositor.ModeTwoBitmapPass(Mask()));

        var destination = Image(10, 1);
        AgeGlyphMaskCompositor.DrawGlyph(destination, mask, 0, 0, 1, Style(renderMode: 2));
        Assert.Equal(
            new byte[] { 255, 255, 255, 0, 255, 0, 0, 255, 255, 255 },
            Enumerable.Range(0, 10).Select(x => Pixel(destination, x, 0).A).ToArray());
    }

    [Theory]
    [InlineData(true, 1, 3, 3, 5, 4)]
    [InlineData(false, 1, 5, 4, 7, 5)]
    [InlineData(false, 0, 3, 3, -1, -1)]
    public void AliasedEffectTextStartsAtTheEffectOffset(
        bool antialias, int renderMode, int primaryX, int primaryY, int effectX, int effectY)
    {
        // With AntiFont off the ADV builders go through text_draw_string_primary_or_ruby@0x44fac0, which offsets a
        // mode 1-3 string by the effect offset first. The record keeps the measured cell.
        var mask = new GlyphMask(1, 1, 1, 0, 1, 4, 0, 4, 5, [16], ascent: 1, monochrome: !antialias);
        var destination = Image(16, 16);
        var engine = new RetainedGlyphLayoutEngine(new CountingRasterizer(_ => mask));
        AdvTextStyle style = Style(renderMode) with
        {
            EffectOffsetX = 2, EffectOffsetY = 1, EffectColor = 0x0000ff, TextColor = 0xffffff,
        };

        GlyphTextLayoutResult result = engine.Render(
            destination,
            new GlyphTextLayoutOptions(3, 3, 3, 16, 16, true, style),
            [new GlyphRasterRequest("face", 5, -2, 700, 0x3042, 0x82a0, GlyphRasterPolicy.NativeCp932Gray4,
                antialias)]);

        Assert.Equal(new AdvRetainedGlyphRecord(0, 3, 3, 7, 8), Assert.Single(result.Records));
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), Pixel(destination, primaryX, primaryY));
        if (effectX >= 0)
            Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), Pixel(destination, effectX, effectY));
        Assert.Equal(renderMode == 0 ? 1 : 2,
            Enumerable.Range(0, 16 * 16).Count(i => destination.Pixels[i * 4 + 3] != 0));
    }

    [Fact]
    public void SurfaceTextKeepsItsPositionWhenAliased()
    {
        // draw_string_to_surface@0x450150 (op 0x204) calls the raster workers directly.
        var requests = new List<GlyphRasterRequest>();
        var renderer = new ImmediateSurfaceTextRenderer(new CountingRasterizer(request =>
        {
            requests.Add(request);
            return new GlyphMask(1, 1, 1, 0, 1, 4, 0, 4, 5, [16], ascent: 1, monochrome: !request.Antialias);
        }));
        AdvTextStyle style = Style(renderMode: 1) with { EffectOffsetX = 2, EffectOffsetY = 1 };
        var destination = Image(16, 16);

        renderer.Render(destination, 3, 3, "あ", style, antialias: false);

        Assert.False(Assert.Single(requests).Antialias);
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), Pixel(destination, 3, 3));
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

    [Theory]
    // SYS4433 divides by 17: full coverage is 240, half is 120. An aliased glyph stays opaque.
    [InlineData(16, false, 240)]
    [InlineData(8, false, 120)]
    [InlineData(16, true, 255)]
    public void Sys4433CoverageNeverReachesOpaque(byte coverage, bool monochrome, byte alpha)
    {
        var destination = Image(1, 1);
        var mask = new GlyphMask(1, 1, 1, 0, 1, 1, 0, 1, 1, [coverage], monochrome: monochrome);

        AgeGlyphMaskCompositor.DrawMask(destination, mask, 0, 1, 0x123456,
            coverageAlpha: GlyphCoverageAlpha.Seventeenths);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, alpha }, destination.Pixels);
    }

    [Fact]
    public void Sys4433TextOverItsOutlineKeepsSomeOfTheOutline()
    {
        // Kamidori's mode-3 text: the 84-grey outline, then white over it, both at 240, give 244.
        var destination = Image(1, 1);
        AgeGlyphMaskCompositor.DrawMask(destination, Mask(), 0, 1, 0x545454,
            coverageAlpha: GlyphCoverageAlpha.Seventeenths);
        AgeGlyphMaskCompositor.DrawMask(destination, Mask(), 0, 1, 0xffffff,
            coverageAlpha: GlyphCoverageAlpha.Seventeenths);

        Assert.Equal(new byte[] { 244, 244, 244, 240 }, destination.Pixels);
    }

    [Theory]
    [InlineData("SYS4433", GlyphCoverageAlpha.Seventeenths)]
    [InlineData("SYS4422", GlyphCoverageAlpha.Sixteenths)]
    public void EachEngineRevisionHasItsOwnBlit(string engineAbiId, GlyphCoverageAlpha expected)
        => Assert.Equal(expected, AgeGlyphMaskCompositor.CoverageAlphaFor(engineAbiId));

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
    public void ModeThreeRadiusOneMatchesNativeTwelveSamples()
    {
        // AGE converts the sample angle with angle * 180 / pi, so (1,1) never reaches (+1,-1) or (-1,+1); a
        // 2026-11-06 AntiFont-off capture of SC0000 page 2 shows exactly these six neighbours.
        IReadOnlyList<(int X, int Y)> offsets =
            AgeGlyphMaskCompositor.GetMode3OutlineOffsets(1, 1);

        Assert.Equal(
            new (int, int)[]
            {
                (1, 0), (1, 1), (0, 1), (-1, 0), (-1, -1), (0, -1),
                (1, 0), (1, 1), (0, 1), (-1, 0), (-1, -1), (0, -1),
            },
            offsets);
    }

    [Theory]
    [InlineData(0, 0, 1)]     // sqrt(0): an infinite step samples angle 0 once
    [InlineData(-1, -1, 1)]   // sqrt of a negative sum: a NaN step samples angle 0 once
    [InlineData(2, 0, 12)]    // the step uses sqrt(rx + ry); sqrt(rx^2 + ry^2) would sample 16 times
    public void ModeThreeSampleCountFollowsTheRadiusSum(int radiusX, int radiusY, int count)
    {
        IReadOnlyList<(int X, int Y)> offsets =
            AgeGlyphMaskCompositor.GetMode3OutlineOffsets(radiusX, radiusY);

        Assert.Equal(count, offsets.Count);
        Assert.Equal((radiusX, 0), offsets[0]);
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
    public void RecordedLineBreaksStartRowsAtTheLineOriginAndNeverWrapAtTheEdge()
    {
        var engine = new RetainedGlyphLayoutEngine(new CountingRasterizer(_ => CellMask()));

        // History (op 0x1d1): the second glyph crosses the right bound without wrapping, two breaks before
        // the third glyph leave a blank row, and a break after the last glyph moves only the cursor.
        GlyphTextLayoutResult result = engine.Render(
            Image(16, 32),
            new GlyphTextLayoutOptions(
                CursorX: 6, CursorY: 1, LineOriginX: 2,
                RightBound: 8, BottomBound: 32, WrapHorizontally: false, Style(),
                LineBreaksBefore: [2, 2, 3]),
            [NativeRequest(0x82a0, 0x3042), NativeRequest(0x82a2, 0x3044), NativeRequest(0x82a4, 0x3046)]);

        Assert.Equal(3, result.ConsumedGlyphs);
        Assert.Equal(0, result.WrappedLines);
        Assert.Collection(result.Records,
            first => Assert.Equal(new AdvRetainedGlyphRecord(0, 6, 1, 10, 6), first),
            second => Assert.Equal(new AdvRetainedGlyphRecord(0, 10, 1, 14, 6), second),
            third => Assert.Equal(new AdvRetainedGlyphRecord(0, 2, 13, 6, 18), third));
        Assert.Equal(
            new[]
            {
                new AdvTextRow(0, 2, 6, 1, 14, 6, WrapsAfter: true),
                new AdvTextRow(2, 1, 2, 13, 6, 18, WrapsAfter: true),
            },
            result.Rows);
        Assert.Equal((2, 19), (result.CursorX, result.CursorY));
    }

    [Fact]
    public void GlyphsSitOnTheFontAscentNotThePixelHeight()
    {
        // One ink row just above the baseline. AGE's baseline is cell top + tmAscent (manager +0x458).
        var mask = new GlyphMask(
            width: 1, height: 1, stride: 1,
            originX: 0, originY: 1,
            cellAdvanceX: 4, cellAdvanceY: 0,
            cellWidth: 4, cellHeight: 5,
            coverage: [16],
            ascent: 3);
        var destination = Image(16, 16);
        var engine = new RetainedGlyphLayoutEngine(new CountingRasterizer(_ => mask));

        GlyphTextLayoutResult result = engine.Render(
            destination,
            new GlyphTextLayoutOptions(
                CursorX: 2, CursorY: 1, LineOriginX: 2,
                RightBound: 16, BottomBound: 16, WrapHorizontally: true,
                Style()),
            [NativeRequest(0x82a0, 0x3042)]);

        Assert.Equal(new AdvRetainedGlyphRecord(0, 2, 1, 6, 6), Assert.Single(result.Records));
        Assert.Equal(255, Pixel(destination, 2, 3).A);
        Assert.Equal(1, Enumerable.Range(0, 16).Sum(y => Pixel(destination, 2, y).A / 255));
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
