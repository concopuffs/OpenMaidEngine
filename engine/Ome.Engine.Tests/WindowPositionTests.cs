using Ome.Engine.Frontend.Shell;

/// <summary>
/// The kept window position (the catalog's SAVEWINPOS, AGE 4.38D; docs/engine-re.md, "The kept window position"):
/// where a window opens from a kept position, and the two OME.INI keys that keep it. Synthetic screens and a
/// temporary file; the catalog key's own test is in <see cref="LineEndCharactersTests"/>.
/// </summary>
public class WindowPositionTests
{
    private static readonly ScreenRect[] OneScreen = [new(0, 0, 1920, 1080)];

    /// <summary>A second screen to the right of the primary and a third to its left, as a desktop numbers them.</summary>
    private static readonly ScreenRect[] ThreeScreens =
        [new(0, 0, 1920, 1080), new(1920, 0, 1280, 1024), new(-1600, 180, 1600, 900)];

    [Theory]
    [InlineData(0, 0)]
    [InlineData(300, 200)]
    [InlineData(896, 504)]   // the last position at which a 1024 by 576 window lies on 1920 by 1080
    public void AKeptPositionOnTheScreenStands(int left, int top)
    {
        var kept = new WindowPosition(left, top);
        Assert.Equal(kept, WindowPosition.Restore(kept, 1024, 576, OneScreen, primary: 0));
    }

    [Theory]
    [InlineData(-1, 200)]     // native's loader: left < 0
    [InlineData(897, 200)]    // left + width past the screen's width
    [InlineData(300, -1)]     // top < 0
    [InlineData(300, 505)]    // top + height past the screen's height
    [InlineData(-32000, -32000)]   // what Windows reports for a minimized window
    public void AKeptPositionOffTheScreenIsCentredAsNativeCentresIt(int left, int top)
    {
        // (1920 - 1024) / 2 and (1080 - 576) / 2.
        Assert.Equal(new WindowPosition(448, 252),
                     WindowPosition.Restore(new WindowPosition(left, top), 1024, 576, OneScreen, primary: 0));
    }

    [Fact]
    public void WithNothingKeptTheWindowIsCentredHalvingTowardZero()
    {
        Assert.Equal(new WindowPosition(448, 252), WindowPosition.Restore(null, 1024, 576, OneScreen, primary: 0));
        // An odd difference, and a window larger than the screen: C's division, toward zero.
        Assert.Equal(new WindowPosition(447, 251), WindowPosition.Restore(null, 1025, 577, OneScreen, primary: 0));
        Assert.Equal(new WindowPosition(-40, -60), WindowPosition.Restore(null, 2001, 1201, OneScreen, primary: 0));
    }

    [Fact]
    public void AWindowLeftOnAnotherScreenOpensThereWhereNativeKnowsOnlyThePrimary()
    {
        var right = new WindowPosition(2000, 100);
        var left = new WindowPosition(-1500, 300);
        Assert.Equal(right, WindowPosition.Restore(right, 1024, 576, ThreeScreens, primary: 0));
        Assert.Equal(left, WindowPosition.Restore(left, 1024, 576, ThreeScreens, primary: 0));
        // Across two screens it lies on neither, and is centred on the primary, which need not be the first.
        Assert.Equal(new WindowPosition(448, 252),
                     WindowPosition.Restore(new WindowPosition(1500, 100), 1024, 576, ThreeScreens, primary: 0));
        Assert.Equal(new WindowPosition(1920 + 128, 224),
                     WindowPosition.Restore(new WindowPosition(1500, 100), 1024, 576, ThreeScreens, primary: 1));
    }

    [Fact]
    public void WithoutAScreenTheKeptPositionStands()
    {
        Assert.Equal(new WindowPosition(-5, 7), WindowPosition.Restore(new WindowPosition(-5, 7), 1024, 576, [], 0));
        Assert.Equal(default, WindowPosition.Restore(null, 1024, 576, [], 0));
    }

    [Theory]
    [InlineData("[display]\nWindowLeft=300\nWindowTop=200\n", 300, 200)]
    [InlineData("[DISPLAY]\nwindowleft = -1500 \nWINDOWTOP=40\n", -1500, 40)]   // names case-blind, a left screen
    public void TheFileKeepsThePositionInItsDisplaySection(string text, int left, int top) =>
        Assert.Equal(new WindowPosition(left, top), WindowPosition.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("[display]\nWindowWidth=1280\nWindowHeight=720\n")]
    [InlineData("[display]\nWindowLeft=300\n")]                      // half a position is none
    [InlineData("[display]\nWindowLeft=300\nWindowTop=high\n")]
    [InlineData("[window]\nWindowLeft=300\nWindowTop=200\n")]        // another section's keys
    public void AFileWithoutBothCoordinatesKeepsNoPosition(string text) => Assert.Null(WindowPosition.Parse(text));

    [Fact]
    public void ThePositionAndTheOptionsShareTheFileWithoutLosingEachOther()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"ome-window-position-{Guid.NewGuid():N}");
        try
        {
            var store = new OmeIniStore(Path.Combine(folder, PortOptions.FileName));
            Assert.Null(store.LoadWindowPosition());   // no file yet

            var options = new PortOptions("en", 1280, 720, FpsCounter: true, LocatorHud: false);
            store.Save(options);
            Assert.Null(store.LoadWindowPosition());
            store.SaveWindowPosition(new WindowPosition(300, 200));

            Assert.Equal(new WindowPosition(300, 200), store.LoadWindowPosition());
            Assert.Equal(options, store.Load());

            // The options panel's write keeps the position, and a later exit replaces it in place.
            store.Save(options with { Language = "ja", WindowWidth = 0, WindowHeight = 0 });
            Assert.Equal(new WindowPosition(300, 200), store.LoadWindowPosition());
            store.SaveWindowPosition(new WindowPosition(-1500, 40));
            Assert.Equal(new WindowPosition(-1500, 40), store.LoadWindowPosition());
            Assert.Equal("ja", store.Load().Language);
            Assert.Equal(
                "[ui]\nLanguage=ja\n[display]\nWindowWidth=0\nWindowHeight=0\nWindowLeft=-1500\nWindowTop=40\n" +
                "[debug]\nFpsCounter=1\nLocatorHud=0\n",
                File.ReadAllText(store.FilePath));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
