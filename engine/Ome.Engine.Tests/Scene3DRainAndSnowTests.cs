using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Ome.Engine.Devices;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Xunit;

/// <summary>
/// AGE 4.38D's rain (op 0x327) and snow (op 0x326), which 創刻のアテリアル's DEBUG.BIN shows over the title with its
/// leaves (docs/engine-re.md, "The 3D scene"): their motion on the game's generator, the snow's sort, and their
/// lowering into polygon items. Every drop and flake placed here is placed for the test.
/// </summary>
public class Scene3DRainAndSnowTests
{
    private const int Width = 1024, Height = 576;
    /// <summary>cot(π/8) · 288: a unit across at a distance of one, in pixels, on a 576-pixel-high screen.</summary>
    private const double PixelsAtOne = 2.414213562373095 * 288;

    // The game's generator from the same seed, read as native's effect code reads it.
    private sealed class Draws
    {
        private readonly CrtRandom _random;
        public Draws(uint seed, long skip = 0)
        {
            _random = new CrtRandom(seed);
            for (long draw = 0; draw < skip; draw++) _random.Next();
        }
        public float Unit() => (float)(_random.Next() % 100 / 100.0);
        public float Centered() => (float)(Unit() - 0.5);
    }

    private static Graphics NewGraphics(uint seed, int width = Width, int height = Height)
        => new(null, new FrameClock(), new Sys4LogicalCanvas(width, height), gameRandom: new CrtRandom(seed));

    private static RgbaImage White() => GraphicsTestSupport.Image(2, 2, (_, _) => [255, 255, 255, 255]);

    [Fact]
    public void ADropIsBornFromThirteenDrawsInNativesOrder()
    {
        Graphics graphics = NewGraphics(11);
        graphics.SetRainEffect(2);

        Assert.Equal(26L, graphics.GameRandom.Draws);
        var draws = new Draws(11);
        foreach (Scene3DRainDrop drop in graphics.Scene3D.Rain!.Drops)
        {
            float x = (float)((double)draws.Centered() * 20), y = (float)((double)draws.Unit() * 10);
            float z = (float)((double)draws.Centered() * 20);
            Assert.Equal(new Vector3(x, y, z), drop.Position);
            float vx = (float)((double)draws.Unit() * 0.05f);
            float vy = (float)((-0.2f - (double)-0.1f) * draws.Unit() + -0.1f);
            float vz = (float)((double)draws.Unit() * 0.05f);
            Assert.Equal(new Vector3(vx, vy, vz), drop.Velocity);
            Assert.InRange(vy, -0.2f, -0.1f);
            // The acceleration: the same three draws' kind again, each kept as a float, times the float 0.01.
            float ax = (float)((double)(float)((double)draws.Unit() * 0.05f) * 0.01f);
            float ay = (float)((double)(float)((-0.2f - (double)-0.1f) * draws.Unit() + -0.1f) * 0.01f);
            float az = (float)(0.01f * (double)(float)((double)draws.Unit() * 0.05f));
            Assert.Equal(new Vector3(ax, ay, az), drop.Acceleration);
            float yaw = (float)(0.01f * (double)draws.Unit()), pitch = (float)(0.01f * (double)draws.Unit());
            float roll = (float)(0.1f + 0.01f * (double)draws.Unit());
            Assert.Equal(new Vector3(yaw, pitch, roll), drop.Rotation);
            Assert.Equal((float)((1.1f - (double)1f) * draws.Unit() + 1f), drop.Scale);
            Assert.False(drop.Rippling);
        }
    }

    [Fact]
    public void ADropFallsFasterEachStepThenRipplesForItsLifeThenStartsAgainAtTheTop()
    {
        Graphics graphics = NewGraphics(3);
        graphics.SetRainEffect(1);
        Scene3DRainEffect rain = graphics.Scene3D.Rain!;
        var placed = new Scene3DRainDrop(new Vector3(1, 0.15f, 2), new Vector3(0.01f, -0.1f, 0.02f),
                                         new Vector3(0, -0.001f, 0), new Vector3(0, 0, 0.1f), 1.05f, false, 0, 0);
        rain.Place(0, placed);

        rain.Step(graphics.GameRandom);
        Assert.Equal(placed.Position + placed.Velocity, rain.Drops[0].Position);
        Assert.Equal(placed.Velocity + placed.Acceleration, rain.Drops[0].Velocity);
        Assert.Equal(13L, graphics.GameRandom.Draws);

        // Past the floor: one draw for the ring's life, and the drop lies at the ring's height, unscaled, unturned.
        rain.Step(graphics.GameRandom);
        int life = (int)(new Draws(3, 13).Unit() * 5.0 + 15.0);
        Scene3DRainDrop ring = rain.Drops[0];
        Assert.Equal((true, 0, life, 0.01f, 0f, Vector3.Zero),
                     (ring.Rippling, ring.RippleAge, ring.RippleLife, ring.Position.Y, ring.Scale, ring.Rotation));
        Assert.Equal(placed.Position.X + placed.Velocity.X * 2 + placed.Acceleration.X, ring.Position.X, 5);
        Assert.InRange(life, 15, 19);
        Assert.Equal(14L, graphics.GameRandom.Draws);

        for (int age = 1; age <= life; age++)
        {
            rain.Step(graphics.GameRandom);
            Assert.Equal((true, age, (float)(age * 0.009999999776482582)),
                         (rain.Drops[0].Rippling, rain.Drops[0].RippleAge, rain.Drops[0].Scale));
        }
        rain.Step(graphics.GameRandom);                     // a step past its life: born again, at the top
        Assert.False(rain.Drops[0].Rippling);
        Assert.Equal(10f, rain.Drops[0].Position.Y);
        Assert.Equal(14L + 13, graphics.GameRandom.Draws);
    }

    [Fact]
    public void AFlakeIsBornFromFourDrawsAndFallsAndDriftsBySpeedTimesAHundredth()
    {
        Graphics graphics = NewGraphics(5);
        graphics.PublishSurfacePixels(40, White());
        Assert.True(graphics.SetSnowEffect(2, 40, 2f, 1));

        Assert.Equal(8L, graphics.GameRandom.Draws);
        var draws = new Draws(5);
        Scene3DSnowEffect snow = graphics.Scene3D.Snow!;
        foreach (Scene3DSnowFlake flake in snow.Flakes)
        {
            float a = draws.Unit(), b = draws.Unit(), c = draws.Centered();
            // x starts on the negative side alone: -a · speed · 20.
            Assert.Equal(new Vector3((float)(-(double)a * 2f * 20f), (float)((double)b * 5), (float)((double)c * 20)),
                         flake.Position);
            Assert.Equal((float)((0.06f - (double)0.03f) * draws.Unit() + 0.03f), flake.Size);
        }

        var placed = new Scene3DSnowFlake(new Vector3(1, 0.03f, 3), 0.04f);
        snow.Place(0, placed);
        snow.Step(graphics.GameRandom);
        Assert.Equal(new Vector3((float)(1 - 2.0 * -0.01f), (float)(2.0 * -0.01f + 0.03f), 3),
                     snow.Flakes[0].Position);
        Assert.Equal(8L, graphics.GameRandom.Draws);

        // Below the floor: three draws, a centred x and z and the size, and the flake is at the top.
        snow.Step(graphics.GameRandom);
        var again = new Draws(5, 8);
        float x = (float)((double)again.Centered() * 20), z = (float)((double)again.Centered() * 20);
        float size = (float)((0.06f - (double)0.03f) * again.Unit() + 0.03f);
        Assert.Equal(new Scene3DSnowFlake(new Vector3(x, 5, z), size), snow.Flakes[0]);
        Assert.Equal(11L, graphics.GameRandom.Draws);
    }

    [Theory]
    [InlineData(2.5f, true, 255u)]      // half way down: sin(π/2)
    [InlineData(5f, true, 0u)]          // at the top: sin 0
    [InlineData(3.75f, true, 180u)]     // sin(π/4) = 0.7071
    [InlineData(5f, false, 255u)]       // without the fade, always 1
    public void AFlakeFadesInBelowTheTopWhenItsOptionAsksForIt(float y, bool fades, uint alpha)
    {
        Graphics graphics = NewGraphics(1);
        graphics.PublishSurfacePixels(40, White());
        graphics.SetSnowEffect(1, 40, 1f, fades ? 1 : 0);
        Scene3DSnowEffect snow = graphics.Scene3D.Snow!;
        snow.Place(0, new Scene3DSnowFlake(new Vector3(0, y, 10), 0.05f));

        FrameItem item = Assert.Single(graphics.LowerFrame(0).Items);

        Assert.All(item.Polygon!.Vertices, corner => Assert.Equal(alpha << 24 | 0xffffff, corner.Argb));
    }

    [Fact]
    public void AFlakeIsASquareOfItsTextureFacingTheViewerAtTheDisplaysView()
    {
        Graphics graphics = NewGraphics(1);
        graphics.PublishSurfacePixels(40, White());
        graphics.SetSnowEffect(1, 40, 1f, 1);
        graphics.Scene3D.Snow!.Place(0, new Scene3DSnowFlake(new Vector3(0, 2.5f, 10), 0.05f));
        // Op 0x32c moves the device's camera, not the view the display handed its effects: still the identity.
        graphics.SetCameraLookAt((0, 2.5f, -10), (0, 2.5f, 0));

        FrameItem item = Assert.Single(graphics.LowerFrame(0).Items);

        Assert.Equal(40L, item.DynamicKey);
        Assert.NotNull(item.Pixels);
        FramePolygonVertex[] corners = item.Polygon!.Vertices;
        double centreY = 288.5 - 2.5 / 10 * PixelsAtOne, half = 0.05 / 10 * PixelsAtOne;
        // Top left, top right, bottom left; top right, bottom right, bottom left: the strip's two triangles.
        (double X, double Y, float U, float V)[] expected =
        [
            (512.5 - half, centreY - half, 0, 0), (512.5 + half, centreY - half, 1, 0),
            (512.5 - half, centreY + half, 0, 1), (512.5 + half, centreY - half, 1, 0),
            (512.5 + half, centreY + half, 1, 1), (512.5 - half, centreY + half, 0, 1),
        ];
        Assert.Equal(expected.Length, corners.Length);
        for (int index = 0; index < corners.Length; index++)
        {
            Assert.Equal(expected[index].X, corners[index].X, 3);
            Assert.Equal(expected[index].Y, corners[index].Y, 3);
            Assert.Equal((expected[index].U, expected[index].V), (corners[index].U, corners[index].V));
        }

        // A screen-mode switch sets the device up again: the display hands its effects the camera's view, ten units
        // farther from the flake, which is half the size and on the axis.
        graphics.SetUpDeviceTransforms();
        corners = Assert.Single(graphics.LowerFrame(0).Items).Polygon!.Vertices;
        Assert.Equal(512.5 - half / 2, corners[0].X, 3);
        Assert.Equal(288.5 - half / 2, corners[0].Y, 3);
    }

    [Fact]
    public void TheFlakesAreDrawnFarthestFirstAndKeepThatOrder()
    {
        Graphics graphics = NewGraphics(1);
        graphics.PublishSurfacePixels(40, White());
        graphics.SetSnowEffect(3, 40, 1f, 0);
        Scene3DSnowEffect snow = graphics.Scene3D.Snow!;
        snow.Place(0, new Scene3DSnowFlake(new Vector3(0, 0, 5), 0.03f));
        snow.Place(1, new Scene3DSnowFlake(new Vector3(0, 0, 15), 0.04f));
        snow.Place(2, new Scene3DSnowFlake(new Vector3(0, 0, -5), 0.05f));     // behind the viewer: not drawn

        IReadOnlyList<FrameItem> items = graphics.LowerFrame(0).Items;

        Assert.Equal([0.04f, 0.03f, 0.05f], snow.Flakes.Select(flake => flake.Size));   // depths 15, 5, -5
        Assert.Equal(2, items.Count);
        float Across(FrameItem item) => item.Polygon!.Vertices[1].X - item.Polygon.Vertices[0].X;
        Assert.True(Across(items[0]) < Across(items[1]));                        // the far one first, smaller
    }

    [Fact]
    public void NativesQsortLeavesEqualItemsInItsOwnOrder()
    {
        // Eight or fewer: the selection sort moves the greatest to the end, the first of equals staying the greatest.
        string[] items = ["a", "b", "c"];
        MsvcQsort.Sort(items, (_, _) => 0);
        Assert.Equal(["b", "c", "a"], items);

        // More: the quicksort, which still orders what it can tell apart.
        var random = new Random(4);
        int[] keys = Enumerable.Range(0, 200).Select(_ => random.Next(20)).ToArray();
        int[] sorted = (int[])keys.Clone();
        MsvcQsort.Sort(sorted, (a, b) => a.CompareTo(b));
        Assert.Equal(keys.OrderBy(key => key), sorted);
    }

    [Theory]
    [InlineData(1f, 3f, 4)]           // 2: ceil + floor, 2 + 2
    [InlineData(1f, 1.5f, 1)]         // 0.5: 1 + 0
    [InlineData(1.5f, 1f, -1)]        // -0.5: -0 + -1
    [InlineData(2f, 2f, 0)]
    public void TheSnowsComparisonIsCeilPlusFloorOfTheDifference(float a, float b, int result)
        => Assert.Equal(result, Scene3DSnowEffect.CompareDepth(a, b));

    // A graphics native with the camera ten units in front of the origin at half the box's height, ambient 0x404040,
    // and a rain effect of the drops given.
    private static Graphics RainScene(params Scene3DRainDrop[] drops)
    {
        Graphics graphics = NewGraphics(2);
        graphics.SetCameraLookAt((0, 2.5f, -10), (0, 2.5f, 0));
        graphics.SetAmbientLight(0xff404040);
        graphics.SetRainEffect(drops.Length);
        for (int index = 0; index < drops.Length; index++) graphics.Scene3D.Rain!.Place(index, drops[index]);
        return graphics;
    }

    private static Scene3DRainDrop Streak(float y)
        => new(new Vector3(0, y, 0), Vector3.Zero, Vector3.Zero, Vector3.Zero, 1f, false, 0, 0);

    private static Scene3DRainDrop Ring(int age, int life)
        => new(new Vector3(0, 0.01f, 0), Vector3.Zero, Vector3.Zero, Vector3.Zero, age * 0.01f, true, age, life);

    [Fact]
    public void AStreakIsALinePixelThickInTheAmbientColourAtHalfTheFade()
    {
        Graphics graphics = RainScene(Streak(5f));
        graphics.SetEffectHandles(0, 0);

        FramePolygonVertex[] corners = Assert.Single(graphics.LowerFrame(0).Items).Polygon!.Vertices;

        // sin(π/2) / 2 = 0.5: alpha 128; the colour is the ambient light's alone.
        Assert.All(corners, corner => Assert.Equal(0x80404040u, corner.Argb));
        // The streak runs from y = 5.5 to 4.5, ten units off: a tall line, so half a pixel either side in x.
        Assert.Equal([512f, 513f], corners.Select(corner => corner.X).Distinct().Order());
        double top = 288.5 - 3.0 / 10 * PixelsAtOne, bottom = 288.5 - 2.0 / 10 * PixelsAtOne;
        Assert.Equal(top, corners.Min(corner => corner.Y), 3);
        Assert.Equal(bottom, corners.Max(corner => corner.Y), 3);
        Assert.Equal(6, corners.Length);
    }

    [Fact]
    public void ARingIsSixteenLinesAndFadesOverItsLife()
    {
        Graphics graphics = RainScene(Ring(age: 5, life: 20));
        graphics.SetEffectHandles(0, 0);

        FramePolygonVertex[] corners = Assert.Single(graphics.LowerFrame(0).Items).Polygon!.Vertices;

        Assert.Equal(16 * 6, corners.Length);
        Assert.All(corners, corner => Assert.Equal((uint)(0.75f * 255 + 0.5f) << 24 | 0x404040u, corner.Argb));
    }

    [Fact]
    public void ALineBehindTheViewerIsNotDrawnAndOneAcrossTheNearPlaneIsCut()
    {
        Graphics behind = RainScene(Streak(5f) with { Position = new Vector3(0, 5, -12) });
        Assert.Empty(behind.LowerFrame(0).Items);

        // Tilted a quarter turn about x, a streak at the camera's plane runs from in front of it to behind: what is
        // left starts at the near plane, a unit in front of the camera.
        Graphics across = RainScene(Streak(2.5f) with
        {
            Position = new Vector3(0.5f, 2.5f, -9f), Rotation = new Vector3(0, MathF.PI / 2, 0),
        });
        FramePolygonVertex[] corners = Assert.Single(across.LowerFrame(0).Items).Polygon!.Vertices;
        Assert.All(corners, corner => Assert.True(float.IsFinite(corner.X) && float.IsFinite(corner.Y)));
        Assert.Equal(512.5 + 0.5 / 1 * PixelsAtOne, corners.Max(corner => corner.X), 1);
    }

    private static GfxState.PolygonVertex[] Square(uint argb) =>
    [
        new(0, 0, 0, argb, 0, 0), new(4, 0, 0, argb, 1, 0), new(0, 4, 0, argb, 0, 1), new(4, 4, 0, argb, 1, 1),
    ];

    [Fact]
    public void TheRingsAreDrawnAtTheFirstHandleAndTheStreaksSnowAndLeavesAtTheSecond()
    {
        Graphics graphics = RainScene(Ring(age: 1, life: 15), Streak(5f));
        graphics.PublishSurfacePixels(40, White());
        graphics.SetSnowEffect(1, 40, 1f, 0);
        graphics.Scene3D.Snow!.Place(0, new Scene3DSnowFlake(new Vector3(0, 0, 10), 0.05f));
        graphics.SetEffectHandles(5, 11);
        graphics.State.CreatePolygon(1, Square(0xff000000), 0);
        graphics.State.CreatePolygon(10, Square(0xffffffff), 0);

        FrameDescription frame = graphics.LowerFrame(0);

        // The polygon at 1, the ring, the polygon at 10, then after the last: the streak and the flake.
        Assert.Equal([6, 96, 6, 6, 6], frame.Items.Select(item => item.Polygon!.Vertices.Length));
        Assert.Equal(0x80404040u, frame.Items[3].Polygon!.Vertices[0].Argb);
        Assert.Equal(40L, frame.Items[4].DynamicKey);
    }

    // A 16 by 16 screen with one flake of the white slot 40, two units in front of the eye: a quarter unit either side
    // of its centre is about two and a half pixels at the screen's scale.
    private static (Graphics Graphics, FrameClock Clock) OneFlakeOnASmallScreen()
    {
        var clock = new FrameClock();
        var graphics = new Graphics(null, clock, new Sys4LogicalCanvas(16, 16), gameRandom: new CrtRandom(1));
        graphics.PublishSurfacePixels(40, White());
        graphics.SetSnowEffect(1, 40, 1f, 0);
        graphics.Scene3D.Snow!.Place(0, new Scene3DSnowFlake(new Vector3(0, 0.1f, 2), 0.25f));
        return (graphics, clock);
    }

    [Fact]
    public void AFramePresentedIntoARenderTargetDrawsTheEffectsThereAtItsSizeThenStepsThem()
    {
        (Graphics graphics, FrameClock clock) = OneFlakeOnASmallScreen();
        graphics.LowerFrame(clock.NowMs);                        // the effects' clock starts here
        graphics.CreateSurface(5, 8, 8);
        graphics.State.SelectRenderTarget(5);
        clock.Advance(0.05);                                     // three 1/60 s ticks

        Assert.False(graphics.PresentFrame());

        // The surface is the viewport: the flake lands at its centre, (4, 4), not at the screen's.
        Assert.Equal([255, 255, 255, 255], graphics.PixelAt(5, 4, 4));
        Assert.Equal([0, 0, 0, 0], graphics.PixelAt(5, 0, 0));
        Assert.Equal(0.07f, graphics.Scene3D.Snow!.Flakes[0].Position.Y, 5);
    }

    [Theory]
    [InlineData(5, false)]       // the walk ends at handle 5, before the second handle, 11
    [InlineData(11, true)]
    [InlineData(20, true)]
    public void ARangePresentDrawsAGroupOnlyWhenItsHandleIsWithinTheRange(long count, bool drawn)
    {
        (Graphics graphics, _) = OneFlakeOnASmallScreen();
        graphics.SetEffectHandles(5, 11);
        var destination = new RgbaImage(16, 16, new byte[16 * 16 * 4]);

        graphics.DrawObjectRange(destination, [], 0, count, sceneEffects: true);

        Assert.Equal(drawn ? 255 : 0, destination.Pixels[(8 * 16 + 8) * 4 + 3]);
    }

    [Fact]
    public void EachTickStepsTheRainThenTheSnowThenTheLeaves()
    {
        Graphics graphics = NewGraphics(9, 16, 16);
        graphics.PublishSurfacePixels(40, White());
        graphics.SetRainEffect(1);
        graphics.SetSnowEffect(1, 40, 1f, 1);
        Assert.Equal(17L, graphics.GameRandom.Draws);
        // Both pass the floor on the next step: the drop draws its ring's life, the flake its three.
        graphics.Scene3D.Rain!.Place(0, new Scene3DRainDrop(new Vector3(0, 0.05f, 0), new Vector3(0, -0.1f, 0),
                                                            Vector3.Zero, Vector3.Zero, 1f, false, 0, 0));
        graphics.Scene3D.Snow!.Place(0, new Scene3DSnowFlake(new Vector3(0, 0.005f, 0), 0.05f));

        graphics.LowerFrame(1000);            // the first lowering only sets the clock
        graphics.LowerFrame(1017);            // one tick

        var draws = new Draws(9, 17);
        Assert.Equal((int)(draws.Unit() * 5.0 + 15.0), graphics.Scene3D.Rain.Drops[0].RippleLife);
        float x = (float)((double)draws.Centered() * 20), z = (float)((double)draws.Centered() * 20);
        float size = (float)((0.06f - (double)0.03f) * draws.Unit() + 0.03f);
        Assert.Equal(new Scene3DSnowFlake(new Vector3(x, 5, z), size), graphics.Scene3D.Snow.Flakes[0]);
        Assert.Equal(21L, graphics.GameRandom.Draws);
    }
}
