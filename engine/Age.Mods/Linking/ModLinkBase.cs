using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Script.Abi;
using Age.Script.Modules;
using Age.Script.Patch;
using Age.Script.Text;

namespace Age.Mods.Linking;

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
    public required OpcodeTable Table { get; init; }
    public required OpcodeContracts Contracts { get; init; }
    public required IntrinsicCatalog Intrinsics { get; init; }
    /// <summary>The profile's OMS modules (globals, scripts, source preludes).</summary>
    public IReadOnlyList<GameModule> Modules { get; init; } = [];
    public OmsIdioms? Idioms { get; init; }
    public OmsPatchPoints? PatchPoints { get; init; }

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
            Table = table,
            Contracts = contracts,
            Intrinsics = intrinsics,
            Modules = modules,
            Idioms = modules.Any(module => module.Source != null) ? OmsIdioms.Build(modules, table.AbiId, intrinsics) : null,
            PatchPoints = patchPointsJson != null && File.Exists(patchPointsJson) ? OmsPatchPoints.Load(patchPointsJson) : null,
        };
    }
}
