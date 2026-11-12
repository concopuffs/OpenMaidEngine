using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Script.Abi;
using Ome.Script.Modules;
using Ome.Script.Patch;
using Ome.Script.Text;

namespace Ome.Mods.Linking;

/// <summary>
/// The base a stack links against (spec §2): the selected profile, the catalog's fingerprint, the scripts the
/// catalog serves (overlays included), and the toolchain that compiles fragments against them.
/// </summary>
public sealed record ModLinkBase
{
    public required string Profile { get; init; }
    /// <summary><c>sha256:</c> of <c>SYS4INI.BIN</c>, matched against <c>[game].revisions</c>.</summary>
    public required string CatalogFingerprint { get; init; }
    /// <summary>A script's bytes as the base serves them, or null when the catalog has no such script.</summary>
    public required Func<string, byte[]?> ReadScript { get; init; }
    /// <summary>Every installed script, the append packs' included (<c>$1$CIINIT.BIN</c>), with a reader of its bytes:
    /// the link reports catalog entries that match none of their text (localization spec §9.4) and binds site entries
    /// by their fingerprints. Empty skips that report.</summary>
    public IReadOnlyList<(string Name, Func<byte[]> Read)> InstalledScripts { get; init; } = [];
    public required OpcodeTable Table { get; init; }
    public required OpcodeContracts Contracts { get; init; }
    public required IntrinsicCatalog Intrinsics { get; init; }
    /// <summary>The profile's OMS modules (globals, scripts, source preludes).</summary>
    public IReadOnlyList<GameModule> Modules { get; init; } = [];
    public OmsIdioms? Idioms { get; init; }
    public OmsPatchPoints? PatchPoints { get; init; }
    /// <summary>The base catalog's title, which every append catalog repeats (AGE mounts only its own game's).</summary>
    public string CatalogTitle { get; init; } = "";
    /// <summary>The append magic of the base catalog family, such as <c>"S4AC422 "</c>.</summary>
    public string AppendMagic { get; init; } = "S4AC422 ";
    /// <summary>The selectors the install's own <c>*.AAI</c> catalogs use; mods never receive them.</summary>
    public IReadOnlySet<int> InstallSelectors { get; init; } = new HashSet<int>();
    /// <summary>Whether the base catalog, or an append catalog the install mounts, has a real (non-placeholder)
    /// record of that basename; an <c>assets/</c> file of such a name overrides it, any other is a new asset (spec
    /// §4.4). An append record's name keeps its <c>$n$</c> prefix, as AGE opens it from the game root.</summary>
    public Func<string, bool> HasAsset { get; init; } = _ => false;
    /// <summary>The toolchain's identity for the link cache key (spec §6.4): the compiler and linker builds and the
    /// profile's opcode table, preludes and patch points.</summary>
    public string ToolchainStamp { get; init; } = "";

    /// <summary>The opcode table's content (each opcode's label, operand count and semantic name), for the stamp.</summary>
    private static string TableFingerprint(OpcodeTable table)
    {
        var text = new System.Text.StringBuilder();
        foreach (OpcodeDefinition entry in table.Entries.OrderBy(entry => entry.Opcode))
            text.Append(entry.Opcode).Append(':').Append(entry.Label).Append(':').Append(entry.Argc).Append(':')
                .Append(entry.SemanticName).Append('\n');
        return OmsPatchPlan.FingerprintOf(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>
    /// The base of an installed game: the mounted catalog and its asset store (overlay roots included), with the
    /// profile's generated globals, its OMS source modules (which also give the idioms that name base labels) and its
    /// patch points, each when present.
    /// </summary>
    public static ModLinkBase ForInstall(string profile, string sys4IniPath, Sys4AssetCatalog catalog, IAssetStore store,
                                         OpcodeTable table, OpcodeContracts contracts, string? globalsJson = null,
                                         string? omsModulesDirectory = null, string? patchPointsJson = null)
    {
        var intrinsics = new IntrinsicCatalog(table, contracts);
        var stamp = new List<string>
        {
            typeof(Ome.Script.Compile.OmsCompiler).Assembly.ManifestModule.ModuleVersionId.ToString(),
            typeof(ModLinkBase).Assembly.ManifestModule.ModuleVersionId.ToString(),
            table.AbiId,
            // The table's content, not only its ABI id: opcodes_build.py can change framing or a contract under the
            // same id, and a link compiled against the old registry must not be mounted.
            TableFingerprint(table),
            contracts.Fingerprint(),
        };
        foreach (string? file in new[] { globalsJson, patchPointsJson })
            if (file != null && File.Exists(file)) stamp.Add(OmsPatchPlan.FingerprintOf(File.ReadAllBytes(file)));
        if (omsModulesDirectory != null && Directory.Exists(omsModulesDirectory))
            foreach (string file in Directory.EnumerateFiles(omsModulesDirectory, "*", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
                stamp.Add(Path.GetRelativePath(omsModulesDirectory, file).Replace('\\', '/') + "="
                          + OmsPatchPlan.FingerprintOf(File.ReadAllBytes(file)));
        var modules = new List<GameModule>();
        if (globalsJson != null && File.Exists(globalsJson)) modules.Add(GameModule.LoadGlobals(profile, globalsJson));
        modules.Add(GameModule.Scripts(profile, catalog.EnumerateScripts().Select(packed => (packed.Asset.Name, packed.PackedId))));
        if (omsModulesDirectory != null && Directory.Exists(omsModulesDirectory))
            modules.AddRange(GameModule.LoadSources(omsModulesDirectory, profile));
        return new ModLinkBase
        {
            Profile = profile,
            CatalogFingerprint = OmsPatchPlan.FingerprintOf(File.ReadAllBytes(sys4IniPath)),
            ReadScript = name => catalog.ResolveName(name) is { IsPlaceholder: false } entry
                                 && entry.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)
                ? store.ReadAll(entry) : null,
            InstalledScripts = catalog.EnumerateScripts().Where(packed => !packed.Asset.IsPlaceholder)
                .Select(packed => (packed.Asset.Name, (Func<byte[]>)(() => store.ReadAll(packed.Asset)))).ToList(),
            Table = table,
            Contracts = contracts,
            Intrinsics = intrinsics,
            Modules = modules,
            Idioms = modules.Any(module => module.Source != null) ? OmsIdioms.Build(modules, table.AbiId, intrinsics) : null,
            PatchPoints = patchPointsJson != null && File.Exists(patchPointsJson) ? OmsPatchPoints.Load(patchPointsJson) : null,
            CatalogTitle = catalog.Title,
            AppendMagic = ("S4AC" + catalog.Magic[4..]).PadRight(8)[..8],
            InstallSelectors = catalog.AppendPacks.Keys.ToHashSet(),
            HasAsset = name => catalog.ResolveMountedName(name) is { IsPlaceholder: false },
            ToolchainStamp = string.Join("\n", stamp),
        };
    }
}
