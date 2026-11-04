using Age.Mods.Linking;
using Age.Mods.Manifest;
using Age.Mods.Packages;

namespace Age.Mods.Launch;

/// <summary>The engine's mod flags (spec §9): <c>--mod PATH</c> (repeatable), <c>--no-mods</c>,
/// <c>--skip-failed-mods</c> and <c>--forget-mod ID</c> (repeatable).</summary>
public sealed record ModLaunchOptions(IReadOnlyList<string> ExtraPackages, bool NoMods, bool SkipFailedMods,
                                      IReadOnlyList<string> ForgetMods)
{
    public const string ModOption = "--mod", NoModsOption = "--no-mods", SkipFailedOption = "--skip-failed-mods",
                        ForgetOption = "--forget-mod";

    public static ModLaunchOptions Resolve(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var extra = new List<string>();
        var forget = new List<string>();
        bool noMods = false, skip = false;
        for (int k = 0; k < arguments.Count; k++)
        {
            string argument = arguments[k];
            if (argument == NoModsOption) noMods = true;
            else if (argument == SkipFailedOption) skip = true;
            else if (argument is ModOption or ForgetOption)
            {
                if (k + 1 >= arguments.Count || arguments[k + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{argument} requires {(argument == ModOption ? "a package path" : "a mod id")}");
                string value = arguments[++k];
                if (argument == ModOption) extra.Add(Path.GetFullPath(value, workingDirectory));
                else forget.Add(value);
            }
        }
        return new ModLaunchOptions(extra, noMods, skip, forget);
    }
}

/// <summary>The packages a discovery opened, the user order, and whether any candidate failed to open.</summary>
public sealed record ModDiscovery(IReadOnlyList<ModPackage> Packages, IReadOnlyList<string> UserOrder, bool FailedToOpen);

/// <summary>The outcome of startup linking: the layer to mount (null without mods or on failure), the report, and
/// where it was written.</summary>
public sealed record ModLaunchResult(MountedModLayer? Layer, ModLinkReport Report, string? ReportPath, bool Failed,
                                     bool FromCache, IReadOnlyList<ModStackEntry> Stack)
{
    public static ModLaunchResult None(ModLinkReport report) => new(null, report, null, false, false, []);
}

/// <summary>
/// Startup linking (spec §6.1 and §9): discovers the profile's <c>mods/</c> folder and the <c>--mod</c> packages, applies
/// <c>mods/load-order.txt</c>, releases <c>--forget-mod</c> selectors, links or reuses the cache, and writes the report
/// to <c>diagnostics/mod-link.txt</c>.
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

    /// <summary>
    /// Opens the folder's candidates and the extra packages. Mods that <c>load-order.txt</c> disables are skipped (an
    /// extra package is always enabled); a package that does not open is an error named by its file. The user order is
    /// the listed ids, then the folder's other mods by id, then the extra packages in argument order. The caller
    /// disposes the packages.
    /// </summary>
    public static ModDiscovery Discover(string modsDirectory, IReadOnlyList<string> extraPackages, ModLinkReport report)
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
        var userOrder = listed
            .Concat(folderIds.Where(id => !listed.Contains(id)).Order(StringComparer.Ordinal))
            .Concat(extraIds.Where(id => !listed.Contains(id)))
            .Distinct().ToList();
        return new ModDiscovery(packages, userOrder, failedToOpen);
    }

    public static ModLaunchResult Run(ModLinkBase linkBase, string profileRoot, string diagnosticsDirectory,
                                      ModLaunchOptions options)
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

        ModDiscovery discovery = Discover(modsDirectory, options.ExtraPackages, report);
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
                var merged = new ModLinkReport { Order = final.Order, Dropped = final.Dropped };
                merged.AddRange(report.Lines);
                merged.AddRange(final.Lines);
                final = merged;
            }
            string reportPath = Path.Combine(diagnosticsDirectory, ReportFile);
            Directory.CreateDirectory(diagnosticsDirectory);
            File.WriteAllText(reportPath, final.Render() + (stack.Layer is { } layer && stack.FromCache
                ? $"(linked earlier; cached report follows)\n{layer.ReportText}" : ""));
            return new ModLaunchResult(stack.Layer, final, reportPath, stack.Layer == null, stack.FromCache,
                stack.Layer?.Manifest.Order ?? []);
        }
        finally
        {
            foreach (ModPackage package in packages) package.Dispose();
        }
    }
}
