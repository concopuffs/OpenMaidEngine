using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ome.Mods.Launch;

namespace Ome.Launcher.Core;

/// <summary>
/// The program a launch starts, with the arguments that come before Godot's flags (launcher spec §8.5): the engine
/// beside the launcher, or the development engine <c>--engine PROGRAM</c> names with its <c>--engine-arg</c>s (the
/// Godot editor with <c>--path godot</c>). Only the launcher's own flags name one; no file can (spec §12).
/// </summary>
public sealed record EngineProgram(string Program, IReadOnlyList<string> Arguments)
{
    /// <summary>The engine beside the launcher: <c>OME.exe</c> on Windows, <c>OME</c> elsewhere.</summary>
    public static EngineProgram BesideLauncher(string launcherDirectory, OmePlatform platform)
        => new(Path.Combine(Path.GetFullPath(launcherDirectory), platform == OmePlatform.Windows ? "OME.exe" : "OME"), []);

    /// <summary>A development engine. Its program is made absolute against <paramref name="workingDirectory"/>; its
    /// arguments are passed as given, and an argument of <c>--</c> is refused, since the launcher puts the <c>--</c>
    /// after Godot's flags itself.</summary>
    public static EngineProgram Development(string program, IReadOnlyList<string> arguments, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        if (arguments.Contains("--"))
            throw new ArgumentException("an --engine-arg of -- is refused: the launcher puts the -- after Godot's flags");
        return new(Path.GetFullPath(program, workingDirectory), arguments);
    }
}

/// <summary>A command to start: the program, its arguments, its working folder and the environment it sets, as
/// <c>--print-command</c> prints it (spec §8.6).</summary>
public sealed record EngineCommand(string Program, IReadOnlyList<string> Arguments, string WorkingDirectory,
                                   IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>The command as JSON: <c>program</c>, <c>arguments</c>, <c>working_directory</c>, <c>environment</c>.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
        {
            json.WriteStartObject();
            json.WriteString("program", Program);
            json.WriteStartArray("arguments");
            foreach (string argument in Arguments) json.WriteStringValue(argument);
            json.WriteEndArray();
            json.WriteString("working_directory", WorkingDirectory);
            json.WriteStartObject("environment");
            foreach (var (name, value) in Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                json.WriteString(name, value);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The start with both output streams redirected, read as UTF-8 (launcher plan §3.6).</summary>
    public ProcessStartInfo StartInfo()
    {
        var start = new ProcessStartInfo(Program)
        {
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
        };
        foreach (string argument in Arguments) start.ArgumentList.Add(argument);
        foreach (var (name, value) in Environment) start.Environment[name] = value;
        return start;
    }
}

/// <summary>The Shortcut dialog's two forms (spec §8.4): through the launcher, which runs the checks and shows a failed
/// start, and the engine directly, which is what <c>--print-command</c> prints.</summary>
public sealed record ShortcutForms(EngineCommand ThroughLauncher, EngineCommand Direct);

/// <summary>The one builder of the engine command (spec §8.5).</summary>
public static class EngineCommands
{
    /// <summary>The program and its own arguments; then Godot's flags (<c>--rendering-driver d3d12</c> when the profile
    /// asks for Direct3D 12 on Windows); then <c>--</c>, <c>--data-root</c> with the root, and the run's own arguments.
    /// Every path is absolute. The working folder is the program's; on Linux <c>PWD</c> is set to it, because the
    /// engine prefers <c>PWD</c> to its real working folder there.</summary>
    /// <param name="runArguments">Engine flags for this run (<c>OME-Launcher --launch ID -- ARGS</c>). A
    /// <c>--data-root</c> among them is refused: the profile names the root, and its checks are the ones that ran.</param>
    public static EngineCommand For(EngineProgram engine, ProfileEntry profile, IReadOnlyList<string> runArguments,
                                    OmePlatform? platform = null)
    {
        OmePlatform system = platform ?? LauncherEnvironment.Current.Platform;
        ProfileDocument document = (profile.Loaded ?? throw new ArgumentException(
            $"{profile.Id} has no readable {ProfileFile.FileName}: {profile.Problem}")).Document;
        if (runArguments.Contains(RunResolver.DataRootOption))
            throw new ArgumentException($"{RunResolver.DataRootOption} is refused among a run's arguments: the profile "
                                        + "names its root");
        var arguments = new List<string>(engine.Arguments);
        if (system == OmePlatform.Windows && document.Launcher.GraphicsApi == "d3d12")
            arguments.AddRange(["--rendering-driver", "d3d12"]);
        arguments.Add("--");
        arguments.AddRange([RunResolver.DataRootOption, Path.GetFullPath(profile.Root)]);
        arguments.AddRange(runArguments);
        string working = Path.GetDirectoryName(engine.Program)!;
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (system == OmePlatform.Linux) environment["PWD"] = working;
        return new EngineCommand(engine.Program, arguments, working, environment);
    }

    /// <summary>The Shortcut dialog's forms. The first is <c>--launch ID</c>, with <c>--home</c> when the home was named
    /// by <c>--home</c>: the default and the portable home are found by the launcher itself, and a portable one moves
    /// with it.</summary>
    public static ShortcutForms Shortcut(string launcherProgram, OmeHome home, ProfileEntry profile, EngineProgram engine,
                                         OmePlatform? platform = null)
    {
        string launcher = Path.GetFullPath(launcherProgram);
        var arguments = new List<string> { "--launch", profile.Id };
        if (home.Kind == OmeHomeKind.Explicit) arguments.AddRange(["--home", Path.GetFullPath(home.Root)]);
        var through = new EngineCommand(launcher, arguments, Path.GetDirectoryName(launcher)!,
                                        new Dictionary<string, string>());
        return new ShortcutForms(through, For(engine, profile, [], platform));
    }
}

/// <summary>A launch's outcome. <see cref="Started"/> is false when a check refused it
/// (<see cref="Refusal"/>); a start failed when the engine exited with anything but 0, and then
/// <see cref="LogTail"/> is the log's end and <see cref="Checks"/> the checks run again.</summary>
public sealed record LaunchResult(bool Started, int? ExitCode, string? Refusal, string? LogPath,
                                  IReadOnlyList<string> LogTail, ProfileCheckResult Checks)
{
    public bool Failed => !Started || ExitCode != 0;
}

/// <summary>Starting a profile (spec §8.5).</summary>
public static class EngineLaunch
{
    public const string DiagnosticsFolder = "diagnostics";
    public const string LogFile = "engine.log";
    public const int TailLines = 40;

    /// <summary>Runs the checks, then starts <paramref name="command"/> with its standard output and
    /// error written to <c>diagnostics/engine.log</c> line by line, and returns when the engine exits. A refused start
    /// opens no log, so a running session's log is never cut off. <paramref name="started"/> is told of the process
    /// once it runs.</summary>
    public static LaunchResult Run(ProfileEntry profile, EngineCommand command, CheckOptions? options = null,
                                   Action<Process>? started = null)
    {
        ProfileCheckResult checks = ProfileChecks.Run(profile, options);
        if (!checks.CanPlay)
            return new LaunchResult(false, null, checks.Running
                ? "the profile is running"
                : "a check failed: " + string.Join("; ", checks.Checks.Where(check => check.Status == CheckStatus.Failed)
                                                         .Select(check => $"{check.Kind}: {check.Message}")),
                null, [], checks);

        string logPath = Path.Combine(profile.Root, DiagnosticsFolder, LogFile);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var tail = new Queue<string>();
        int exitCode;
        using (var log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true })
        using (var process = new Process { StartInfo = command.StartInfo() })
        {
            var gate = new object();
            void Line(string? line)
            {
                if (line == null) return;
                lock (gate)
                {
                    log.WriteLine(line);
                    tail.Enqueue(line);
                    if (tail.Count > TailLines) tail.Dequeue();
                }
            }
            process.OutputDataReceived += (_, received) => Line(received.Data);
            process.ErrorDataReceived += (_, received) => Line(received.Data);
            log.WriteLine($"[launcher] {command.Program} {string.Join(' ', command.Arguments)} (in {command.WorkingDirectory})");
            try
            {
                process.Start();
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException)
            {
                string reason = $"{command.Program} cannot be started: {error.Message}";
                log.WriteLine($"[launcher] {reason}");
                return new LaunchResult(false, null, reason, logPath, [reason], checks);
            }
            started?.Invoke(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();   // without a timeout it also waits for both streams to end
            exitCode = process.ExitCode;
            lock (gate) log.WriteLine($"[launcher] the engine exited with {exitCode}");
        }
        return exitCode == 0
            ? new LaunchResult(true, 0, null, logPath, [], checks)
            : new LaunchResult(true, exitCode, null, logPath, tail.ToList(), ProfileChecks.Run(profile, options));
    }
}
