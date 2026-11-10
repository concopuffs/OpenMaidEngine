using System.Globalization;
using System.Text;

namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// The options the port adds (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §5.6). They live in
/// <c>OME.INI</c>, one file the port shares across games, so <c>SYS4REG.INI</c> keeps only native keys and native AGE
/// can read the port's file. A command-line flag wins for its run and never reaches the file
/// (<see cref="PortOptionsRun"/>).
/// </summary>
/// <param name="Language">ui:Language, the shell's string table (<see cref="ShellStrings.Languages"/>).</param>
/// <param name="WindowWidth">display:WindowWidth: the window's client width in pixels, or 0 for the game's canvas.</param>
/// <param name="WindowHeight">display:WindowHeight, likewise.</param>
/// <param name="FpsCounter">debug:FpsCounter: the frame-rate overlay.</param>
/// <param name="LocatorHud">debug:LocatorHud: the page locator overlay at startup (F2 still toggles it).</param>
public sealed record PortOptions(string Language, int WindowWidth, int WindowHeight, bool FpsCounter, bool LocatorHud)
{
    public const string FileName = "OME.INI";

    public static PortOptions Defaults { get; } = new(ShellStrings.FallbackLanguage, 0, 0, false, false);

    /// <summary>Whether the window size follows the game's canvas.</summary>
    public bool CanvasWindowSize => WindowWidth == 0 || WindowHeight == 0;

    /// <summary>The values a file holds, each checked against its domain: an unknown language, a size out of range
    /// or a half-given size reads as its default.</summary>
    public static PortOptions Parse(string text)
    {
        var ini = OmeIniDocument.Parse(text);
        string language = (ini.Get("ui", "Language") ?? "").Trim().ToLowerInvariant();
        if (!ShellStrings.Languages.Contains(language)) language = Defaults.Language;
        int width = Dimension(ini.Get("display", "WindowWidth"));
        int height = Dimension(ini.Get("display", "WindowHeight"));
        if (width == 0 || height == 0) width = height = 0;
        return new PortOptions(language, width, height, Switch(ini.Get("debug", "FpsCounter")),
                               Switch(ini.Get("debug", "LocatorHud")));
    }

    private static int Dimension(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pixels)
        && pixels is > 0 and <= WindowLaunchOptions.MaximumDimension
            ? pixels
            : 0;

    private static bool Switch(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number != 0;

    /// <summary>The file for these values. Keys the port does not know keep their place after its own, so a newer
    /// port's options survive an older one's write.</summary>
    public string Serialize(string? previous = null)
    {
        var ini = OmeIniDocument.Parse(previous ?? "");
        ini.Set("ui", "Language", Language);
        ini.Set("display", "WindowWidth", WindowWidth.ToString(CultureInfo.InvariantCulture));
        ini.Set("display", "WindowHeight", WindowHeight.ToString(CultureInfo.InvariantCulture));
        ini.Set("debug", "FpsCounter", FpsCounter ? "1" : "0");
        ini.Set("debug", "LocatorHud", LocatorHud ? "1" : "0");
        return ini.Serialize();
    }
}

/// <summary>Which port options a command-line flag set for this run.</summary>
[Flags]
public enum PortOptionFlags
{
    None = 0,
    Language = 1,
    WindowSize = 2,
    FpsCounter = 4,
    LocatorHud = 8,
}

/// <summary>
/// One run's port options: the file's values, the flags that override them for this run, and the values the run uses.
/// The panel shows <see cref="Effective"/>; its 決定 writes only the options the player changed, so a flag never
/// reaches the file.
/// </summary>
public sealed record PortOptionsRun(PortOptions Saved, PortOptions Effective, PortOptionFlags Overridden)
{
    /// <summary>Apply this run's flags: <c>--language ja|en</c>, <c>--window-width</c>/<c>--window-height</c> (checked
    /// by <see cref="WindowLaunchOptions"/>), <c>--fps-counter</c> and <c>--locator-hud</c>.</summary>
    /// <exception cref="ArgumentException">A flag has a missing or unknown value.</exception>
    public static PortOptionsRun Resolve(PortOptions saved, IReadOnlyList<string> arguments)
    {
        PortOptions effective = saved;
        PortOptionFlags overridden = PortOptionFlags.None;
        for (int i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--language":
                    string language = i + 1 < arguments.Count ? arguments[++i].Trim().ToLowerInvariant() : "";
                    if (!ShellStrings.Languages.Contains(language))
                        throw new ArgumentException(
                            $"--language must be one of {string.Join(", ", ShellStrings.Languages)}; got '{language}'");
                    effective = effective with { Language = language };
                    overridden |= PortOptionFlags.Language;
                    break;
                case "--window-width" or "--window-height":
                    overridden |= PortOptionFlags.WindowSize;
                    break;
                case "--fps-counter":
                    effective = effective with { FpsCounter = true };
                    overridden |= PortOptionFlags.FpsCounter;
                    break;
                case "--locator-hud":
                    effective = effective with { LocatorHud = true };
                    overridden |= PortOptionFlags.LocatorHud;
                    break;
            }
        }
        return new PortOptionsRun(saved, effective, overridden);
    }

    /// <summary>What the file holds after the panel commits <paramref name="edited"/>, opened on
    /// <paramref name="opened"/>: each option the player changed takes the edited value, and every other keeps the
    /// file's.</summary>
    public PortOptions SavedAfter(PortOptions opened, PortOptions edited) => new(
        edited.Language != opened.Language ? edited.Language : Saved.Language,
        edited.WindowWidth != opened.WindowWidth || edited.WindowHeight != opened.WindowHeight
            ? edited.WindowWidth : Saved.WindowWidth,
        edited.WindowWidth != opened.WindowWidth || edited.WindowHeight != opened.WindowHeight
            ? edited.WindowHeight : Saved.WindowHeight,
        edited.FpsCounter != opened.FpsCounter ? edited.FpsCounter : Saved.FpsCounter,
        edited.LocatorHud != opened.LocatorHud ? edited.LocatorHud : Saved.LocatorHud);
}

/// <summary>Reads and writes <c>OME.INI</c> (UTF-8). A missing or unreadable file reads as the defaults.</summary>
public sealed class OmeIniStore(string path)
{
    public string FilePath { get; } = Path.GetFullPath(path);

    /// <summary>The file beside a driver sandbox's saves, else the port's shared user folder.</summary>
    public static OmeIniStore For(string userDataRoot, string? profileRootOverride) =>
        new(Path.Combine(profileRootOverride ?? userDataRoot, PortOptions.FileName));

    public PortOptions Load()
    {
        try
        {
            return File.Exists(FilePath) ? PortOptions.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) : PortOptions.Defaults;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return PortOptions.Defaults;
        }
    }

    /// <exception cref="IOException">The file could not be written.</exception>
    public void Save(PortOptions options)
    {
        string? previous = File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = $"{FilePath}.tmp-{Guid.NewGuid():N}";   // unique, so two writers never share one
        File.WriteAllText(temporary, options.Serialize(previous), new UTF8Encoding(false));
        File.Move(temporary, FilePath, overwrite: true);
    }
}

/// <summary>A small INI document: <c>[section]</c> lines and <c>key=value</c> lines, sections and keys case-blind,
/// the last of a repeated key winning. Comments and blank lines are not kept.</summary>
internal sealed class OmeIniDocument
{
    private readonly List<(string Name, List<(string Key, string Value)> Keys)> _sections = [];

    public static OmeIniDocument Parse(string text)
    {
        var document = new OmeIniDocument();
        List<(string Key, string Value)>? current = null;
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                current = document.Section(line[1..^1].Trim());
                continue;
            }
            int equals = line.IndexOf('=');
            if (current == null || equals <= 0) continue;
            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();
            int at = current.FindIndex(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) current[at] = (current[at].Key, value);
            else current.Add((key, value));
        }
        return document;
    }

    private List<(string Key, string Value)> Section(string name)
    {
        int at = _sections.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) return _sections[at].Keys;
        _sections.Add((name, []));
        return _sections[^1].Keys;
    }

    public string? Get(string section, string key)
    {
        foreach ((string name, List<(string Key, string Value)> keys) in _sections)
            if (string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
                foreach ((string k, string value) in keys)
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return value;
        return null;
    }

    public void Set(string section, string key, string value)
    {
        List<(string Key, string Value)> keys = Section(section);
        int at = keys.FindIndex(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) keys[at] = (keys[at].Key, value);
        else keys.Add((key, value));
    }

    public string Serialize()
    {
        var text = new StringBuilder();
        foreach ((string name, List<(string Key, string Value)> keys) in _sections)
        {
            text.Append('[').Append(name).Append("]\n");
            foreach ((string key, string value) in keys) text.Append(key).Append('=').Append(value).Append('\n');
        }
        return text.ToString();
    }
}
