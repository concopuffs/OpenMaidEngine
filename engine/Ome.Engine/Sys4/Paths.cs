namespace Ome.Engine.Sys4;
public static class Paths
{
    public static string Repo { get; } = FindRepo();
    public static string Workspace => Directory.GetParent(Repo)!.FullName;
    public static string Extracted => Path.Combine(Workspace, "extracted");
    public static string Data1 => Path.Combine(Extracted, "DATA1");
    public static string GameDir => Path.Combine(Workspace, "Himegari_Game");
    /// <summary>The clean Kamidori install.</summary>
    public static string KamidoriDir => Path.Combine(Workspace, "Kamidori");
    /// <summary>A Kamidori install with the English fan translation (patch/, translated AGE.EXE and agerc.dll, root
    /// <c>$n$</c> scripts and BMP graphics), kept beside the clean one for the translation-support tests.</summary>
    public static string KamidoriTranslatedDir => Path.Combine(Workspace, "Kamidori_Translated");
    public static string Build => Path.Combine(Repo, "build");
    public static string OpcodesJson => Path.Combine(Build, "opcodes.json");
    /// <summary>Per-profile generated build output, <c>build/games/&lt;profile&gt;</c>.</summary>
    public static string GameBuild(string profileId) => Path.Combine(Build, "games", profileId);
    /// <summary>A profile's diagnostic asset index (<c>tools/parse_sys4ini.py</c>).</summary>
    public static string AssetIndexJson(string profileId) => Path.Combine(GameBuild(profileId), "asset-index.json");
    /// <summary>A profile's scene section bases (<c>tools/resolve_asset.py --build</c>).</summary>
    public static string AssetSectionsJson(string profileId) =>
        Path.Combine(GameBuild(profileId), "asset-sections.json");
    /// <summary>A profile's extracted *INIT tables (<c>tools/extract_init.py</c>).</summary>
    public static string GameData(string profileId) => Path.Combine(GameBuild(profileId), "data");
    /// <summary>A profile's generated global registry view (<c>globals_build.py --build</c>).</summary>
    public static string GlobalsJson(string profileId) => Path.Combine(GameBuild(profileId), "globals.json");
    /// <summary>Generated named patch points (tools/patch_points_build.py --build; mod loader spec §5).</summary>
    public static string PatchPointsJson(string profileId) => Path.Combine(GameBuild(profileId), "patch-points.json");
    /// <summary>A profile's hand-written Open Maid Script source modules, <c>oms/&lt;profile&gt;</c>.</summary>
    public static string OmsModules(string profileId) => Path.Combine(Repo, "oms", profileId);
    /// <summary>A profile's converter BMPs (<c>tools/convert_agf.py</c>), the AGF decoder's pixel oracle.</summary>
    public static string Textures(string profileId) => Path.Combine(GameBuild(profileId), "textures");
    public static string Sys4Ini => Path.Combine(GameDir, "SYS4INI.BIN");
    public static string Append01Aai => Path.Combine(GameDir, "APPEND01.AAI");
    public static string BinExtractAlf => Path.Combine(Repo, "bin", "BinExtractALF.exe");

    private static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            if (File.Exists(Path.Combine(d.FullName, "global.json"))
                && File.Exists(Path.Combine(d.FullName, "engine", "OmeEngine.sln"))
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
