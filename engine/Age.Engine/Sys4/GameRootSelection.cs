namespace Age.Engine.Sys4;

public enum GameRootSource
{
    LaunchArgument,
    ExecutableDirectory,
    CurrentDirectory,
}

/// <summary>
/// Resolves the installed game's root independently of repository/development paths.
/// A valid root is anchored by the game's authoritative SYS4INI catalog.
/// </summary>
public readonly record struct GameRootSelection(string Root, GameRootSource Source)
{
    public const string OptionName = "--game-root";

    public string Sys4IniPath => Path.Combine(Root, "SYS4INI.BIN");

    public string SourceName => Source switch
    {
        GameRootSource.LaunchArgument => "launch-argument",
        GameRootSource.ExecutableDirectory => "executable-directory",
        GameRootSource.CurrentDirectory => "current-directory",
        _ => throw new InvalidOperationException($"unknown game-root source: {Source}"),
    };

    public static GameRootSelection Resolve(
        IReadOnlyList<string> arguments, string? executablePath, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        string cwd = Path.GetFullPath(currentDirectory);
        string? overridePath = null;
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] != OptionName) continue;
            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{OptionName} requires a directory path");
            overridePath = arguments[++index];
        }

        if (overridePath != null)
        {
            string root = Path.GetFullPath(overridePath, cwd);
            RequireCatalog(root, $"{OptionName} '{overridePath}'");
            return new GameRootSelection(root, GameRootSource.LaunchArgument);
        }

        var candidates = new List<(string Root, GameRootSource Source)>();
        string? executableDirectory = TryGetExecutableDirectory(executablePath, cwd);
        if (executableDirectory != null)
            candidates.Add((executableDirectory, GameRootSource.ExecutableDirectory));
        if (!candidates.Any(candidate => SamePath(candidate.Root, cwd)))
            candidates.Add((cwd, GameRootSource.CurrentDirectory));

        foreach ((string root, GameRootSource source) in candidates)
            if (File.Exists(Path.Combine(root, "SYS4INI.BIN")))
                return new GameRootSelection(root, source);

        string searched = string.Join(", ", candidates.Select(candidate => $"'{candidate.Root}'"));
        throw new ArgumentException(
            $"game root not found: SYS4INI.BIN was not present in {searched}; " +
            $"pass {OptionName} <directory>");
    }

    private static string? TryGetExecutableDirectory(string? executablePath, string cwd)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(executablePath, cwd));
        }
        catch (Exception error) when (
            error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static void RequireCatalog(string root, string description)
    {
        if (!File.Exists(Path.Combine(root, "SYS4INI.BIN")))
            throw new ArgumentException(
                $"{description} does not contain SYS4INI.BIN (resolved to '{root}')");
    }

    private static bool SamePath(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            comparison);
    }
}
