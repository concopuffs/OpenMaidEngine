using Age.Engine.Sys4;

public class WindowLaunchOptionsTests
{
    private static readonly Sys4LogicalCanvas LogicalCanvas = new(1024, 576);

    [Fact]
    public void DefaultsPhysicalWindowToLogicalCanvas()
    {
        var options = WindowLaunchOptions.Resolve([], LogicalCanvas);

        Assert.Equal(new WindowLaunchOptions(1024, 576, false, false), options);
        Assert.False(options.IsOverridden);
    }

    [Fact]
    public void WidthAndHeightOverrideOnlyPhysicalDimensions()
    {
        var options = WindowLaunchOptions.Resolve(
            ["--scene", "SC0000", "--window-width", "1600", "--window-height", "900"],
            LogicalCanvas);

        Assert.Equal(new WindowLaunchOptions(1600, 900, true, true), options);
        Assert.Equal(new Sys4LogicalCanvas(1024, 576), LogicalCanvas);
    }

    [Theory]
    [InlineData("--window-width", "1280", 1280, 576, true, false)]
    [InlineData("--window-height", "720", 1024, 720, false, true)]
    public void PartialOverrideKeepsOtherLogicalDefault(
        string argument, string value, int width, int height,
        bool widthOverridden, bool heightOverridden)
    {
        var options = WindowLaunchOptions.Resolve([argument, value], LogicalCanvas);

        Assert.Equal(new WindowLaunchOptions(
            width, height, widthOverridden, heightOverridden), options);
    }

    [Fact]
    public void DuplicateDimensionUsesLastValue()
    {
        var options = WindowLaunchOptions.Resolve(
            ["--window-width", "1200", "--window-width", "1400"], LogicalCanvas);

        Assert.Equal(1400, options.Width);
        Assert.Equal(576, options.Height);
    }

    [Theory]
    [InlineData("--window-width")]
    [InlineData("--window-width", "0")]
    [InlineData("--window-height", "-1")]
    [InlineData("--window-width", "not-a-number")]
    [InlineData("--window-height", "16385")]
    [InlineData("--window-width", "--window-height", "720")]
    public void InvalidOrMissingDimensionIsRejected(params string[] arguments)
    {
        var error = Assert.Throws<ArgumentException>(
            () => WindowLaunchOptions.Resolve(arguments, LogicalCanvas));

        Assert.Contains(arguments[0], error.Message);
    }
}
