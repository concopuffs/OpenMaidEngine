using System;
using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Devices;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;
using static GraphicsTestSupport;

/// <summary>
/// AGE 4.38D's polygon objects, ops 0x320 to 0x323 (docs/superpowers/specs/2026-11-19-polygon-objects-design.md; read
/// in 創刻のアテリアル's image, where its scripts use them at 22,324 sites): the handlers, the fade's rules, the polygon
/// item of the frame description and what the reference renderer draws for it.
/// </summary>
public class PolygonObjectsTests
{
    private const int Imm = 0, Float = 1, GlobalInt = 3, GlobalFloat = 4;
    private const int Exit = 0x2, EraseObjects = 0x1f7, ClearObjects = 0x1f6, CloneObject = 0x21d, MovFloat = 0x2d5;
    private const int CreatePolygon = 0x320, SetTexture = 0x321, SetColor = 0x322, FadeColor = 0x323;

    private static Operand I(long value) => new(Imm, value);
    private static Operand F(float value) => new(Float, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    private static Operand G(int address) => new(GlobalInt, address);
    private static Operand GF(int address) => new(GlobalFloat, address);
    private static (int, Operand[]) Mov(int address, long value) => (0x55, [G(address), I(value)]);

    // A quad as LOGO.BIN builds its fades: a strip of four vertices from float cells for x, y, z, u and v and integer
    // cells for each vertex's alpha and RGB.
    private static IEnumerable<(int, Operand[])> Quad(long handle, float x0, float y0, float x1, float y1,
                                                      long alpha, long rgb, long flags = 0)
    {
        float[] xs = [x0, x1, x0, x1], ys = [y0, y0, y1, y1], us = [0, 1, 0, 1], vs = [0, 0, 1, 1];
        for (int index = 0; index < 4; index++)
        {
            yield return (MovFloat, [GF(0x10 + index), F(xs[index])]);
            yield return (MovFloat, [GF(0x20 + index), F(ys[index])]);
            yield return (MovFloat, [GF(0x30 + index), F(0)]);
            yield return (MovFloat, [GF(0x40 + index), F(us[index])]);
            yield return (MovFloat, [GF(0x50 + index), F(vs[index])]);
            yield return Mov(0x400 + index, alpha);
            yield return Mov(0x410 + index, rgb);
        }
        yield return (CreatePolygon,
            [I(handle), GF(0x10), GF(0x20), GF(0x30), G(0x400), G(0x410), GF(0x40), GF(0x50), I(4), I(flags)]);
    }

    private static VirtualMachine Run(params IEnumerable<(int, Operand[])>[] parts)
        => Run(new RecordingWaits(), parts);

    private static VirtualMachine Run(RecordingWaits waits, params IEnumerable<(int, Operand[])>[] parts)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4438");
        var ops = parts.SelectMany(part => part).Append((Exit, Array.Empty<Operand>())).ToList();
        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "POLYGONS.BIN", ops, []), table, waits);
        vm.Run();
        return vm;
    }

    private static IEnumerable<(int, Operand[])> Ops(params (int, Operand[])[] ops) => ops;

    [Fact]
    public void CreatePolygonReadsItsArraysAndPacksEachVertexColour()
    {
        VirtualMachine vm = Run(Quad(0x15f91, 0, 0, 1024, 576, alpha: 0x1ff, rgb: 0x123456));

        Assert.Equal("exit", vm.HaltReason);
        GfxState.GfxPolygon polygon = Assert.IsType<GfxState.GfxPolygon>(vm.Gfx.TryGetPolygon(0x15f91));
        Assert.True(polygon.Made);
        Assert.False(polygon.Fan);
        // The alpha's low byte goes above the RGB's low 24 bits.
        Assert.Equal(
            [
                new GfxState.PolygonVertex(0, 0, 0, 0xff123456, 0, 0),
                new GfxState.PolygonVertex(1024, 0, 0, 0xff123456, 1, 0),
                new GfxState.PolygonVertex(0, 576, 0, 0xff123456, 0, 1),
                new GfxState.PolygonVertex(1024, 576, 0, 0xff123456, 1, 1),
            ],
            polygon.Vertices);
        Assert.Equal((-1, -1, 0, 0xffffffffu), (polygon.TextureSlot0, polygon.TextureSlot1, polygon.BlendMode, polygon.Color));
    }

    [Fact]
    public void ACountBelowOneIsSkippedWithAWarningAndAnArrayThatIsNoFloatCellHalts()
    {
        var waits = new RecordingWaits();
        VirtualMachine skipped = Run(waits, Ops(
            (CreatePolygon, [I(7), GF(0x10), GF(0x20), GF(0x30), G(0x400), G(0x410), GF(0x40), GF(0x50), I(0), I(0)])));

        Assert.Equal("exit", skipped.HaltReason);
        Assert.Null(skipped.Gfx.TryGetPolygon(7));
        Assert.Contains(waits.Warnings, warning => warning.Contains("頂点数0は不正です．"));

        // Native reads an integer cell's bytes as a float there; the port has no such view of a cell.
        VirtualMachine halted = Run(Ops(
            (CreatePolygon, [I(7), G(0x10), GF(0x20), GF(0x30), G(0x400), G(0x410), GF(0x40), GF(0x50), I(3), I(0)])));

        Assert.Equal("float-reference-names:Global 0x10", halted.HaltReason);
    }

    [Fact]
    public void TheColourOperandsClampTheAlphaAndKeepWhatANegativeOneNames()
    {
        VirtualMachine vm = Run(
            Quad(5, 0, 0, 4, 4, alpha: 0xff, rgb: 0xffffff),
            Ops(
                (SetColor, [I(5), I(1), I(0x80), I(0x102030)]),
                (SetColor, [I(5), I(3), I(-1), I(0x405060)]),      // keeps the alpha
                (SetColor, [I(6), I(0), I(0x200), I(-1)])));       // no record yet: its colour reads 0xffffffff

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((3, 0x80405060u), (vm.Gfx.TryGetPolygon(5)!.BlendMode, vm.Gfx.TryGetPolygon(5)!.Color));
        Assert.Equal(0xffffffffu, vm.Gfx.TryGetPolygon(6)!.Color);
        Assert.False(vm.Gfx.TryGetPolygon(6)!.Made);   // a colour makes the record, not the polygon
    }

    [Fact]
    public void ATextureStageOrSlotOutsideNativesTablesHalts()
    {
        VirtualMachine stored = Run(Ops((SetTexture, [I(5), I(1), I(42)]), (SetTexture, [I(5), I(0), I(-1)])));
        VirtualMachine stage = Run(Ops((SetTexture, [I(5), I(2), I(42)])));
        VirtualMachine slot = Run(Ops((SetTexture, [I(5), I(0), I(1000)])));

        Assert.Equal((-1, 42), (stored.Gfx.TryGetPolygon(5)!.TextureSlot0, stored.Gfx.TryGetPolygon(5)!.TextureSlot1));
        Assert.StartsWith("index-out-of-range:", stage.HaltReason);
        Assert.StartsWith("index-out-of-range:", slot.HaltReason);
    }

    [Fact]
    public void AFadeStartsAtItsFirstDrawHoldsThroughItsDelayAndMixesTruncated()
    {
        VirtualMachine vm = Run(
            Quad(5, 0, 0, 4, 4, alpha: 0xff, rgb: 0xffffff),
            Ops(
                (SetColor, [I(5), I(0), I(0xff), I(0x000000)]),
                (FadeColor, [I(5), I(100), I(400), I(0), I(0xffffff)])));
        GfxState gfx = vm.Gfx;
        GfxState.GfxPolygon polygon = gfx.TryGetPolygon(5)!;

        Assert.True(gfx.HasActiveTimedPresentation(0));      // an effect wait waits for it before any draw
        gfx.SnapshotVisibleObjects(1000);                    // the first draw: the fade starts here
        Assert.Equal((1000L, 0xff000000u), (polygon.FadeStartMs, polygon.ShownColor));
        gfx.SnapshotVisibleObjects(1100);                    // the delay's last frame
        Assert.Equal(0xff000000u, polygon.ShownColor);
        gfx.SnapshotVisibleObjects(1201);                    // 101 of 400 ms: each channel truncated
        Assert.Equal(GfxState.MixPolygonColor(0xff000000, 0x00ffffff, 101f / 400f), polygon.ShownColor);
        Assert.Equal(0xbe404040u, polygon.ShownColor);   // alpha 255 * 0.7475 = 190.6, RGB 255 * 0.2525 = 64.4
        Assert.Equal(0xff000000u, polygon.Color);            // the colour itself moves only at the end
        Assert.True(gfx.HasActiveTimedPresentation(1499));
        Assert.False(gfx.HasActiveTimedPresentation(1500));  // the wait ends on the clock; the next draw commits
        gfx.SnapshotVisibleObjects(1500);
        Assert.Equal((0x00ffffffu, 0x00ffffffu, 0xffffffffu, false),
                     (polygon.Color, polygon.ShownColor, polygon.FadeTarget, polygon.FadeEnabled));
        Assert.Equal((0L, 0L, -1L), (polygon.FadeDelayMs, polygon.FadeDurationMs, polygon.FadeStartMs));
    }

    [Fact]
    public void AFadeWithNoDurationOnlyEndsAndAForcedRenderTakesTheTarget()
    {
        VirtualMachine vm = Run(
            Quad(5, 0, 0, 4, 4, alpha: 0xff, rgb: 0xffffff),
            Quad(6, 0, 0, 4, 4, alpha: 0xff, rgb: 0xffffff),
            Ops(
                (FadeColor, [I(5), I(0), I(0), I(0), I(0)]),
                (FadeColor, [I(6), I(5000), I(500), I(0x40), I(0x102030)])));
        GfxState gfx = vm.Gfx;

        Assert.True(gfx.HasActiveTimedPresentation(0));     // polygon 6's fade; polygon 5's never runs
        gfx.SnapshotVisibleObjects(10);
        GfxState.GfxPolygon none = gfx.TryGetPolygon(5)!, forced = gfx.TryGetPolygon(6)!;
        Assert.Equal((false, 0xffffffffu, 0u), (none.FadeEnabled, none.Color, none.FadeTarget));   // target never applied

        Assert.Equal(1, gfx.ForceCompleteTimedPresentationForFastForward(20));
        Assert.Equal((false, 0x40102030u, 0x40102030u), (forced.FadeEnabled, forced.Color, forced.ShownColor));
        Assert.False(gfx.HasActiveTimedPresentation(20));
    }

    [Fact]
    public void AFadeThatReplacesAnotherStartsFromThePolygonsColourNotFromWhereTheOtherCame()
    {
        VirtualMachine vm = Run(
            Quad(5, 0, 0, 4, 4, alpha: 0xff, rgb: 0xffffff),
            Ops((FadeColor, [I(5), I(0), I(1000), I(0), I(0)])));
        GfxState gfx = vm.Gfx;
        GfxState.GfxPolygon polygon = gfx.TryGetPolygon(5)!;
        gfx.SnapshotVisibleObjects(0);
        gfx.SnapshotVisibleObjects(500);
        uint halfway = polygon.ShownColor;
        Assert.Equal(0x7f7f7f7fu, halfway);

        gfx.FadePolygonColor(5, 100, 1000, 0xff0000ff);
        gfx.SnapshotVisibleObjects(600);      // the new fade's first draw
        gfx.SnapshotVisibleObjects(700);      // still in its delay: the vertices keep what the old fade last drew
        Assert.Equal(halfway, polygon.ShownColor);
        gfx.SnapshotVisibleObjects(1200);     // half of the new fade, from the colour the old one started from
        Assert.Equal(GfxState.MixPolygonColor(0xffffffff, 0xff0000ff, 0.5f), polygon.ShownColor);
    }

    [Fact]
    public void TheColourMixRoundsEachOperationToAFloatAsNativesSinglePrecisionX87Does()
    {
        // 127 · 0.003 + 127 · (1 - 0.003), each product and the sum rounded to a float, is 126.99999: a channel that
        // does not change still drops by one mid-fade, as on native. A double gives 127.
        Assert.Equal(0x7e7e7e7eu, GfxState.MixPolygonColor(0x7f7f7f7f, 0x7f7f7f7f, 0.003f));
        // 200 · 0.005 rounds up to 1 in a float, where a double gives 0.99999998.
        Assert.Equal(0x00000001u, GfxState.MixPolygonColor(0x00000000, 0x000000c8, 0.005f));
    }

    [Fact]
    public void EraseClearAndCloneTreatAHandlesPolygonAsItsObject()
    {
        VirtualMachine erased = Run(
            Quad(5, 0, 0, 4, 4, 0xff, 0), Quad(6, 0, 0, 4, 4, 0xff, 0), Quad(9, 0, 0, 4, 4, 0xff, 0),
            Ops((EraseObjects, [I(5), I(2)])));
        VirtualMachine cleared = Run(Quad(5, 0, 0, 4, 4, 0xff, 0), Ops((ClearObjects, [])));
        VirtualMachine cloned = Run(
            Quad(5, 0, 0, 4, 4, 0xff, 0x00ff00),
            Ops((SetColor, [I(5), I(1), I(0x80), I(0x808080)]), (CloneObject, [I(5), I(8)])));

        Assert.Equal((null, null, true), (erased.Gfx.TryGetPolygon(5), erased.Gfx.TryGetPolygon(6),
                                          erased.Gfx.TryGetPolygon(9) != null));
        Assert.Equal(0, cleared.Gfx.PolygonCount);
        GfxState.GfxPolygon copy = cloned.Gfx.TryGetPolygon(8)!;
        Assert.Equal((true, 1, 0x80808080u, 4), (copy.Made, copy.BlendMode, copy.Color, copy.Vertices.Length));
        Assert.NotSame(cloned.Gfx.TryGetPolygon(5), copy);
    }

    [Fact]
    public void APolygonIsDrawnWhereItsHandleFallsAmongTheObjects()
    {
        var clock = new FrameClock();
        var graphics = new Graphics(null, clock, new Sys4LogicalCanvas(4, 4));
        GfxState gfx = graphics.State;
        graphics.CreateSurface(2, 4, 4);
        graphics.PublishSurfacePixels(2, Image(4, 4, (_, _) => [200, 100, 50, 255]));
        gfx.BindDraw(10, 2, 0, 0, 4, 4, 0, 0);
        gfx.BindDraw(30, 2, 0, 0, 2, 2, 2, 2);
        // A polygon whose handle an object shares is drawn before that object; one above every object, last.
        gfx.CreatePolygon(10, Square(0, 0, 4, 4, 0xff000000), 0);
        gfx.CreatePolygon(20, Square(0, 0, 2, 2, 0x80ffffff), 0);
        gfx.CreatePolygon(40, Square(3, 3, 4, 4, 0xff00ff00), 0);
        gfx.SetPolygonColor(99, 0, 0xffffffff);      // a record with no vertices is not drawn

        FrameDescription frame = graphics.LowerFrame(clock.NowMs);

        Assert.Equal(
            [FrameItemKind.Polygon, FrameItemKind.Texture, FrameItemKind.Polygon, FrameItemKind.Texture, FrameItemKind.Polygon],
            frame.Items.Select(item => item.Kind));
        Assert.All(frame.Items.Where(item => item.Kind == FrameItemKind.Polygon),
                   item => Assert.Equal(6, item.Polygon!.Vertices.Length));   // a strip of four is two triangles
        var reference = new ReferenceRenderer(new Sys4LogicalCanvas(4, 4));
        reference.Draw(frame);
        byte[] At(int x, int y) => reference.Pixels.AsSpan((y * 4 + x) * 4, 4).ToArray();
        Assert.Equal([227, 177, 152, 255], At(0, 0));   // white at alpha 0x80 over the picture, over the black polygon
        Assert.Equal([200, 100, 50, 255], At(2, 2));    // object 30 above polygon 20
        Assert.Equal([0, 255, 0, 255], At(3, 3));       // polygon 40 above everything
        // The lowered triangles are the same object until the polygon changes: a renderer may keep what it built.
        Assert.Same(frame.Items[0].Polygon, graphics.LowerFrame(clock.NowMs).Items[0].Polygon);
        gfx.SetPolygonColor(10, 0, 0x80ffffff);
        Assert.NotSame(frame.Items[0].Polygon, graphics.LowerFrame(clock.NowMs).Items[0].Polygon);
    }

    [Fact]
    public void ThePolygonsColourMultipliesEachVertexAndAFanSharesItsFirstVertex()
    {
        var gfx = new GfxState();
        GfxState.PolygonVertex[] corners =
        [
            new(0, 0, 0, 0xff804020, 0, 0), new(4, 0, 0, 0xffffffff, 1, 0),
            new(4, 4, 0, 0x80ffffff, 1, 1), new(0, 4, 0, 0xff000000, 0, 1),
        ];
        gfx.CreatePolygon(5, corners, flags: 1);
        gfx.SetPolygonColor(5, 3, 0x80ff8040);

        RenderObject entry = Assert.Single(gfx.SnapshotVisibleObjects(0));
        FramePolygonGeometry geometry = entry.Polygon!.Value.Geometry;

        Assert.Equal(FramePolygonBlend.Subtractive, geometry.Blend);
        // 0 1 2, 0 2 3: a fan; each channel is the vertex's times the polygon's, divided by 255.
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }.Select(index => (corners[index].X, corners[index].Y)),
                     geometry.Vertices.Select(vertex => (vertex.X, vertex.Y)));
        Assert.Equal(0x80802008u, geometry.Vertices[0].Argb);
        Assert.Equal(0x80ff8040u, geometry.Vertices[1].Argb);
        Assert.Equal(0x40ff8040u, geometry.Vertices[2].Argb);
    }

    // The lowered triangles of one polygon, as op 0x320 gives it.
    private static FramePolygonVertex[] Lowered(GfxState.PolygonVertex[] vertices, long flags = 0)
    {
        var gfx = new GfxState();
        gfx.CreatePolygon(5, vertices, flags);
        return Assert.Single(gfx.SnapshotVisibleObjects(0)).Polygon!.Value.Geometry.Vertices;
    }

    [Fact]
    public void ATriangleWoundCounterClockwiseOnScreenIsCulledAsDirect3DsDefaultCullsIt()
    {
        // Native drew these on 2026-11-20 (engine-re.md, "Polygon objects: winding, depth, ..."): the strip top-left, top-right, bottom-left,
        // bottom-right is whole, since a strip's odd triangle counts its first two corners swapped.
        Assert.Equal(6, Lowered(Square(0, 0, 4, 4, 0xffffffff)).Length);
        // The same square from top-left, bottom-left, top-right, bottom-right is wound the other way: nothing.
        GfxState.PolygonVertex[] mirrored =
        [
            new(0, 0, 0, 0xffffffff, 0, 0), new(0, 4, 0, 0xffffffff, 0, 1),
            new(4, 0, 0, 0xffffffff, 1, 0), new(4, 4, 0, 0xffffffff, 1, 1),
        ];
        Assert.Empty(Lowered(mirrored));
        // A fan top-left, bottom-left, bottom-right, top-right likewise; the other way round it is whole.
        GfxState.PolygonVertex[] fan = [mirrored[0], mirrored[1], mirrored[3], mirrored[2]];
        Assert.Empty(Lowered(fan, flags: 1));
        Assert.Equal(6, Lowered([.. fan.Reverse()], flags: 1).Length);
        // One triangle of a strip may face away while the other is drawn.
        GfxState.PolygonVertex[] bent =
        [
            new(0, 0, 0, 0xffffffff, 0, 0), new(4, 0, 0, 0xffffffff, 1, 0),
            new(0, 4, 0, 0xffffffff, 0, 1), new(-4, 0, 0, 0xffffffff, 1, 1),
        ];
        Assert.Equal([(0f, 0f), (4f, 0f), (0f, 4f)], Lowered(bent).Select(vertex => (vertex.X, vertex.Y)));
    }

    [Fact]
    public void OnlyWhatLiesAtDepthsZeroToOneIsDrawn()
    {
        static GfxState.PolygonVertex[] At(float z0, float z1) =>
        [
            new(0, 0, z0, 0xff000000, 0, 0), new(8, 0, z1, 0xffffffff, 1, 0),
            new(0, 4, z0, 0xff000000, 0, 1), new(8, 4, z1, 0xffffffff, 1, 1),
        ];

        // Native drew z 0, 0.5 and 1, and not 1.5 or -0.5.
        Assert.Equal(6, Lowered(At(0, 0)).Length);
        Assert.Equal(6, Lowered(At(1, 1)).Length);
        Assert.Empty(Lowered(At(1.5f, 1.5f)));
        Assert.Empty(Lowered(At(-0.5f, -0.5f)));

        // A z that runs from 0.5 to 1.5 across the square is cut where it reaches 1, halfway: x 4, with the colour, the
        // texture coordinates and the depth taken in proportion.
        FramePolygonVertex[] cut = Lowered(At(0.5f, 1.5f));
        Assert.Equal(4f, cut.Max(vertex => vertex.X));
        Assert.Equal(0f, cut.Min(vertex => vertex.X));
        FramePolygonVertex edge = cut.First(vertex => vertex.X == 4f);
        Assert.Equal((0.5f, 0xff808080u), (edge.U, edge.Argb));
    }

    [Fact]
    public void TwoTrianglesThatShareAnEdgeDrawEachPixelOnce()
    {
        // Additive, so a pixel drawn twice would show: the top-left rule gives the diagonal to one triangle.
        var pixels = new byte[8 * 8 * 4];
        SoftwareAffineRasterizer.DrawPolygonRgba(
            pixels, 8, 8, Triangles(Square(1, 1, 7, 7, 0xff101010)), FramePolygonBlend.Additive, 1f,
            null, 0, 0, null, 0, 0);

        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            bool inside = x >= 1 && x < 7 && y >= 1 && y < 7;
            Assert.Equal(inside ? 0x10 : 0, pixels[(y * 8 + x) * 4]);
        }
    }

    [Fact]
    public void ColoursAreInterpolatedAndATextureAtItsOwnSizeCopiesItsTexels()
    {
        // A horizontal gradient from black to white over eight pixels: each pixel centre takes its share.
        var gradient = new byte[8 * 1 * 4];
        FramePolygonVertex[] strip =
        [
            new(0, 0, 0xff000000, 0, 0), new(8, 0, 0xffffffff, 1, 0), new(0, 1, 0xff000000, 0, 1),
            new(8, 0, 0xffffffff, 1, 0), new(0, 1, 0xff000000, 0, 1), new(8, 1, 0xffffffff, 1, 1),
        ];
        SoftwareAffineRasterizer.DrawPolygonRgba(gradient, 8, 1, strip, FramePolygonBlend.Replace, 1f,
                                                 null, 0, 0, null, 0, 0);
        Assert.Equal(Enumerable.Range(0, 8).Select(x => (byte)((x + 0.5) / 8 * 255 + 0.5)),
                     Enumerable.Range(0, 8).Select(x => gradient[x * 4]));

        // A texture drawn at its own size, corner to corner, lands texel for texel; a half-white vertex colour
        // halves it, and a second texture goes over the first by its own alpha.
        RgbaImage first = Image(4, 4, (x, y) => [(byte)(x * 60), (byte)(y * 60), 200, 255]);
        RgbaImage second = Image(4, 4, (x, _) => [0, 0, 0, (byte)(x < 2 ? 255 : 0)]);
        var drawn = new byte[4 * 4 * 4];
        SoftwareAffineRasterizer.DrawPolygonRgba(
            drawn, 4, 4, Triangles(Square(0, 0, 4, 4, 0xff808080)), FramePolygonBlend.Replace, 1f,
            first.Pixels, 4, 4, second.Pixels, 4, 4);

        byte[] At(int x, int y) => drawn.AsSpan((y * 4 + x) * 4, 4).ToArray();
        Assert.Equal([0, 0, 0, 255], At(1, 3));                                   // under the second texture
        Assert.Equal([(byte)(180 * 0x80 / 255), (byte)(60 * 0x80 / 255), (byte)(200 * 0x80 / 255), 255], At(3, 1));
    }

    [Theory]
    [InlineData(FramePolygonBlend.Alpha, 150, 49, 50, 255)]           // (200, 0, 100) at alpha 0x80 over (100, 100, 0)
    [InlineData(FramePolygonBlend.Additive, 200, 100, 50, 255)]
    [InlineData(FramePolygonBlend.Replace, 200, 0, 100, 0x80)]
    [InlineData(FramePolygonBlend.Subtractive, 0, 100, 0, 255)]
    public void EachBlendLandsAsDirect3DsDoes(FramePolygonBlend blend, byte red, byte green, byte blue, byte alpha)
    {
        byte[] pixels = [100, 100, 0, 255];
        SoftwareAffineRasterizer.DrawPolygonRgba(
            pixels, 1, 1, Triangles(Square(0, 0, 1, 1, 0x80c80064)), blend, 1f, null, 0, 0, null, 0, 0);

        Assert.Equal([red, green, blue, alpha], pixels);
    }

    // A square as a strip of four vertices, in one colour.
    private static GfxState.PolygonVertex[] Square(float x0, float y0, float x1, float y1, uint argb)
        =>
        [
            new(x0, y0, 0, argb, 0, 0), new(x1, y0, 0, argb, 1, 0),
            new(x0, y1, 0, argb, 0, 1), new(x1, y1, 0, argb, 1, 1),
        ];

    private static FramePolygonVertex[] Triangles(GfxState.PolygonVertex[] strip)
    {
        var triangles = new List<FramePolygonVertex>();
        for (int index = 0; index + 2 < strip.Length; index++)
            for (int corner = 0; corner < 3; corner++)
            {
                GfxState.PolygonVertex vertex = strip[index + corner];
                triangles.Add(new FramePolygonVertex(vertex.X, vertex.Y, vertex.Argb, vertex.U, vertex.V));
            }
        return triangles.ToArray();
    }
}
