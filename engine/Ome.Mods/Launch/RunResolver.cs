using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Script.Patch;

namespace Ome.Mods.Launch;

/// <summary>A run the resolver refuses to start: a flag a profile file forbids, an unreadable profile file, or a
/// profile whose game the install does not match. Startup stops with the message (exit code 2).</summary>
public sealed class RunResolutionException : Exception
{
    public RunResolutionException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// The run the command line and the profile file describe together (launcher spec §5.2).
/// </summary>
/// <param name="Arguments">The normalized argument list every existing selection reads: the command line's arguments
/// in their order, with one occurrence of each single-valued option (the last given), followed by the profile file's
/// equivalents of the options the command line did not give. A flag given for one run therefore wins over the file,
/// and the host and <c>Ome.Cli</c> cannot disagree about a repeated option.</param>
/// <param name="ProfileFile">The data root's <c>profile.toml</c>, or null when there is none.</param>
/// <param name="DataRoot">The full path of <c>--data-root</c>, or null when it is not given.</param>
/// <param name="Warnings">What startup reports and continues past, such as a changed install fingerprint.</param>
public sealed record ResolvedRun(
    IReadOnlyList<string> Arguments,
    ProfileFile? ProfileFile,
    string? DataRoot,
    IReadOnlyList<string> Warnings)
{
    /// <summary>The <c>[mods]</c> table, or null: the data root's <c>mods/</c> folder works as before.</summary>
    public ProfileMods? Mods => ProfileFile?.Mods;

    /// <summary>The mode a profile file names, or null without one (everything is admitted).</summary>
    public ProfileMode? Mode => ProfileFile?.Mode;

    /// <summary>The <c>[profile-file]</c> startup line: the file and the values in effect, or null without one.</summary>
    public string? Describe()
    {
        if (ProfileFile is not { } file) return null;
        string Effective(string option) => RunResolver.LastValue(Arguments, option) ?? "default";
        var parts = new List<string>
        {
            $"file={file.Path}",
            $"game={file.Game}",
            $"mode={file.ModeName}",
            $"install={file.Install.Root}",
            "overlays=" + (file.Install.Overlays.Count == 0 ? "<none>" : string.Join(" -> ", file.Install.Overlays)),
            $"bmp-as-agf={(file.Install.AllowBmpAsAgf ? "enabled" : "disabled")}",
            "mods=" + (file.Mods is { } mods
                ? $"{mods.Enabled.Count()} enabled from {mods.Folders.Count} folder(s), on-failure={(mods.SkipFailed ? "skip" : "stop")}"
                : "the data root's mods/"),
            $"renderer={Effective("--render-backend")}",
            $"text-backend={Effective("--text-backend")}",
            $"lua-errors={Effective("--lua-errors")}",
        };
        return string.Join(" ", parts);
    }
}

/// <summary>
/// The one resolver of the command line and a data root's <c>profile.toml</c>, for the Godot host and <c>Ome.Cli</c>
/// (launcher spec §5.2). It maps the file's typed keys to the flags the existing selections read, so a profile file
/// holds no engine arguments and every option keeps one parser.
/// </summary>
public static class RunResolver
{
    public const string DataRootOption = "--data-root";
    public const string GameRootOption = GameRootSelection.OptionName;
    public const string RenderBackendOption = "--render-backend";
    public const string TextBackendOption = "--text-backend";
    public const string LuaErrorsOption = "--lua-errors";

    /// <summary>Options that take one value, where the last given wins. <c>--game</c> and <c>--profile</c> are one
    /// option and come out as <c>--game</c>.</summary>
    private static readonly string[] SingleValued =
    [
        GameProfileSelection.GameOptionName, GameRootOption, DataRootOption,
        RenderBackendOption, TextBackendOption, LuaErrorsOption,
    ];

    /// <summary>What a command-line flag cannot change once a profile file names the game and the install.</summary>
    private static readonly string[] RefusedWithProfileFile =
    [
        GameProfileSelection.GameOptionName, GameProfileSelection.ProfileOptionName, GameRootOption,
        AssetLaunchOptions.OverlayRootOptionName, AssetLaunchOptions.AllowBmpAsAgfOptionName,
        GameProfileSelection.ProbeOptionName,
    ];

    public static ResolvedRun Resolve(IReadOnlyList<string> arguments, string workingDirectory,
                                      GameProfileRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        registry ??= GameProfileRegistry.BuiltIn;

        List<string> normalized = Normalize(arguments, Path.GetFullPath(workingDirectory));
        string? dataRoot = LastValue(normalized, DataRootOption);
        if (dataRoot == null)
            return new ResolvedRun(normalized, null, null, []);

        ProfileFile? file;
        try
        {
            file = ProfileFile.TryRead(dataRoot);
        }
        catch (ProfileFileException error)
        {
            throw new RunResolutionException(error.Message, error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new RunResolutionException($"{Path.Combine(dataRoot, ProfileFile.FileName)}: {error.Message}", error);
        }
        if (file == null)
            return new ResolvedRun(normalized, null, dataRoot, []);

        // Refused flags are checked on the command line alone, before the file's equivalents join the list.
        foreach (string argument in arguments)
            if (Array.IndexOf(RefusedWithProfileFile, argument) >= 0)
                throw new RunResolutionException(RefusalReason(argument, file.Path));

        var warnings = new List<string>();
        CheckInstall(file, registry, warnings);

        normalized.AddRange([GameProfileSelection.GameOptionName, file.Game, GameRootOption, file.Install.Root]);
        foreach (string overlay in file.Install.Overlays)
            normalized.AddRange([AssetLaunchOptions.OverlayRootOptionName, overlay]);
        if (file.Install.AllowBmpAsAgf) normalized.Add(AssetLaunchOptions.AllowBmpAsAgfOptionName);
        AddUnlessGiven(normalized, RenderBackendOption, file.Launch.Renderer);
        AddUnlessGiven(normalized, TextBackendOption, file.Launch.TextBackend);
        AddUnlessGiven(normalized, LuaErrorsOption, file.Launch.LuaErrors);
        if (file.Mods is { SkipFailed: true } && !normalized.Contains(ModLaunchOptions.SkipFailedOption))
            normalized.Add(ModLaunchOptions.SkipFailedOption);
        return new ResolvedRun(normalized, file, dataRoot, warnings);
    }

    /// <summary>The value of the last <paramref name="option"/>, or null when it is absent or has no value.</summary>
    public static string? LastValue(IReadOnlyList<string> arguments, string option)
    {
        for (int index = arguments.Count - 1; index >= 0; index--)
            if (arguments[index] == option)
                return HasValue(arguments, index) ? arguments[index + 1] : null;
        return null;
    }

    /// <summary>The command line with one occurrence of each single-valued option, the last given, in its place;
    /// <c>--profile</c> is spelled <c>--game</c> and <c>--data-root</c> is a full path. An option without a value
    /// is kept as given, so its own reader reports it.</summary>
    private static List<string> Normalize(IReadOnlyList<string> arguments, string workingDirectory)
    {
        static string Canonical(string argument)
            => GameProfileSelection.IsGameOption(argument) ? GameProfileSelection.GameOptionName : argument;

        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index++)
        {
            string option = Canonical(arguments[index]);
            if (Array.IndexOf(SingleValued, option) < 0) continue;
            last[option] = index;
            if (HasValue(arguments, index)) index++;
        }

        var normalized = new List<string>(arguments.Count);
        for (int index = 0; index < arguments.Count; index++)
        {
            string option = Canonical(arguments[index]);
            if (Array.IndexOf(SingleValued, option) < 0)
            {
                normalized.Add(arguments[index]);
                continue;
            }
            bool hasValue = HasValue(arguments, index);
            if (last[option] == index)
            {
                normalized.Add(option);
                if (hasValue)
                    normalized.Add(option == DataRootOption
                        ? Path.GetFullPath(arguments[index + 1], workingDirectory)
                        : arguments[index + 1]);
            }
            if (hasValue) index++;
        }
        return normalized;
    }

    private static bool HasValue(IReadOnlyList<string> arguments, int index)
        => index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal);

    private static void AddUnlessGiven(List<string> arguments, string option, string? value)
    {
        if (value != null && !arguments.Contains(option)) arguments.AddRange([option, value]);
    }

    private static string RefusalReason(string argument, string path) => argument switch
    {
        GameProfileSelection.ProbeOptionName =>
            $"{argument} is refused with a profile file ({path}): probing needs a game named on the command line; "
            + "probe a data root without profile.toml",
        _ => $"{argument} is refused with a profile file ({path}): the profile names the game and the install, "
             + "and its saves and selectors belong to that game and that install",
    };

    /// <summary>The strict identity check of a profile file (launcher spec §5.2): the game must exist and match the
    /// install's <c>SYS4INI.BIN</c> header. A recorded fingerprint that differs is a warning.</summary>
    private static void CheckInstall(ProfileFile file, GameProfileRegistry registry, List<string> warnings)
    {
        GameProfileManifest game = registry.Find(file.Game)
            ?? throw new RunResolutionException(
                $"{file.Path}: game: unknown game '{file.Game}'; available games: "
                + string.Join(", ", registry.Profiles.Select(value => value.Id)));
        string sys4Ini = Path.Combine(file.Install.Root, "SYS4INI.BIN");
        byte[] catalog;
        try
        {
            catalog = File.ReadAllBytes(sys4Ini);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new RunResolutionException(
                $"{file.Path}: install.root: {file.Install.Root} holds no readable SYS4INI.BIN ({error.Message})", error);
        }
        GameCatalogIdentity detected;
        try
        {
            detected = GameCatalogIdentity.ReadSys4IniHeader(sys4Ini);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        {
            throw new RunResolutionException($"{file.Path}: install.root: {sys4Ini}: {error.Message}", error);
        }
        if (!game.Matches(detected))
            throw new RunResolutionException(
                $"{file.Path}: the install at {file.Install.Root} is {detected.Display}, not the game '{file.Game}' "
                + $"({string.Join(" or ", game.CatalogIdentities.Select(value => value.Display))})");
        if (file.Install.Fingerprint is { } recorded && OmsPatchPlan.FingerprintOf(catalog) is var actual
            && actual != recorded)
            warnings.Add($"{file.Path}: install.fingerprint {recorded} differs from the install's SYS4INI.BIN "
                         + $"({actual}): its catalog changed since the profile accepted it");
    }
}
