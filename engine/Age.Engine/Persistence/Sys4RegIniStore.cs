using System.Globalization;
using System.Text;
using Age.Engine.Model;
using Age.Engine.Sys4;

namespace Age.Engine.Persistence;

/// <summary>
/// Reader/writer for AGE's native SYS4REG.INI engine-settings file. The current projection owns the audio
/// keys and the [message] MessageSpeed/MesWinAlpha keys; every other value survives. Like AGE, loading
/// binds every key engine_settings_load_sys4reg_ini@0x46b6e0 binds (defaults for missing ones), and every
/// write emits them in AGE's canonical order (engine_settings_write_sys4reg_ini@0x46e8b0); keys AGE does not
/// bind are kept after them.
/// </summary>
public sealed class Sys4RegIniStore
{
    public const string FileName = "SYS4REG.INI";
    private const string SoundSection = "sound";
    private const string MessageSection = "message";
    public const string MessageSpeedKey = "MessageSpeed";   // op 0x1b5 per-glyph delay, milliseconds
    public const string MesWinAlphaKey = "MesWinAlpha";     // ops 0x131/0x141 message-window transparency
    // engine_settings_register_defaults@0x46be30 registers message:MessageSpeed=5 and message:MesWinAlpha=8;
    // Himegari's SYS4INI trailer overrides neither. (CONFIG's reset button instead sets 50 ms and 4.)
    public const int DefaultMessageSpeed = 5;
    public const int DefaultMesWinAlpha = 8;
    // message:AdvanceMesOnWheel (registered default 0): 1 makes a wheel-down advance an ADV text wait and finish a text
    // reveal (adv_input_service_poll@0x411230, the reveal service at 0x406fa0). Neither game's scripts change it.
    public const string AdvanceMesOnWheelKey = "AdvanceMesOnWheel";
    private static readonly Encoding Cp932 = CreateCp932();
    private static readonly string[] RouteKeys = ["", "Music", "SE", "Voice", "Movie"];

    // engine_settings_load_sys4reg_ini@0x46b6e0 binds these keys, in this order, to the engine settings;
    // the values are engine_settings_register_defaults@0x46be30's. A SYS4INI trailer pair named like the key
    // overrides the default (Himegari: FULLSCREENBIT=32, FONT), sound:Music follows NOSETMUSIC (see ForPath),
    // and message:UseAntiFont is bound only when ENABLEANTIFONT is set.
    private static readonly (string Section, string Key, string Default)[] NativeBindings =
    [
        ("display", "UseDirectDrawEmuration", "0"), ("display", "UseWideSurface", "0"),
        ("display", "ScreenMode", "0"), ("display", "FullScreenBit", "16"), ("display", "UseIVideoWindow", "1"),
        ("display", "DeviceType", "0"), ("display", "VertexProcessing", "0"), ("display", "PresentInterval", "0"),
        ("display", "TextureBit", "32"), ("display", "PixelShader", "-1"), ("display", "ForceScreen", "0"),
        ("display", "VirtualFullScreen", "0"), ("display", "AspectMode", "0"), ("display", "FullScreenWidth", "0"),
        ("display", "FullScreenHeight", "0"), ("display", "LimitAero", "0"),
        ("sound", "Sound", "1"), ("sound", "Music", "0"), ("sound", "SE", "1"), ("sound", "Voice", "1"),
        ("sound", "Movie", "1"), ("sound", "UseDirectSound", "1"), ("sound", "UseDirectSoundEmuration", "1"),
        ("sound", "DirectSoundCooperativeLevel", "1"), ("sound", "Volume0", "-1"), ("sound", "Volume1", "-1"),
        ("sound", "Volume2", "-1"), ("sound", "Volume3", "-1"), ("sound", "Volume4", "-1"),
        ("sound", "MusicFadeOnVoicePlaying", "1"), ("sound", "MusicFadeOnVoicePlayingVolume", "50"),
        ("message", "Font", ""), ("message", "RMouseEvent", "0"), ("message", "MessageSpeed", "5"),
        ("message", "MesWinAlpha", "8"), ("message", "UseAntiFont", "1"), ("message", "AutoMessageTime0", "500"),
        ("message", "AutoMessageTime1", "2000"), ("message", "ReadTextSkip", "0"),
        ("message", "AdvanceMesOnWheel", "0"),
        ("system", "SaveBMPPath", ""), ("system", "UseMMX", "1"), ("system", "EffectSkipOnClick", "0"),
        ("system", "OutErrorLog", "0"),
    ];
    private readonly Sys4StartupSettings _startupSettings;

    private readonly string _path;
    private readonly object _lock = new();
    private readonly int _defaultMusicRouteValue;
    private readonly int[] _routeValues = [0, 0, 1, 1, 1];
    private IniDocument? _document;

    public Sys4RegIniStore(string path, int defaultMusicRouteValue, Sys4StartupSettings? startupSettings = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Settings path is required.", nameof(path));
        _path = Path.GetFullPath(path);
        _startupSettings = startupSettings ?? Sys4StartupSettings.Empty;
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
        return new Sys4RegIniStore(path, defaultMusic, startupSettings);
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
            WriteLocked();
        }
    }

    /// <summary>A [message] integer, or the native registered default when the key is absent.</summary>
    public int LoadMessageInt(string key, int defaultValue) => LoadInt(MessageSection, key, defaultValue);

    /// <summary>An integer of any section, or <paramref name="defaultValue"/> when the key is absent.</summary>
    public int LoadInt(string section, string key, int defaultValue)
    {
        lock (_lock)
        {
            _document ??= LoadDocument();
            return _document.TryGetInt(section, key, out int value) ? value : defaultValue;
        }
    }

    public void SaveMessageInt(string key, int value)
    {
        lock (_lock)
        {
            _document ??= LoadDocument();
            _document.SetInt(MessageSection, key, value);
            WriteLocked();
        }
    }

    private void WriteLocked()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = _path + ".$tmp";
        File.WriteAllText(temporary, _document!.SerializeCanonical(BoundKeys()), Cp932);
        File.Move(temporary, _path, overwrite: true);
    }

    private bool AntiFontBound
        => int.TryParse(_startupSettings.GetValueOrDefault("ENABLEANTIFONT")?.Trim(),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int enableAntiFont) && enableAntiFont != 0;

    private List<(string Section, string Key)> BoundKeys()
    {
        bool antiFont = AntiFontBound;
        return NativeBindings.Where(b => b.Key != "UseAntiFont" || antiFont)
            .Select(b => (b.Section, b.Key)).ToList();
    }

    private void AddMissingNativeKeys()
    {
        bool antiFont = AntiFontBound;
        foreach ((string section, string key, string fallback) in NativeBindings)
        {
            if (key == "UseAntiFont" && !antiFont) continue;
            string value = key == "Music"
                ? _defaultMusicRouteValue.ToString(CultureInfo.InvariantCulture)
                : _startupSettings.GetValueOrDefault(key.ToUpperInvariant()) ?? fallback;
            _document!.AddIfMissing(section, key, value);
        }
    }

    // The loaded document already holds every bound key (AGE binds them all before reading the file), so
    // the next write leaves AGE's complete option set.
    private IniDocument LoadDocument()
    {
        _document = File.Exists(_path)
            ? IniDocument.Parse(File.ReadAllText(_path, Cp932))
            : IniDocument.Empty();
        AddMissingNativeKeys();
        return _document;
    }

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    private sealed class IniDocument
    {
        private readonly List<string> _lines;

        private IniDocument(List<string> lines) => _lines = lines;

        public static IniDocument Empty() => new([]);

        public static IniDocument Parse(string text)
            => new(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList());

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

        public void AddIfMissing(string section, string key, string value)
        {
            if (!Contains(section, key)) SetString(section, key, value);
        }

        private bool Contains(string section, string key)
        {
            string? currentSection = null;
            foreach (string line in _lines)
            {
                if (TryParseSection(line, out string? parsedSection)) currentSection = parsedSection;
                else if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase)
                         && TryParseKey(line, out string? parsedKey, out _)
                         && string.Equals(parsedKey, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void SetInt(string section, string key, int value)
            => SetString(section, key, value.ToString(CultureInfo.InvariantCulture));

        private void SetString(string section, string key, string rendered)
        {
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
            // Like AGE's own file: new sections follow directly, and new keys go after the section's
            // last key rather than after trailing blank lines.
            if (sectionStart < 0)
            {
                _lines.Add($"[{section}]");
                _lines.Add($"{key}={rendered}");
            }
            else
            {
                while (sectionEnd - 1 > sectionStart && _lines[sectionEnd - 1].Trim().Length == 0) sectionEnd--;
                _lines.Insert(sectionEnd, $"{key}={rendered}");
            }
        }

        /// <summary>
        /// engine_settings_write_sys4reg_ini@0x46e8b0 rewrites the file from its bindings: a section line
        /// whenever the section changes, then key=value lines in binding order, CRLF. The port writes the
        /// same, then keeps keys AGE does not bind after a section's bound keys and unbound sections last
        /// (AGE drops them; it never reads them either way). Comments and blank lines are not kept.
        /// </summary>
        public string SerializeCanonical(IReadOnlyList<(string Section, string Key)> bound)
        {
            var sections = new List<(string Name, List<(string Key, string Line)> Keys)>();
            List<(string Key, string Line)>? current = null;
            foreach (string line in _lines)
            {
                if (TryParseSection(line, out string? name))
                {
                    int index = sections.FindIndex(
                        s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                    {
                        sections.Add((name!, new List<(string Key, string Line)>()));
                        index = sections.Count - 1;
                    }
                    current = sections[index].Keys;
                }
                else if (current != null && TryParseKey(line, out string? key, out _))
                {
                    current.Add((key!, line));
                }
            }

            var output = new List<string>();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string Id(string section, string key) => section + "\u0001" + key;
            List<(string Key, string Line)> KeysOf(string section)
                => sections.Find(s => string.Equals(s.Name, section, StringComparison.OrdinalIgnoreCase)).Keys
                   ?? new List<(string Key, string Line)>();
            void WriteUnbound(string section)
            {
                foreach ((string key, string line) in KeysOf(section))
                    if (written.Add(Id(section, key))) output.Add(line);
            }

            string? open = null;
            foreach ((string section, string key) in bound)
            {
                if (!string.Equals(open, section, StringComparison.OrdinalIgnoreCase))
                {
                    if (open != null) WriteUnbound(open);
                    output.Add($"[{section}]");
                    open = section;
                }
                // The last occurrence wins, as TryGetInt reads it.
                (string Key, string Line) match = KeysOf(section).LastOrDefault(
                    k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));
                if (match.Line != null && written.Add(Id(section, key))) output.Add(match.Line);
            }
            if (open != null) WriteUnbound(open);
            foreach ((string name, List<(string Key, string Line)> keys) in sections)
            {
                if (bound.Any(b => string.Equals(b.Section, name, StringComparison.OrdinalIgnoreCase))) continue;
                output.Add($"[{name}]");
                foreach ((string key, string line) in keys)
                    if (written.Add(Id(name, key))) output.Add(line);
            }
            return string.Join("\r\n", output) + "\r\n";
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
