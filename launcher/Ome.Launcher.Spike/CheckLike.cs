using System.Runtime.InteropServices;
using System.Text;

namespace Ome.Launcher.Spike;

/// <summary>
/// Stands for the launcher's <c>--check</c> (spec §8.6): runs before Avalonia, prints a few lines to standard
/// output and one to standard error, and exits 1 as a failed check does. <c>--no-attach</c> skips attaching to the
/// parent's console; <c>--output FILE</c> writes the same lines to a file as well.
/// </summary>
internal static class CheckLike
{
    public static int Run(string[] args)
    {
        string console = args.Contains("--no-attach") ? ConsoleBridge.Describe() : ConsoleBridge.AttachIfNeeded();
        string? output = Program.OptionValue(args, "--output");
        string[] lines =
        [
            "spike check: 1/4 the install holds the profile's game",
            $"spike check: 2/4 console: {console}",
            "spike check: 3/4 profile 姫狩り (日本語) — non-ASCII line",
            "spike check: 4/4 FAILED: a check failed, exit code 1",
        ];
        foreach (string line in lines)
        {
            Console.Out.WriteLine(line);
        }
        Console.Out.Flush();
        Console.Error.WriteLine("spike check: a line on standard error");
        Console.Error.Flush();
        if (output is not null)
        {
            File.WriteAllLines(output, lines, new UTF8Encoding(false));
        }
        return 1;
    }
}

/// <summary>
/// How a windowed program reaches a console on Windows. A WinExe started from a console gets no standard handles
/// unless they were redirected; <c>AttachConsole(ATTACH_PARENT_PROCESS)</c> joins the parent's console, and the
/// handles are then opened on <c>CONOUT$</c> before <see cref="Console"/> first reads them.
/// </summary>
internal static class ConsoleBridge
{
    private const int StdOutput = -11;
    private const int StdError = -12;
    private const int AttachParentProcess = -1;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;

    public static string Describe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "not Windows: standard handles as inherited";
        }
        return $"stdout {HandleKind(GetStdHandle(StdOutput))}, stderr {HandleKind(GetStdHandle(StdError))}, not attached";
    }

    public static string AttachIfNeeded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "not Windows: standard handles as inherited";
        }
        string before = HandleKind(GetStdHandle(StdOutput));
        if (before is not ("none" or "unknown"))
        {
            return $"stdout inherited ({before}), not attached";
        }
        if (!AttachConsole(AttachParentProcess))
        {
            return $"stdout {before}; AttachConsole failed (error {Marshal.GetLastWin32Error()}): no console to print to";
        }
        string afterAttach = HandleKind(GetStdHandle(StdOutput));
        if (afterAttach is "none" or "unknown")
        {
            IntPtr conout = CreateFileW("CONOUT$", GenericWrite, FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            SetStdHandle(StdOutput, conout);
            SetStdHandle(StdError, conout);
            return $"stdout {before}; attached to the parent's console; handles were {afterAttach}, reopened on CONOUT$ ({HandleKind(conout)}), code page {GetConsoleOutputCP()}";
        }
        return $"stdout {before}; attached to the parent's console; handles {afterAttach} after attaching, code page {GetConsoleOutputCP()}";
    }

    private static string HandleKind(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return "none";
        }
        if (handle == new IntPtr(-1))
        {
            return "invalid";
        }
        return GetFileType(handle) switch
        {
            1 => "disk",
            2 => "char",
            3 => "pipe",
            _ => "unknown",
        };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll")]
    private static extern bool SetStdHandle(int which, IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleOutputCP();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
