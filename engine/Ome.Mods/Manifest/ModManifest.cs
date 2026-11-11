using Ome.Mods.Localization;

namespace Ome.Mods.Manifest;

/// <summary>A hard dependency (spec §3.3 <c>[[requires]]</c>).</summary>
public sealed record ModRequirement(string Id, VersionRange Version);

/// <summary>A declared hard conflict (spec §3.3 <c>[[conflicts]]</c>).</summary>
public sealed record ModConflict(string Id, string? Reason);

/// <summary>A <c>[replace]</c> entry: this mod's replacement of <see cref="Script"/> deliberately supersedes
/// <see cref="Over"/>'s (spec §4.2).</summary>
public sealed record ModReplacementOrder(string Script, string Over);

/// <summary>A parsed and validated <c>manifest.toml</c> (spec §3.3; format version 2 adds the localization spec's
/// <c>[[language]]</c> tables, §9.2).</summary>
public sealed record ModManifest
{
    /// <summary>The newest format version the reader takes; it also reads version 1.</summary>
    public const int CurrentOmpVersion = 2;
    /// <summary>The first format version with <c>[[language]]</c>, <c>text/</c> and <c>fonts/</c>.</summary>
    public const int LanguageOmpVersion = 2;
    public const int CurrentOmsVersion = 1;

    /// <summary>The manifest's <c>omp</c>.</summary>
    public int OmpVersion { get; init; } = 1;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required SemanticVersion Version { get; init; }
    public IReadOnlyList<string> Authors { get; init; } = [];
    public string? Description { get; init; }

    /// <summary>The embedded profile id the mod targets (<c>[game].profile</c>); null for a version 2 package
    /// without <c>[game]</c>, which every profile accepts (localization spec §11.1).</summary>
    public string? Profile { get; init; }
    /// <summary><c>[game].target</c>; <c>native</c> is the only version 1 target. Null without <c>[game]</c>.</summary>
    public string? Target { get; init; }
    /// <summary><c>[game].revisions</c>: <c>sha256:</c> fingerprints of <c>SYS4INI.BIN</c>; empty admits any.</summary>
    public IReadOnlyList<string> Revisions { get; init; } = [];

    public int OmsVersion { get; init; } = CurrentOmsVersion;

    public IReadOnlyList<ModRequirement> Requires { get; init; } = [];
    public IReadOnlyList<ModConflict> Conflicts { get; init; } = [];
    public IReadOnlyList<string> After { get; init; } = [];
    public IReadOnlyList<string> Before { get; init; } = [];

    /// <summary>The append catalog's first records, in order; record 0 runs at INIT2 (spec §6.2).</summary>
    public IReadOnlyList<string> ScriptNames { get; init; } = [];
    /// <summary>New assets, as the catalog records after the scripts.</summary>
    public IReadOnlyList<string> AssetNames { get; init; } = [];
    public IReadOnlyList<ModReplacementOrder> ReplacementOrders { get; init; } = [];

    /// <summary>The languages the mod defines (localization spec §9.2), in manifest order, bundled fonts as written
    /// (<c>fonts/NAME</c>).</summary>
    public IReadOnlyList<LanguageSettings> Languages { get; init; } = [];

    /// <summary>Whether the package targets no profile (localization spec §11.1): it may hold only
    /// <c>[[language]]</c>, catalogs with <c>[[engine]]</c> entries alone, and <c>fonts/</c>.</summary>
    public bool TargetsAnyProfile => Profile == null;

    /// <summary>Whether the mod owns an append catalog and so a selector (spec §6.2).</summary>
    public bool NeedsCatalog => ScriptNames.Count > 0 || AssetNames.Count > 0;
}
