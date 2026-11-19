using System.Text.RegularExpressions;
using Ome.Mods.Data;
using Ome.Mods.Localization;
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

    /// <summary>A handler (Lua scripting spec §6.2): a <c>.lua</c> file under <c>lua/</c>, a colon, and a Lua
    /// identifier.</summary>
    [GeneratedRegex(@"^(?<file>[A-Za-z0-9_\-]+(?:/[A-Za-z0-9_\-]+)*\.lua):(?<function>[A-Za-z_][A-Za-z0-9_]*)$")]
    private static partial Regex HandlerPattern();

    /// <summary>A dotted name: an event, an engine event or a patch point (<c>himegari.experience.award</c>).</summary>
    [GeneratedRegex(@"^[a-z0-9_]+(?:-[a-z0-9_]+)*(?:\.[a-z0-9_]+(?:-[a-z0-9_]+)*)+$")]
    private static partial Regex DottedNamePattern();

    /// <summary>A registry name a mod writes (<c>stage_turn_limit</c>).</summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex GlobalNamePattern();

    public static bool IsValidId(string id) => id.Length <= 64 && IdPattern().IsMatch(id);
    public static bool IsValidScriptName(string name) => ScriptNamePattern().IsMatch(name);
    public static bool IsValidAssetName(string name) => AssetNamePattern().IsMatch(name);
    public static bool IsValidFingerprint(string text) => FingerprintPattern().IsMatch(text);

    public static ModManifest Parse(string text, string origin = FileName)
    {
        TomlTable root;
        try
        {
            root = TomlDocument.Parse(text)
                   ?? throw new ModManifestException(origin, ["the manifest is empty"]);
        }
        catch (TomlException error)
        {
            throw new ModManifestException(origin, [$"not valid TOML: {error.Message}"]);
        }

        var reader = new TableReader(root, "", []);
        int omp = reader.Integer("omp", required: true) ?? 0;
        if (omp is < 1 or > ModManifest.CurrentOmpVersion && reader.Problems.Count == 0)
            reader.Problems.Add($"omp: format version {omp} is not supported (this loader reads 1 to {ModManifest.CurrentOmpVersion})");

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

        // Localization spec §11.1: from format version 2 a package may target no profile; it then holds only
        // presentation (languages, engine text, fonts), and every profile's loader takes it.
        string? profile = null, target = null;
        List<string> revisions = [];
        if (reader.Table("game", required: omp < ModManifest.LanguageOmpVersion) is { } game)
        {
            profile = game.String("profile", required: true) ?? "";
            target = game.String("target", required: true) ?? "";
            if (target == ModManifest.ExtendedTarget && omp < ModManifest.LuaOmpVersion)
                game.Problems.Add($"game.target: \"{ModManifest.ExtendedTarget}\" needs omp = {ModManifest.LuaOmpVersion}");
            else if (target is { Length: > 0 } && target != ModManifest.NativeTarget && target != ModManifest.ExtendedTarget)
                game.Problems.Add($"game.target: '{target}' is not supported; a mod targets \"{ModManifest.NativeTarget}\" "
                                  + $"or \"{ModManifest.ExtendedTarget}\"");
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
        if (profile == null)
            foreach (string table in new[] { "scripts", "assets", "replace" }.Where(root.ContainsKey))
                reader.Problems.Add($"{table}: a package without [game] holds no scripts or assets (it may have "
                                    + "[[language]], text/ with [[engine]] entries only, and fonts/)");
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

        // Localization spec §9.2: format version 2 adds [[language]]; a version 1 manifest that has one is an error,
        // as an unknown key is.
        var languages = new List<LanguageSettings>();
        var languageTables = reader.RawTableArray("language");
        if (languageTables.Count > 0 && omp < ModManifest.LanguageOmpVersion)
            reader.Problems.Add($"language: [[language]] needs omp = {ModManifest.LanguageOmpVersion}");
        else
        {
            var tags = new HashSet<string>(StringComparer.Ordinal);
            for (int k = 0; k < languageTables.Count; k++)
                if (TextCatalogFileReader.ReadLanguage(languageTables[k], $"language[{k}]", reader.Problems, tags)
                    is { } language)
                    languages.Add(language);
        }

        // Lua scripting spec §6.2: format version 3 adds [lua] and [[hooks]].
        ModLuaSettings? lua = null;
        var hooks = new List<ModHook>();
        if ((root.ContainsKey("lua") || root.ContainsKey("hooks")) && omp < ModManifest.LuaOmpVersion)
        {
            reader.Consume("lua");
            reader.Consume("hooks");
            reader.Problems.Add($"lua: [lua] and [[hooks]] need omp = {ModManifest.LuaOmpVersion}");
        }
        else
        {
            if (reader.Table("lua") is { } luaTable) lua = ReadLua(luaTable, profile == null);
            int index = 0;
            foreach (TableReader entry in reader.TableArray("hooks"))
                if (ReadHook(entry, index++) is { } hook) hooks.Add(hook);
            if (profile == null && root.ContainsKey("hooks"))
                reader.Problems.Add("hooks: a package without [game] is a library: a hook names something of one game "
                                    + "(Lua scripting spec §6.4)");
            else if (hooks.Count > 0 && lua == null)
                reader.Problems.Add("hooks: [[hooks]] needs a [lua] table");
            if (lua != null && profile != null && target is { Length: > 0 } && target != ModManifest.ExtendedTarget)
                reader.Problems.Add($"game.target: a mod with Lua targets \"{ModManifest.ExtendedTarget}\"; native AGE "
                                    + "cannot run it");
        }

        // Data patches spec §5.1, §5.2: format version 4 adds [[data]] and [[accepts]].
        var data = new List<ModDataEntry>();
        var accepts = new List<ModKind>();
        if ((root.ContainsKey("data") || root.ContainsKey("accepts")) && omp < ModManifest.DataOmpVersion)
        {
            reader.Consume("data");
            reader.Consume("accepts");
            reader.Problems.Add($"data: [[data]] and [[accepts]] need omp = {ModManifest.DataOmpVersion}");
        }
        else
        {
            int index = 0;
            foreach (TableReader entry in reader.TableArray("data"))
                if (ReadData(entry, index++) is { } file) data.Add(file);
            index = 0;
            foreach (TableReader entry in reader.TableArray("accepts"))
                if (ReadKind(entry, index++) is { } kind) accepts.Add(kind);
            if (profile == null)
            {
                if (root.ContainsKey("data"))
                    reader.Problems.Add("data: a package without [game] holds no data files: a kind is one game's (data "
                                        + "patches spec §5.1)");
                if (root.ContainsKey("accepts"))
                    reader.Problems.Add("accepts: a package without [game] is a library, and declares no kind (data "
                                        + "patches spec §5.2)");
            }
            else if (data.Count > 0 && target is { Length: > 0 } && target != ModManifest.ExtendedTarget)
                reader.Problems.Add($"game.target: a mod with data files targets \"{ModManifest.ExtendedTarget}\": "
                                    + "the port applies them through a mod's Lua, which native AGE cannot run");
            if (accepts.Count > 0 && lua == null)
                reader.Problems.Add("accepts: [[accepts]] needs a [lua] table: a kind's handlers are the owner's Lua");
            if (data.Count > DataBounds.MaxFilesPerPackage)
                reader.Problems.Add($"data: a package names at most {DataBounds.MaxFilesPerPackage} data files; this one "
                                    + $"names {data.Count}");
            foreach (var repeated in data.GroupBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
                reader.Problems.Add($"data: data/{repeated.Key} is named by {repeated.Count()} entries; a file has one");
            foreach (var repeated in accepts.GroupBy(kind => kind.Kind, StringComparer.Ordinal).Where(group => group.Count() > 1))
                reader.Problems.Add($"accepts: the kind '{repeated.Key}' is declared {repeated.Count()} times");
        }

        reader.RejectUnknown();
        if (reader.Problems.Count > 0) throw new ModManifestException(origin, reader.Problems);
        return new ModManifest
        {
            OmpVersion = omp, Languages = languages, Lua = lua, Hooks = hooks, Data = data, Accepts = accepts,
            Id = id, Name = name, Version = version, Authors = authors, Description = description,
            Profile = profile, Target = target, Revisions = revisions, OmsVersion = oms,
            Requires = requires, Conflicts = conflicts, After = after, Before = before,
            ScriptNames = scriptNames, AssetNames = assetNames, ReplacementOrders = replacementOrders,
        };
    }

    /// <summary>A <c>[lua]</c> table. A package without <c>[game]</c> is a library: it may give only <c>api</c>
    /// (Lua scripting spec §6.4).</summary>
    private static ModLuaSettings ReadLua(TableReader table, bool library)
    {
        int api = table.Integer("api", required: true) ?? ModLuaSettings.CurrentApi;
        if (api != ModLuaSettings.CurrentApi)
            table.Problems.Add($"lua.api: maid API version {api} is not supported (this engine provides {ModLuaSettings.CurrentApi})");
        var capabilities = table.Strings("capabilities");
        foreach (string capability in capabilities.Where(c => !ModLuaSettings.KnownCapabilities.Contains(c)))
            table.Problems.Add($"lua.capabilities: '{capability}' is not a capability (version 1 has \"{ModLuaSettings.VmCapability}\")");
        var writes = table.Strings("writes");
        foreach (string name in writes.Where(name => !GlobalNamePattern().IsMatch(name)))
            table.Problems.Add($"lua.writes: '{name}' is not a global's name");
        var calls = table.Strings("calls");
        foreach (string script in calls.Where(script => !IsValidScriptName(
                     script.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase) ? script[..^4] : script)))
            table.Problems.Add($"lua.calls: '{script}' is not a script name");
        foreach (var (key, names) in new[] { ("capabilities", capabilities), ("writes", writes), ("calls", calls) })
            foreach (var duplicate in names.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1))
                table.Problems.Add($"lua.{key}: '{duplicate.Key}' is listed more than once");
        if (library)
            foreach (string key in new[] { "capabilities", "writes", "calls" }.Where(table.Has))
                table.Problems.Add($"lua.{key}: a package without [game] is a library: it has no capabilities, writes "
                                   + "or calls; a mod passes it access (Lua scripting spec §6.4)");
        table.RejectUnknown();
        return new ModLuaSettings(api, capabilities, writes, calls);
    }

    /// <summary>One <c>[[hooks]]</c> entry (Lua scripting spec §6.3): exactly one form, and the keys that form takes.
    /// Whether the form is built yet, and whether its target exists, the link decides.</summary>
    private static ModHook? ReadHook(TableReader entry, int index)
    {
        string? eventName = entry.String("event"), engine = entry.String("engine"), point = entry.String("point");
        string? script = entry.String("script"), watch = entry.String("watch");
        string? fingerprint = entry.String("fingerprint"), when = entry.String("when");
        int? site = entry.Integer("site");
        int priority = entry.Integer("priority") ?? 0;
        bool skip = entry.Boolean("skip") ?? false;
        string handlerText = entry.String("handler", required: true) ?? "";

        var forms = new List<(ModHookForm Form, string Target)>();
        if (eventName != null) forms.Add((ModHookForm.Event, eventName));
        if (engine != null) forms.Add((ModHookForm.Engine, engine));
        if (point != null) forms.Add((ModHookForm.Point, point));
        if (watch != null) forms.Add((ModHookForm.Watch, watch));
        if (script != null) forms.Add((site != null || fingerprint != null ? ModHookForm.Site : ModHookForm.Script, script));
        else if (site != null || fingerprint != null)
            entry.Problems.Add($"{entry.Path}script: a site hook names its script, fingerprint and site");
        string where = entry.Path.TrimEnd('.');
        if (forms.Count != 1)
        {
            entry.Problems.Add(forms.Count == 0
                ? $"{where}: a hook names one of event, engine, point, script or watch"
                : $"{where}: a hook has one form; this one names "
                  + string.Join(" and ", forms.Select(f => f.Form.ToString().ToLowerInvariant())));
            entry.RejectUnknown();
            return null;
        }
        var (form, target) = forms[0];
        switch (form)
        {
            case ModHookForm.Event or ModHookForm.Point when !DottedNamePattern().IsMatch(target):
                entry.Problems.Add($"{entry.Path}{form.ToString().ToLowerInvariant()}: '{target}' is not a dotted name");
                break;
            case ModHookForm.Engine when !target.StartsWith("ome.", StringComparison.Ordinal) || !DottedNamePattern().IsMatch(target):
                entry.Problems.Add($"{entry.Path}engine: '{target}' is not an engine event (ome.…)");
                break;
            case ModHookForm.Site or ModHookForm.Script when !target.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)
                                                             || !IsValidScriptName(target[..^4]):
                entry.Problems.Add($"{entry.Path}script: '{target}' is not a script file name (NAME.BIN)");
                break;
            case ModHookForm.Watch when ModWatchTarget.Parse(target) is not { } watched:
                entry.Problems.Add($"{entry.Path}watch: '{target}' is not a global's name, name[*] for a whole table, or "
                                   + "G[0x…] or GS[0x…] for a cell");
                break;
            case ModHookForm.Watch when ModWatchTarget.Parse(target) is { Bank: "GF" }:
                entry.Problems.Add($"{entry.Path}watch: version 1 watches integer and string cells (G[0x…], GS[0x…]); "
                                   + $"'{target}' is a float cell");
                break;
        }
        if (form == ModHookForm.Site)
        {
            if (fingerprint == null) entry.Problems.Add($"{entry.Path}fingerprint: required for a site hook");
            else if (!IsValidFingerprint(fingerprint))
                entry.Problems.Add($"{entry.Path}fingerprint: '{fingerprint}' is not a sha256:<64 lowercase hex> fingerprint");
            if (site == null) entry.Problems.Add($"{entry.Path}site: required for a site hook");
            else if (site < 0) entry.Problems.Add($"{entry.Path}site: an offset is not negative");
        }
        string[] whens = form switch
        {
            ModHookForm.Site => ["before", "after"],
            ModHookForm.Script => ["enter", "exit"],
            _ => [],
        };
        string choices = string.Join(" or ", whens.Select(w => $"\"{w}\""));
        if (whens.Length > 0 && when == null)
            entry.Problems.Add($"{entry.Path}when: required ({choices})");
        else if (when != null && !whens.Contains(when))
            entry.Problems.Add(whens.Length == 0
                ? $"{entry.Path}when: a {form.ToString().ToLowerInvariant()} hook takes no 'when'"
                : $"{entry.Path}when: '{when}' is not {choices}");
        if (skip && !(form == ModHookForm.Point || form == ModHookForm.Site && when == "before"))
            entry.Problems.Add($"{entry.Path}skip: only a point hook, or a site hook before its instruction, may skip");

        ModHandlerRef handler = new("", "");
        if (handlerText.Length > 0)
        {
            Match match = HandlerPattern().Match(handlerText);
            if (match.Success) handler = new ModHandlerRef(match.Groups["file"].Value, match.Groups["function"].Value);
            else entry.Problems.Add($"{entry.Path}handler: '{handlerText}' is not FILE.lua:FUNCTION (a file under lua/ and "
                                    + "a top-level function of it)");
        }
        entry.RejectUnknown();
        return new ModHook(index, form, target, handler)
        {
            Priority = priority, Fingerprint = fingerprint, Site = site, When = when, Skip = skip,
        };
    }

    /// <summary>One <c>[[data]]</c> entry (data patches spec §5.1). Whether a mod of the stack accepts the kind, and
    /// whether the file parses, the link decides.</summary>
    private static ModDataEntry ReadData(TableReader entry, int index)
    {
        string kind = entry.String("kind", required: true) ?? "";
        string file = entry.String("file", required: true) ?? "";
        int priority = entry.Integer("priority") ?? 0;
        if (kind.Length > 0 && !DottedNamePattern().IsMatch(kind))
            entry.Problems.Add($"{entry.Path}kind: '{kind}' is not a dotted name (a kind is named as an event is: "
                               + "example.lists)");
        if (file.Length > 0 && DataPathProblem(file) is { } why)
            entry.Problems.Add($"{entry.Path}file: '{file}' {why}");
        entry.RejectUnknown();
        return new ModDataEntry(index, kind, file, priority);
    }

    /// <summary>Why a <c>[[data]]</c> file is not a path under <c>data/</c> (plan §4.2), or null.</summary>
    public static string? DataPathProblem(string file)
    {
        if (file.Contains('\\')) return "has a backslash; a path under data/ uses forward slashes";
        if (file.Any(char.IsControl)) return "has a control character";
        if (file.Split('/').Any(part => part is "" or "." or ".."))
            return "is not a path under data/: no part of it is empty, '.' or '..'";
        if (!file.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)) return "is not a .toml file";
        return null;
    }

    /// <summary>One <c>[[accepts]]</c> entry (data patches spec §5.2): the kind and at least one event with its handler.
    /// Whether the owner can subscribe to the event, and whether the function is declared, the link decides.</summary>
    private static ModKind ReadKind(TableReader entry, int index)
    {
        string kind = entry.String("kind", required: true) ?? "";
        string? description = entry.String("description");
        if (kind.Length > 0 && !DottedNamePattern().IsMatch(kind))
            entry.Problems.Add($"{entry.Path}kind: '{kind}' is not a dotted name (a kind is named as an event is: "
                               + "example.lists)");
        // Spec §7: a shape is checked from milestone 3. Taking one now and checking nothing would skip a check unsaid.
        if (entry.Has("shape"))
        {
            entry.Consume("shape");
            entry.Problems.Add($"{entry.Path}shape: shape checks come with data patches milestone 3; this engine does "
                               + "not read a shape yet");
        }
        var at = new List<ModKindEvent>();
        int place = 0;
        foreach (TableReader item in entry.TableArray("at"))
        {
            string eventName = item.String("event", required: true) ?? "";
            string handlerText = item.String("handler", required: true) ?? "";
            if (eventName.Length > 0 && !DottedNamePattern().IsMatch(eventName))
                item.Problems.Add($"{item.Path}event: '{eventName}' is not a dotted name");
            ModHandlerRef handler = new("", "");
            if (handlerText.Length > 0)
            {
                Match match = HandlerPattern().Match(handlerText);
                if (match.Success) handler = new ModHandlerRef(match.Groups["file"].Value, match.Groups["function"].Value);
                else item.Problems.Add($"{item.Path}handler: '{handlerText}' is not FILE.lua:FUNCTION (a file under lua/ "
                                       + "and a top-level function of it)");
            }
            item.RejectUnknown();
            at.Add(new ModKindEvent(place++, eventName, handler));
        }
        if (at.Count == 0 && !entry.Problems.Any(problem => problem.StartsWith($"{entry.Path}at", StringComparison.Ordinal)))
            entry.Problems.Add($"{entry.Path}at: a kind is delivered at one event at least ([[accepts.at]] with event "
                               + "and handler)");
        foreach (var repeated in at.Where(item => item.Event.Length > 0).GroupBy(item => item.Event, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            entry.Problems.Add($"{entry.Path}at: the event {repeated.Key} is named {repeated.Count()} times");
        entry.RejectUnknown();
        return new ModKind(index, kind, description, at);
    }

    private static void CheckReference(TableReader table, string key, string other, string self)
    {
        if (other.Length == 0) return;
        if (!IsValidId(other)) table.Problems.Add($"{table.Path}{key}: '{other}' is not a mod id");
        else if (other == self) table.Problems.Add($"{table.Path}{key}: a mod cannot name itself");
    }
}
