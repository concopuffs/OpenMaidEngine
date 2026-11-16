using System.Diagnostics;
using Avalonia;

namespace Ome.Launcher.Spike;

/// <summary>
/// M0's measurement program (plan §2.1). The modes that return before Avalonia starts stand for the real launcher's
/// <c>--check</c> and <c>--print-command</c> (spec §8.6), its capture of the engine's output (spec §8.5) and the
/// run lock's open (spec §5.5). With no mode it opens the window.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--check-like":
                    return CheckLike.Run(args[1..]);
                case "--capture":
                    return Capture.Run(args[1..]);
                case "--lock":
                    return LockProbe.Run(args[1..]);
            }
        }

        SpikeApp.Options = WindowOptions.Parse(args);
        return AppBuilder.Configure<SpikeApp>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>The time since the operating system created this process.</summary>
    public static TimeSpan SinceProcessStart()
    {
        using Process self = Process.GetCurrentProcess();
        return DateTime.Now - self.StartTime;
    }

    public static string? OptionValue(string[] args, string name)
    {
        int at = Array.LastIndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}

internal sealed record WindowOptions(string? FirstFrameFile, string? ScreenshotFile, string? ModulesFile, bool ExitAfterFirstFrame)
{
    public static WindowOptions Parse(string[] args) => new(
        Program.OptionValue(args, "--first-frame-file"),
        Program.OptionValue(args, "--screenshot"),
        Program.OptionValue(args, "--modules-file"),
        args.Contains("--exit-after-first-frame"));
}
