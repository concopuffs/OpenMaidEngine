using Ome.Mods.Linking;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

namespace Ome.Mods.Launch;

/// <summary>The engine's mod flags (spec §9): <c>--mod PATH</c> (repeatable), <c>--no-mods</c>,
/// <c>--skip-failed-mods</c>, <c>--forget-mod ID</c> (repeatable), and the Lua scripting spec's <c>--lua-dev</c>.</summary>
public sealed record ModLaunchOptions(IReadOnlyList<string> ExtraPackages, bool NoMods, bool SkipFailedMods,
                                      IReadOnlyList<string> ForgetMods, bool LuaDev = false)
{
    public const string ModOption = "--mod", NoModsOption = "--no-mods", SkipFailedOption = "--skip-failed-mods",
                        ForgetOption = "--forget-mod", LuaDevOption = "--lua-dev";

    public static ModLaunchOptions Resolve(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var extra = new List<string>();
        var forget = new List<string>();
        bool noMods = false, skip = false, luaDev = false;
        for (int k = 0; k < arguments.Count; k++)
        {
            string argument = arguments[k];
            if (argument == NoModsOption) noMods = true;
            else if (argument == SkipFailedOption) skip = true;
            else if (argument == LuaDevOption) luaDev = true;
            else if (argument is ModOption or ForgetOption)
            {
                if (k + 1 >= arguments.Count || arguments[k + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{argument} requires {(argument == ModOption ? "a package path" : "a mod id")}");
                string value = arguments[++k];
                if (argument == ModOption) extra.Add(Path.GetFullPath(value, workingDirectory));
                else forget.Add(value);
            }
        }
        return new ModLaunchOptions(extra, noMods, skip, forget, luaDev);
    }
}

/// <summary>The packages a discovery opened, the user order, and whether discovery failed: a candidate that did not
/// open, or one of a <c>[mods]</c> table's errors (launcher spec §5.3).</summary>
public sealed record ModDiscovery(IReadOnlyList<ModPackage> Packages, IReadOnlyList<string> UserOrder, bool FailedToOpen);

/// <summary>The outcome of startup linking: the layer to mount (null without mods or on failure), the report, and
/// where it was written. <paramref name="UnpackedLua"/> gives each unpacked mod that ships Lua by id, with its folder:
/// the folders <c>--lua-reload</c> watches (Lua scripting spec §13).</summary>
public sealed record ModLaunchResult(MountedModLayer? Layer, ModLinkReport Report, string? ReportPath, bool Failed,
                                     bool FromCache, IReadOnlyList<ModStackEntry> Stack,
                                     IReadOnlyDictionary<string, string>? UnpackedLua = null)
{
    public static ModLaunchResult None(ModLinkReport report) => new(null, report, null, false, false, []);

    /// <summary>The unpacked mods that ship Lua, empty when none.</summary>
    public IReadOnlyDictionary<string, string> UnpackedLuaFolders => UnpackedLua ?? new Dictionary<string, string>();
}

/// <summary>
/// Startup linking (spec §6.1 and §9): discovers the profile's <c>mods/</c> folder and the <c>--mod</c> packages, applies
/// <c>mods/load-order.txt</c>, releases <c>--forget-mod</c> selectors, links or reuses the cache, and writes the report
/// to <c>diagnostics/mod-link.txt</c>. A profile file's <c>[mods]</c> table replaces the folder and its order (launcher
/// spec §5.3); the selectors and the cache stay in the profile root's <c>mods/</c> either way.
/// </summary>
public static class ModLaunch
{
    public const string ModsFolder = "mods", LoadOrderFile = "load-order.txt", ReportFile = "mod-link.txt";

    /// <summary>The ids of <c>load-order.txt</c>, in order, and the ids it disables (<c>-ID</c>). Blank lines and lines
    /// starting with <c>#</c> are ignored.</summary>
    public static (IReadOnlyList<string> Order, IReadOnlySet<string> Disabled) ReadLoadOrder(string modsDirectory)
    {
        var order = new List<string>();
        var disabled = new HashSet<string>(StringComparer.Ordinal);
        string path = Path.Combine(modsDirectory, LoadOrderFile);
        if (!File.Exists(path)) return (order, disabled);
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith('-')) disabled.Add(line[1..].Trim());
            else if (!order.Contains(line)) order.Add(line);
        }
        return (order, disabled);
    }

    /// <summary>The candidates of a mods folder: each <c>.omp</c> file and each folder with a <c>manifest.toml</c>,
    /// skipping dot-entries such as <c>.cache</c>.</summary>
    public static IReadOnlyList<string> Candidates(string modsDirectory)
    {
        if (!Directory.Exists(modsDirectory)) return [];
        var files = Directory.EnumerateFiles(modsDirectory, "*" + ModPackage.PackageExtension)
            .Where(path => !Path.GetFileName(path).StartsWith('.'));
        var folders = Directory.EnumerateDirectories(modsDirectory)
            .Where(path => !Path.GetFileName(path).StartsWith('.') && File.Exists(Path.Combine(path, ManifestReader.FileName)));
        return files.Concat(folders).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Whether discovery could find any package: a <c>[mods]</c> table that enables one, or without a table a
    /// candidate in the profile's <c>mods/</c> folder. The host's early "no mods" test.</summary>
    public static bool MayHavePackages(string modsDirectory, ProfileMods? table)
        => table == null ? Candidates(modsDirectory).Count > 0 : table.Enabled.Any();

    /// <summary>Discovery for <paramref name="table"/> when there is one, else for the profile's <c>mods/</c> folder and
    /// its <c>load-order.txt</c>. <paramref name="mode"/> is a profile file's mode (<see cref="ModeCheck"/>), null
    /// without one.</summary>
    public static ModDiscovery Discover(string modsDirectory, IReadOnlyList<string> extraPackages, ModLinkReport report,
                                        ProfileMods? table, string game, string? mode)
        => table == null ? Discover(modsDirectory, extraPackages, report, mode)
            : DiscoverTable(table, modsDirectory, game, extraPackages, report, mode);

    /// <summary>
    /// Discovery for a profile's <c>[mods]</c> table (launcher spec §5.3). Every candidate of every folder is indexed
    /// by its manifest alone; only the packages <c>order</c> enables, and the <c>--mod</c> packages, are opened and
    /// hashed. A candidate whose manifest cannot be read is a note, so a broken file in a shared folder stops no
    /// profile that leaves it off; a candidate for another game is left out unless no candidate for this game has an
    /// id <c>order</c> enables. These stop the start before the link (or, with <c>--skip-failed-mods</c>, drop the mod):
    /// an enabled id in no folder, an enabled id found twice, and an enabled package whose hard dependency is in a
    /// folder but not enabled. A base mod's id in <c>order</c> with no such package is a note and changes nothing: the
    /// engine's own joins when an enabled mod requires it. <c>mods/load-order.txt</c> is not read.
    /// </summary>
    public static ModDiscovery DiscoverTable(ProfileMods table, string modsDirectory, string game,
                                             IReadOnlyList<string> extraPackages, ModLinkReport report, string? mode = null)
    {
        string loadOrder = Path.Combine(modsDirectory, LoadOrderFile);
        if (File.Exists(loadOrder))
            report.Add(ModReportClass.Note, LoadOrderFile, null,
                $"{loadOrder} is not read: the profile's profile.toml [mods] table names the enabled mods");

        var index = new List<PackageIndexEntry>();
        foreach (string folder in table.Folders)
        {
            IReadOnlyList<string> candidates;
            try
            {
                if (!Directory.Exists(folder))
                {
                    report.Add(ModReportClass.Note, Path.GetFileName(folder), null, $"the mod folder {folder} does not exist");
                    continue;
                }
                candidates = Candidates(folder);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                report.Add(ModReportClass.Note, Path.GetFileName(folder), null, $"the mod folder {folder} cannot be read: {error.Message}");
                continue;
            }
            foreach (string candidate in candidates)
                try
                {
                    index.Add(PackageIndex.Read(candidate));
                }
                catch (Exception error) when (error is ModPackageException or ModManifestException or IOException
                                                  or InvalidDataException or UnauthorizedAccessException)
                {
                    report.Add(ModReportClass.Note, Path.GetFileName(candidate), null,
                        $"{candidate} is left out: {error.Message}");
                }
        }

        bool failed = false;
        var packages = new List<ModPackage>();
        var extraIds = new List<string>();
        foreach (string path in extraPackages)
        {
            try
            {
                ModPackage package = ModPackage.Open(path);
                packages.Add(package);
                extraIds.Add(package.Id);
            }
            catch (Exception error) when (error is ModPackageException or ModManifestException or IOException
                                              or InvalidDataException or UnauthorizedAccessException)
            {
                report.Add(ModReportClass.Error, Path.GetFileName(path), null, $"{path}: {error.Message}");
                failed = true;
            }
        }

        var shipped = ShippedPackages.Ids.ToHashSet(StringComparer.Ordinal);
        var chosen = new List<PackageIndexEntry>();
        foreach (string id in table.Enabled)
        {
            var found = index.Where(entry => entry.Id == id).ToList();
            if (found.Any(entry => entry.IsFor(game))) found = found.Where(entry => entry.IsFor(game)).ToList();
            if (found.Count == 0)
            {
                if (shipped.Contains(id))
                    report.Add(ModReportClass.Note, id, null,
                        $"profile.toml enables {id}, which no mod folder holds: this changes nothing, since the engine's "
                        + $"own {id} joins when an enabled mod requires it");
                else if (!extraIds.Contains(id))
                {
                    report.Add(ModReportClass.Error, id, null, $"profile.toml enables {id}, which none of its mod folders "
                                                               + $"holds ({string.Join(", ", table.Folders)})");
                    failed = true;
                }
                continue;
            }
            if (found.Count > 1)
            {
                report.Add(ModReportClass.Error, id, null, $"profile.toml enables {id}, which is found {found.Count} "
                                                           + $"times: {string.Join(", ", found.Select(entry => entry.Path))}");
                failed = true;
                continue;
            }
            chosen.Add(found[0]);
        }
        foreach (ProfileModEntry disabled in table.Order.Where(entry => !entry.Enabled))
            if (index.Any(entry => entry.Id == disabled.Id && entry.IsFor(game)))
                report.Add(ModReportClass.Note, disabled.Id, null, "disabled by profile.toml");

        // A hard dependency in a folder must be enabled too: the launcher enables it with the mod (spec §8.4), so the
        // engine stays strict. The engine's own base mods satisfy a requirement whatever the folders hold.
        var enabled = chosen.Select(entry => entry.Id).Concat(extraIds).ToHashSet(StringComparer.Ordinal);
        var dependents = chosen.Select(entry => (entry.Id, entry.Requires, Chosen: (PackageIndexEntry?)entry))
            .Concat(packages.Select(package => (package.Id, package.Manifest.Requires, Chosen: (PackageIndexEntry?)null)));
        var blocked = new HashSet<PackageIndexEntry>();
        foreach (var (id, requires, entry) in dependents)
            foreach (ModRequirement requirement in requires)
            {
                if (enabled.Contains(requirement.Id) || shipped.Contains(requirement.Id)) continue;
                if (index.FirstOrDefault(candidate => candidate.Id == requirement.Id && candidate.IsFor(game)) is not { } held)
                    continue;   // in no folder: the link reports it as not installed
                report.Add(ModReportClass.Error, id, null,
                    $"requires {requirement.Id}, which is in {held.Path} but not enabled in profile.toml: enable it too");
                failed = true;
                if (entry != null) blocked.Add(entry);
            }

        var opened = new List<string>();
        foreach (PackageIndexEntry entry in chosen.Where(entry => !blocked.Contains(entry)))
            try
            {
                ModPackage package = ModPackage.Open(entry.Path);
                packages.Add(package);
                opened.Add(package.Id);
            }
            catch (Exception error) when (error is ModPackageException or ModManifestException or IOException
                                              or InvalidDataException or UnauthorizedAccessException)
            {
                report.Add(ModReportClass.Error, Path.GetFileName(entry.Path), null, $"{entry.Path}: {error.Message}");
                failed = true;
            }
        // Launcher spec §5.4: before a base mod mounts, so a refusal names the package the player enabled.
        failed |= ModeCheck.Apply(packages, mode, report);
        packages.AddRange(ShippedPackages.Required(packages, report));
        var present = packages.Select(package => package.Id).ToHashSet(StringComparer.Ordinal);
        var userOrder = opened.Concat(extraIds.Where(id => !opened.Contains(id))).Where(present.Contains).Distinct().ToList();
        return new ModDiscovery(packages, userOrder, failed);
    }

    /// <summary>
    /// Opens the folder's candidates and the extra packages. Mods that <c>load-order.txt</c> disables are skipped (an
    /// extra package is always enabled); a package that does not open is an error named by its file. The engine's own
    /// base mods join when an enabled package requires one that is not installed (<see cref="ShippedPackages"/>). The
    /// user order is the listed ids, then the folder's other mods by id, then the extra packages in argument order. The
    /// caller disposes the packages.
    /// </summary>
    public static ModDiscovery Discover(string modsDirectory, IReadOnlyList<string> extraPackages, ModLinkReport report,
                                        string? mode = null)
    {
        var (listed, disabled) = ReadLoadOrder(modsDirectory);
        var packages = new List<ModPackage>();
        var folderIds = new List<string>();
        var extraIds = new List<string>();
        bool failedToOpen = false;
        foreach (var (path, extra) in Candidates(modsDirectory).Select(path => (path, false))
                     .Concat(extraPackages.Select(path => (path, true))))
        {
            try
            {
                ModPackage package = ModPackage.Open(path);
                if (!extra && disabled.Contains(package.Id))
                {
                    report.Add(ModReportClass.Note, package.Id, null, "disabled by load-order.txt");
                    package.Dispose();
                    continue;
                }
                packages.Add(package);
                (extra ? extraIds : folderIds).Add(package.Id);
            }
            catch (Exception error) when (error is ModPackageException or ModManifestException or IOException
                                              or InvalidDataException or UnauthorizedAccessException)
            {
                // The package has no trustworthy id, so it is named by its file.
                report.Add(ModReportClass.Error, Path.GetFileName(path), null, $"{path}: {error.Message}");
                failedToOpen = true;
            }
        }
        // Launcher spec §5.4: a profile file without a [mods] table still has a mode.
        failedToOpen |= ModeCheck.Apply(packages, mode, report);
        packages.AddRange(ShippedPackages.Required(packages, report));
        var userOrder = listed
            .Concat(folderIds.Where(id => !listed.Contains(id)).Order(StringComparer.Ordinal))
            .Concat(extraIds.Where(id => !listed.Contains(id)))
            .Distinct().ToList();
        return new ModDiscovery(packages, userOrder, failedToOpen);
    }

    /// <param name="table">A profile file's <c>[mods]</c> table, which replaces the <c>mods/</c> folder's packages and
    /// its <c>load-order.txt</c> (launcher spec §5.3); null without a profile file.</param>
    public static ModLaunchResult Run(ModLinkBase linkBase, string profileRoot, string diagnosticsDirectory,
                                      ModLaunchOptions options, ProfileMods? table = null)
    {
        string modsDirectory = Path.Combine(profileRoot, ModsFolder);
        var report = new ModLinkReport();
        if (options.ForgetMods.Count > 0)
        {
            string selectorsPath = Path.Combine(modsDirectory, SelectorStore.FileName);
            SelectorStore store = SelectorStore.Load(selectorsPath);
            foreach (string id in options.ForgetMods)
                report.Add(ModReportClass.Note, id, null,
                    store.Forget(id) ? "--forget-mod released its selector" : "--forget-mod: it had no selector");
            store.Save(selectorsPath);
        }
        if (options.NoMods) return ModLaunchResult.None(report);
        linkBase = linkBase with { LuaDev = options.LuaDev };

        ModDiscovery discovery = Discover(modsDirectory, options.ExtraPackages, report, table, linkBase.Profile,
                                          linkBase.ProfileMode);
        var packages = discovery.Packages;
        bool failedToOpen = discovery.FailedToOpen;
        try
        {
            if (packages.Count == 0 && !failedToOpen) return ModLaunchResult.None(report);
            IReadOnlyList<string> userOrder = discovery.UserOrder;
            ModStackResult stack = failedToOpen && !options.SkipFailedMods
                ? new ModStackResult(null, report, false)
                : ModStack.Prepare(linkBase, packages, userOrder, modsDirectory, options.SkipFailedMods);
            ModLinkReport final = stack.Report;
            if (!ReferenceEquals(final, report))
            {
                var merged = new ModLinkReport { Order = final.Order, Dropped = final.Dropped, Lua = final.Lua };
                merged.AddRange(report.Lines);
                merged.AddRange(final.Lines);
                final = merged;
            }
            string reportPath = Path.Combine(diagnosticsDirectory, ReportFile);
            Directory.CreateDirectory(diagnosticsDirectory);
            File.WriteAllText(reportPath, final.Render() + (stack.Layer is { } layer && stack.FromCache
                ? $"(linked earlier; cached report follows)\n{layer.ReportText}" : ""));
            var unpackedLua = packages.Where(package => package.Unpacked && ModLuaLinker.ShipsLua(package))
                .GroupBy(package => package.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Location, StringComparer.Ordinal);
            return new ModLaunchResult(stack.Layer, final, reportPath, stack.Layer == null, stack.FromCache,
                stack.Layer?.Manifest.Order ?? [], unpackedLua);
        }
        finally
        {
            foreach (ModPackage package in packages) package.Dispose();
        }
    }
}
