using System.Globalization;
using System.Text;
using Ome.Engine.Frontend;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Localization;
using Ome.Mods;
using Ome.Mods.Localization;
using Ome.Mods.Manifest;
using Tomlyn;
using Tomlyn.Model;

namespace Ome.Launcher.Core;

/// <summary>The launcher's theme (spec §9).</summary>
public enum LauncherTheme
{
    System,
    Light,
    Dark,
}

/// <summary>What the launcher's window does while a game it started runs (spec §8.5). The launcher's process stays
/// alive either way.</summary>
public enum WhileRunning
{
    Stay,
    Minimize,
    Close,
}

/// <summary>Whether the launcher may download a game's art (spec §8.7).</summary>
public enum ArtConsent
{
    Ask,
    Always,
    Never,
}

/// <summary>Where the launcher's window was.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);

/// <summary>A profile root outside <c>profiles/</c>, with the id the launcher knows it by (spec §4.1).</summary>
public sealed record ExternalRoot(string Path, string Id);

/// <summary>
/// The launcher's own settings (launcher spec §8.8), kept in <c>launcher.toml</c> in OME home. It names no program
/// (spec §12). <see cref="NewProfile"/> is the <c>OME.INI</c> a new profile starts with (spec §4.4).
/// </summary>
public sealed record LauncherSettings
{
    /// <summary>The launcher's UI language, or null for the operating system's when the launcher has that table.</summary>
    public string? Language { get; init; }
    public LauncherTheme Theme { get; init; } = LauncherTheme.System;
    public WindowPlacement? Window { get; init; }
    public WhileRunning WhileRunning { get; init; } = WhileRunning.Stay;
    public ArtConsent Art { get; init; } = ArtConsent.Ask;
    public string? LastProfile { get; init; }
    /// <summary>The profile roots outside <c>profiles/</c>, full paths.</summary>
    public IReadOnlyList<ExternalRoot> Roots { get; init; } = [];
    /// <summary>The roots the player reviewed and accepted (spec §12), full paths.</summary>
    public IReadOnlyList<string> Reviewed { get; init; } = [];
    public PortOptions NewProfile { get; init; } = PortOptions.Defaults;

    public static LauncherSettings Defaults { get; } = new();
}

/// <summary>A loaded <c>launcher.toml</c>: the settings, what was wrong with the file, and whether the launcher may
/// write over it. A version this launcher does not know is never written over; an unreadable file is kept beside the
/// new one when the launcher writes.</summary>
public sealed record LauncherSettingsFile(LauncherSettings Settings, IReadOnlyList<string> Warnings, bool Writable,
                                          bool Unreadable = false);

/// <summary>Reads and writes <c>launcher.toml</c>. Each key has a type; a key of the wrong type or value, an unknown
/// key, and a root with a bad id are warnings, and the setting keeps its default. Writing goes through a temporary
/// file, so a failed write keeps the old one.</summary>
public static class LauncherSettingsStore
{
    public const int CurrentVersion = 1;
    public const int MaxBytes = 1 << 20;
    public const string UnreadableSuffix = ".unreadable";

    private static readonly string[] Themes = ["system", "light", "dark"];
    private static readonly string[] WhileRunningValues = ["stay", "minimize", "close"];
    private static readonly string[] ArtValues = ["ask", "always", "never"];

    public static LauncherSettingsFile Load(OmeHome home)
    {
        string path = home.SettingsPath;
        if (!File.Exists(path)) return new LauncherSettingsFile(LauncherSettings.Defaults, [], Writable: true);
        long length = new FileInfo(path).Length;
        if (length > MaxBytes)
            return Unreadable($"{path}: the file is {length} bytes; at most {MaxBytes} are read");
        string text;
        try
        {
            text = File.ReadAllText(path, new UTF8Encoding(false, throwOnInvalidBytes: true));
        }
        catch (DecoderFallbackException)
        {
            return Unreadable($"{path}: not UTF-8 text");
        }
        return Parse(text, home, path);
    }

    public static LauncherSettingsFile Parse(string text, OmeHome home, string origin = OmeHome.SettingsFile)
    {
        TomlTable table;
        try
        {
            table = TomlDocument.Parse(text) ?? new TomlTable();
        }
        catch (TomlException error)
        {
            return Unreadable($"{origin}: not valid TOML: {error.Message}");
        }

        var problems = new List<string>();
        var reader = new TableReader(table, "", problems);
        int? version = reader.Integer("ome_launcher");
        if (version == null && problems.Count == 0)
            problems.Add("ome_launcher: missing; read as format version 1");
        else if (version != null && version != CurrentVersion)
            return new LauncherSettingsFile(LauncherSettings.Defaults,
                [$"{origin}: format version {version}; this launcher reads version {CurrentVersion}, so its settings are "
                 + "not used and the file is not written over"], Writable: false);
        else if (version == null)
            return new LauncherSettingsFile(LauncherSettings.Defaults,
                [$"{origin}: ome_launcher is not a format version, so the settings are not used and the file is not "
                 + "written over"], Writable: false);

        var settings = new LauncherSettings
        {
            Language = Language(reader, "language"),
            Theme = OneOf(reader, "theme", Themes) switch
            {
                "light" => LauncherTheme.Light,
                "dark" => LauncherTheme.Dark,
                _ => LauncherTheme.System,
            },
            WhileRunning = OneOf(reader, "while_running", WhileRunningValues) switch
            {
                "minimize" => WhileRunning.Minimize,
                "close" => WhileRunning.Close,
                _ => WhileRunning.Stay,
            },
            Art = OneOf(reader, "art", ArtValues) switch
            {
                "always" => ArtConsent.Always,
                "never" => ArtConsent.Never,
                _ => ArtConsent.Ask,
            },
            LastProfile = Id(reader, "last_profile", ""),
            Reviewed = Paths(reader, "reviewed", home),
            Window = Window(reader.Table("window")),
            NewProfile = NewProfile(reader.Table("new_profile")),
            Roots = Roots(reader, home),
        };
        reader.RejectUnknown();
        return new LauncherSettingsFile(settings, problems.Select(problem => $"{origin}: {problem}").ToList(), Writable: true);
    }

    /// <summary>The file's text. Paths inside OME home are written relative to it (spec §4.3), so a portable home
    /// moves with its drive letter.</summary>
    public static string Serialize(LauncherSettings settings, OmeHome home)
    {
        var text = new StringBuilder();
        text.Append("# Open Maid Engine launcher settings (launcher spec §8.8). The launcher writes this file.\n");
        text.Append("ome_launcher = ").Append(CurrentVersion).Append('\n');
        if (settings.Language != null) Key(text, "language", settings.Language);
        Key(text, "theme", settings.Theme.ToString().ToLowerInvariant());
        Key(text, "while_running", settings.WhileRunning.ToString().ToLowerInvariant());
        Key(text, "art", settings.Art.ToString().ToLowerInvariant());
        if (settings.LastProfile != null) Key(text, "last_profile", settings.LastProfile);
        if (settings.Reviewed.Count > 0)
        {
            text.Append("reviewed = [\n");
            foreach (string root in settings.Reviewed)
                text.Append("  ").Append(TextCatalogFileWriter.Quote(HomePaths.ForFile(root, home.Root, home))).Append(",\n");
            text.Append("]\n");
        }
        if (settings.Window is { } window)
            text.Append("\n[window]\n")
                .Append("x = ").Append(Number(window.X)).Append('\n')
                .Append("y = ").Append(Number(window.Y)).Append('\n')
                .Append("width = ").Append(Number(window.Width)).Append('\n')
                .Append("height = ").Append(Number(window.Height)).Append('\n')
                .Append("maximized = ").Append(window.Maximized ? "true" : "false").Append('\n');
        PortOptions profile = settings.NewProfile;
        text.Append("\n[new_profile]                        # the OME.INI a new profile starts with\n");
        Key(text, "language", profile.Language);
        text.Append("window_width = ").Append(Number(profile.WindowWidth)).Append('\n')
            .Append("window_height = ").Append(Number(profile.WindowHeight)).Append('\n')
            .Append("fps_counter = ").Append(profile.FpsCounter ? "true" : "false").Append('\n')
            .Append("locator_hud = ").Append(profile.LocatorHud ? "true" : "false").Append('\n');
        foreach (ExternalRoot root in settings.Roots)
        {
            text.Append("\n[[roots]]\n");
            Key(text, "path", HomePaths.ForFile(root.Path, home.Root, home));
            Key(text, "id", root.Id);
        }
        return text.ToString();
    }

    /// <summary>Writes <paramref name="settings"/>. Refused when <paramref name="loaded"/> came from a version this
    /// launcher does not know; an unreadable file is first kept as <c>launcher.toml.unreadable</c>.</summary>
    public static void Save(OmeHome home, LauncherSettingsFile loaded, LauncherSettings settings)
    {
        if (!loaded.Writable)
            throw new InvalidOperationException($"{home.SettingsPath} is from another launcher version and is not written over");
        Directory.CreateDirectory(home.Root);
        string path = home.SettingsPath;
        if (loaded.Unreadable && File.Exists(path)) File.Copy(path, path + UnreadableSuffix, overwrite: true);
        string temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        File.WriteAllText(temporary, Serialize(settings, home), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static LauncherSettingsFile Unreadable(string problem)
        => new(LauncherSettings.Defaults, [problem + $"; the defaults are used, and the file is kept as "
                                           + $"{OmeHome.SettingsFile}{UnreadableSuffix} when the launcher next writes"],
               Writable: true, Unreadable: true);

    private static void Key(StringBuilder text, string key, string value)
        => text.Append(key).Append(" = ").Append(TextCatalogFileWriter.Quote(value)).Append('\n');

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? OneOf(TableReader table, string key, IReadOnlyList<string> values)
    {
        string? value = table.String(key);
        if (value == null || values.Contains(value)) return value;
        table.Problems.Add($"{table.Path}{key}: '{value}' is not one of {string.Join(", ", values.Select(v => $"\"{v}\""))}");
        return null;
    }

    /// <summary>A language tag by the engine's own rule (<see cref="LanguageTag"/>), normalized.</summary>
    private static string? Language(TableReader table, string key)
    {
        string? value = table.String(key);
        if (value == null) return null;
        string tag = LanguageTag.Normalize(value);
        if (LanguageTag.IsWellFormed(tag)) return tag;
        table.Problems.Add($"{table.Path}{key}: '{value}' is not a language tag");
        return null;
    }

    private static string? Id(TableReader table, string key, string context)
    {
        string? value = table.String(key);
        if (value == null || ProfileId.IsValid(value)) return value;
        table.Problems.Add($"{table.Path}{key}: '{value}' is not a profile id{context}");
        return null;
    }

    private static IReadOnlyList<string> Paths(TableReader table, string key, OmeHome home)
    {
        var paths = new List<string>();
        foreach (string written in table.Strings(key))
            if (Resolve(table, key, written, home) is { } full
                && !paths.Any(path => OmeHome.PathsEqual(path, full, LauncherEnvironment.Current.Platform)))
                paths.Add(full);
        return paths;
    }

    private static string? Resolve(TableReader table, string key, string written, OmeHome home)
    {
        try
        {
            if (written.Length == 0) throw new ArgumentException("an empty path");
            return HomePaths.Resolve(written, home.Root);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            table.Problems.Add($"{table.Path}{key}: '{written}' is not a usable path ({error.Message})");
            return null;
        }
    }

    private static WindowPlacement? Window(TableReader? table)
    {
        if (table == null) return null;
        int? x = table.Integer("x"), y = table.Integer("y"), width = table.Integer("width"), height = table.Integer("height");
        bool maximized = table.Boolean("maximized") ?? false;
        table.RejectUnknown();
        if (x == null || y == null || width is not > 0 || height is not > 0)
        {
            table.Problems.Add("window: needs x, y and a positive width and height; the window's place is not used");
            return null;
        }
        return new WindowPlacement(x.Value, y.Value, width.Value, height.Value, maximized);
    }

    private static PortOptions NewProfile(TableReader? table)
    {
        PortOptions defaults = PortOptions.Defaults;
        if (table == null) return defaults;
        // OME.INI's own domain (PortOptions.Parse): 0 is the canvas size, and a size needs both dimensions.
        int Size(string key)
        {
            int? value = table.Integer(key);
            if (value is null or 0 or (> 0 and <= WindowLaunchOptions.MaximumDimension)) return value ?? 0;
            table.Problems.Add($"{table.Path}{key}: {value} is not 0 (the canvas size) or 1..{WindowLaunchOptions.MaximumDimension}");
            return 0;
        }
        int width = Size("window_width"), height = Size("window_height");
        if ((width == 0) != (height == 0))
        {
            table.Problems.Add($"{table.Path}window_width, window_height: a window size needs both; the canvas size is used");
            width = height = 0;
        }
        var options = new PortOptions(
            Language(table, "language") ?? defaults.Language,
            width,
            height,
            table.Boolean("fps_counter") ?? defaults.FpsCounter,
            table.Boolean("locator_hud") ?? defaults.LocatorHud);
        table.RejectUnknown();
        return options;
    }

    private static IReadOnlyList<ExternalRoot> Roots(TableReader reader, OmeHome home)
    {
        var roots = new List<ExternalRoot>();
        foreach (TableReader entry in reader.TableArray("roots"))
        {
            string? written = entry.String("path", required: true);
            string? id = Id(entry, "id", "; the root is not listed");
            if (entry.Has("id") && id == null) { entry.RejectUnknown(); continue; }
            if (!entry.Has("id")) entry.Problems.Add($"{entry.Path}id: required; the root is not listed");
            entry.RejectUnknown();
            if (written == null || id == null || Resolve(entry, "path", written, home) is not { } path) continue;
            if (roots.Any(root => root.Id == id))
                entry.Problems.Add($"{entry.Path}id: '{id}' is listed twice; the later root is not listed");
            else if (roots.Any(root => OmeHome.PathsEqual(root.Path, path, LauncherEnvironment.Current.Platform)))
                entry.Problems.Add($"{entry.Path}path: {path} is listed twice; the later entry is not used");
            else roots.Add(new ExternalRoot(path, id));
        }
        return roots;
    }
}
