namespace Ome.Engine.Text;

/// <summary>One face of an installed family: a face of a file and, for a variable face, the named instance it stands
/// for.</summary>
public sealed record InstalledFace(OpenTypeFace Face, OpenTypeNamedInstance? Instance)
{
    /// <summary>The weight GDI matches a request against: the instance's <c>wght</c>, else <c>usWeightClass</c>.</summary>
    public int Weight => Instance != null && Instance.Coordinates.TryGetValue("wght", out float weight)
        ? (int)Math.Round(weight)
        : Face.WeightClass;

    public bool Italic => Instance != null && Instance.Coordinates.TryGetValue("ital", out float italic)
        ? italic >= 1
        : Face.IsItalic;
}

/// <summary>An installed family as GDI lists it: its name and its faces in the order they were found.</summary>
public sealed record InstalledFontFamily(string Name, IReadOnlyList<InstalledFace> Faces)
{
    /// <summary>The face GDI describes the family by: the upright face nearest weight 400.</summary>
    public InstalledFace Regular => Faces.OrderBy(face => face.Italic).ThenBy(face => Math.Abs(face.Weight - 400)).First();

    /// <summary>The family's <c>lfPitchAndFamily</c> in GDI's font list (<see cref="GdiFontRules.PitchAndFamily"/>).
    /// </summary>
    public byte PitchAndFamily => GdiFontRules.PitchAndFamily(Regular.Face);

    /// <summary>Whether GDI lists the family under Shift-JIS (<see cref="GdiFontRules.ListsUnderShiftJis"/>).</summary>
    public bool ListedUnderShiftJis => Faces.Any(face => GdiFontRules.ListsUnderShiftJis(face.Face));
}

/// <summary>
/// The fonts installed on a machine, grouped into families as GDI groups them (<see cref="GdiFontRules.Families"/>),
/// for the platforms where text is drawn without GDI: the installed-font list the scripts read
/// (<see cref="OpenTypeFontFamilySource"/>), and the face a rasterizer draws a family name with (<see cref="Resolve"/>).
/// A family is found by any of its names in any language; the list shows the name GDI shows on a Japanese-locale
/// Windows. Families keep the order their first face was found in.
/// </summary>
public sealed class InstalledFontIndex
{
    private readonly List<InstalledFontFamily> _families;
    private readonly Dictionary<string, InstalledFontFamily> _byName;

    private InstalledFontIndex(List<InstalledFontFamily> families, Dictionary<string, InstalledFontFamily> byName)
    {
        _families = families;
        _byName = byName;
    }

    /// <summary>An index of no fonts.</summary>
    public static InstalledFontIndex Empty { get; } = FromFaces([]);

    /// <summary>The families in the order their first face was found.</summary>
    public IReadOnlyList<InstalledFontFamily> Families => _families;

    /// <summary>Index the faces of <paramref name="files"/>, in order; a file that is not a font is skipped and
    /// reported to <paramref name="warn"/>. A file named twice is read once.</summary>
    public static InstalledFontIndex Scan(IEnumerable<string> files, Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        var seen = new HashSet<string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var faces = new List<OpenTypeFace>();
        foreach (string file in files)
        {
            string full;
            try
            {
                full = Path.GetFullPath(file);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                warn?.Invoke($"{file}: {error.Message}");
                continue;
            }
            if (!seen.Add(full)) continue;
            try
            {
                faces.AddRange(OpenTypeFace.ReadFile(full));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
                                              or ArgumentException or OverflowException)
            {
                warn?.Invoke($"{full}: {error.Message}");
            }
        }
        return FromFaces(faces);
    }

    /// <summary>Index faces already read, in order.</summary>
    public static InstalledFontIndex FromFaces(IEnumerable<OpenTypeFace> faces)
    {
        ArgumentNullException.ThrowIfNull(faces);
        var order = new List<string>();
        var members = new Dictionary<string, List<InstalledFace>>(StringComparer.OrdinalIgnoreCase);
        var aliases = new List<(string Alias, string Family)>();
        foreach (OpenTypeFace face in faces)
        {
            IReadOnlyList<(string Family, OpenTypeNamedInstance? Instance)> listed = GdiFontRules.Families(face);
            foreach ((string family, OpenTypeNamedInstance? instance) in listed)
            {
                if (!members.TryGetValue(family, out List<InstalledFace>? list))
                {
                    members.Add(family, list = []);
                    order.Add(family);
                }
                list.Add(new InstalledFace(face, instance));
            }
            // Every name the face's families are found by, in any language, points at the family it is listed under.
            foreach ((string alias, OpenTypeNamedInstance? instance) in GdiFontRules.FamilyNames(face))
                foreach ((string family, OpenTypeNamedInstance? listedInstance) in listed)
                    if (ReferenceEquals(listedInstance, instance))
                        aliases.Add((alias, family));
        }
        var families = order.Select(name => new InstalledFontFamily(name, members[name])).ToList();
        var byName = new Dictionary<string, InstalledFontFamily>(StringComparer.OrdinalIgnoreCase);
        foreach (InstalledFontFamily family in families) byName.TryAdd(family.Name, family);
        var listedFamilies = new Dictionary<string, InstalledFontFamily>(byName, StringComparer.OrdinalIgnoreCase);
        foreach ((string alias, string family) in aliases)
            if (listedFamilies.TryGetValue(family, out InstalledFontFamily? target))
                byName.TryAdd(alias, target);
        return new InstalledFontIndex(families, byName);
    }

    /// <summary>The family <paramref name="name"/> names, by any of its names; a leading '@' (a vertical face) is
    /// ignored. Null when no installed family has the name.</summary>
    public InstalledFontFamily? Family(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string bare = name.StartsWith('@') ? name[1..] : name;
        return _byName.TryGetValue(bare, out InstalledFontFamily? family) ? family : null;
    }

    /// <summary>The face GDI draws family <paramref name="name"/> with at <paramref name="weight"/>: of the faces of
    /// the asked slant (upright when the family has none), the one whose weight is nearest. Null when the family is
    /// not installed.</summary>
    public InstalledFace? Resolve(string name, int weight, bool italic = false)
    {
        if (Family(name) is not { } family) return null;
        IEnumerable<InstalledFace> slanted = family.Faces.Where(face => face.Italic == italic);
        if (!slanted.Any()) slanted = family.Faces;
        return slanted.OrderBy(face => Math.Abs(face.Weight - weight)).First();
    }
}

/// <summary>
/// SYS4433's installed-font queries (<see cref="IFontFamilySource"/>) answered from font files
/// (<see cref="InstalledFontIndex"/>), where GDI does not answer them: the families GDI lists under Shift-JIS, with its
/// names and pitch, and its VALIDFONTCHECKER probe (<see cref="GdiFontRules.HasCheckerGlyph"/>). The FontLink names
/// AGE removes are read from the registry on Windows, as AGE reads them; elsewhere there are none. The families keep
/// the order their files were found in, which is not GDI's (the order fonts were installed in).
/// </summary>
public sealed class OpenTypeFontFamilySource(InstalledFontIndex index) : IFontFamilySource
{
    private const string SystemLinkKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontLink\SystemLink";

    private readonly InstalledFontIndex _index = index ?? throw new ArgumentNullException(nameof(index));

    public IReadOnlyList<EnumeratedFontFamily> EnumerateShiftJisFamilies()
        => _index.Families.Where(family => family.ListedUnderShiftJis)
                          .Select(family => new EnumeratedFontFamily(family.Name, family.PitchAndFamily))
                          .ToArray();

    public IReadOnlyList<string> SystemLinkFaceNames()
    {
        if (!OperatingSystem.IsWindows()) return [];
        // The 32-bit view, as the 32-bit AGE reads it.
        using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                  Microsoft.Win32.RegistryView.Registry32);
        using Microsoft.Win32.RegistryKey? key = root.OpenSubKey(SystemLinkKey);
        return key?.GetValueNames() ?? [];
    }

    public bool HasGlyph(string faceName, ushort code)
        => _index.Family(faceName) is not { } family || GdiFontRules.HasCheckerGlyph(family.Regular.Face, code);
}

/// <summary>The directories a platform keeps installed fonts in, and the font files in them.</summary>
public static class SystemFontFiles
{
    private static readonly string[] Extensions = [".ttf", ".ttc", ".otf", ".otc"];

    /// <summary>
    /// The font directories of the running platform, system first: Windows' Fonts folder and the per-user one;
    /// on macOS the system, local and user Library/Fonts; elsewhere the XDG data directories' <c>fonts</c>
    /// (<c>/usr/share/fonts</c>, <c>/usr/local/share/fonts</c>, <c>~/.local/share/fonts</c>) and <c>~/.fonts</c>.
    /// </summary>
    public static IReadOnlyList<string> Directories()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return [Path.Combine(windows, "Fonts"), Path.Combine(local, "Microsoft", "Windows", "Fonts")];
        }
        if (OperatingSystem.IsMacOS())
            return ["/System/Library/Fonts", "/Library/Fonts", Path.Combine(home, "Library", "Fonts")];
        var directories = new List<string>();
        string dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs
            ? dirs : "/usr/local/share:/usr/share";
        // The system directories in XDG order, /usr/share before /usr/local/share as fontconfig lists them.
        foreach (string dir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries).Reverse())
            directories.Add(Path.Combine(dir, "fonts"));
        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dataHomeDir
            ? dataHomeDir : Path.Combine(home, ".local", "share");
        directories.Add(Path.Combine(dataHome, "fonts"));
        directories.Add(Path.Combine(home, ".fonts"));
        return directories.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>The TrueType and OpenType files under <paramref name="directories"/> (searched recursively), each
    /// directory's files in ordinal name order; a directory that is missing or unreadable is skipped.</summary>
    public static IEnumerable<string> Enumerate(IEnumerable<string> directories)
    {
        ArgumentNullException.ThrowIfNull(directories);
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory)) continue;
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
                if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return file;
        }
    }
}
