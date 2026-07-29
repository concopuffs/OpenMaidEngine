using System.Globalization;
using System.Text;
using Age.Engine.Model;
using Age.Engine.Sys4;

namespace Age.Engine.Persistence;

/// <summary>
/// Preserving reader/writer for AGE's native SYS4REG.INI engine-settings file. The current projection
/// owns only the audio keys; all other sections, keys, comments, ordering, and newline style survive.
/// </summary>
public sealed class Sys4RegIniStore
{
    public const string FileName = "SYS4REG.INI";
    private const string SoundSection = "sound";
    private static readonly Encoding Cp932 = CreateCp932();
    private static readonly string[] RouteKeys = ["", "Music", "SE", "Voice", "Movie"];

    private readonly string _path;
    private readonly object _lock = new();
    private readonly int _defaultMusicRouteValue;
    private readonly int[] _routeValues = [0, 0, 1, 1, 1];
    private IniDocument? _document;

    public Sys4RegIniStore(string path, int defaultMusicRouteValue)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Settings path is required.", nameof(path));
        _path = Path.GetFullPath(path);
        _defaultMusicRouteValue = defaultMusicRouteValue;
        _routeValues[(int)AudioMixerCategory.Music] = defaultMusicRouteValue;
    }

    public string FilePath => _path;

    /// <summary>
    /// Create a store at an already-resolved location while retaining SYS4INI's native defaults.
    /// Path policy belongs to Sys4PersistencePaths so save and settings locations are selected together.
    /// </summary>
    public static Sys4RegIniStore ForPath(
        Sys4StartupSettings startupSettings,
        string path)
    {
        ArgumentNullException.ThrowIfNull(startupSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        int defaultMusic = int.TryParse(
            startupSettings.GetValueOrDefault("NOSETMUSIC"),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int noSetMusic)
            && noSetMusic != 0
            ? noSetMusic - 1
            : 0;
        return new Sys4RegIniStore(path, defaultMusic);
    }

    public AudioMixerSettings Load()
    {
        var settings = new AudioMixerSettings();
        lock (_lock)
        {
            _document = LoadDocument();
            int[] volumes = Enumerable.Repeat(
                AudioMixerSettings.UnconfiguredVolume, AudioMixerSettings.CategoryCount).ToArray();
            bool[] routes = [false, true, true, true, true];

            for (int category = 0; category < AudioMixerSettings.CategoryCount; category++)
            {
                if (_document.TryGetInt(SoundSection, $"Volume{category}", out int value)
                    && value is >= AudioMixerSettings.UnconfiguredVolume
                        and <= AudioMixerSettings.MaximumVolume)
                    volumes[category] = value;
            }

            _routeValues[(int)AudioMixerCategory.Music] = _document.TryGetInt(
                SoundSection, RouteKeys[(int)AudioMixerCategory.Music], out int music)
                ? music
                : _defaultMusicRouteValue;
            routes[(int)AudioMixerCategory.Music] =
                _routeValues[(int)AudioMixerCategory.Music] >= 0;
            for (int category = (int)AudioMixerCategory.SoundEffect;
                 category < AudioMixerSettings.CategoryCount;
                 category++)
            {
                _routeValues[category] = _document.TryGetInt(
                    SoundSection, RouteKeys[category], out int value)
                    ? value
                    : 1;
                routes[category] = _routeValues[category] != 0;
            }
            settings.Replace(new AudioMixerSettingsSnapshot(volumes, routes));
        }
        return settings;
    }

    public void Save(AudioMixerSettingsSnapshot snapshot)
    {
        AudioMixerSettings.ValidateSnapshot(snapshot);
        lock (_lock)
        {
            _document ??= LoadDocument();
            for (int category = (int)AudioMixerCategory.Music;
                 category < AudioMixerSettings.CategoryCount;
                 category++)
            {
                bool wasEnabled = category == (int)AudioMixerCategory.Music
                    ? _routeValues[category] >= 0
                    : _routeValues[category] != 0;
                bool enabled = snapshot.Routes[category];
                if (wasEnabled != enabled)
                {
                    _routeValues[category] = category == (int)AudioMixerCategory.Music
                        ? checked(_routeValues[category] + (enabled ? 3 : -3))
                        : enabled ? 1 : 0;
                }
                _document.SetInt(SoundSection, RouteKeys[category], _routeValues[category]);
            }
            for (int category = 0; category < AudioMixerSettings.CategoryCount; category++)
                _document.SetInt(SoundSection, $"Volume{category}", snapshot.Volumes[category]);

            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = _path + ".$tmp";
            File.WriteAllText(temporary, _document.Serialize(), Cp932);
            File.Move(temporary, _path, overwrite: true);
        }
    }

    private IniDocument LoadDocument()
        => File.Exists(_path)
            ? IniDocument.Parse(File.ReadAllText(_path, Cp932))
            : IniDocument.Empty();

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    private sealed class IniDocument
    {
        private readonly List<string> _lines;
        private readonly string _newline;
        private readonly bool _trailingNewline;

        private IniDocument(List<string> lines, string newline, bool trailingNewline)
        {
            _lines = lines;
            _newline = newline;
            _trailingNewline = trailingNewline;
        }

        public static IniDocument Empty() => new([], "\r\n", true);

        public static IniDocument Parse(string text)
        {
            if (text.Length == 0) return Empty();
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
                : text.Contains('\n') ? "\n"
                : text.Contains('\r') ? "\r"
                : "\r\n";
            bool trailing = text.EndsWith("\r\n", StringComparison.Ordinal)
                || text.EndsWith('\n') || text.EndsWith('\r');
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n').ToList();
            if (trailing && lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return new IniDocument(lines, newline, trailing);
        }

        public bool TryGetInt(string section, string key, out int value)
        {
            value = default;
            bool found = false;
            string? currentSection = null;
            foreach (string line in _lines)
            {
                if (TryParseSection(line, out string? parsedSection))
                {
                    currentSection = parsedSection;
                    continue;
                }
                if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase)
                    || !TryParseKey(line, out string? parsedKey, out string? rawValue)
                    || !string.Equals(parsedKey, key, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (int.TryParse(rawValue!.Trim(), NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out int parsed))
                {
                    value = parsed;
                    found = true;
                }
            }
            return found;
        }

        public void SetInt(string section, string key, int value)
        {
            string rendered = value.ToString(CultureInfo.InvariantCulture);
            int sectionStart = -1;
            int sectionEnd = _lines.Count;
            string? currentSection = null;
            bool replaced = false;

            for (int index = 0; index < _lines.Count; index++)
            {
                string line = _lines[index];
                if (TryParseSection(line, out string? parsedSection))
                {
                    if (sectionStart >= 0)
                    {
                        sectionEnd = index;
                        break;
                    }
                    currentSection = parsedSection;
                    if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                        sectionStart = index;
                    continue;
                }
                if (sectionStart < 0
                    || !string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase)
                    || !TryParseKey(line, out string? parsedKey, out _)
                    || !string.Equals(parsedKey, key, StringComparison.OrdinalIgnoreCase))
                    continue;
                int equals = line.IndexOf('=');
                _lines[index] = line[..(equals + 1)] + rendered;
                replaced = true;
            }

            if (replaced) return;
            if (sectionStart < 0)
            {
                if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add("");
                _lines.Add($"[{section}]");
                _lines.Add($"{key}={rendered}");
            }
            else
            {
                _lines.Insert(sectionEnd, $"{key}={rendered}");
            }
        }

        public string Serialize()
        {
            string text = string.Join(_newline, _lines);
            return _trailingNewline ? text + _newline : text;
        }

        private static bool TryParseSection(string line, out string? section)
        {
            string trimmed = line.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            {
                section = trimmed[1..^1].Trim();
                return true;
            }
            section = null;
            return false;
        }

        private static bool TryParseKey(string line, out string? key, out string? value)
        {
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] is ';' or '#')
            {
                key = null;
                value = null;
                return false;
            }
            int equals = line.IndexOf('=');
            if (equals < 0)
            {
                key = null;
                value = null;
                return false;
            }
            key = line[..equals].Trim();
            value = line[(equals + 1)..];
            return key.Length != 0;
        }
    }
}
