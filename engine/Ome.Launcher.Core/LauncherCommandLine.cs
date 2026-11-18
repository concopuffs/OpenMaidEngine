using System.Text;
using Ome.Mods.Launch;

namespace Ome.Launcher.Core;

public enum LauncherCommand { Window, Launch, Check, PrintCommand }

/// <summary>Why the window would open (spec §8.6): on request, at a failed check, at a failed start, or at the review of
/// a root the launcher did not create.</summary>
public enum WindowReason { None, Requested, CheckFailed, StartFailed, Review }

/// <summary>A command line the launcher refuses; it exits with <see cref="LauncherCommandLine.UsageError"/>.</summary>
public sealed class LauncherUsageException(string message) : Exception(message);

/// <summary>The launcher's command line (spec §8.6), parsed.</summary>
/// <param name="Target">The <c>ID|DIR</c> of <c>--launch</c>, <c>--check</c> or <c>--print-command</c>.</param>
/// <param name="RunArguments">What follows <c>--</c>: engine flags for that run.</param>
public sealed record LauncherArguments(LauncherCommand Command, string? Target, IReadOnlyList<string> RunArguments,
                                       string? Home, string? Output, string? Engine, IReadOnlyList<string> EngineArguments)
{
    public static LauncherArguments Parse(IReadOnlyList<string> arguments)
    {
        LauncherCommand command = LauncherCommand.Window;
        string? target = null, home = null, output = null, engine = null;
        var engineArguments = new List<string>();
        var run = new List<string>();
        bool separated = false;
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            string Value()
            {
                if (index + 1 >= arguments.Count) throw new LauncherUsageException($"{argument} needs a value");
                return arguments[++index];
            }
            void Once(ref string? slot, string value)
            {
                if (slot != null) throw new LauncherUsageException($"{argument} is given twice");
                slot = value;
            }
            void Command(LauncherCommand chosen)
            {
                if (command != LauncherCommand.Window)
                    throw new LauncherUsageException("give one of --launch, --check and --print-command");
                command = chosen;
                target = Value();
            }
            switch (argument)
            {
                case "--":
                    run.AddRange(arguments.Skip(index + 1));
                    separated = true;
                    index = arguments.Count;
                    break;
                case "--launch": Command(LauncherCommand.Launch); break;
                case "--check": Command(LauncherCommand.Check); break;
                case "--print-command": Command(LauncherCommand.PrintCommand); break;
                case "--home": Once(ref home, Value()); break;
                case "--output": Once(ref output, Value()); break;
                case "--engine": Once(ref engine, Value()); break;
                case "--engine-arg":
                    string value = Value();
                    if (value == "--")
                        throw new LauncherUsageException("an --engine-arg of -- is refused: the launcher puts the -- "
                                                         + "after Godot's flags itself");
                    engineArguments.Add(value);
                    break;
                default:
                    throw new LauncherUsageException($"unknown option '{argument}'");
            }
        }
        if (engineArguments.Count > 0 && engine == null)
            throw new LauncherUsageException("--engine-arg goes with --engine");
        if (separated && command is not (LauncherCommand.Launch or LauncherCommand.PrintCommand))
            throw new LauncherUsageException("-- ARGS goes with --launch or --print-command");
        if (target is { Length: 0 }) throw new LauncherUsageException("a profile id or folder is empty");
        return new LauncherArguments(command, target, run, home, output, engine, engineArguments);
    }
}

/// <summary>Where one launcher run is: its program (the shortcut's target and, by its folder, the engine beside it and a
/// portable home), its working folder, its environment, and where it prints.</summary>
public sealed record LauncherContext(string LauncherProgram, string WorkingDirectory, LauncherEnvironment Environment,
                                     TextWriter Out, TextWriter Error, CheckOptions? CheckOptions = null);

/// <summary>What the window opens with (launcher plan §6.1): OME home, the engine its Play starts (the one beside the
/// launcher, or <c>--engine</c>'s), and for a launch that did not start or failed, the profile and what happened.</summary>
public sealed record WindowStart(OmeHome Home, EngineProgram Engine, ProfileEntry? Profile = null,
                                 LaunchResult? Launch = null);

/// <summary>A run's exit code, and where the window opens, if it does, with what it opens with.</summary>
public sealed record LauncherOutcome(int ExitCode, WindowReason Window, WindowStart? Start = null);

/// <summary>
/// The launcher's command line (spec §8.6), before any window: <c>--launch ID|DIR [-- ARGS]</c>, <c>--check</c>,
/// <c>--print-command</c> (JSON), <c>--home</c>, <c>--output</c>, <c>--engine</c> and <c>--engine-arg</c>. The program's
/// <c>Main</c> calls <see cref="Run"/> and does what its outcome's <see cref="LauncherOutcome.Window"/> says.
/// </summary>
public static class LauncherCommandLine
{
    public const int Success = 0, Failure = 1, UsageError = 2;

    public const string Usage =
        "usage: OME-Launcher [--launch ID|DIR [-- ARGS] | --check ID|DIR | --print-command ID|DIR [-- ARGS]] "
        + "[--home DIR] [--output FILE] [--engine PROGRAM [--engine-arg ARG]...]";

    public static LauncherOutcome Run(IReadOnlyList<string> arguments, LauncherContext context)
    {
        LauncherArguments parsed;
        try
        {
            parsed = LauncherArguments.Parse(arguments);
        }
        catch (LauncherUsageException error)
        {
            context.Error.WriteLine($"OME-Launcher: {error.Message}");
            context.Error.WriteLine(Usage);
            return new LauncherOutcome(UsageError, WindowReason.None);
        }

        StreamWriter? file = null;
        try
        {
            if (parsed.Output != null)
            {
                string path = Path.GetFullPath(parsed.Output, context.WorkingDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                file = new StreamWriter(path, append: false, new UTF8Encoding(false));
            }
            TextWriter output = file == null ? context.Out : new TeeWriter(context.Out, file);
            return Execute(parsed, context, output);
        }
        catch (Exception error) when (error is LauncherUsageException or ArgumentException)
        {
            context.Error.WriteLine($"OME-Launcher: {error.Message}");
            return new LauncherOutcome(UsageError, WindowReason.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            context.Error.WriteLine($"OME-Launcher: {error.Message}");
            return new LauncherOutcome(Failure, WindowReason.None);
        }
        finally
        {
            file?.Dispose();
        }
    }

    private static LauncherOutcome Execute(LauncherArguments parsed, LauncherContext context, TextWriter output)
    {
        string launcherDirectory = Path.GetDirectoryName(Path.GetFullPath(context.LauncherProgram))!;
        OmeHome home = OmeHome.Resolve(parsed.Home, launcherDirectory, context.WorkingDirectory, context.Environment);
        WindowStart Start(ProfileEntry? profile = null, LaunchResult? launch = null)
            => new(home, Engine(parsed, context, launcherDirectory), profile, launch);
        if (parsed.Command == LauncherCommand.Window)
            return new LauncherOutcome(Success, WindowReason.Requested, Start());

        LauncherSettingsFile settingsFile = LauncherSettingsStore.Load(home);
        foreach (string warning in settingsFile.Warnings) context.Error.WriteLine($"warning: {warning}");
        string machineFolder = context.CheckOptions?.MachineFolder ?? MachineState.DefaultFolder();
        MachineState Machine() => MachineState.Load(machineFolder);
        ProfileEntry profile = Locate(parsed.Target!, home, settingsFile.Settings, Machine, context.WorkingDirectory,
                                      context.Environment.Platform);

        if (parsed.Command == LauncherCommand.PrintCommand)
        {
            output.WriteLine(EngineCommands.For(Engine(parsed, context, launcherDirectory), Usable(profile),
                                                parsed.RunArguments, context.Environment.Platform).ToJson());
            return new LauncherOutcome(Success, WindowReason.None);
        }
        if (profile.NeedsReview)
        {
            output.WriteLine($"{profile.Root} is a profile root the launcher did not create: nothing opens its paths "
                             + "before the player accepts its review in the launcher's window");
            return new LauncherOutcome(Failure, WindowReason.Review, Start(profile));
        }
        Usable(profile);

        if (parsed.Command == LauncherCommand.Check)
        {
            ProfileCheckResult checks = ProfileChecks.Run(profile, context.CheckOptions);
            Print(checks, output);
            return new LauncherOutcome(checks.CanPlay ? Success : Failure, WindowReason.None);
        }

        EngineCommand command = EngineCommands.For(Engine(parsed, context, launcherDirectory), profile,
                                                   parsed.RunArguments, context.Environment.Platform);
        LaunchResult launched = EngineLaunch.Run(profile, command, context.CheckOptions);
        if (!launched.Started)
        {
            output.WriteLine($"{profile.Id} is not started: {launched.Refusal}");
            if (launched.LogPath == null) Print(launched.Checks, output);
            return new LauncherOutcome(Failure, WindowReason.CheckFailed, Start(profile, launched));
        }
        if (launched.Failed)
        {
            output.WriteLine($"the engine exited with {launched.ExitCode}; the end of {launched.LogPath}:");
            foreach (string line in launched.LogTail) output.WriteLine($"  {line}");
            output.WriteLine("the checks, run again:");
            Print(launched.Checks, output);
            return new LauncherOutcome(Failure, WindowReason.StartFailed, Start(profile, launched));
        }
        return new LauncherOutcome(Success, WindowReason.None);
    }

    /// <summary>A profile by its id, else by its folder; a folder no profile lists is a root the launcher did not
    /// create, which needs its review (spec §12).</summary>
    private static ProfileEntry Locate(string target, OmeHome home, LauncherSettings settings, Func<MachineState> machine,
                                       string workingDirectory, OmePlatform platform)
    {
        IReadOnlyList<ProfileEntry> profiles = ProfileStore.List(home, settings, machine);
        if (ProfileId.IsValid(target) && ProfileStore.Find(profiles, target) is { } byId) return byId;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target, workingDirectory));
        if (profiles.FirstOrDefault(entry => OmeHome.PathsEqual(entry.Root, root, platform))
            is { } byRoot)
            return byRoot;
        if (!RootSafety.IsNetworkPath(root) && !Directory.Exists(root))
            throw new LauncherUsageException($"no profile has the id '{target}', and no folder {root} exists");
        return ProfileStore.Open(Path.GetFileName(root), root, settings, machine);
    }

    private static ProfileEntry Usable(ProfileEntry profile)
        => profile.Usable
            ? profile
            : throw new IOException($"{profile.Id} ({profile.Root}) cannot be used: {profile.Problem}");

    private static EngineProgram Engine(LauncherArguments parsed, LauncherContext context, string launcherDirectory)
        => parsed.Engine == null
            ? EngineProgram.BesideLauncher(launcherDirectory, context.Environment.Platform)
            : EngineProgram.Development(parsed.Engine, parsed.EngineArguments, context.WorkingDirectory);

    /// <summary>One line per check, and the link report under a link check that did not pass.</summary>
    private static void Print(ProfileCheckResult checks, TextWriter output)
    {
        foreach (ProfileCheck check in checks.Checks)
        {
            string status = check.Status switch
            {
                CheckStatus.Passed => "passed",
                CheckStatus.Warning => "warning",
                CheckStatus.Failed => "FAILED",
                _ => "not run",
            };
            output.WriteLine($"{status,-8} {check.Kind}: {check.Message}");
            if (check.Kind == ProfileCheckKind.Link && check.Status is CheckStatus.Failed or CheckStatus.Warning
                && checks.Link is { } link)
                foreach (string line in link.Text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'))
                    output.WriteLine($"         {line}");
        }
    }

    /// <summary>Writes to the console and to <c>--output</c>'s file.</summary>
    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => first.Encoding;
        public override void Write(char value)
        {
            first.Write(value);
            second.Write(value);
        }
        public override void Write(string? value)
        {
            first.Write(value);
            second.Write(value);
        }
        public override void WriteLine(string? value)
        {
            first.WriteLine(value);
            second.WriteLine(value);
        }
        public override void Flush()
        {
            first.Flush();
            second.Flush();
        }
    }
}
