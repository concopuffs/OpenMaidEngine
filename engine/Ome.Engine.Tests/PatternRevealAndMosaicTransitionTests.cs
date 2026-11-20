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

/// <summary>
/// The legacy whole-screen transitions that are not a blend: the patterned reveals (ops 0x26, 0x27 and 0x28) with the
/// rectangles native's tick draws, and op 0x36's mosaic transition (docs/engine-re.md, "The pattern reveals' geometry"
/// and "The mosaic transition").
/// </summary>
public class PatternRevealAndMosaicTransitionTests
{
    private static Operand I(long value) => new(0, value);

    private static FrameRect[] Rects(int width, int height, int divisions, int mode, long periods)
    {
        var rects = new List<FrameRect>();
        LegacyPatternReveal.Rects(width, height, divisions, (SurfacePatternTransitionMode)mode, periods, rects);
        return rects.ToArray();
    }

    // ---- the geometry ----

    [Fact]
    public void AWipeGrowsFromItsEdgeByOneDivisionAPeriod()
    {
        // 12 x 8 in 4 divisions: 3 pixels a period across, 2 down.
        Assert.Equal([new FrameRect(0, 0, 6, 8)], Rects(12, 8, 4, 0, 2));
        Assert.Equal([new FrameRect(6, 0, 12, 8)], Rects(12, 8, 4, 1, 2));
        Assert.Equal([new FrameRect(0, 0, 12, 4)], Rects(12, 8, 4, 2, 2));
        Assert.Equal([new FrameRect(0, 4, 12, 8)], Rects(12, 8, 4, 3, 2));
        // The counter stops at the division count.
        Assert.Equal([new FrameRect(0, 0, 12, 8)], Rects(12, 8, 4, 0, 9));
        Assert.Empty(Rects(12, 8, 4, 0, 0));
        Assert.Equal(4, LegacyPatternReveal.Span(12, 8, 4, SurfacePatternTransitionMode.WipeLeftToRight));
    }

    [Fact]
    public void TheStripsGrowTogetherEachFromItsOwnEdge()
    {
        // 12 wide in 3 strips of 4; after one period every strip shows one pixel.
        Assert.Equal([new FrameRect(0, 0, 1, 8), new FrameRect(4, 0, 5, 8), new FrameRect(8, 0, 9, 8)],
                     Rects(12, 8, 3, 4, 1));
        Assert.Equal([new FrameRect(11, 0, 12, 8), new FrameRect(7, 0, 8, 8), new FrameRect(3, 0, 4, 8)],
                     Rects(12, 8, 3, 5, 1));
        // 8 high in 2 strips of 4.
        Assert.Equal([new FrameRect(0, 0, 12, 3), new FrameRect(0, 4, 12, 7)], Rects(12, 8, 2, 6, 3));
        Assert.Equal([new FrameRect(0, 5, 12, 8), new FrameRect(0, 1, 12, 4)], Rects(12, 8, 2, 7, 3));
        Assert.Equal(4, LegacyPatternReveal.Span(12, 8, 3, SurfacePatternTransitionMode.VerticalStripsLeftToRight));
    }

    [Fact]
    public void TheStaggeredStripsStartOnePeriodApart()
    {
        // 12 wide in 3 strips of 4: after 2 periods the first strip shows 2 pixels, the second 1, the third none.
        Assert.Equal([new FrameRect(0, 0, 2, 8), new FrameRect(4, 0, 5, 8)], Rects(12, 8, 3, 8, 2));
        Assert.Equal([new FrameRect(10, 0, 12, 8), new FrameRect(7, 0, 8, 8)], Rects(12, 8, 3, 9, 2));
        Assert.Equal([new FrameRect(0, 0, 12, 2), new FrameRect(0, 4, 12, 5)], Rects(12, 8, 2, 10, 2));
        Assert.Equal([new FrameRect(0, 6, 12, 8), new FrameRect(0, 3, 12, 4)], Rects(12, 8, 2, 11, 2));
        // A strip stops at its thickness, and the last one reaches it after divisions - 1 + thickness periods.
        Assert.Equal([new FrameRect(0, 0, 4, 8), new FrameRect(4, 0, 8, 8), new FrameRect(8, 0, 12, 8)],
                     Rects(12, 8, 3, 8, 6));
        Assert.Equal(6, LegacyPatternReveal.Span(12, 8, 3, SurfacePatternTransitionMode.StaggeredVerticalStripsLeftToRight));
    }

    [Theory]
    [InlineData(0, 5)]      // the width does not divide
    [InlineData(2, 3)]      // the height does not divide
    [InlineData(12, 4)]     // no such mode
    [InlineData(0, 0)]
    public void ARevealTheScreenRefusesHasNoSpanAndNoRectangle(int mode, int divisions)
    {
        Assert.Equal(0, LegacyPatternReveal.Span(12, 8, divisions, (SurfacePatternTransitionMode)mode));
        Assert.Empty(Rects(12, 8, divisions, mode, 3));
    }

    [Fact]
    public void AWipeLastsItsDivisionsTimesItsPeriod()
    {
        // SC0000's wipe: 64 steps of 1 ms. An interval of 0 counts as 1, as sleep_timer_arm takes it.
        Assert.Equal(64, LegacyScreenTransitionTiming.PatternDurationMilliseconds(
            1280, 720, 1, 64, SurfacePatternTransitionMode.WipeLeftToRight));
        Assert.Equal(480, LegacyScreenTransitionTiming.PatternDurationMilliseconds(
            1280, 720, 30, 16, SurfacePatternTransitionMode.WipeBottomToTop));
        Assert.Equal(64, LegacyScreenTransitionTiming.PatternDurationMilliseconds(
            1280, 720, 0, 64, SurfacePatternTransitionMode.WipeRightToLeft));
    }

    // ---- the pixels ----

    private static byte[] Solid(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, value);
        return pixels;
    }

    [Fact]
    public void ARevealKeepsTheTargetInsideItsRectanglesAndTheSourceOutside()
    {
        byte[] target = Solid(4, 3, 200), source = Solid(4, 3, 10);

        LegacyScreenTransitionComposite.RevealTargetOverSource(
            target, source, 4, 3, [new FrameRect(1, 0, 3, 2), new FrameRect(-5, 2, 1, 9)]);   // the second is clipped

        byte At(int x, int y) => target[(y * 4 + x) * 4];
        Assert.Equal(new byte[] { 10, 200, 200, 10 }, new[] { At(0, 0), At(1, 0), At(2, 0), At(3, 0) });
        Assert.Equal(new byte[] { 10, 200, 200, 10 }, new[] { At(0, 1), At(1, 1), At(2, 1), At(3, 1) });
        Assert.Equal(new byte[] { 200, 10, 10, 10 }, new[] { At(0, 2), At(1, 2), At(2, 2), At(3, 2) });
        Assert.All(Enumerable.Range(0, 12), index => Assert.Equal(255, target[index * 4 + 3]));
    }

    [Fact]
    public void AMosaicShowsEachBlocksTopLeftPixel()
    {
        // 5 x 4 where every pixel's red is 10 * y + x; blocks of 3 from the top-left corner.
        var pixels = new byte[5 * 4 * 4];
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 5; x++)
            pixels[(y * 5 + x) * 4] = (byte)(10 * y + x);

        LegacyScreenTransitionComposite.Mosaic(pixels, 5, 4, 3);

        byte[] Row(int y) => Enumerable.Range(0, 5).Select(x => pixels[(y * 5 + x) * 4]).ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, 3, 3 }, Row(0));
        Assert.Equal(new byte[] { 0, 0, 0, 3, 3 }, Row(1));
        Assert.Equal(new byte[] { 0, 0, 0, 3, 3 }, Row(2));
        Assert.Equal(new byte[] { 30, 30, 30, 33, 33 }, Row(3));

        byte[] untouched = (byte[])pixels.Clone();
        LegacyScreenTransitionComposite.Mosaic(pixels, 5, 4, 1);
        LegacyScreenTransitionComposite.Mosaic(pixels, 5, 4, 0);
        Assert.Equal(untouched, pixels);
    }

    [Fact]
    public void TheMosaicGrowsOnTheSourceThenShrinksOnTheTarget()
    {
        // The tick draws with its counter before it counts the period it runs in.
        Assert.Equal((false, 0), LegacyScreenTransitionTiming.MosaicAt(1));
        Assert.Equal((false, 1), LegacyScreenTransitionTiming.MosaicAt(2));
        Assert.Equal((false, 79), LegacyScreenTransitionTiming.MosaicAt(80));
        Assert.Equal((true, 81), LegacyScreenTransitionTiming.MosaicAt(81));
        Assert.Equal((true, 2), LegacyScreenTransitionTiming.MosaicAt(160));
        // Only a tick that has missed periods reaches the cap, where the target is whole.
        Assert.Equal((true, 1), LegacyScreenTransitionTiming.MosaicAt(161));
        Assert.Equal((true, 1), LegacyScreenTransitionTiming.MosaicAt(100_000));

        Assert.Equal(480, LegacyScreenTransitionTiming.MosaicDurationMilliseconds(3));
        Assert.Equal(3200, LegacyScreenTransitionTiming.MosaicDurationMilliseconds(20));
        Assert.Equal(160, LegacyScreenTransitionTiming.MosaicDurationMilliseconds(0));
    }

    // ---- the graphics native and the reference renderer ----

    // An opaque image whose colour channels are all <paramref name="value"/>.
    private static RgbaImage Image(int width, int height, byte value)
    {
        byte[] pixels = Solid(width, height, value);
        for (int index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        return new RgbaImage(width, height, pixels);
    }

    /// <summary>A 4 x 2 screen whose surface 5 holds a captured frame that is all 200, under a live frame of
    /// <paramref name="live"/>. The two frames use different objects: a captured object's surface is looked up when
    /// it is drawn.</summary>
    private static (Graphics Graphics, FrameClock Clock) SurfaceFiveTwoHundredUnder(RgbaImage live)
    {
        var clock = new FrameClock();
        var graphics = new Graphics(null, clock, new Sys4LogicalCanvas(4, 2));
        GfxState gfx = graphics.State;
        graphics.CreateSurface(2, 4, 2);
        graphics.PublishSurfacePixels(2, Image(4, 2, 200));
        graphics.CreateSurface(3, 4, 2);
        graphics.PublishSurfacePixels(3, live);
        graphics.CreateSurface(5, 4, 2);
        gfx.BindDraw(20, 2, 0, 0, 4, 2, 0, 0);
        gfx.SelectRenderTarget(5);
        graphics.PresentFrame();                       // surface 5 captures object 20 alone: all 200
        gfx.SelectRenderTarget(-1);
        gfx.BindDraw(30, 3, 0, 0, 4, 2, 0, 0);         // object 30 covers it: the live frame
        return (graphics, clock);
    }

    private static byte[] Reds(ReferenceRenderer renderer, int y)
        => Enumerable.Range(0, 4).Select(x => renderer.Pixels[(y * 4 + x) * 4]).ToArray();

    [Fact]
    public void AWipeShowsTheSurfaceInTheRectangleItsPeriodsHaveReached()
    {
        (Graphics graphics, FrameClock clock) = SurfaceFiveTwoHundredUnder(Image(4, 2, 10));
        var renderer = new ReferenceRenderer(new Sys4LogicalCanvas(4, 2));

        // Mode 0 in 4 divisions of 100 ms: one column a period, from the left.
        ScreenTransitionStart start = graphics.StartSurfacePatternReveal(
            new SurfacePatternTransitionRequest(5, 100, 4, SurfacePatternTransitionMode.WipeLeftToRight), false);
        Assert.Equal(400, start.DurationMs);

        renderer.Draw(graphics.LowerFrame(clock.NowMs + 50));         // before the first period: the old frame
        Assert.Equal(new byte[] { 10, 10, 10, 10 }, Reds(renderer, 0));

        FrameDescription frame = graphics.LowerFrame(clock.NowMs + 250);
        Assert.True(frame.ScreenTransition!.Patterned);
        Assert.Equal([new FrameRect(0, 0, 2, 2)], frame.ScreenTransition.Reveal);
        renderer.Draw(frame);
        Assert.Equal(new byte[] { 200, 200, 10, 10 }, Reds(renderer, 0));
        Assert.Equal(new byte[] { 200, 200, 10, 10 }, Reds(renderer, 1));

        // Forced to its end, as its wait does: the surface alone, with no pattern.
        Assert.True(graphics.ForceScreenTransition());
        frame = graphics.LowerFrame(clock.NowMs + 260);
        Assert.False(frame.ScreenTransition!.Patterned);
        renderer.Draw(frame);
        Assert.Equal(new byte[] { 200, 200, 200, 200 }, Reds(renderer, 0));
        Assert.False(graphics.ScreenTransitionPending);
    }

    [Fact]
    public void TheMosaicDrawsOneSurfaceAloneInBlocks()
    {
        // Source: surface 6, a capture of a live frame that is 1, 2, 3, 4 across. Target: surface 5.
        var across = new byte[4 * 2 * 4];
        for (int index = 0; index < 8; index++)
        {
            across[index * 4] = (byte)(index % 4 + 1);
            across[index * 4 + 3] = 255;
        }
        (Graphics graphics, FrameClock clock) = SurfaceFiveTwoHundredUnder(new RgbaImage(4, 2, across));
        GfxState gfx = graphics.State;
        graphics.CreateSurface(6, 4, 2);
        gfx.SelectRenderTarget(6);
        graphics.PresentFrame();
        gfx.SelectRenderTarget(-1);
        var renderer = new ReferenceRenderer(new Sys4LogicalCanvas(4, 2));

        ScreenTransitionStart start = graphics.StartSurfaceMosaic(6, 5, 10, false);
        Assert.Equal(1600, start.DurationMs);

        // Before the first period, and on it (a dot of 0), the source as it is.
        renderer.Draw(graphics.LowerFrame(clock.NowMs + 5));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, Reds(renderer, 0));
        // Period 3: the counter is 2, so the source in blocks of 2.
        FrameDescription frame = graphics.LowerFrame(clock.NowMs + 35);
        Assert.Equal(2, frame.ScreenTransition!.MosaicDot);
        Assert.Equal(0, frame.ScreenTransition.Alpha);
        renderer.Draw(frame);
        Assert.Equal(new byte[] { 1, 1, 3, 3 }, Reds(renderer, 0));
        // The second half shows the target.
        renderer.Draw(graphics.LowerFrame(clock.NowMs + 1000));
        Assert.Equal(new byte[] { 200, 200, 200, 200 }, Reds(renderer, 0));
    }

    // ---- the handlers ----

    private static (VirtualMachine Vm, RecordingWaits Host) Run(bool fastForward, params (int, Operand[])[] ops)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4438");
        var instructions = new List<(int, Operand[])>(ops) { (0x2, Array.Empty<Operand>()) };
        var host = new RecordingWaits();
        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "WIPES.BIN", instructions, []), table, host,
                                    build: EngineBuilds.Age438D,
                                    skipAndAuto: fastForward ? TestSkipAndAuto.HoldingFastForward() : null);
        vm.Run();
        return (vm, host);
    }

    // The created flag (+0x10) of scratch surface 0x20's record, as a numbered save would hold it.
    private static bool ScratchCreated(VirtualMachine vm)
    {
        vm.Gfx.SnapshotSurfaceRecords();
        return BitConverter.ToInt32(vm.Gfx.CaptureNativeSurfaceRecords(), 0x20 * 0x14 + 0x10) == 1;
    }

    [Fact]
    public void TheWipeOpcodeMapsItsTypeToModesZeroToFour()
    {
        (VirtualMachine vm, RecordingWaits host) = Run(false,
            (0x26, [I(2), I(1), I(64), I(0)]),
            (0x26, [I(2), I(30), I(16), I(3)]),
            (0x26, [I(2), I(1), I(64), I(4)]),
            (0x26, [I(2), I(1), I(0), I(1)]),           // no divisions: it finishes at once
            (0x26, [I(2), I(1), I(64), I(5)]));         // traced and skipped

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(new[]
        {
            new SurfacePatternTransitionRequest(2, 1, 64, SurfacePatternTransitionMode.WipeLeftToRight),
            new SurfacePatternTransitionRequest(2, 30, 16, SurfacePatternTransitionMode.WipeBottomToTop),
            new SurfacePatternTransitionRequest(2, 1, 64, SurfacePatternTransitionMode.VerticalStripsLeftToRight),
            new SurfacePatternTransitionRequest(2, 1, 0, SurfacePatternTransitionMode.WipeRightToLeft),
        }, host.SurfacePatternTransitions);
        Assert.Equal(new[] { false, false, false, true }, host.SurfacePatternForceEndpoints);
        Assert.Contains("関数：ComEfWp エラー：Typeは0から1の間にしてください", Assert.Single(host.Warnings));
        Assert.False(ScratchCreated(vm));
    }

    [Fact]
    public void TheMosaicOpcodeForwardsItsSurfacesAndWritesTheScratchSurface()
    {
        (VirtualMachine vm, RecordingWaits host) = Run(false, (0x36, [I(1), I(2), I(20)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((1, 2, 20L), Assert.Single(host.SurfaceMosaics));
        Assert.False(Assert.Single(host.SurfaceMosaicForceEndpoints));
        Assert.True(ScratchCreated(vm));           // begin creates 0x20 blank
    }

    [Fact]
    public void UnderFastForwardTheMosaicFinishesOnItsTargetAndReadsNoSource()
    {
        (VirtualMachine vm, RecordingWaits host) = Run(true, (0x36, [I(1000), I(2), I(3)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.True(Assert.Single(host.SurfaceMosaicForceEndpoints));
        Assert.False(ScratchCreated(vm));          // finalize fills it only for a negative target

        (vm, _) = Run(true, (0x36, [I(1), I(-1), I(3)]));
        Assert.Equal("exit", vm.HaltReason);
        Assert.True(ScratchCreated(vm));
    }
}
