using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Styling;
using Avalonia.Threading;
using Ome.Engine.Sys4;
using Ome.Launcher;

/// <summary>
/// The headless harness of the window's tests (launcher plan §6.1): one Avalonia session for the test process, on
/// Skia, so a page is drawn as the program draws it, with no display. <see cref="Render"/> lays a window out, fails on
/// any warning Avalonia logged meanwhile (a binding that does not resolve, a resource that is missing), and writes the
/// frame to <c>build/launcher-shots/</c> to be looked at against the mock. Nothing is clicked (spec §13).
/// </summary>
internal static class Headless
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() =>
    {
        HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        session.Dispatch(() => Logger.Sink = Warnings.Sink, CancellationToken.None).GetAwaiter().GetResult();
        return session;
    });

    public static string ShotsFolder => Path.Combine(Paths.Repo, "build", "launcher-shots");

    /// <summary>Runs <paramref name="action"/> on the session's UI thread.</summary>
    public static T Run<T>(Func<T> action) => Session.Value.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();

    public static void Run(Action action) => Run(() => { action(); return 0; });

    /// <summary>Runs <paramref name="action"/> on the session's UI thread, awaiting it there.</summary>
    public static void Run(Func<Task> action)
        => Session.Value.Dispatch(async () => { await action(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Shows <paramref name="window"/> in <paramref name="theme"/>, lays it out, and returns its frame, which is
    /// written to <c>build/launcher-shots/&lt;name&gt;.png</c>. Fails on a warning Avalonia logged while it drew. Call on
    /// the UI thread.</summary>
    public static Avalonia.Media.Imaging.WriteableBitmap Render(Window window, ThemeVariant theme, string name)
    {
        Warnings.Sink.Clear();
        window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException($"{name}: the window drew no frame");
        Directory.CreateDirectory(ShotsFolder);
        frame.Save(Path.Combine(ShotsFolder, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        string[] logged = Warnings.Sink.Take();
        Assert.True(logged.Length == 0, $"{name}: Avalonia logged\n" + string.Join("\n", logged));
        return frame;
    }

    /// <summary>The colour of one pixel of a frame, as 0xRRGGBB.</summary>
    /// <summary>Waits until <paramref name="condition"/> holds, failing after a minute.</summary>
    public static async Task Until(Func<bool> condition, string what)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(60), $"timed out waiting for {what}");
            await Task.Delay(20);
        }
    }

    public static uint Pixel(Avalonia.Media.Imaging.WriteableBitmap frame, int x, int y)
    {
        using var locked = frame.Lock();
        unsafe
        {
            uint bgra = ((uint*)locked.Address)[y * (locked.RowBytes / 4) + x];
            return locked.Format == Avalonia.Platform.PixelFormat.Rgba8888
                ? ((bgra & 0xff) << 16) | (bgra & 0xff00) | ((bgra >> 16) & 0xff)
                : bgra & 0xffffff;
        }
    }

    /// <summary>The app the session runs: the launcher's, drawn by Skia with HarfBuzz's shaping, as on a desktop.</summary>
    public static class HeadlessApp
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>Avalonia's warnings and errors, kept until a render takes them.</summary>
    private sealed class Warnings : ILogSink
    {
        public static readonly Warnings Sink = new();
        private readonly List<string> _logged = [];

        public void Clear()
        {
            lock (_logged) _logged.Clear();
        }

        public string[] Take()
        {
            lock (_logged)
            {
                string[] taken = [.. _logged];
                _logged.Clear();
                return taken;
            }
        }

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
            => Log(level, area, source, messageTemplate, []);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
                        params object?[] propertyValues)
        {
            if (!IsEnabled(level, area)) return;
            lock (_logged) _logged.Add($"[{level} {area}] {source}: {messageTemplate} {string.Join(", ", propertyValues)}");
        }
    }
}
