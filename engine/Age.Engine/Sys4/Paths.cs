namespace Age.Engine.Sys4;
public static class Paths
{
    public static string Repo { get; } = FindRepo();
    public static string Workspace => Directory.GetParent(Repo)!.FullName;
    public static string Extracted => Path.Combine(Workspace, "extracted");
    public static string Data1 => Path.Combine(Extracted, "DATA1");
    public static string GameDir => Path.Combine(Workspace, "Himegari_Game");
    public static string Build => Path.Combine(Repo, "build");
    public static string OpcodesJson => Path.Combine(Build, "opcodes.json");
    public static string AssetSectionsJson => Path.Combine(Build, "asset-sections.json");
    public static string AssetIndexJson => Path.Combine(Build, "asset-index.json");
    public static string CallscriptNamesJson => Path.Combine(Build, "callscript-names.json");
    public static string Textures => Path.Combine(Build, "textures");
    public static string Sys4Ini => Path.Combine(GameDir, "SYS4INI.BIN");
    public static string Append01Aai => Path.Combine(GameDir, "APPEND01.AAI");
    public static string BinExtractAlf => Path.Combine(Repo, "bin", "BinExtractALF.exe");

    private static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            if (File.Exists(Path.Combine(d.FullName, "global.json"))
                && File.Exists(Path.Combine(d.FullName, "engine", "AgeEngine.sln"))
                && File.Exists(Path.Combine(d.FullName, "vm-map", "opcodes.toml")))
                return d.FullName;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException(
            "repository root not found above " + AppContext.BaseDirectory);
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
