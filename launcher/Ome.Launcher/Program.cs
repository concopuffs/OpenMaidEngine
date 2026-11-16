using System.Runtime.InteropServices;
using Ome.Launcher.Core;

namespace Ome.Launcher;

/// <summary>
/// OME-Launcher's entry point. The command line (spec §8.6) runs before any window: <see cref="LauncherCommandLine"/>
/// does the work, and where its outcome says the window would open, M2's program says that the window is not built yet
/// (launcher plan M3) and exits non-zero.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ParentConsole.AttachIfNeeded();
        string program = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "OME-Launcher.exe" : "OME-Launcher");
        var context = new LauncherContext(program, Environment.CurrentDirectory, LauncherEnvironment.Current, Console.Out,
                                          Console.Error);
        LauncherOutcome outcome = LauncherCommandLine.Run(args, context);
        if (outcome.Window != WindowReason.None)
        {
            Console.Error.WriteLine($"OME-Launcher: {Reason(outcome.Window)}, but the launcher's window is not built yet "
                                    + "(launcher plan M3)");
            if (outcome.Window == WindowReason.Requested) Console.Error.WriteLine(LauncherCommandLine.Usage);
            return outcome.ExitCode == LauncherCommandLine.Success ? LauncherCommandLine.Failure : outcome.ExitCode;
        }
        return outcome.ExitCode;
    }

    private static string Reason(WindowReason reason) => reason switch
    {
        WindowReason.CheckFailed => "the window would open at the failed check",
        WindowReason.StartFailed => "the window would open at the failed start",
        WindowReason.Review => "the window would open at the root's review",
        _ => "the window would open",
    };
}

/// <summary>
/// How a windowed program prints on Windows (spec §8.6, plan §3.5): with no standard output of its own, it joins the
/// console it was started from with <c>AttachConsole(ATTACH_PARENT_PROCESS)</c>, before <see cref="Console"/> first
/// reads its handles. A redirected output (a pipe, a file) is used as it is. Elsewhere the standard handles are
/// inherited.
/// </summary>
internal static class ParentConsole
{
    private const int StdOutput = -11;
    private const int AttachParentProcess = -1;
    private const uint FileTypeUnknown = 0;

    public static void AttachIfNeeded()
    {
        if (!OperatingSystem.IsWindows()) return;
        IntPtr output = GetStdHandle(StdOutput);
        if (output != IntPtr.Zero && output != new IntPtr(-1) && GetFileType(output) != FileTypeUnknown) return;
        AttachConsole(AttachParentProcess);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);
}
