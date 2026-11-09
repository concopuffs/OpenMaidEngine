using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Packages;
using Ome.Script.Compile;
using Ome.Script.Ir;
using Ome.Script.Modules;
using Ome.Script.Syntax;

namespace Ome.Mods.Linking;

/// <summary>One record of a mod's append catalog: its catalog name, the manifest name scripts reach it by, and its
/// bytes. <see cref="Stub"/> marks the generated record 0 of a mod without scripts.</summary>
public sealed record LinkedRecord(string CatalogName, string Name, bool IsScript, bool Stub, byte[] Bytes);

/// <summary>A mod's append catalog (spec §6.2): its selector, its records in packed-id order, and the
/// <c>APPENDnn.AAI</c>/<c>APPENDnn.ALF</c> pair that holds them.</summary>
public sealed record LinkedCatalog(string ModId, int Selector, IReadOnlyList<LinkedRecord> Records)
{
    public string CatalogFile => $"APPEND{Selector:00}.AAI";
    public string ArchiveFile => $"APPEND{Selector:00}.ALF";

    public uint PackedId(int record) => (uint)(Selector << 24) | (uint)record;

    public (byte[] Catalog, byte[] Archive) Write(string appendMagic, string title)
        => Sys4AppendCatalogWriter.Write(appendMagic, title, Selector, ArchiveFile,
            Records.Select(record => new Sys4AppendCatalogWriter.File(record.CatalogName, record.Bytes)).ToList());
}

/// <summary>
/// Builds a mod's append catalog: its new scripts in <c>[scripts].names</c> order, then its new assets in
/// <c>[assets].names</c> order. Record 0 is what op 0x143 runs at INIT2, so a mod without scripts gets a generated
/// stub there. Records are named <c>$selector$NAME</c> (as <c>oms-append</c> names them), which keeps their basenames
/// apart from the base's and from other mods' for loose lookup.
/// </summary>
public static class ModCatalogBuilder
{
    public const string StubName = "OMPSTUB";

    /// <summary>Ops that mark a script as a scene: text display and the waits for the player (input, hotspot menus,
    /// the choice table, the ADV coroutine yield). Record 0 runs inside INIT2 (native op 0x143), before the title
    /// screen, and native AGE hangs at boot on any of them.</summary>
    private static readonly HashSet<string> SceneOps = new(StringComparer.Ordinal)
    {
        "show-text", "wait-for-input", "arm-hotspot-wait", "wait-hotspot-menu", "wait-hotspot-menu-with-poll-callback",
        "wait-choice-table", "yield-adv-coroutine",
    };

    /// <summary>The scene ops <paramref name="script"/> uses, by name, in first-use order.</summary>
    public static IReadOnlyList<string> SceneOpsUsed(IrScript script, OpcodeTable table)
        => script.Code
            .Select(instruction => table.TryGetDefinition(instruction.Opcode, out OpcodeDefinition definition)
                ? definition.CanonicalLabel : "")
            .Where(SceneOps.Contains)
            .Distinct()
            .ToList();

    /// <summary>The record layout of a mod, before anything is compiled: the names its references resolve to.</summary>
    public static IReadOnlyList<(string Name, bool IsScript)> Layout(ModPackage mod)
    {
        var names = mod.Manifest.ScriptNames.Select(name => (name, true)).ToList();
        if (names.Count == 0) names.Add((StubName, true));
        names.AddRange(mod.Manifest.AssetNames.Select(name => (name, false)));
        return names;
    }

    public static string CatalogName(int selector, string name, bool isScript)
        => $"${selector}${name}" + (isScript ? ".BIN" : "");

    /// <summary>Compiles the mod's scripts with <paramref name="resolve"/> binding every resource reference, and
    /// gathers its new assets. Errors go to <paramref name="report"/>; the catalog is null when any record failed.</summary>
    public static LinkedCatalog? Build(ModPackage mod, int selector, OmsResourceScope scope, ModLinkBase linkBase,
                                       Func<string, uint> resolve, ModLinkReport report)
    {
        var records = new List<LinkedRecord>();
        bool failed = false;
        GameModule? Module(string name) => linkBase.Modules.FirstOrDefault(module => module.Name == name);

        foreach (ModScriptFile script in mod.Layout.Scripts)
        {
            string catalogName = CatalogName(selector, script.Name, true);
            try
            {
                ScriptUnit unit = Parser.ParseScript(mod.ReadText(script.Source), $"{mod.Id}/{script.Source.Path}");
                if (unit.IsPatch)
                {
                    report.Add(ModReportClass.Error, mod.Id, catalogName,
                        $"{script.Source.Path} is a patch unit; fragments go in patches/");
                    failed = true;
                    continue;
                }
                var warnings = new List<OmsWarning>();
                IrScript compiled = OmsCompiler.Compile(unit, linkBase.Table.AbiId, linkBase.Table, null, linkBase.Intrinsics,
                    Module, warnings, scope);
                compiled = compiled.ResolveResources(resolve);
                foreach (OmsWarning warning in warnings) report.Add(ModReportClass.Note, mod.Id, catalogName, warning.ToString());
                if (ReferenceEquals(script, mod.Layout.Scripts[0]) && SceneOpsUsed(compiled, linkBase.Table) is { Count: > 0 } sceneOps)
                    report.Add(ModReportClass.Note, mod.Id, catalogName,
                        $"warning: record 0 runs at boot (INIT2), but scripts/{script.Name}.oms uses "
                        + $"{string.Join(", ", sceneOps)}; native AGE hangs at boot on a record 0 that shows text or "
                        + "waits for the player. Put a short boot script that ends with exit first in [scripts].names "
                        + "(docs/modding/new-scenes.md, \"Record 0 runs at boot\")");
                records.Add(new LinkedRecord(catalogName, script.Name, true, false, IrEncoder.Encode(compiled)));
            }
            catch (Exception error) when (error is OmsSyntaxException or OmsCompileException or InvalidDataException
                                              or KeyNotFoundException)
            {
                report.Add(ModReportClass.Error, mod.Id, catalogName, $"scripts/{script.Name}.oms: {error.Message}");
                failed = true;
            }
        }
        if (mod.Manifest.ScriptNames.Count == 0)
        {
            string stubName = CatalogName(selector, StubName, true);
            ScriptUnit stub = Parser.ParseScript($"oms 1\nscript \"{stubName}\"\n\n    exit\n", "(generated record 0)");
            records.Add(new LinkedRecord(stubName, StubName, true, true,
                IrEncoder.Encode(OmsCompiler.Compile(stub, linkBase.Table.AbiId, linkBase.Table, null, linkBase.Intrinsics))));
        }
        foreach (var (name, isScript) in Layout(mod))
            if (CatalogName(selector, name, isScript).Length >= 64)
            {
                report.Add(ModReportClass.Error, mod.Id, name,
                    $"the record name {CatalogName(selector, name, isScript)} does not fit an append record's 63 bytes");
                failed = true;
            }
        if (failed) return null;
        foreach (string name in mod.Manifest.AssetNames)
        {
            ModAssetFile file = mod.Layout.Assets.First(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            records.Add(new LinkedRecord(CatalogName(selector, name, false), name, false, false, mod.ReadAll(file.File)));
        }
        return failed ? null : new LinkedCatalog(mod.Id, selector, records);
    }
}
