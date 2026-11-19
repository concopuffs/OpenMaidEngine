using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ome.Engine.Model;
using Ome.Engine.Profiles;

namespace Ome.Engine.Sys4;

/// <summary>One of the checks that say an install is of a SYS generation the engine already runs
/// (docs/adding-a-game.md §2).</summary>
public sealed record IntakeCheck(string Name, bool Passed, string Detail);

/// <summary>The install's <c>AGE.EXE</c> as found. The command adds the version resource.</summary>
public sealed record IntakeExecutable(string Name, long Size, string Sha256);

/// <summary>An append catalog (<c>*.AAI</c>) beside the base catalog; <see cref="Problem"/> when it did not mount.</summary>
public sealed record IntakeAppend(string File, int Selector, int Entries, string? Problem);

public sealed record IntakeSetting(string Key, string Value);

/// <summary>A font the game ships in its catalog (<see cref="Sys4CatalogFont"/>), with the catalog file its id
/// names, or an empty name when the id names none.</summary>
public sealed record IntakeFont(int FileId, string File, string FaceName);

/// <summary>An install the intake compared with: a supported game's, or one the caller named.</summary>
public sealed record IntakeComparedInstall(string Name, string Root, string CatalogRevision, string Title);

public sealed record IntakeScriptFailure(string Script, string Error);

public sealed record IntakeScriptCount(string Script, long Occurrences);

/// <summary>An opcode the install's scripts use that the port has no handler for.</summary>
public sealed record IntakeOpcode(
    string Opcode, string Label, long Occurrences, int ScriptCount, IReadOnlyList<IntakeScriptCount> TopScripts);

/// <summary>The opcodes the install's scripts use that a supported game's scripts never do.</summary>
public sealed record IntakeProfileDelta(string ProfileId, string ScriptRevision, IReadOnlyList<string> Opcodes);

/// <summary>What <see cref="InstallIntake"/> found. Opcodes and operand types are written as hexadecimal text.</summary>
public sealed record InstallIntakeReport
{
    public required string Root { get; init; }
    public required string CatalogRevision { get; init; }
    public required string Title { get; init; }
    /// <summary>The profile whose identity the install has, or null: the install has no profile yet.</summary>
    public string? MatchingProfile { get; init; }
    public IntakeExecutable? Executable { get; init; }

    /// <summary>The header's saved-prefix counts, a profile's <c>bankDimensions</c> (sys4-format-notes.md, "SYS4INI
    /// file header"): integers, floats, strings, integer pointers, float pointers, string pointers.</summary>
    public IReadOnlyList<int> SavedPrefix { get; init; } = [];
    /// <summary>The header's full bank sizes, in the same order.</summary>
    public IReadOnlyList<int> BankSizes { get; init; } = [];

    /// <summary>Why the catalog could not be read at all, or null. The scripts are then the loose files only.</summary>
    public string? CatalogProblem { get; init; }
    public IReadOnlyList<string> Archives { get; init; } = [];
    public int Entries { get; init; }
    public int Placeholders { get; init; }
    public IReadOnlyDictionary<string, int> FileTypes { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<IntakeAppend> Appends { get; init; } = [];
    public IReadOnlyList<IntakeSetting> Settings { get; init; } = [];
    public IReadOnlyList<IntakeFont> Fonts { get; init; } = [];
    /// <summary>What the catalog's trailer holds that the reader does not know, or null.</summary>
    public string? TrailerProblem { get; init; }
    public string UnreadTrailerHex { get; init; } = "";
    public string UnreadTrailerText { get; init; } = "";

    public IReadOnlyList<IntakeComparedInstall> ComparedWith { get; init; } = [];
    public IReadOnlyList<string> SettingsKeysNoComparedInstallHas { get; init; } = [];
    public IReadOnlyList<string> FileTypesNoComparedInstallHas { get; init; } = [];

    /// <summary><c>catalog</c>: every script the catalog and its appends list, loose files first as AGE reads them.
    /// <c>loose files</c>: the catalog did not load, so only the scripts in the install's root were read.</summary>
    public required string ScriptSource { get; init; }
    public int Scripts { get; init; }
    public int BaseScripts { get; init; }
    public int AppendScripts { get; init; }
    public long Instructions { get; init; }
    public IReadOnlyDictionary<string, int> ScriptRevisions { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<IntakeScriptFailure> Failures { get; init; } = [];
    public IReadOnlyList<string> UnknownOperandTypes { get; init; } = [];
    public IReadOnlyDictionary<string, long> OpcodeOccurrences { get; init; } = new Dictionary<string, long>();
    /// <summary>For every opcode, one operand-type histogram per operand position: <c>catalog-scan</c>'s census,
    /// which <c>tools/operand_access.py</c> reads from this report while the game has no opcode table to scan with.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, long>>> OperandTypeOccurrences { get; init; }
        = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, long>>>();
    public IReadOnlyList<IntakeOpcode> WithoutHandler { get; init; } = [];
    public IReadOnlyList<IntakeProfileDelta> NotUsedByProfile { get; init; } = [];
    public IReadOnlyList<string> UsedByNoProfile { get; init; } = [];

    public IReadOnlyList<IntakeCheck> Checks { get; init; } = [];
    /// <summary>Every check passed: the install is of a SYS generation the engine already runs.</summary>
    public bool SameGeneration => Checks.Count > 0 && Checks.All(check => check.Passed);
}

/// <summary>
/// A first look at an install that needs no profile, engine build or ABI snapshot (docs/adding-a-game.md, step 1): who
/// the install says it is, whether its catalog reads, whether every script decodes with the whole opcode catalog,
/// which opcodes the port has no handler for, and which settings keys and file types no compared install has. It
/// reads the install and runs nothing.
/// </summary>
public static class InstallIntake
{
    private const int HeaderBytes = 0x12c, SavedPrefixOffset = 0xf0, BankSizesOffset = 0x114;
    private const int TopScriptsShown = 5;

    /// <param name="installRoot">The install: the folder that holds <c>SYS4INI.BIN</c>.</param>
    /// <param name="catalogTable">The whole opcode catalog, not one game's snapshot (<c>OpcodeTableJson.Load</c>
    /// without an ABI): an opcode outside it fails the decode.</param>
    /// <param name="hasHandler">Whether the port runs an opcode (<c>VirtualMachine.Implements</c>).</param>
    /// <param name="knownOperandTypes">The operand type tags the registry names (<c>meta.arg_types</c>).</param>
    /// <param name="compareInstalls">Installs to compare settings keys and file types with, each under a name; one
    /// that is this install, or holds no catalog, is left out.</param>
    public static InstallIntakeReport Run(
        string installRoot,
        OpcodeTable catalogTable,
        Func<int, bool> hasHandler,
        IReadOnlySet<int> knownOperandTypes,
        GameProfileRegistry registry,
        IEnumerable<(string Name, string Root)> compareInstalls)
    {
        ArgumentNullException.ThrowIfNull(catalogTable);
        ArgumentNullException.ThrowIfNull(hasHandler);
        ArgumentNullException.ThrowIfNull(knownOperandTypes);
        ArgumentNullException.ThrowIfNull(registry);
        string root = Path.GetFullPath(installRoot);
        string sys4Ini = Path.Combine(root, "SYS4INI.BIN");
        if (!File.Exists(sys4Ini))
            throw new FileNotFoundException($"no SYS4INI.BIN in {root}: not an install", sys4Ini);

        byte[] catalogBytes = File.ReadAllBytes(sys4Ini);
        GameCatalogIdentity identity = GameCatalogIdentity.ReadSys4IniHeader(sys4Ini);
        bool baseCatalog = catalogBytes.AsSpan().StartsWith("S4IC"u8);

        Sys4AssetCatalog? catalog = null;
        string? catalogProblem = null, trailerProblem = null;
        byte[] unread = [];
        try
        {
            catalog = Sys4AssetCatalog.ParseLeniently(catalogBytes, "SYS4INI.BIN", out unread, out trailerProblem);
        }
        catch (InvalidDataException error)
        {
            catalogProblem = error.Message;
        }

        var appends = new List<IntakeAppend>();
        if (catalog != null && baseCatalog)
            foreach (string appendPath in Directory.EnumerateFiles(root, "*.AAI").Order(StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.GetFileName(appendPath);
                try
                {
                    Sys4AssetCatalog append = Sys4AssetCatalog.Parse(File.ReadAllBytes(appendPath), file);
                    catalog.MountAppend(append);
                    appends.Add(new IntakeAppend(file, append.PackId, append.Files.Count, null));
                }
                catch (InvalidDataException error)
                {
                    appends.Add(new IntakeAppend(file, 0, 0, error.Message));
                }
            }

        ScriptCensus census = catalog != null
            ? ReadScripts(CatalogScripts(catalog, root), catalogTable, hasHandler)
            : ReadScripts(LooseScripts(root), catalogTable, hasHandler);

        IReadOnlyDictionary<string, int> fileTypes = catalog == null ? new Dictionary<string, int>() : FileTypes(catalog);
        IReadOnlyList<IntakeSetting> settings = catalog == null ? []
            : catalog.StartupSettings.Pairs.Select(pair => new IntakeSetting(pair.Key, pair.Value)).ToArray();

        var compared = new List<IntakeComparedInstall>();
        var comparedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var comparedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string otherRoot) in compareInstalls)
        {
            string other = Path.GetFullPath(otherRoot);
            string otherIni = Path.Combine(other, "SYS4INI.BIN");
            if (string.Equals(other, root, StringComparison.OrdinalIgnoreCase) || !File.Exists(otherIni)) continue;
            Sys4AssetCatalog otherCatalog;
            try
            {
                otherCatalog = Sys4AssetCatalog.ParseLeniently(File.ReadAllBytes(otherIni), "SYS4INI.BIN", out _, out _);
            }
            catch (InvalidDataException)
            {
                continue;
            }
            compared.Add(new IntakeComparedInstall(name, other, otherCatalog.Magic.Trim(), otherCatalog.Title));
            comparedKeys.UnionWith(otherCatalog.StartupSettings.Pairs.Select(pair => pair.Key));
            comparedTypes.UnionWith(FileTypes(otherCatalog).Keys);
        }

        var observedByAny = new HashSet<int>();
        var deltas = new List<IntakeProfileDelta>();
        foreach (OpcodeCorpusObservation observation in catalogTable.Observations.Values.OrderBy(o => o.ProfileId, StringComparer.Ordinal))
        {
            observedByAny.UnionWith(observation.Opcodes);
            deltas.Add(new IntakeProfileDelta(observation.ProfileId, observation.ScriptRevision,
                census.Occurrences.Keys.Where(opcode => !observation.Opcodes.Contains(opcode)).Select(Hex).ToArray()));
        }

        string[] unknownTypes = census.OperandTypes.Where(type => !knownOperandTypes.Contains(type)).Order().Select(Hex).ToArray();
        string[] otherRevisions = census.Revisions.Keys.Where(revision => !revision.StartsWith("SYS4", StringComparison.Ordinal)).ToArray();
        IntakeCheck[] checks =
        [
            new("catalog signature", baseCatalog && catalogProblem == null,
                catalogProblem ?? (baseCatalog ? $"{identity.CatalogRevision} reads as an S4IC catalog"
                                               : $"{identity.CatalogRevision} is not an S4IC catalog")),
            new("script revisions", census.Scripts > 0 && otherRevisions.Length == 0,
                census.Scripts == 0 ? "no script was read"
                : otherRevisions.Length == 0 ? string.Join(", ", census.Revisions.Select(pair => $"{pair.Key} ×{pair.Value}"))
                : $"not SYS4: {string.Join(", ", otherRevisions)}"),
            new("whole-catalog decode", census.Scripts > 0 && census.Failures.Count == 0,
                census.Failures.Count == 0 ? $"{census.Scripts} scripts, {census.Instructions} instructions"
                                           : $"{census.Failures.Count} of {census.Scripts} scripts do not decode"),
            new("operand types", unknownTypes.Length == 0,
                unknownTypes.Length == 0 ? "every operand type is one the registry names"
                                         : $"unknown: {string.Join(", ", unknownTypes)}"),
        ];

        return new InstallIntakeReport
        {
            Root = root,
            CatalogRevision = identity.CatalogRevision,
            Title = identity.Title,
            MatchingProfile = registry.Profiles.FirstOrDefault(profile => profile.Matches(identity))?.Id,
            Executable = Executable(root),
            SavedPrefix = HeaderCounts(catalogBytes, SavedPrefixOffset),
            BankSizes = HeaderCounts(catalogBytes, BankSizesOffset),
            CatalogProblem = catalogProblem,
            Archives = catalog?.Archives ?? [],
            Entries = catalog?.Files.Count ?? 0,
            Placeholders = catalog == null ? 0 : catalog.RawSlots.Count - catalog.Files.Count,
            FileTypes = fileTypes,
            Appends = appends,
            Settings = settings,
            Fonts = catalog == null ? []
                : catalog.Fonts.Select(font => new IntakeFont(
                    font.FileId, catalog.ResolveRaw(font.FileId)?.Name ?? "", font.FaceName)).ToArray(),
            TrailerProblem = trailerProblem,
            UnreadTrailerHex = Convert.ToHexString(unread).ToLowerInvariant(),
            UnreadTrailerText = Printable(unread),
            ComparedWith = compared,
            SettingsKeysNoComparedInstallHas = compared.Count == 0 ? []
                : settings.Select(setting => setting.Key).Where(key => !comparedKeys.Contains(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            FileTypesNoComparedInstallHas = compared.Count == 0 ? []
                : fileTypes.Keys.Where(type => !comparedTypes.Contains(type)).ToArray(),
            ScriptSource = catalog != null ? "catalog" : "loose files",
            Scripts = census.Scripts,
            BaseScripts = census.BaseScripts,
            AppendScripts = census.Scripts - census.BaseScripts,
            Instructions = census.Instructions,
            ScriptRevisions = census.Revisions,
            Failures = census.Failures,
            UnknownOperandTypes = unknownTypes,
            OpcodeOccurrences = census.Occurrences.ToDictionary(pair => Hex(pair.Key), pair => pair.Value),
            OperandTypeOccurrences = census.OperandPositions.ToDictionary(
                pair => Hex(pair.Key),
                pair => (IReadOnlyList<IReadOnlyDictionary<string, long>>)pair.Value
                    .Select(position => (IReadOnlyDictionary<string, long>)position.ToDictionary(
                        type => Hex(type.Key), type => type.Value)).ToArray()),
            WithoutHandler = census.WithoutHandler
                .OrderByDescending(pair => census.Occurrences[pair.Key]).ThenBy(pair => pair.Key)
                .Select(pair => new IntakeOpcode(
                    Hex(pair.Key),
                    catalogTable.TryGetDefinition(pair.Key, out OpcodeDefinition definition) ? definition.CanonicalLabel : "",
                    census.Occurrences[pair.Key],
                    pair.Value.Count,
                    pair.Value.OrderByDescending(script => script.Value).ThenBy(script => script.Key, StringComparer.OrdinalIgnoreCase)
                        .Take(TopScriptsShown).Select(script => new IntakeScriptCount(script.Key, script.Value)).ToArray()))
                .ToArray(),
            NotUsedByProfile = deltas,
            UsedByNoProfile = deltas.Count == 0 ? []
                : census.Occurrences.Keys.Where(opcode => !observedByAny.Contains(opcode)).Select(Hex).ToArray(),
            Checks = checks,
        };
    }

    private sealed record ScriptCensus(
        int Scripts, int BaseScripts, long Instructions,
        SortedDictionary<string, int> Revisions,
        SortedDictionary<int, long> Occurrences,
        SortedSet<int> OperandTypes,
        SortedDictionary<int, SortedDictionary<int, long>[]> OperandPositions,
        SortedDictionary<int, Dictionary<string, long>> WithoutHandler,
        List<IntakeScriptFailure> Failures);

    /// <summary>The catalog's scripts and its appends', each read loose-first as the engine reads it.</summary>
    private static IEnumerable<(string Name, uint PackedId, Func<byte[]> Read)> CatalogScripts(Sys4AssetCatalog catalog, string root)
    {
        var store = new Sys4AssetStore(catalog, root, root);
        foreach (PackedAssetEntry packed in catalog.EnumerateScripts())
            yield return (packed.Asset.Name, checked((uint)packed.PackedId), () => store.ReadAll(packed.Asset));
    }

    /// <summary>The root's own <c>.BIN</c> files that are scripts, for a catalog that does not read.</summary>
    private static IEnumerable<(string Name, uint PackedId, Func<byte[]> Read)> LooseScripts(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!path.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)) continue;
            byte[] data = File.ReadAllBytes(path);
            if (data.AsSpan().StartsWith("SYS"u8))
                yield return (Path.GetFileName(path), 0, () => data);
        }
    }

    private static ScriptCensus ReadScripts(
        IEnumerable<(string Name, uint PackedId, Func<byte[]> Read)> scripts, OpcodeTable table, Func<int, bool> hasHandler)
    {
        int count = 0, baseCount = 0;
        long instructions = 0;
        var revisions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var occurrences = new SortedDictionary<int, long>();
        var operandTypes = new SortedSet<int>();
        var operandPositions = new SortedDictionary<int, SortedDictionary<int, long>[]>();
        var withoutHandler = new SortedDictionary<int, Dictionary<string, long>>();
        var handled = new Dictionary<int, bool>();
        var failures = new List<IntakeScriptFailure>();
        foreach ((string name, uint packedId, Func<byte[]> read) in scripts)
        {
            count++;
            if (packedId >> 24 == 0) baseCount++;
            try
            {
                byte[] data = read();
                string revision = Encoding.ASCII.GetString(data, 0, Math.Min(8, data.Length)).TrimEnd('\0', ' ');
                revisions[revision] = revisions.GetValueOrDefault(revision) + 1;
                Script script = Sys4Loader.Parse(data, table, name, packedId);
                instructions += script.Instructions.Count;
                foreach (Instruction instruction in script.Instructions)
                {
                    int opcode = instruction.Opcode;
                    occurrences[opcode] = occurrences.GetValueOrDefault(opcode) + 1;
                    if (!operandPositions.TryGetValue(opcode, out SortedDictionary<int, long>[]? positions))
                        operandPositions[opcode] = positions = Enumerable.Range(0, instruction.Args.Count)
                            .Select(_ => new SortedDictionary<int, long>()).ToArray();
                    for (int k = 0; k < instruction.Args.Count; k++)
                    {
                        int type = instruction.Args[k].Type;
                        operandTypes.Add(type);
                        positions[k][type] = positions[k].GetValueOrDefault(type) + 1;
                    }
                    if (!handled.TryGetValue(opcode, out bool has)) handled[opcode] = has = hasHandler(opcode);
                    if (has) continue;
                    if (!withoutHandler.TryGetValue(opcode, out Dictionary<string, long>? sites))
                        withoutHandler[opcode] = sites = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    sites[name] = sites.GetValueOrDefault(name) + 1;
                }
            }
            catch (Exception error) when (
                error is IOException or InvalidDataException or ArgumentException or OverflowException)
            {
                failures.Add(new IntakeScriptFailure(name, error.Message));
            }
        }
        return new ScriptCensus(count, baseCount, instructions, revisions, occurrences, operandTypes, operandPositions,
                                withoutHandler, failures);
    }

    private static SortedDictionary<string, int> FileTypes(Sys4AssetCatalog catalog)
    {
        var types = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (PackedAssetEntry entry in catalog.EnumerateAssets())
        {
            string type = Path.GetExtension(entry.Asset.Name).ToUpperInvariant();
            types[type] = types.GetValueOrDefault(type) + 1;
        }
        return types;
    }

    private static int[] HeaderCounts(byte[] catalog, int offset)
        => catalog.Length < HeaderBytes || !catalog.AsSpan().StartsWith("S4IC"u8) ? []
            : Enumerable.Range(0, 6)
                .Select(k => BinaryPrimitives.ReadInt32LittleEndian(catalog.AsSpan(offset + 4 * k, 4))).ToArray();

    private static IntakeExecutable? Executable(string root)
    {
        string? path = Directory.EnumerateFiles(root)
            .FirstOrDefault(file => string.Equals(Path.GetFileName(file), "AGE.EXE", StringComparison.OrdinalIgnoreCase));
        if (path == null) return null;
        byte[] data = File.ReadAllBytes(path);
        return new IntakeExecutable(Path.GetFileName(path), data.LongLength, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
    }

    private static string Hex(int value) => $"0x{value:x}";

    private static string Printable(byte[] bytes)
        => string.Concat(bytes.Select(value => value is >= 0x20 and < 0x7f ? (char)value : '.'));
}
