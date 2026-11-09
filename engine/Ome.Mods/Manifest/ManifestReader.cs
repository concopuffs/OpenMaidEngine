using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Ome.Mods.Manifest;

/// <summary>A manifest that does not parse or validate: every problem found, each prefixed with its key path.</summary>
public sealed class ModManifestException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public ModManifestException(string origin, IReadOnlyList<string> problems)
        : base($"{origin}: " + string.Join("; ", problems))
        => Problems = problems;
}

/// <summary>
/// Reads <c>manifest.toml</c> (spec §3.3). Unknown keys and wrongly typed values are errors, so a typo cannot drop
/// a setting silently. The TOML library sits behind this class only.
/// </summary>
public static partial class ManifestReader
{
    public const string FileName = "manifest.toml";
    /// <summary>Longest manifest the reader accepts.</summary>
    public const int MaxBytes = 1 << 20;

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*)*$")]
    private static partial Regex IdPattern();

    /// <summary>A script record name: ASCII letters, digits and underscores, short enough for the catalog's
    /// 64-byte record name once a <c>$nn$</c> prefix and <c>.BIN</c> are added.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_]{1,48}$")]
    private static partial Regex ScriptNamePattern();

    /// <summary>A new asset's name: an ASCII basename with an extension.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,56}\.[A-Za-z0-9]{1,6}$")]
    private static partial Regex AssetNamePattern();

    [GeneratedRegex(@"^sha256:[0-9a-f]{64}$")]
    private static partial Regex FingerprintPattern();

    public static bool IsValidId(string id) => id.Length <= 64 && IdPattern().IsMatch(id);
    public static bool IsValidScriptName(string name) => ScriptNamePattern().IsMatch(name);
    public static bool IsValidAssetName(string name) => AssetNamePattern().IsMatch(name);
    public static bool IsValidFingerprint(string text) => FingerprintPattern().IsMatch(text);

    public static ModManifest Parse(string text, string origin = FileName)
    {
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text)
                   ?? throw new ModManifestException(origin, ["the manifest is empty"]);
        }
        catch (TomlException error)
        {
            throw new ModManifestException(origin, [$"not valid TOML: {error.Message}"]);
        }

        var reader = new TableReader(root, "", []);
        int omp = reader.Integer("omp", required: true) ?? 0;
        if (omp != ModManifest.CurrentOmpVersion && reader.Problems.Count == 0)
            reader.Problems.Add($"omp: format version {omp} is not supported (this loader reads {ModManifest.CurrentOmpVersion})");

        string id = reader.String("id", required: true) ?? "";
        if (id.Length > 0 && !IsValidId(id))
            reader.Problems.Add($"id: '{id}' is not a lowercase, dot-separated id");
        string name = reader.String("name", required: true) ?? "";
        string versionText = reader.String("version", required: true) ?? "";
        SemanticVersion version = new(0, 0, 0);
        if (versionText.Length > 0 && !SemanticVersion.TryParse(versionText, out version))
            reader.Problems.Add($"version: '{versionText}' is not a semantic version (MAJOR.MINOR.PATCH)");
        var authors = reader.Strings("authors");
        string? description = reader.String("description");

        string profile = "", target = "";
        List<string> revisions = [];
        if (reader.Table("game", required: true) is { } game)
        {
            profile = game.String("profile", required: true) ?? "";
            target = game.String("target", required: true) ?? "";
            if (target.Length > 0 && target != "native")
                game.Problems.Add($"game.target: '{target}' is not supported; version 1 mods target \"native\"");
            revisions = game.Strings("revisions");
            foreach (string revision in revisions.Where(revision => !IsValidFingerprint(revision)))
                game.Problems.Add($"game.revisions: '{revision}' is not a sha256:<64 lowercase hex> fingerprint");
            game.RejectUnknown();
        }

        int oms = ModManifest.CurrentOmsVersion;
        if (reader.Table("engine") is { } engine)
        {
            oms = engine.Integer("oms") ?? ModManifest.CurrentOmsVersion;
            if (oms != ModManifest.CurrentOmsVersion)
                engine.Problems.Add($"engine.oms: OMS version {oms} is not supported");
            engine.RejectUnknown();
        }

        var requires = new List<ModRequirement>();
        foreach (TableReader entry in reader.TableArray("requires"))
        {
            string requiredId = entry.String("id", required: true) ?? "";
            CheckReference(entry, "id", requiredId, id);
            VersionRange range = VersionRange.Any;
            if (entry.String("version") is { } rangeText && !VersionRange.TryParse(rangeText, out range, out string? why))
                entry.Problems.Add($"{entry.Path}version: {why}");
            entry.RejectUnknown();
            requires.Add(new ModRequirement(requiredId, range));
        }

        var conflicts = new List<ModConflict>();
        foreach (TableReader entry in reader.TableArray("conflicts"))
        {
            string conflictId = entry.String("id", required: true) ?? "";
            CheckReference(entry, "id", conflictId, id);
            conflicts.Add(new ModConflict(conflictId, entry.String("reason")));
            entry.RejectUnknown();
        }

        List<string> after = [], before = [];
        if (reader.Table("order") is { } order)
        {
            after = order.Strings("after");
            before = order.Strings("before");
            foreach (string other in after.Concat(before)) CheckReference(order, "after/before", other, id);
            order.RejectUnknown();
        }

        List<string> scriptNames = [];
        if (reader.Table("scripts") is { } scripts)
        {
            scriptNames = scripts.Strings("names");
            foreach (string script in scriptNames.Where(script => !IsValidScriptName(script)))
                scripts.Problems.Add($"scripts.names: '{script}' is not a script name (ASCII letters, digits, '_', at most 48)");
            scripts.RejectUnknown();
        }

        List<string> assetNames = [];
        if (reader.Table("assets") is { } assets)
        {
            assetNames = assets.Strings("names");
            foreach (string asset in assetNames.Where(asset => !IsValidAssetName(asset)))
                assets.Problems.Add($"assets.names: '{asset}' is not an ASCII basename with an extension");
            assets.RejectUnknown();
        }
        foreach (var duplicate in scriptNames.Concat(assetNames).GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
            reader.Problems.Add($"scripts.names/assets.names: '{duplicate.Key}' is listed more than once");

        var replacementOrders = new List<ModReplacementOrder>();
        if (reader.Table("replace") is { } replace)
        {
            foreach (string script in replace.Keys())
            {
                if (!script.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase) || script.Contains('/') || script.Contains('\\'))
                {
                    replace.Problems.Add($"replace.\"{script}\": the key must be a script file name (NAME.BIN)");
                    replace.Consume(script);
                    continue;
                }
                if (replace.Table(script, quoted: true) is not { } item) continue;
                string over = item.String("over", required: true) ?? "";
                CheckReference(item, "over", over, id);
                item.RejectUnknown();
                replacementOrders.Add(new ModReplacementOrder(script, over));
            }
            replace.RejectUnknown();
        }

        reader.RejectUnknown();
        if (reader.Problems.Count > 0) throw new ModManifestException(origin, reader.Problems);
        return new ModManifest
        {
            Id = id, Name = name, Version = version, Authors = authors, Description = description,
            Profile = profile, Target = target, Revisions = revisions, OmsVersion = oms,
            Requires = requires, Conflicts = conflicts, After = after, Before = before,
            ScriptNames = scriptNames, AssetNames = assetNames, ReplacementOrders = replacementOrders,
        };
    }

    private static void CheckReference(TableReader table, string key, string other, string self)
    {
        if (other.Length == 0) return;
        if (!IsValidId(other)) table.Problems.Add($"{table.Path}{key}: '{other}' is not a mod id");
        else if (other == self) table.Problems.Add($"{table.Path}{key}: a mod cannot name itself");
    }

    /// <summary>Typed access to one TOML table that records each problem and every key it read.</summary>
    private sealed class TableReader
    {
        private readonly TomlTable _table;
        private readonly HashSet<string> _read = new(StringComparer.Ordinal);
        public string Path { get; }
        public List<string> Problems { get; }

        public TableReader(TomlTable table, string path, List<string> problems)
        {
            _table = table;
            Path = path;
            Problems = problems;
        }

        public IEnumerable<string> Keys() => _table.Keys.ToArray();
        public void Consume(string key) => _read.Add(key);

        private object? Get(string key, bool required)
        {
            _read.Add(key);
            if (_table.TryGetValue(key, out object? value)) return value;
            if (required) Problems.Add($"{Path}{key}: required");
            return null;
        }

        public string? String(string key, bool required = false)
        {
            object? value = Get(key, required);
            if (value is null or string) return (string?)value;
            Problems.Add($"{Path}{key}: expected a string");
            return null;
        }

        public int? Integer(string key, bool required = false)
        {
            object? value = Get(key, required);
            if (value is null) return null;
            if (value is long number and >= int.MinValue and <= int.MaxValue) return (int)number;
            Problems.Add($"{Path}{key}: expected an integer");
            return null;
        }

        public List<string> Strings(string key)
        {
            object? value = Get(key, false);
            if (value is null) return [];
            if (value is TomlArray array && array.All(item => item is string))
                return array.Cast<string>().ToList();
            Problems.Add($"{Path}{key}: expected an array of strings");
            return [];
        }

        public TableReader? Table(string key, bool required = false, bool quoted = false)
        {
            object? value = Get(key, required);
            string child = quoted ? $"{Path}\"{key}\"." : $"{Path}{key}.";
            if (value is null) return null;
            if (value is TomlTable table) return new TableReader(table, child, Problems);
            Problems.Add($"{Path}{(quoted ? $"\"{key}\"" : key)}: expected a table");
            return null;
        }

        public IEnumerable<TableReader> TableArray(string key)
        {
            object? value = Get(key, false);
            if (value is null) return [];
            if (value is TomlTableArray array)
                return array.Select((table, k) => new TableReader(table, $"{Path}{key}[{k}].", Problems)).ToList();
            Problems.Add($"{Path}{key}: expected an array of tables ([[{key}]])");
            return [];
        }

        public void RejectUnknown()
        {
            foreach (string key in _table.Keys.Where(key => !_read.Contains(key)))
                Problems.Add($"{Path}{key}: unknown key");
        }
    }
}
