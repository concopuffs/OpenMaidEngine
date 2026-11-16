using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Ome.Launcher.ViewModels;
using Ome.Launcher.Views;

/// <summary>The window's frame, drawn headless (launcher plan §6.1): both themes take the mock's palette, and the text
/// is set in the bundled Noto Sans JP.</summary>
public sealed class WindowTests : IDisposable
{
    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public void TheWindowDrawsInBothThemesWithTheMocksPaletteAndTheLaunchersFont()
    {
        Headless.Run(() =>
        {
            foreach ((ThemeVariant theme, string name, uint side, uint win) in new[]
                     {
                         (ThemeVariant.Light, "light", 0xeeeeebu, 0xf7f7f5u),
                         (ThemeVariant.Dark, "dark", 0x1f2023u, 0x18191bu),
                     })
            {
                var window = new MainWindow { DataContext = new MainViewModel(_home.Session(language: "ja")) };
                var frame = Headless.Render(window, theme, $"6.1-frame-{name}");
                Assert.Equal(side, Headless.Pixel(frame, 150, 600));
                Assert.Equal(win, Headless.Pixel(frame, 800, 400));

                TextBlock heading = window.GetLogicalDescendants().OfType<TextBlock>().First();
                Assert.Equal("プロファイル", heading.Text);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(heading.FontFamily), out var face));
                Assert.Equal("Noto Sans JP", face!.FamilyName);
                window.Close();
            }
        });
    }
}
