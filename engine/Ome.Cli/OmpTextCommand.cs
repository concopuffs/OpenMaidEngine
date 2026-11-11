using System.Text.RegularExpressions;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Localization;
using Ome.Mods.Localization.Import;

/// <summary>
/// <c>omp text</c> (localization spec §13), against the selected profile's install (<c>--game-root</c>):
/// <c>extract</c> writes catalog templates from its scripts; <c>check</c> validates catalogs (packages and a text
/// root) and <c>stats</c> counts what they translate; <c>import-scripts</c> builds a catalog from an install whose
/// scripts were translated in place and writes <c>import-report.json</c> (spec §18.1).
/// </summary>
internal static partial class OmpTextCommand
{
    [GeneratedRegex(@"^[a-z]{2,3}(?:-[a-z0-9]{2,8})*$")]
    private static partial Regex LanguageTag();

    public static int Run(string[] args, string baseRoot, Sys4AssetCatalog baseCatalog, OpcodeTable table, string profileId)
    {
        string verb = args.Length > 2 ? args[2] : "";
        return verb switch
        {
            "import-scripts" => ImportScripts(args, baseRoot, baseCatalog, table),
            "extract" => Extract(args, baseRoot, baseCatalog, table, profileId),
            "check" or "stats" => CheckOrStats(verb == "stats", args, baseRoot, baseCatalog, table, profileId),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: omp text extract --language TAG --output DIR");
        Console.Error.WriteLine("       omp text check [--mod PATH ...] [--data-root DIR] [--text-root DIR] [--language TAG] [--no-scripts]");
        Console.Error.WriteLine("       omp text stats [--mod PATH ...] [--data-root DIR] [--text-root DIR] [--language TAG] [--scripts]");
        Console.Error.WriteLine("       omp text import-scripts --from GAME_ROOT [--overlay-root DIR] [--allow-bmp-as-agf] "
                                + "--output DIR [--language TAG] [--verify] [--compare-archives]");
        return 2;
    }

    /// <summary>The shell's tables, for engine entries (localization spec §11).</summary>
    private static Ome.Mods.Tools.EngineTextKeys EngineKeys { get; } = new(
        ShellStrings.Embedded(ShellStrings.FallbackLanguage)!, ShellStrings.Embedded, ShellStrings.EntryProblem);

    private static List<string> Options(string[] args, string name)
        => args.Select((arg, k) => (arg, k)).Where(pair => pair.arg == name && pair.k + 1 < args.Length)
            .Select(pair => Path.GetFullPath(args[pair.k + 1])).ToList();

    /// <summary>The install's scripts as <c>extract</c>, <c>check</c> and <c>stats</c> read them: the base catalog's
    /// own, before any mod or overlay.</summary>
    private static ScriptTextCorpus Corpus(string baseRoot, Sys4AssetCatalog baseCatalog, OpcodeTable table)
    {
        var store = new Sys4AssetStore(baseCatalog, baseRoot, baseRoot);
        var scanner = new ScriptTextScanner(table, Ome.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson));
        return ScriptTextCorpus.Scan(scanner, baseCatalog.EnumerateScripts().Where(packed => !packed.Asset.IsPlaceholder)
            .Select(packed => (packed.Asset.Name, (Func<byte[]>)(() => store.ReadAll(packed.Asset)))));
    }

    private static int Extract(string[] args, string baseRoot, Sys4AssetCatalog baseCatalog, OpcodeTable table,
                               string profileId)
    {
        string? output = Option(args, "--output");
        string? language = Option(args, "--language");
        if (output == null || language == null) return Usage();
        if (!LanguageTag().IsMatch(language) || language == Ome.Engine.Localization.LanguageTag.Source)
        {
            Console.Error.WriteLine($"error: --language '{language}' is not a lowercase BCP 47 tag other than the source's, "
                                    + "such as en or pt-br");
            return 2;
        }
        ScriptTextCorpus corpus = Corpus(baseRoot, baseCatalog, table);
        return Ome.Mods.Tools.TextCommands.Extract(corpus, language, output, $"the {profileId} install at {baseRoot}",
                                                   Console.Out, EngineKeys);
    }

    private static int CheckOrStats(bool stats, string[] args, string baseRoot, Sys4AssetCatalog baseCatalog,
                                    OpcodeTable table, string profileId)
    {
        List<string> packages = Options(args, "--mod");
        string? dataRoot = Option(args, "--data-root") is { } root ? Path.GetFullPath(root) : null;
        string? textRoot = Option(args, "--text-root") is { } text ? Path.GetFullPath(text) : null;
        if (packages.Count == 0 && dataRoot == null && textRoot == null)
        {
            Console.Error.WriteLine($"error: omp text {(stats ? "stats" : "check")} needs --mod PATH, --data-root DIR or --text-root DIR");
            return 2;
        }
        if (textRoot != null && !Directory.Exists(textRoot))
        {
            Console.Error.WriteLine($"error: --text-root {textRoot}: no such folder");
            return 2;
        }
        var contracts = Ome.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson);
        var store = new Sys4AssetStore(baseCatalog, baseRoot, baseRoot);
        var linkBase = Ome.Mods.Linking.ModLinkBase.ForInstall(profileId, Path.Combine(baseRoot, "SYS4INI.BIN"), baseCatalog,
            store, table, contracts, Paths.GlobalsJson(profileId), Paths.OmsModules(profileId), Paths.PatchPointsJson(profileId));
        ScriptTextCorpus? corpus = stats || !args.Contains("--no-scripts") ? Corpus(baseRoot, baseCatalog, table) : null;
        return Ome.Mods.Tools.TextCommands.Run(stats, linkBase, dataRoot, packages, textRoot, corpus,
            Option(args, "--language"), args.Contains("--scripts"), Console.Out, EngineKeys);
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ImportScripts(string[] args, string baseRoot, Sys4AssetCatalog baseCatalog, OpcodeTable table)
    {
        string? from = Option(args, "--from");
        string? output = Option(args, "--output");
        string language = Option(args, "--language") ?? "en";
        if (from == null || output == null) return Usage();
        if (!LanguageTag().IsMatch(language))
        {
            Console.Error.WriteLine($"error: --language '{language}' is not a lowercase BCP 47 tag such as en or pt-br");
            return 2;
        }
        string translatedRoot = Path.GetFullPath(from);
        string translatedIni = Path.Combine(translatedRoot, "SYS4INI.BIN");
        if (!File.Exists(translatedIni))
        {
            Console.Error.WriteLine($"error: {translatedIni} does not exist");
            return 2;
        }
        // --overlay-root and --allow-bmp-as-agf belong to the translated install here; the base reads its own root.
        AssetLaunchOptions translatedOptions = AssetLaunchOptions.Resolve(args, translatedRoot);
        Sys4AssetCatalog translatedCatalog = Sys4AssetCatalog.Load(translatedIni);
        var translatedStore = new Sys4AssetStore(translatedCatalog, translatedRoot, translatedOptions.LooseRoots.ToArray());
        var baseStore = new Sys4AssetStore(baseCatalog, baseRoot, baseRoot);
        var contracts = Ome.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson);

        var report = new ImportReport
        {
            BaseRoot = baseRoot,
            TranslatedRoot = translatedRoot,
            TranslatedOverlays = translatedOptions.OverlayRoots.ToList(),
        };
        var translatedScripts = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        foreach (PackedAssetEntry packed in translatedCatalog.EnumerateScripts())
            translatedScripts.TryAdd(packed.Asset.Name.ToUpperInvariant(), packed.Asset);
        var baseNames = new HashSet<string>(StringComparer.Ordinal);

        IEnumerable<ScriptTranslationPair> Pairs()
        {
            foreach (PackedAssetEntry packed in baseCatalog.EnumerateScripts())
            {
                string name = packed.Asset.Name.ToUpperInvariant();
                if (!baseNames.Add(name)) continue;
                byte[] baseBytes = baseStore.ReadAll(packed.Asset);
                byte[]? translatedBytes = translatedScripts.TryGetValue(name, out AssetEntry? other)
                    ? translatedStore.ReadAll(other)
                    : null;
                yield return new ScriptTranslationPair(packed.Asset.Name, baseBytes, translatedBytes);
            }
        }

        ScriptImport import = new ScriptTranslationImporter(table, contracts, language).Import(Pairs(), report);
        report.Scripts.TranslatedOnly.AddRange(translatedScripts.Keys.Where(name => !baseNames.Contains(name))
            .Order(StringComparer.Ordinal));
        report.Assets = InstallAssetComparer.Compare(baseCatalog, baseStore, baseRoot, translatedCatalog, translatedStore,
            translatedRoot, translatedOptions.OverlayRoots, args.Contains("--compare-archives"));

        string textDirectory = Path.Combine(Path.GetFullPath(output), "text", language);
        Directory.CreateDirectory(textDirectory);
        // The folder holds only this import's catalogs: files a previous run wrote for scripts that no longer have
        // entries would otherwise stay and be read as part of it.
        foreach (string stale in Directory.EnumerateFiles(textDirectory, "*.toml")) File.Delete(stale);
        string[] header =
        [
            $"Imported by omp text import-scripts from {translatedRoot} against {baseRoot}.",
            "Local test data (localization spec §18.1): the translated text belongs to its translators. Do not commit or ship it.",
        ];
        foreach (var (fileName, file) in import.Files)
            File.WriteAllText(Path.Combine(textDirectory, fileName), TextCatalogFileWriter.Write(file, header));

        if (args.Contains("--verify"))
        {
            var entries = new List<TextEntry>();
            foreach (string path in OrderedCatalogFiles(textDirectory))
                entries.AddRange(TextCatalogFileReader.Read(path, Path.GetRelativePath(output, path)).Entries);
            report.Verify = ImportVerifier.Verify(TextCatalog.Build(language, entries), import.Occurrences);
        }

        string reportPath = Path.Combine(Path.GetFullPath(output), "import-report.json");
        File.WriteAllText(reportPath, report.ToJson());
        PrintSummary(report, textDirectory, reportPath, import.Files.Count);
        return report.Verify is { Mismatches.Count: > 0 } ? 1 : 0;
    }

    /// <summary>The global file first, then the script files by name: the load order the import assumes.</summary>
    private static IEnumerable<string> OrderedCatalogFiles(string directory)
        => Directory.EnumerateFiles(directory, "*.toml")
            .OrderBy(path => Path.GetFileName(path) == ScriptTranslationImporter.GlobalFileName ? 0 : 1)
            .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal);

    private static void PrintSummary(ImportReport report, string textDirectory, string reportPath, int fileCount)
    {
        ScriptCounts s = report.Scripts;
        MessageCounts m = report.Messages;
        ScopeCounts c = report.Scope;
        UnjoinedCounts u = report.Unjoined;
        Console.WriteLine($"import-scripts: {s.Base} base scripts: {s.Identical} identical, {s.Changed} changed "
                          + $"({s.Aligned} aligned, {s.NotAligned.Count} not aligned), {s.BaseOnly.Count} only in the base, "
                          + $"{s.TranslatedOnly.Count} only in the translation, {s.Unreadable.Count} unreadable");
        Console.WriteLine($"  inserted: {report.Alignment.InsertedShowText} show-text, "
                          + $"{report.Alignment.InsertedEndTextLine} end-text-line; "
                          + $"{report.Alignment.TargetDisagreements.Count} jump targets disagree");
        Console.WriteLine($"  messages: {m.Total} ({m.Literal} literal, {m.WithVariables} with variables, "
                          + $"{m.VariableOnly} of them variables only): "
                          + $"{m.Translated} translated, {m.Untranslated} untranslated; {m.WithAuthoredBreaks} with "
                          + $"authored breaks");
        BreakCounts k = report.Breaks;
        Console.WriteLine($"  line breaks: {k.Wraps} wraps, {k.Deliberate} deliberate ({k.MessagesWithDeliberate} "
                          + $"messages), {k.Uncertain} uncertain, {k.KeptByCount} kept by the added-break count; "
                          + $"edges for {k.Edges.Count} script layouts "
                          + $"({k.Edges.Count(e => e.From == "install")} from the whole install), "
                          + $"{k.WiderThanEdge.Count} lines wider than their edge; "
                          + $"{k.AuthoredWithoutDeliberate.Count} messages lose an authored break, "
                          + $"{k.DeliberateWithoutAuthored.Count} gain a deliberate one");
        Console.WriteLine($"  entries: {c.GlobalEntries} global, {c.ScriptEntries} script, {c.SiteEntries} site, "
                          + $"{c.PatternEntries} pattern; messages served at global {c.MessagesAtGlobal}, script "
                          + $"{c.MessagesAtScript}, site {c.MessagesAtSite}, untranslated {c.MessagesUntranslated}");
        Console.WriteLine($"  sources: {c.DistinctSources} distinct, {c.SourcesWithSeveralTranslations} with several "
                          + $"translations, {c.TranslatedSeveralWaysInOneScript.Count} translated several ways in one script");
        Console.WriteLine($"  not joined: {u.Emptied.Count} emptied, {u.AmbiguousBoundary.Count} ambiguous boundaries, "
                          + $"{u.OrphanInsertions.Count} orphan insertions, {u.DamagedStrings.Count} damaged strings, "
                          + $"{u.InsertedVariables.Count} inserted variables, {u.Unrepresentable.Count} unrepresentable");
        Console.WriteLine($"  literals changed outside messages: "
                          + string.Join(", ", report.Literals.ChangedByOpcode.OrderByDescending(p => p.Value)
                              .Select(p => $"{p.Key} {p.Value}"))
                          + $"; {report.Literals.FeedCompares.Count} feed compares, {report.Literals.FeedConcat.Count} "
                          + $"feed concat; stored literals: {report.Literals.UnscopableConflicts.Count} unscopable "
                          + $"conflicts (frame locals), {report.Literals.UntranslatedInInstall.Count} untranslated in "
                          + $"the install that the catalog translates; empty literals filled with text: "
                          + $"{report.Literals.EmptyFolded.Count} folded into the line before, "
                          + $"{report.Literals.EmptyFilled.Count} not carried");
        ComposedCounts cc = report.Composed;
        Console.WriteLine($"  composed strings: {cc.Stores} stores and draws, {cc.Templates} templates "
                          + $"({cc.Untranslated} untranslated), {cc.Entries} pattern entries; {cc.Skipped.Count} walks "
                          + $"gave up, {cc.Unreadable.Count} templates unreadable");
        DamageCounts d = report.Damage;
        Console.WriteLine($"  not translations, kept as source: {d.LostCharacters.Count} strings missing characters "
                          + $"({d.LostSeveral.Count} lost several, {d.ShortenedTranslations} of them kept as "
                          + $"translations), "
                          + $"{d.EmptyWrittenAsSpace.Count} empty strings written as spaces; "
                          + $"{d.ControlCharacters.Count} strings with control characters read leniently");
        Console.WriteLine($"  furigana: {report.Furigana.BaseRuns} base runs, {report.Furigana.TranslatedEmptied} emptied, "
                          + $"{report.Furigana.TranslatedKeptReading} keep a reading");
        if (report.Assets is { } a)
            Console.WriteLine($"  assets: {a.ReplacedImages.Count} replaced images, {a.ChangedOther.Count} other changed, "
                              + $"{a.TranslatedOnly.Count} only in the translation, {a.BaseOnly.Count} only in the base, "
                              + $"{a.NonAssetFiles.Count} non-asset files (archives compared by {a.ArchivesCompared})");
        if (report.Verify is { } v)
            Console.WriteLine($"  verify: {v.CheckedMessages} messages, {v.CheckedDrawLiterals} drawn literals, "
                              + $"{v.CheckedPatterns} patterns, {v.CheckedComposed} composed, "
                              + $"{v.CheckedStoredLiterals} stored literals: "
                              + $"{v.Mismatches.Count} mismatches, {v.StoredLiteralConflicts.Count} stored-literal conflicts, "
                              + $"{v.StoredLiteralsTranslatedAnyway.Count} stored literals translated anyway");
        Console.WriteLine($"  wrote {fileCount} catalog files to {textDirectory} and {reportPath}");
    }
}
