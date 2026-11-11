using System.IO.Compression;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Script.Compile;
using Ome.Script.Patch;
using Ome.Script.Syntax;

namespace Ome.Mods.Tools;

/// <summary>The <c>omp</c> command group of spec §10: <c>build</c>, <c>check</c> and <c>explain</c>.</summary>
public static class OmpCommands
{
    /// <summary>The fixed timestamp of every <c>.omp</c> entry, so a package's bytes depend only on its content.</summary>
    public static readonly DateTimeOffset EntryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// <c>omp build DIR</c>: validates the folder, links it (with <paramref name="dependencies"/>) against the base so
    /// every fragment and new script compiles, stores each fragment's plan beside it, and writes a deterministic
    /// <c>.omp</c>: entries in ordinal order, fixed timestamps. The source folder is not changed.
    /// </summary>
    public static int Build(ModLinkBase linkBase, string directory, string? output, IReadOnlyList<string> dependencies,
                            TextWriter console)
    {
        if (!Directory.Exists(directory))
        {
            console.WriteLine($"error: {directory} is not a folder");
            return 1;
        }
        var opened = new List<ModPackage>();
        try
        {
            ModPackage mod;
            try
            {
                mod = ModPackage.Open(directory);
                opened.Add(mod);
                opened.AddRange(dependencies.Select(path => ModPackage.Open(path)));
            }
            catch (Exception error) when (error is ModPackageException or Manifest.ModManifestException or IOException)
            {
                console.WriteLine($"error: {error.Message}");
                return 1;
            }
            ModLinkResult link = ModLinker.Link(linkBase, opened, []);
            console.Write(link.Report.Render());
            if (!link.Succeeded)
            {
                console.WriteLine($"omp build: {mod.Id} does not link: {link.Report.Summary()}");
                return 1;
            }

            var plans = new SortedDictionary<string, string>(StringComparer.Ordinal);
            OmsResourceScope scope = ModLinker.ScopeOf(mod, opened.ToDictionary(package => package.Id, StringComparer.Ordinal));
            foreach (ModPatchFiles patch in mod.Layout.Patches.Where(patch => patch.Source != null))
            {
                ScriptUnit unit = Parser.ParseScript(mod.ReadText(patch.Source!), patch.Source!.Path);
                string target = unit.Name.ToUpperInvariant();
                ModReplacementFile? own = mod.Layout.Replacements.FirstOrDefault(file =>
                    file.Script.Equals(target, StringComparison.OrdinalIgnoreCase));
                byte[] bytes = own != null ? mod.ReadAll(own.File) : linkBase.ReadScript(target)!;
                OmsPatchPlan plan = OmsPatchCompiler.Compile(unit, bytes, linkBase.Table, linkBase.Contracts,
                    linkBase.Intrinsics, linkBase.Modules, linkBase.Idioms, null, null, linkBase.PatchPoints, scope);
                plans[$"patches/{patch.Stem}{ModLayout.PlanSuffix}"] = plan.ToJson();
            }

            string packagePath = Path.GetFullPath(output ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!,
                $"{mod.Id}-{mod.Manifest.Version}{ModPackage.PackageExtension}"));
            string temporary = packagePath + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var names = mod.Container.Entries.Select(entry => entry.Path)
                    .Where(path => !plans.ContainsKey(path)).Concat(plans.Keys).Order(StringComparer.Ordinal);
                foreach (string name in names)
                {
                    byte[] content = plans.TryGetValue(name, out string? json)
                        ? new System.Text.UTF8Encoding(false).GetBytes(json)
                        : mod.ReadAll(mod.Container.Find(name)!);
                    ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                    entry.LastWriteTime = EntryTime;
                    using Stream writer = entry.Open();
                    writer.Write(content);
                }
            }
            File.Move(temporary, packagePath, overwrite: true);
            console.WriteLine($"omp build: {mod.Id} {mod.Manifest.Version} -> {packagePath} ({plans.Count} stored plans)");
            return 0;
        }
        finally
        {
            foreach (ModPackage package in opened) package.Dispose();
        }
    }

    /// <summary><c>omp check</c>: links the data root's mods (when given) and the extra packages, without writing a
    /// cache or saving selectors, and prints the report. The data root's kept selectors are used.</summary>
    public static int Check(ModLinkBase linkBase, string? profileRoot, IReadOnlyList<string> extraPackages, bool skipFailed,
                            TextWriter console)
    {
        var (result, opened) = LinkForInspection(linkBase, profileRoot, extraPackages, skipFailed, console);
        try
        {
            if (result == null) return 1;
            console.Write(result.Report.Render());
            foreach (LinkedCatalog catalog in result.Catalogs)
                console.WriteLine($"catalog: {catalog.ModId} selector {catalog.Selector}: "
                                  + string.Join(", ", catalog.Records.Select((record, k) => $"0x{catalog.PackedId(k):x8} {record.CatalogName}")));
            console.WriteLine($"omp check: {(result.Succeeded ? "links" : "does not link")}: {result.Report.Summary()}");
            return result.Succeeded ? 0 : 1;
        }
        finally
        {
            foreach (ModPackage package in opened) package.Dispose();
        }
    }

    /// <summary><c>omp explain NAME</c>: which layer and mod supplies a script or asset, and every plan applied to a
    /// script.</summary>
    public static int Explain(ModLinkBase linkBase, Sys4AssetCatalog catalog, Sys4AssetStore store, string? profileRoot,
                              IReadOnlyList<string> extraPackages, string name, TextWriter console)
    {
        var (result, opened) = LinkForInspection(linkBase, profileRoot, extraPackages, false, console);
        try
        {
            if (result == null) return 1;
            if (!result.Succeeded) console.WriteLine($"note: the stack does not link ({result.Report.Summary()}); showing what linked");
            bool found = false;
            if (result.Scripts.TryGetValue(name, out LinkedScript? script))
            {
                found = true;
                console.WriteLine($"{script.Name}: linked mod layer (loose/), "
                                  + (script.ReplacedBy != null ? $"replaced by {script.ReplacedBy}" : "base script"));
                foreach (string plan in script.Plans) console.WriteLine($"  plan: {plan}");
            }
            if (result.Assets.TryGetValue(name, out LinkedAsset? asset))
            {
                found = true;
                console.WriteLine($"{asset.Name}: linked mod layer (loose/), asset override from {asset.ModId}");
            }
            foreach (LinkedCatalog modCatalog in result.Catalogs)
                for (int k = 0; k < modCatalog.Records.Count; k++)
                {
                    LinkedRecord record = modCatalog.Records[k];
                    if (!record.CatalogName.Equals(name, StringComparison.OrdinalIgnoreCase)
                        && !$"{modCatalog.ModId}:{record.Name}".Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    found = true;
                    console.WriteLine($"{record.CatalogName}: {modCatalog.ModId}'s append catalog {modCatalog.CatalogFile}, "
                                      + $"record {k}, packed id 0x{modCatalog.PackedId(k):x8}"
                                      + (record.Stub ? " (generated record-0 stub)" : ""));
                }
            if (!found && catalog.ResolveName(name) is { IsPlaceholder: false } entry)
            {
                found = true;
                AssetLocation location = store.Locate(entry);
                console.WriteLine($"{entry.Name}: base, packed id 0x{entry.PackedId:x8}, "
                                  + (location.Loose ? $"loose file {location.Path}" : $"{location.Path} at {location.Offset}+{location.Size}"));
            }
            if (!found) console.WriteLine($"{name}: neither the base nor the stack supplies it");
            return found ? 0 : 1;
        }
        finally
        {
            foreach (ModPackage package in opened) package.Dispose();
        }
    }

    /// <summary>
    /// <c>omp export --output-dir DIR</c> (spec §10.1): links the stack, then writes what native AGE loads into
    /// <paramref name="outputDirectory"/> only: patched and replaced scripts and asset overrides as loose files, and one
    /// <c>APPENDnn.AAI</c>/<c>.ALF</c> pair per mod catalog. Refused when a mod's target is not <c>native</c>. The data
    /// root's kept selectors are used, so an export matches the port's saves; installing the files is the user's step.
    /// </summary>
    public static int Export(ModLinkBase linkBase, string? profileRoot, IReadOnlyList<string> extraPackages,
                             string outputDirectory, TextWriter console)
    {
        var (result, opened) = LinkForInspection(linkBase, profileRoot, extraPackages, false, console);
        try
        {
            if (result == null) return 1;
            console.Write(result.Report.Render());
            var portOnly = result.Order.Where(mod => !mod.Manifest.Target.Equals("native", StringComparison.Ordinal)).ToList();
            if (portOnly.Count > 0)
            {
                console.WriteLine("omp export: refused: " + string.Join(", ", portOnly.Select(mod => $"{mod.Id} targets {mod.Manifest.Target}"))
                                  + "; only target = \"native\" mods export");
                return 1;
            }
            if (!result.Succeeded)
            {
                console.WriteLine($"omp export: the stack does not link: {result.Report.Summary()}");
                return 1;
            }
            Directory.CreateDirectory(outputDirectory);
            var written = new List<string>();
            void Put(string name, byte[] bytes)
            {
                File.WriteAllBytes(Path.Combine(outputDirectory, name), bytes);
                written.Add(name);
            }
            foreach (LinkedScript script in result.Scripts.Values.OrderBy(script => script.Name, StringComparer.Ordinal))
                Put(script.Name, script.Bytes);
            foreach (LinkedAsset asset in result.Assets.Values.OrderBy(asset => asset.Name, StringComparer.Ordinal))
                Put(asset.Name, asset.Bytes);
            foreach (LinkedCatalog catalog in result.Catalogs)
            {
                var (aai, alf) = catalog.Write(linkBase.AppendMagic, linkBase.CatalogTitle);
                Put(catalog.CatalogFile, aai);
                Put(catalog.ArchiveFile, alf);
            }
            foreach (string name in written) console.WriteLine($"  {name}");
            // Localization spec §9.4: native AGE has no presenter, so text and fonts stay with the port.
            var presenting = result.Order.Where(mod => mod.Layout.Text.Count > 0 || mod.Layout.Fonts.Count > 0
                                                       || mod.Manifest.Languages.Count > 0).Select(mod => mod.Id).ToList();
            if (presenting.Count > 0)
                console.WriteLine($"note: text/, fonts/ and [[language]] of {string.Join(", ", presenting)} are not exported: "
                                  + "native AGE cannot load them");
            console.WriteLine($"omp export: {result.Order.Count} mods, {written.Count} files -> {Path.GetFullPath(outputDirectory)}");
            return 0;
        }
        finally
        {
            foreach (ModPackage package in opened) package.Dispose();
        }
    }

    internal static (ModLinkResult? Result, IReadOnlyList<ModPackage> Opened) LinkForInspection(
        ModLinkBase linkBase, string? profileRoot, IReadOnlyList<string> extraPackages, bool skipFailed, TextWriter console)
    {
        var report = new ModLinkReport();
        string modsDirectory = profileRoot == null ? "" : Path.Combine(profileRoot, ModLaunch.ModsFolder);
        ModDiscovery discovery = ModLaunch.Discover(profileRoot == null ? Path.Combine(Path.GetTempPath(), "omp-none-" + Guid.NewGuid().ToString("N")) : modsDirectory,
                                                    extraPackages, report);
        foreach (ModReportLine line in report.Lines) console.WriteLine(line);
        if (discovery.FailedToOpen && !skipFailed) return (null, discovery.Packages);
        SelectorStore selectors = profileRoot == null ? new SelectorStore()
            : SelectorStore.Load(Path.Combine(modsDirectory, SelectorStore.FileName));
        return (ModLinker.Link(linkBase, discovery.Packages, discovery.UserOrder, selectors, skipFailed), discovery.Packages);
    }
}
