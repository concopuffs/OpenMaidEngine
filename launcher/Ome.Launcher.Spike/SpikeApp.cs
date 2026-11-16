using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Ome.Launcher.Spike;

internal sealed class SpikeApp : Application
{
    public static WindowOptions Options { get; set; } = new(null, null, null, false);

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SpikeWindow(Options);
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class SpikeWindow : Window
{
    private const string FontUri = "avares://Ome.Launcher.Spike/Assets/Fonts#Noto Sans JP";
    private readonly WindowOptions _options;
    private readonly TextBlock _timing = new() { FontSize = 14 };
    private int _frames;

    public SpikeWindow(WindowOptions options)
    {
        _options = options;
        Title = "Open Maid Engine — launcher spike";
        Width = 640;
        Height = 280;
        FontFamily = new FontFamily(FontUri);
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "姫狩りダンジョンマイスター", FontSize = 32 },
                new TextBlock { Text = "神採りアルケミーマイスター · Kamidori Alchemy Meister", FontSize = 18 },
                _timing,
            },
        };
        Opened += (_, _) => RequestAnimationFrame(OnFrame);
    }

    /// <summary>
    /// The first callback runs as the first frame starts; the second one runs once that frame was rendered, so
    /// its time is the one recorded as "first frame".
    /// </summary>
    private void OnFrame(TimeSpan _)
    {
        _frames++;
        if (_frames < 2)
        {
            RequestAnimationFrame(OnFrame);
            return;
        }

        TimeSpan elapsed = Program.SinceProcessStart();
        string ms = elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
        _timing.Text = $"first frame {ms} ms after process start · font {ResolvedFamily()}";
        if (_options.FirstFrameFile is { } frameFile)
        {
            File.AppendAllText(frameFile, ms + Environment.NewLine);
        }
        if (_options.ModulesFile is { } modulesFile)
        {
            File.WriteAllLines(modulesFile, NativeModules());
        }
        if (_options.ScreenshotFile is { } shot)
        {
            // One more frame, so the timing line is in the picture.
            RequestAnimationFrame(_ => SaveScreenshot(shot));
            return;
        }
        Finish();
    }

    private void SaveScreenshot(string path)
    {
        var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(this);
        bitmap.Save(path);
        Finish();
    }

    private void Finish()
    {
        if (_options.ExitAfterFirstFrame)
        {
            Dispatcher.UIThread.Post(Close);
        }
    }

    private string ResolvedFamily()
    {
        var typeface = new Typeface(FontFamily);
        return FontManager.Current.TryGetGlyphTypeface(typeface, out GlyphTypeface? glyphs)
            ? glyphs.FamilyName
            : "(not resolved)";
    }

    /// <summary>
    /// Every native library this process mapped, with its path. On Linux that includes what Avalonia opens with
    /// <c>dlopen</c> (X11, fontconfig), which <c>ldd</c> does not show.
    /// </summary>
    private static IEnumerable<string> NativeModules()
    {
        IEnumerable<string> paths;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            paths = File.ReadAllLines("/proc/self/maps")
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length >= 6 && parts[5].StartsWith('/'))
                .Select(parts => parts[5]);
        }
        else
        {
            using Process self = Process.GetCurrentProcess();
            paths = self.Modules.Cast<ProcessModule>().Select(m => m.FileName ?? m.ModuleName).ToList();
        }
        return paths
            .Distinct()
            .Where(p => p.Contains(".so", StringComparison.Ordinal) || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Order();
    }
}
