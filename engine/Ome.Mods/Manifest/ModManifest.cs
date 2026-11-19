using Ome.Mods.Localization;

namespace Ome.Mods.Manifest;

/// <summary>A hard dependency (spec §3.3 <c>[[requires]]</c>).</summary>
public sealed record ModRequirement(string Id, VersionRange Version);

/// <summary>A declared hard conflict (spec §3.3 <c>[[conflicts]]</c>).</summary>
public sealed record ModConflict(string Id, string? Reason);

/// <summary>A <c>[replace]</c> entry: this mod's replacement of <see cref="Script"/> deliberately supersedes
/// <see cref="Over"/>'s (spec §4.2).</summary>
public sealed record ModReplacementOrder(string Script, string Over);

/// <summary>A <c>[[data]]</c> entry (data patches spec §5.1): a data file the package ships under <c>data/</c>, of a kind
/// that a mod of the stack accepts, delivered at <see cref="Priority"/> as a hook of the mod would run.</summary>
public sealed record ModDataEntry(int Index, string Kind, string File, int Priority)
{
    /// <summary>The folder the files are under.</summary>
    public const string Folder = "data";

    /// <summary>The file's path in the package, as the report, the trace and the contribution print it:
    /// <c>data/slimes.toml</c>.</summary>
    public string PackagePath => $"{Folder}/{File}";

    /// <summary>How reports name the entry: <c>data[0] (data/slimes.toml)</c>.</summary>
    public string Describe() => $"data[{Index}] ({PackagePath})";
}

/// <summary>An <c>[[accepts.at]]</c> entry: an event a kind's documents are delivered at, and the owner's handler there.</summary>
public sealed record ModKindEvent(int Index, string Event, ModHandlerRef Handler);

/// <summary>An <c>[[accepts]]</c> entry (data patches spec §5.2): a kind the mod declares and gives its meaning, and the
/// events its documents are delivered at.</summary>
public sealed record ModKind(int Index, string Kind, string? Description, IReadOnlyList<ModKindEvent> At)
{
    /// <summary>How reports name the entry: <c>accepts[0] kamidori.tables</c>.</summary>
    public string Describe() => $"accepts[{Index}] {Kind}";
}

/// <summary>A parsed and validated <c>manifest.toml</c> (spec §3.3; format version 2 adds the localization spec's
/// <c>[[language]]</c> tables, §9.2; version 3 adds the Lua scripting spec's <c>[lua]</c> and <c>[[hooks]]</c>,
/// §6.2; version 4 adds the data patches spec's <c>[[data]]</c> and <c>[[accepts]]</c>, §5.1–§5.2).</summary>
public sealed record ModManifest
{
    /// <summary>The newest format version the reader takes; it also reads versions 1 to 3.</summary>
    public const int CurrentOmpVersion = 4;
    /// <summary>The first format version with <c>[[data]]</c>, <c>[[accepts]]</c> and <c>data/</c> (data patches
    /// spec §5.1).</summary>
    public const int DataOmpVersion = 4;
    /// <summary>The first format version with <c>[[language]]</c>, <c>text/</c> and <c>fonts/</c>.</summary>
    public const int LanguageOmpVersion = 2;
    /// <summary>The first format version with <c>[lua]</c>, <c>[[hooks]]</c>, <c>lua/</c>, <c>events/</c> and the
    /// <c>extended</c> target (Lua scripting spec §6.2).</summary>
    public const int LuaOmpVersion = 3;
    /// <summary>The targets of <c>[game].target</c>: native AGE runs a <c>native</c> mod; only the port runs an
    /// <c>extended</c> one, which a mod with Lua must be.</summary>
    public const string NativeTarget = "native", ExtendedTarget = "extended";
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
    /// <summary><c>[game].target</c>: <c>native</c>, or from version 3 <c>extended</c>. Null without
    /// <c>[game]</c>.</summary>
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

    /// <summary>The <c>[lua]</c> table (Lua scripting spec §6.2); null when the manifest has none.</summary>
    public ModLuaSettings? Lua { get; init; }
    /// <summary>The <c>[[hooks]]</c> entries, in manifest order (§6.3).</summary>
    public IReadOnlyList<ModHook> Hooks { get; init; } = [];

    /// <summary>The <c>[[data]]</c> entries, in manifest order (data patches spec §5.1).</summary>
    public IReadOnlyList<ModDataEntry> Data { get; init; } = [];
    /// <summary>The <c>[[accepts]]</c> entries, in manifest order (data patches spec §5.2).</summary>
    public IReadOnlyList<ModKind> Accepts { get; init; } = [];

    /// <summary>Whether the package targets no profile (localization spec §11.1): it may hold only
    /// <c>[[language]]</c>, catalogs with <c>[[engine]]</c> entries alone, and <c>fonts/</c>.</summary>
    public bool TargetsAnyProfile => Profile == null;

    /// <summary>Whether the mod owns an append catalog and so a selector (spec §6.2).</summary>
    public bool NeedsCatalog => ScriptNames.Count > 0 || AssetNames.Count > 0;
}
