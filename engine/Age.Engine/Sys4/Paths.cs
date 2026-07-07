namespace Age.Engine.Sys4;
public static class Paths
{
    public static string Repo { get; } = FindRepo();
    public static string Workspace => Directory.GetParent(Repo)!.FullName;
    public static string Extracted => Path.Combine(Workspace, "extracted");
    public static string Data1 => Path.Combine(Extracted, "DATA1");
    public static string GameDir => Path.Combine(Workspace, "姫狩りダンジョンマイスター");
    public static string Build => Path.Combine(Repo, "build");
    public static string OpcodesJson => Path.Combine(Build, "opcodes.json");
    public static string AssetSectionsJson => Path.Combine(Build, "asset-sections.json");
    public static string AssetIndexJson => Path.Combine(Build, "asset-index.json");
    public static string Textures => Path.Combine(Build, "textures");

    private static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && d.Name != "age-reimpl") d = d.Parent;
        if (d == null) throw new DirectoryNotFoundException(
            "age-reimpl root not found above " + AppContext.BaseDirectory);
        return d.FullName;
    }

    /// name(UPPER).BIN -> path; game-dir loose overrides shadow extracted/DATA1 (mirrors paths.scripts()).
    public static Dictionary<string, string> Scripts()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(Data1))
            foreach (var p in Directory.EnumerateFiles(Data1, "*.BIN"))
                d[Path.GetFileName(p).ToUpperInvariant()] = p;
        if (Directory.Exists(GameDir))
            foreach (var p in Directory.EnumerateFiles(GameDir, "*.BIN"))
                d[Path.GetFileName(p).ToUpperInvariant()] = p;
        return d;
    }
}
