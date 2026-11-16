using System.Diagnostics;
using System.Runtime.InteropServices;
using Ome.Launcher.Core;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>
/// The Shortcut dialog's text and files (launcher spec §8.4, plan §6.10): an argument quoted for Windows splits back
/// into itself through <c>CommandLineToArgvW</c>, a POSIX word through
/// <c>sh</c>'s rules; a <c>.desktop</c> file quotes its <c>Exec</c> by the Desktop Entry Specification; a <c>.lnk</c>
/// written on Windows reads back with the command's target, arguments and working folder.
/// </summary>
public sealed class ShortcutFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-shortcut-" + Guid.NewGuid().ToString("N"));

    public ShortcutFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly string[] Awkward =
    [
        "plain", "", "two words", @"C:\Program Files\OME\", "say \"hi\"", @"back\\slash""quote", @"trailing\",
        "it's", "$HOME", "日本語 フォルダー", "a%b",
    ];

    [Fact]
    public void AnArgumentQuotedForWindowsSplitsBackIntoItself()
    {
        Assert.Equal("plain", ShortcutFile.Quote("plain", OmePlatform.Windows));
        Assert.Equal("\"\"", ShortcutFile.Quote("", OmePlatform.Windows));
        Assert.Equal(@"""C:\Program Files\OME\\""", ShortcutFile.Quote(@"C:\Program Files\OME\", OmePlatform.Windows));
        Assert.Equal(@"""say \""hi\""""", ShortcutFile.Quote("say \"hi\"", OmePlatform.Windows));
        if (!OperatingSystem.IsWindows()) return;   // CommandLineToArgvW is Windows' own parser

        // Windows' own parser splits the line back; the first word is the program, which it reads by other rules.
        string line = string.Join(" ", Awkward.Select(argument => ShortcutFile.Quote(argument, OmePlatform.Windows)));
        Assert.Equal(Awkward, Split("OME.exe " + line).Skip(1));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static string[] Split(string commandLine)
    {
        IntPtr argv = CommandLineToArgvW(commandLine, out int count);
        Assert.NotEqual(IntPtr.Zero, argv);
        try
        {
            return Enumerable.Range(0, count)
                .Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, index * IntPtr.Size))!).ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [Fact]
    public void APosixWordIsQuotedOnlyWhenItNeedsIt()
    {
        Assert.Equal("--data-root", ShortcutFile.Quote("--data-root", OmePlatform.Linux));
        Assert.Equal("/home/ann/OME/profiles/kamidori", ShortcutFile.Quote("/home/ann/OME/profiles/kamidori",
                                                                           OmePlatform.Linux));
        Assert.Equal("''", ShortcutFile.Quote("", OmePlatform.Linux));
        Assert.Equal("'it'\\''s'", ShortcutFile.Quote("it's", OmePlatform.Linux));
        Assert.Equal("'$HOME'", ShortcutFile.Quote("$HOME", OmePlatform.Linux));
        Assert.Equal("'two words'", ShortcutFile.Quote("two words", OmePlatform.Linux));
        if (OperatingSystem.IsWindows()) return;   // sh is the reader

        string line = "printf '%s\\n' " + string.Join(" ", Awkward.Where(word => word.Length > 0)
                                                            .Select(word => ShortcutFile.Quote(word, OmePlatform.Linux)));
        var start = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(line);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(Awkward.Where(word => word.Length > 0), output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void TheCommandLineCarriesTheEnvironmentOnLinuxAndRefusesItOnWindows()
    {
        var linux = new EngineCommand("/opt/OME Engine/OME", ["--", "--data-root", "/home/ann/OME/profiles/k"],
                                      "/opt/OME Engine", new Dictionary<string, string> { ["PWD"] = "/opt/OME Engine" });
        Assert.Equal("env 'PWD=/opt/OME Engine' '/opt/OME Engine/OME' -- --data-root /home/ann/OME/profiles/k",
                     ShortcutFile.CommandLine(linux, OmePlatform.Linux));
        Assert.Throws<ArgumentException>(() => ShortcutFile.CommandLine(linux, OmePlatform.Windows));

        var windows = new EngineCommand(@"C:\Games\OME\OME.exe", ["--", "--data-root", @"C:\Users\ann\OME\profiles\k"],
                                        @"C:\Games\OME", new Dictionary<string, string>());
        Assert.Equal(@"C:\Games\OME\OME.exe -- --data-root C:\Users\ann\OME\profiles\k",
                     ShortcutFile.CommandLine(windows, OmePlatform.Windows));
        Assert.Equal(@"-- --data-root C:\Users\ann\OME\profiles\k", ShortcutFile.Arguments(windows, OmePlatform.Windows));
    }

    [Fact]
    public void ADesktopEntryQuotesItsExecByTheSpecification()
    {
        var command = new EngineCommand("/opt/OME Engine/OME", ["--", "--data-root", "/home/ann/My \"Saves\"/100%", @"a\b"],
                                        "/opt/OME Engine", new Dictionary<string, string> { ["PWD"] = "/opt/OME Engine" });
        Assert.Equal(
            "[Desktop Entry]\n"
            + "Type=Application\n"
            + "Version=1.5\n"
            + "Name=神採り (日本語)\n"
            + "Exec=env \"PWD=/opt/OME Engine\" \"/opt/OME Engine/OME\" -- --data-root "
            + "\"/home/ann/My \\\\\"Saves\\\\\"/100%%\" \"a\\\\\\\\b\"\n"
            + "Path=/opt/OME Engine\n"
            + "Terminal=false\n"
            + "Categories=Game;\n",
            ShortcutFile.DesktopEntry("神採り (日本語)", command));
        Assert.Throws<ArgumentException>(() => ShortcutFile.DesktopEntry("two\nlines", command));

        string path = ShortcutFile.PathFor(_root, "神採り (日本語)", OmePlatform.Linux);
        Assert.Equal(Path.Combine(_root, "神採り (日本語).desktop"), path);
        ShortcutFile.Write(path, "神採り (日本語)", command, OmePlatform.Linux);
        Assert.Equal(ShortcutFile.DesktopEntry("神採り (日本語)", command), File.ReadAllText(path));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void AFileNameKeepsOnlyWhatAFileMayHold()
    {
        Assert.Equal(Path.Combine(_root, "a_b_c.lnk"), ShortcutFile.PathFor(_root, "a/b:c", OmePlatform.Windows));
        Assert.Equal(Path.Combine(_root, "OME profile.lnk"), ShortcutFile.PathFor(_root, "...", OmePlatform.Windows));
        Assert.Equal(Path.Combine(_root, "CON_.lnk"), ShortcutFile.PathFor(_root, "CON", OmePlatform.Windows));
        Assert.Equal(Path.Combine(_root, "Kamidori.desktop"), ShortcutFile.PathFor(_root, " Kamidori. ", OmePlatform.Linux));
    }

    [Fact]
    public void AWindowsShortcutReadsBackWithTheCommand()
    {
        if (!OperatingSystem.IsWindows()) return;   // a .lnk is written through the Windows shell
        string program = Path.Combine(_root, "OME Engine", "OME.exe");
        var command = new EngineCommand(program, ["--", "--data-root", Path.Combine(_root, "profiles", "神採り")],
                                        Path.GetDirectoryName(program)!, new Dictionary<string, string>());
        string path = ShortcutFile.PathFor(_root, "神採り (日本語)", OmePlatform.Windows);
        ShortcutFile.Write(path, "神採り (日本語)", command, OmePlatform.Windows);
        WindowsLink link = ShortcutFile.ReadWindowsLink(path);
        Assert.Equal(program, link.Target);
        Assert.Equal(ShortcutFile.Arguments(command, OmePlatform.Windows), link.Arguments);
        Assert.Equal(Path.GetDirectoryName(program), link.WorkingDirectory);
        Assert.Equal("神採り (日本語)", link.Description);

        // Written again, the file is replaced.
        ShortcutFile.Write(path, "again", command with { Arguments = ["--", "--data-root", "x"] }, OmePlatform.Windows);
        Assert.Equal("-- --data-root x", ShortcutFile.ReadWindowsLink(path).Arguments);
        Assert.Throws<ArgumentException>(() => ShortcutFile.Write(
            path, "env", command with { Environment = new Dictionary<string, string> { ["PWD"] = "x" } },
            OmePlatform.Windows));
    }
}
