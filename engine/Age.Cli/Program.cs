using System.Text.Json;
using System.Text.RegularExpressions;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Profiles;
using Age.Engine.Sys4;
using Age.Engine.Vm;

if (args.Length == 0)
{
    Console.WriteLine("usage: run <file> | trace <out.json> | catalog-scan [--profile ID] [--game-root PATH] [--overlay-root PATH] [--allow-bmp-as-agf] [--output report.json]");
    return 1;
}

string? OptionValue(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

string? requestedProfile = OptionValue("--profile");
string conventionalGameRoot = requestedProfile switch
{
    "kamidori" => Path.Combine(Paths.Workspace, "Kamidori"),
    _ => Paths.GameDir,
};
string gameRoot = Path.GetFullPath(OptionValue("--game-root") ?? conventionalGameRoot);
string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
GameCatalogIdentity catalogIdentity = GameCatalogIdentity.ReadSys4IniHeader(sys4Ini);
SelectedGameProfile selectedProfile = GameProfileSelection.Resolve(args, catalogIdentity);
if (selectedProfile.MismatchDiagnostic is { } mismatchDiagnostic)
    Console.Error.WriteLine($"warning: {mismatchDiagnostic}");
var table = OpcodeTableJson.Load(Paths.OpcodesJson, selectedProfile.Profile.EngineAbiId);
// call-script execution: resolves ids -> scripts. Product paths pass this so subroutines run;
// `trace` stays provider-less on purpose (the base-ISA offset oracle).
var catalog = Sys4AssetCatalog.Load(sys4Ini);
AssetLaunchOptions assetOptions = AssetLaunchOptions.Resolve(args, gameRoot);
var assetStore = new Sys4AssetStore(catalog, gameRoot, assetOptions.LooseRoots.ToArray());
var provider = new Sys4ScriptProvider(
    table, catalog, assetStore);
Script ScriptByName(string name) => provider.RequireByName(name);

// Diagnostics flags (see the TraceSetup class below): --trace (text flow), --trace-steps (every op),
// --trace-ops <csv> (only these mnemonics/hex, tagged with their script), --trace-histogram (op +
// call-site execution counts, dumped after the run), --trace-file <path> (write to a file, else console).

if (args.Contains("oms-residue"))
{
    OmsInspect.LiteralResidue(
        catalog.EnumerateScripts().Select(packed => (
            packed.Asset.Name, packed.PackedId, (Func<byte[]>)(() => assetStore.ReadAll(packed.Asset)))),
        table, Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson),
        int.TryParse(OptionValue("--limit"), out int residueLimit) ? residueLimit : 12);
    return 0;
}

// The profile's OMS modules: generated <profile>.globals (globals_build.py --build; absent until generated) and
// <profile>.scripts (the mounted catalog), then the hand-written source modules in oms/<profile>/*.oms (spec §3.2),
// whose library subs and templates the idiomatic level lifts. --no-names decompiles without any of them.
List<Age.Script.Modules.GameModule> OmsModules()
{
    var omsModules = new List<Age.Script.Modules.GameModule>();
    if (args.Contains("--no-names")) return omsModules;
    string profileId = selectedProfile.Profile.Id;
    if (File.Exists(Paths.GlobalsJson(profileId)))
        omsModules.Add(Age.Script.Modules.GameModule.LoadGlobals(profileId, Paths.GlobalsJson(profileId)));
    omsModules.Add(Age.Script.Modules.GameModule.Scripts(profileId,
        catalog.EnumerateScripts().Select(packed => (packed.Asset.Name, packed.PackedId))));
    omsModules.AddRange(Age.Script.Modules.GameModule.LoadSources(Paths.OmsModules(profileId), profileId));
    return omsModules;
}

if (args.Length > 0 && args[0] == "omp")
{
    // Mod loader spec §10: omp build DIR [--output FILE.omp] [--mod DEP …] | omp check [--data-root DIR] [--mod PATH …]
    // [--skip-failed-mods] | omp explain NAME [--data-root DIR] [--mod PATH …]. --data-root is a port profile root
    // (its mods/ folder, load-order.txt and selectors.toml); without it only --mod packages are linked.
    List<string> Paths_(string flag) => args.Select((arg, k) => (arg, k))
        .Where(pair => pair.arg == flag && pair.k + 1 < args.Length).Select(pair => Path.GetFullPath(args[pair.k + 1])).ToList();
    string profileId = selectedProfile.Profile.Id;
    var linkBase = Age.Mods.Linking.ModLinkBase.ForInstall(profileId, sys4Ini, catalog, assetStore, table,
        Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson), Paths.GlobalsJson(profileId), Paths.OmsModules(profileId),
        Paths.PatchPointsJson(profileId));
    string? dataRoot = OptionValue("--data-root") is { } root ? Path.GetFullPath(root) : null;
    string verb = args.Length > 1 ? args[1] : "";
    return verb switch
    {
        "build" when args.Length > 2 => Age.Mods.Tools.OmpCommands.Build(linkBase, args[2], OptionValue("--output"),
            Paths_("--mod"), Console.Out),
        "check" => Age.Mods.Tools.OmpCommands.Check(linkBase, dataRoot, Paths_("--mod"), args.Contains("--skip-failed-mods"),
            Console.Out),
        "explain" when args.Length > 2 => Age.Mods.Tools.OmpCommands.Explain(linkBase, catalog, assetStore, dataRoot,
            Paths_("--mod"), args[2], Console.Out),
        _ => Usage(),
    };
    static int Usage()
    {
        Console.Error.WriteLine("usage: omp build DIR [--output FILE.omp] [--mod DEP] | omp check [--data-root DIR] "
                                + "[--mod PATH] [--skip-failed-mods] | omp explain NAME [--data-root DIR] [--mod PATH]");
        return 2;
    }
}

if (args.Contains("oms-compile"))
{
    // Compile one OMS source file to a SYS4 script. A sidecar next to the source (<file>.omslayout.json) is applied
    // unless --no-layout asks for the canonical layout; warnings go to stderr.
    string sourcePath = OptionValue("--source") ?? throw new ArgumentException("--source FILE.oms is required");
    string layoutPath = OptionValue("--layout") ?? Path.ChangeExtension(sourcePath, ".omslayout.json");
    string binaryPath = OptionValue("--output") ?? Path.ChangeExtension(sourcePath, ".BIN");
    Age.Script.Layout.OmsLayout? sidecar = !args.Contains("--no-layout") && File.Exists(layoutPath)
        ? Age.Script.Layout.OmsLayout.FromJson(File.ReadAllText(layoutPath)) : null;
    var compileModules = OmsModules();
    var warnings = new List<Age.Script.Compile.OmsWarning>();
    try
    {
        var unit = Age.Script.Syntax.Parser.ParseScript(File.ReadAllText(sourcePath), sourcePath);
        var compiled = Age.Script.Compile.OmsCompiler.Compile(unit, table.AbiId, table, sidecar,
            new Age.Script.Abi.IntrinsicCatalog(table, Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson)),
            name => compileModules.FirstOrDefault(module => module.Name == name), warnings);
        byte[] bytes = Age.Script.Ir.IrEncoder.Encode(compiled);
        File.WriteAllBytes(binaryPath, bytes);
        foreach (var warning in warnings) Console.Error.WriteLine(warning);
        Console.WriteLine($"oms-compile: {sourcePath} -> {binaryPath} ({bytes.Length} bytes, {compiled.Code.Count} instructions"
                          + $"{(sidecar != null ? ", with sidecar" : "")}, {warnings.Count} warnings)");
        return 0;
    }
    catch (Exception error) when (error is Age.Script.Syntax.OmsSyntaxException or Age.Script.Compile.OmsCompileException
                                      or InvalidDataException)
    {
        foreach (var warning in warnings) Console.Error.WriteLine(warning);
        Console.Error.WriteLine($"error: {error.Message}");
        return 1;
    }
}

if (args.Contains("oms-append"))
{
    // Spec §9.3: compile OMS scripts into a new S4AC append catalog for the selected profile. Each --script is one
    // record, in order; record 0 is the script native op 0x143 runs at INIT2. A script's name is its `script "..."`
    // name, conventionally `$<selector>$NAME.BIN`; record n is packed id (selector << 24) | n.
    int selector = int.TryParse(OptionValue("--selector"), out int chosen) ? chosen
        : throw new ArgumentException("--selector N (1..127, not a mounted selector) is required");
    if (catalog.AppendPacks.ContainsKey(selector))
        throw new ArgumentException($"selector {selector} is already mounted by the install");
    string outputDirectory = OptionValue("--output-dir") ?? throw new ArgumentException("--output-dir DIR is required");
    var sources = args.Select((arg, k) => (arg, k)).Where(pair => pair.arg == "--script" && pair.k + 1 < args.Length)
        .Select(pair => args[pair.k + 1]).ToList();
    if (sources.Count == 0) throw new ArgumentException("at least one --script FILE.oms is required");
    var appendModules = OmsModules();
    var appendIntrinsics = new Age.Script.Abi.IntrinsicCatalog(table, Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson));
    var records = new List<Sys4AppendCatalogWriter.File>();
    // --mod-id ID: the scripts are one mod's catalog; `run NAME` with NAME a script's file stem resolves to its record
    // (mod loader spec §6.2), here at once, since the selector is given.
    string? appendModId = OptionValue("--mod-id");
    var recordNames = sources.Select(Path.GetFileNameWithoutExtension).Select(stem => stem!).ToList();
    try
    {
        foreach (string source in sources)
        {
            var warnings = new List<Age.Script.Compile.OmsWarning>();
            var unit = Age.Script.Syntax.Parser.ParseScript(File.ReadAllText(source), source);
            var scope = appendModId == null ? null : new Age.Script.Compile.OmsResourceScope(appendModId, recordNames, []);
            var compiled = Age.Script.Compile.OmsCompiler.Compile(unit, table.AbiId, table, null, appendIntrinsics,
                name => appendModules.FirstOrDefault(module => module.Name == name), warnings, scope);
            compiled = compiled.ResolveResources(reference =>
                (uint)(selector << 24) | (uint)recordNames.IndexOf(reference[(reference.IndexOf(':') + 1)..]));
            foreach (var warning in warnings) Console.Error.WriteLine(warning);
            records.Add(new Sys4AppendCatalogWriter.File(unit.Name, Age.Script.Ir.IrEncoder.Encode(compiled)));
        }
    }
    catch (Exception error) when (error is Age.Script.Syntax.OmsSyntaxException or Age.Script.Compile.OmsCompileException
                                      or InvalidDataException)
    {
        Console.Error.WriteLine($"error: {error.Message}");
        return 1;
    }
    string archiveName = $"APPEND{selector:00}.ALF";
    string appendMagic = ("S4AC" + catalog.Magic[4..]).PadRight(8);
    var (catalogBytes, archiveBytes) = Sys4AppendCatalogWriter.Write(appendMagic, catalog.Title, selector, archiveName, records);
    Directory.CreateDirectory(outputDirectory);
    File.WriteAllBytes(Path.Combine(outputDirectory, $"APPEND{selector:00}.AAI"), catalogBytes);
    File.WriteAllBytes(Path.Combine(outputDirectory, archiveName), archiveBytes);
    for (int k = 0; k < records.Count; k++)
        Console.WriteLine($"  0x{(selector << 24) | k:x8} {records[k].Name} ({records[k].Bytes.Length} bytes)");
    Console.WriteLine($"oms-append: selector {selector}, {records.Count} scripts -> {outputDirectory}");
    return 0;
}

if (args.Contains("oms-patch"))
{
    // Spec §10: compile fragments (patch units) into transform plans against the mounted catalog's scripts, and apply
    // plans, in the order given, to produce patched scripts as loose overrides.
    if (OptionValue("--fingerprint") is { } fingerprintScript)
    {
        AssetEntry fingerprintEntry = catalog.ResolveName(fingerprintScript)
            ?? throw new ArgumentException($"{fingerprintScript} is not in the mounted catalog");
        Console.WriteLine($"patch \"{fingerprintEntry.Name}\" fingerprint "
                          + $"\"{Age.Script.Patch.OmsPatchPlan.FingerprintOf(assetStore.ReadAll(fingerprintEntry))}\"");
        return 0;
    }
    List<string> Values(string flag) => args.Select((arg, k) => (arg, k))
        .Where(pair => pair.arg == flag && pair.k + 1 < args.Length).Select(pair => args[pair.k + 1]).ToList();
    var patchSources = Values("--source");
    var planFiles = Values("--plan");
    if (patchSources.Count == 0 && planFiles.Count == 0)
        throw new ArgumentException("oms-patch needs --source FRAGMENT.oms or --plan PLAN.omspatch.json (or --fingerprint SCRIPT)");
    var patchModules = OmsModules();
    var patchContracts = Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson);
    var patchIntrinsics = new Age.Script.Abi.IntrinsicCatalog(table, patchContracts);
    Age.Script.Text.OmsIdioms? patchIdioms = patchModules.Any(module => module.Source != null)
        ? Age.Script.Text.OmsIdioms.Build(patchModules, table.AbiId, patchIntrinsics) : null;
    var plans = new List<(string Origin, Age.Script.Patch.OmsPatchPlan Plan)>();
    // --mod-id ID --names A,B,ICON.AGF: compile the fragments as part of a mod whose catalog records are those names in
    // order; references to them stay symbolic in the plans (mod loader spec §6.2). --selector N binds them for
    // --output-dir.
    string? patchModId = OptionValue("--mod-id");
    var patchNames = (OptionValue("--names") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
    Age.Script.Compile.OmsResourceScope? patchScope = patchModId == null ? null
        : new Age.Script.Compile.OmsResourceScope(patchModId,
            patchNames.Where(name => !Path.HasExtension(name)), patchNames.Where(Path.HasExtension));
    int? patchSelector = int.TryParse(OptionValue("--selector"), out int boundSelector) ? boundSelector : null;
    string patchPointsPath = Paths.PatchPointsJson(selectedProfile.Profile.Id);
    Age.Script.Patch.OmsPatchPoints? patchPoints = File.Exists(patchPointsPath)
        ? Age.Script.Patch.OmsPatchPoints.Load(patchPointsPath) : null;
    try
    {
        foreach (string source in patchSources)
        {
            var warnings = new List<Age.Script.Compile.OmsWarning>();
            var notes = new List<string>();
            var unit = Age.Script.Syntax.Parser.ParseScript(File.ReadAllText(source), source);
            if (!unit.IsPatch) throw new InvalidDataException($"{source} is not a patch unit (patch \"SCRIPT.BIN\" fingerprint ...)");
            AssetEntry baseEntry = catalog.ResolveName(unit.Name)
                ?? throw new InvalidDataException($"{source}: {unit.Name} is not in the mounted catalog");
            var plan = Age.Script.Patch.OmsPatchCompiler.Compile(unit, assetStore.ReadAll(baseEntry), table, patchContracts,
                patchIntrinsics, patchModules, patchIdioms, warnings, notes, patchPoints, patchScope);
            foreach (var warning in warnings) Console.Error.WriteLine(warning);
            foreach (string note in notes) Console.WriteLine($"  note: {note}");
            string planPath = OptionValue("--plan-dir") is { } planDirectory
                ? Path.Combine(planDirectory, Path.GetFileNameWithoutExtension(source) + ".omspatch.json")
                : Path.ChangeExtension(source, ".omspatch.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(planPath))!);
            File.WriteAllText(planPath, plan.ToJson());
            Console.WriteLine($"oms-patch: {source} -> {planPath} ({plan.Operations.Count} operations, "
                              + $"{plan.Blocks.Sum(block => block.Count)} instructions against {unit.Name})");
            plans.Add((source, plan));
        }
        foreach (string file in planFiles)
            plans.Add((file, Age.Script.Patch.OmsPatchPlan.FromJson(File.ReadAllText(file))));
        if (OptionValue("--output-dir") is { } patchOutput)
        {
            Directory.CreateDirectory(patchOutput);
            foreach (var group in plans.GroupBy(entry => entry.Plan.Script, StringComparer.OrdinalIgnoreCase))
            {
                AssetEntry baseEntry = catalog.ResolveName(group.Key)
                    ?? throw new InvalidDataException($"{group.Key} is not in the mounted catalog");
                var bound = group.Select(entry => entry.Plan.Resources.Count == 0 ? entry.Plan
                    : entry.Plan.ResolveResources(reference =>
                    {
                        string local = reference[(reference.IndexOf(':') + 1)..];
                        int record = patchNames.FindIndex(name => string.Equals(name, local, StringComparison.OrdinalIgnoreCase));
                        return patchSelector is int chosenSelector && record >= 0 && reference.StartsWith(patchModId + ":")
                            ? (uint)(chosenSelector << 24) | (uint)record
                            : throw new InvalidDataException(
                                $"{entry.Origin}: '{reference}' needs --mod-id, --names and --selector to bind it");
                    })).ToList();
                var result = Age.Script.Patch.OmsPatchApplier.Apply(assetStore.ReadAll(baseEntry), baseEntry.Name,
                    bound, table, patchContracts);
                foreach (string interaction in result.Interactions) Console.WriteLine($"  interaction: {interaction}");
                foreach (string note in result.Notes) Console.WriteLine($"  note: {note}");
                string output = Path.Combine(patchOutput, baseEntry.Name);
                File.WriteAllBytes(output, result.Bytes);
                Console.WriteLine($"oms-patch: {group.Count()} plans applied to {baseEntry.Name} -> {output} "
                                  + $"({result.Script.Code.Count} instructions, {result.Bytes.Length} bytes)");
            }
        }
        return 0;
    }
    catch (Exception error) when (error is Age.Script.Syntax.OmsSyntaxException or Age.Script.Compile.OmsCompileException
                                      or Age.Script.Patch.OmsPatchException or InvalidDataException)
    {
        Console.Error.WriteLine($"error: {error.Message}");
        return 1;
    }
}

if (args.Contains("oms-rebuild"))
{
    // Open Maid Script gate 3: canonical rebuild (no sidecar) must be VM-trace-equivalent on the scenes.
    // Scenes default to every SC/SP scene, walked page by page (--halt-at-wait stops at the first prompt);
    // --boot starts them from the profile's data-table bootstrap, itself run on both catalogs.
    var rebuildScenes = OptionValue("--scene") is { } onlyScene
        ? new List<string> { onlyScene }
        : provider.ScriptNames.Where(n => Regex.IsMatch(n, @"^S[CP]\d{4}\.BIN$")).OrderBy(n => n, StringComparer.Ordinal).ToList();
    IReadOnlyList<string> bootScripts = [];
    if (args.Contains("--boot"))
    {
        if (selectedProfile.Profile.DirectSceneDiagnostics is not { } bootPolicy)
        {
            Console.Error.WriteLine($"profile '{selectedProfile.Profile.Id}' has no proven data-table boot policy");
            return 2;
        }
        bootScripts = bootPolicy.DataTableBootstrapScripts;
    }
    return OmsRebuildCheck.Run(catalog, assetStore, table, Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson),
        OmsModules(), rebuildScenes, bootScripts, new VmOptions(HaltAtWaitForInput: args.Contains("--halt-at-wait")),
        int.TryParse(OptionValue("--limit"), out int rebuildLimit) ? rebuildLimit : 30,
        // The profile's runtime facts, as the Godot runtime passes them (the ADV scene-entry gate among them).
        new VmCompatibilityContext(selectedProfile.Profile.Id, selectedProfile.Profile.EngineAbiId,
            selectedProfile.ProbeMode, selectedProfile.Profile.Runtime.SceneEntryCoroutineGateAddress),
        idiomatic: OptionValue("--level") == "idiomatic",
        // Gate 7 (spec §12): patch every script with an inert probe plan (--patch-probe [stride], default 5).
        patchProbe: !args.Contains("--patch-probe") ? 0
            : int.TryParse(OptionValue("--patch-probe"), out int probeStride) ? probeStride : 5,
        // --dump DIR writes every rebuilt (or probed) script there, for diffing against the original.
        dumpDirectory: OptionValue("--dump"));
}

if (args.Contains("oms-temps"))
{
    OmsInspect.TemporaryResidue(
        catalog.EnumerateScripts().Select(packed => (
            packed.Asset.Name, packed.PackedId, (Func<byte[]>)(() => assetStore.ReadAll(packed.Asset)))),
        table, Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson),
        int.TryParse(OptionValue("--limit"), out int tempLimit) ? tempLimit : 25, OptionValue("--match"),
        OptionValue("--dump"));
    return 0;
}

if (args.Contains("oms-roundtrip") && OptionValue("--level") is { } sourceLevel)
{
    // Open Maid Script gate 2 at the raw level: bytes -> OMS source + sidecar -> bytes.
    Age.Script.OmsSourceRoundTripReport source = Age.Script.OmsSourceRoundTrip.Run(
        catalog.EnumerateScripts().Select(packed => (
            packed.Asset.Name, packed.PackedId, (Func<byte[]>)(() => assetStore.ReadAll(packed.Asset)))),
        table, Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson), sourceLevel, OmsModules(),
        fixedPoint: args.Contains("--fixed-point"));
    if (OptionValue("--output") is { } sourceOutput)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(sourceOutput));
        if (directory != null) Directory.CreateDirectory(directory);
        File.WriteAllText(sourceOutput, JsonSerializer.Serialize(new
        {
            profile_id = selectedProfile.Profile.Id,
            engine_abi_id = table.AbiId,
            level = source.Level,
            scripts = source.Scripts,
            identical = source.Identical,
            passed = source.Passed,
            empty_sidecars = source.EmptySidecars,
            sidecar_field_use = source.SidecarFieldUse,
            source_bytes = source.SourceBytes,
            instructions = source.Instructions,
            raw_statements = source.RawStatements,
            folded_instructions = source.FoldedInstructions,
            fold_rejections = source.FoldRejections,
            temp_overrides = source.TempOverrides,
            library_subs = source.LibrarySubs,
            template_lifts = source.TemplateLifts,
            lifted_instructions = source.LiftedInstructions,
            lift_failures = source.LiftFailures,
            failures = source.Failures,
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
    }
    Console.WriteLine(
        $"oms-roundtrip: profile={selectedProfile.Profile.Id} abi={table.AbiId} level={source.Level}"
        + (args.Contains("--fixed-point") ? " fixed-point" : "") + " "
        + $"scripts={source.Scripts} identical={source.Identical} empty-sidecars={source.EmptySidecars} "
        + $"sidecar-fields={string.Join(",", source.SidecarFieldUse.Select(pair => $"{pair.Key}:{pair.Value}"))} "
        + $"source-bytes={source.SourceBytes} instructions={source.Instructions} raw-statements={source.RawStatements} "
        + $"folded={source.FoldedInstructions} fold-rejections={source.FoldRejections} temp-overrides={source.TempOverrides} "
        + (source.Level == Age.Script.OmsSourceRoundTrip.IdiomaticLevel
            ? $"library-subs={source.LibrarySubs} template-lifts={source.TemplateLifts} "
              + $"lift-coverage={string.Join(",", (source.LiftedInstructions ?? new Dictionary<string, long>()).Select(pair => $"{pair.Key}:{pair.Value}({100.0 * pair.Value / Math.Max(1, source.Instructions):0.0}%)"))} "
              + $"lift-failures={source.LiftFailures?.Count ?? 0} " : "")
        + $"failures={source.Failures.Count}");
    foreach (Age.Script.Ir.IrRoundTripFailure failure in source.Failures.Take(20))
        Console.Error.WriteLine($"  0x{failure.PackedId:x8} {failure.Name}: {failure.Error}");
    foreach (Age.Script.Ir.IrRoundTripFailure failure in (source.LiftFailures ?? []).Take(20))
        Console.Error.WriteLine($"  lift abandoned 0x{failure.PackedId:x8} {failure.Name}: {failure.Error}");
    return source.Passed ? 0 : 1;
}

if (args.Contains("oms-decompile"))
{
    // Print one catalog script as OMS source (and its sidecar when non-empty), or write every script to a directory.
    var omsContracts = Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson);
    var omsIntrinsics = new Age.Script.Abi.IntrinsicCatalog(table, omsContracts);
    var omsModules = OmsModules();
    // --level idiomatic lifts the source modules' library subs and templates (spec §11 steps 4 and 5).
    Age.Script.Text.OmsIdioms? omsIdioms = OptionValue("--level") is "idiomatic"
        ? Age.Script.Text.OmsIdioms.Build(omsModules, table.AbiId, omsIntrinsics) : null;
    Age.Script.Text.OmsDecompilation DecompileEntry(PackedAssetEntry packed)
    {
        byte[] scriptBytes = assetStore.ReadAll(packed.Asset);
        // --fixed-point decompiles the canonical rebuild (gate 4's compiler-generated input) instead of the original.
        if (args.Contains("--fixed-point"))
            scriptBytes = Age.Script.OmsSourceRoundTrip.CanonicalRebuild(scriptBytes, packed.Asset.Name, table, omsContracts,
                omsIntrinsics, omsModules, omsIdioms);
        var decoded = Age.Script.Ir.IrDecoder.Decode(scriptBytes, table, omsContracts, packed.Asset.Name);
        Age.Script.Text.OmsDecompilation result = OptionValue("--level") is "raw"
            ? Age.Script.Text.OmsRawDecompiler.Decompile(decoded, table.AbiId)
            : Age.Script.Text.OmsCoreDecompiler.Decompile(decoded, table.AbiId, omsIntrinsics, modules: omsModules,
                idioms: omsIdioms);
        if (result.LiftFailure != null)
            Console.Error.WriteLine($"{packed.Asset.Name}: lifting abandoned, core source written: {result.LiftFailure}");
        return result;
    }
    void WriteDecompilation(Age.Script.Text.OmsDecompilation decompiled, string path)
    {
        File.WriteAllText(path, decompiled.Source);
        if (!decompiled.Layout.IsEmpty)
            File.WriteAllText(Path.ChangeExtension(path, ".omslayout.json"), decompiled.Layout.ToJson());
    }
    if (OptionValue("--output-dir") is { } decompileDirectory)
    {
        Directory.CreateDirectory(decompileDirectory);
        int written = 0;
        foreach (PackedAssetEntry packed in catalog.EnumerateScripts())
        {
            WriteDecompilation(DecompileEntry(packed),
                Path.Combine(decompileDirectory, Path.ChangeExtension(packed.Asset.Name, ".oms")));
            written++;
        }
        Console.WriteLine($"oms-decompile: profile={selectedProfile.Profile.Id} scripts={written} output={decompileDirectory}");
        return 0;
    }
    string scriptName = OptionValue("--script") ?? throw new ArgumentException("--script NAME or --output-dir DIR is required");
    PackedAssetEntry entry = catalog.EnumerateScripts().FirstOrDefault(packed =>
        string.Equals(packed.Asset.Name, scriptName, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"no catalog script named {scriptName}");
    var decompiledScript = DecompileEntry(entry);
    if (OptionValue("--output") is { } decompileOutput)
        WriteDecompilation(decompiledScript, decompileOutput);
    else
    {
        Console.Write(decompiledScript.Source);
        if (!decompiledScript.Layout.IsEmpty) Console.WriteLine("// sidecar:\n" + decompiledScript.Layout.ToJson());
    }
    return 0;
}

if (args.Contains("oms-roundtrip"))
{
    // Open Maid Script gate 1: every catalog script -> intermediate form -> bytes, compared byte for byte.
    var contracts = Age.Script.Abi.OpcodeContracts.Load(Paths.OpcodesJson);
    Age.Script.Ir.IrRoundTripReport roundTrip = Age.Script.Ir.IrRoundTrip.Run(
        catalog.EnumerateScripts().Select(packed => (
            packed.Asset.Name, packed.PackedId, (Func<byte[]>)(() => assetStore.ReadAll(packed.Asset)))),
        table, contracts);
    var roundTripReport = new
    {
        profile_id = selectedProfile.Profile.Id,
        engine_abi_id = table.AbiId,
        scripts = roundTrip.Scripts,
        identical = roundTrip.Identical,
        passed = roundTrip.Passed,
        instructions = roundTrip.Instructions,
        label_operands = roundTrip.LabelOperands,
        literal_code_targets = roundTrip.LiteralCodeTargets,
        data_ref_operands = roundTrip.DataRefOperands,
        data_items = roundTrip.DataItems,
        referenced_raw_items = roundTrip.ReferencedRawItems,
        unreferenced_raw_dwords = roundTrip.UnreferencedRawDwords,
        explicit_table_scripts = roundTrip.ExplicitTableScripts,
        failures = roundTrip.Failures,
    };
    if (OptionValue("--output") is { } roundTripOutput)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(roundTripOutput));
        if (directory != null) Directory.CreateDirectory(directory);
        File.WriteAllText(roundTripOutput, JsonSerializer.Serialize(roundTripReport, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        }));
    }
    Console.WriteLine(
        $"oms-roundtrip: profile={selectedProfile.Profile.Id} abi={table.AbiId} scripts={roundTrip.Scripts} "
        + $"identical={roundTrip.Identical} labels={roundTrip.LabelOperands} "
        + $"literal-code-targets={roundTrip.LiteralCodeTargets} data-refs={roundTrip.DataRefOperands} "
        + $"items={string.Join(",", roundTrip.DataItems.Select(pair => $"{pair.Key}:{pair.Value}"))} "
        + $"referenced-raw={roundTrip.ReferencedRawItems} unreferenced-raw-dwords={roundTrip.UnreferencedRawDwords} "
        + $"explicit-tables={roundTrip.ExplicitTableScripts} failures={roundTrip.Failures.Count}");
    foreach (Age.Script.Ir.IrRoundTripFailure failure in roundTrip.Failures.Take(20))
        Console.Error.WriteLine($"  0x{failure.PackedId:x8} {failure.Name}: {failure.Error}");
    return roundTrip.Passed ? 0 : 1;
}

if (args.Contains("catalog-scan"))
{
    Sys4CorpusScanResult scan = Sys4CorpusScanner.Scan(catalog, provider);
    int[] unsupportedOpcodes = scan.OpcodeOccurrences
        .Where(pair => !OpcodeRuntimeCoverage.TryResolve(table, pair.Key, out var handler)
                       || !handler.IsExecutable)
        .Select(pair => pair.Key)
        .ToArray();
    var unsupportedSites = unsupportedOpcodes.ToDictionary(
        opcode => opcode,
        _ => new List<(Script Script, Instruction Instruction)>());
    foreach (PackedAssetEntry packed in catalog.EnumerateScripts())
    {
        Script? script = provider.GetById(packed.PackedId);
        if (script == null) continue;
        foreach (Instruction instruction in script.Instructions)
            if (unsupportedSites.TryGetValue(instruction.Opcode, out var sites))
                sites.Add((script, instruction));
    }
    var unsupported = unsupportedOpcodes.ToDictionary(opcode => $"0x{opcode:x}", opcode =>
    {
        List<(Script Script, Instruction Instruction)> sites = unsupportedSites[opcode];
        return new
        {
            occurrences = scan.OpcodeOccurrences[opcode],
            label = table.TryGetDefinition(opcode, out OpcodeDefinition definition)
                ? definition.CanonicalLabel : null,
            script_count = sites.Select(site => site.Script.PackedId).Distinct().Count(),
            scripts = sites
                .GroupBy(site => site.Script.PackedId)
                .OrderByDescending(group => group.LongCount())
                .ThenBy(group => group.First().Script.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    packed_id = $"0x{group.Key:x8}",
                    name = group.First().Script.Name,
                    append_selector = (group.Key >> 24) & 0xff,
                    occurrences = group.LongCount(),
                    offsets = group.Select(site => $"0x{site.Instruction.Offset:x}").ToArray(),
                })
                .ToArray(),
            operand_type_signatures = sites
                .GroupBy(site => string.Join(",", site.Instruction.Args.Select(operand => operand.Type)))
                .OrderByDescending(group => group.LongCount())
                .Select(group => new
                {
                    operand_types = group.First().Instruction.Args
                        .Select(operand => operand.Type).ToArray(),
                    occurrences = group.LongCount(),
                })
                .ToArray(),
            representative_sites = sites
                .GroupBy(site => site.Script.PackedId)
                .Select(group => group.OrderBy(site => site.Instruction.Offset).First())
                .OrderBy(site => site.Script.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(site => site.Script.PackedId)
                .Take(8)
                .Select(site => new
                {
                    packed_id = $"0x{site.Script.PackedId:x8}",
                    script = site.Script.Name,
                    offset = $"0x{site.Instruction.Offset:x}",
                    operands = site.Instruction.Args.Select(operand => new
                    {
                        type = operand.Type,
                        value = operand.Value,
                    }).ToArray(),
                })
                .ToArray(),
        };
    });
    var report = new
    {
        profile_id = selectedProfile.Profile.Id,
        engine_abi_id = selectedProfile.Profile.EngineAbiId,
        resolved_abi_layers = table.ResolvedLayers,
        catalog_identity = selectedProfile.DetectedIdentity.Display,
        selection_source = selectedProfile.SourceName,
        identity_matched = selectedProfile.IdentityMatched,
        asset_overlay_roots = assetOptions.OverlayRoots,
        allow_bmp_as_agf = assetOptions.AllowBmpAsAgf,
        script_count = scan.ScriptCount,
        base_script_count = scan.BaseScriptCount,
        append_script_count = scan.AppendScriptCount,
        instruction_count = scan.InstructionCount,
        distinct_opcode_count = scan.OpcodeOccurrences.Count,
        opcode_occurrences = scan.OpcodeOccurrences.ToDictionary(
            pair => $"0x{pair.Key:x}", pair => pair.Value),
        operand_type_occurrences = scan.OperandTypeOccurrences.ToDictionary(
            pair => $"0x{pair.Key:x}",
            pair => pair.Value.Select(position => position.ToDictionary(
                type => $"0x{type.Key:x}", type => type.Value)).ToArray()),
        unsupported_runtime_opcodes = unsupported,
        failures = scan.Failures,
    };
    string? output = OptionValue("--output");
    if (output != null)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (directory != null) Directory.CreateDirectory(directory);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        }));
    }
    Console.WriteLine(
        $"catalog-scan: profile={selectedProfile.Profile.Id} abi={table.AbiId} "
        + $"scripts={scan.ScriptCount} (base={scan.BaseScriptCount}, append={scan.AppendScriptCount}) "
        + $"instructions={scan.InstructionCount} opcodes={scan.OpcodeOccurrences.Count} "
        + $"unsupported={unsupported.Count} failures={scan.Failures.Count}"
        + (output == null ? "" : $" -> {output}"));
    foreach (Sys4CorpusScanFailure failure in scan.Failures.Take(20))
        Console.Error.WriteLine($"  0x{failure.PackedId:x8} {failure.Name}: {failure.Error}");
    return scan.Failures.Count == 0 ? 0 : 1;
}

if (args[0] == "run")
{
    var script = Sys4Loader.Load(args[1], table);
    var runHost = new CaptureHost();
    using var trace = TraceSetup.Build(args, table);
    // Faithful by default: halt at wait-for-input (no player => stop, don't plow past prompts). --plow opts
    // into the old walk-every-page behavior (dialogue coverage). See VmOptions.HaltAtWaitForInput.
    var vm = new VirtualMachine(script, table, runHost, new VmOptions(HaltAtWaitForInput: !args.Contains("--plow")),
                                provider, trace.Sink);
    vm.Run();
    trace.Report();
    Console.WriteLine($"{Path.GetFileName(args[1])}: {vm.Steps} steps, {vm.Emitted.Count} show-text, {vm.CallScriptDispatches} call-scripts (halt: {vm.HaltReason})");
    foreach (var (off, text, scr) in vm.Emitted.Take(30)) Console.WriteLine($"  [{scr} 0x{off:x}] {text}");
    var sources = vm.Emitted.Select(e => e.Script).Distinct().ToList();
    Console.WriteLine($"source scripts ({sources.Count}): {string.Join(", ", sources)}");
    return 0;
}

if (args[0] == "audio")
{
    // audio <SCENE.BIN> — run the scene and dump executed play-bgm/play-voice ops in order,
    // each resolved via ResourceMap (same rule as the Godot host). Diagnostic only.
    var sceneName = args[1];
    var sceneKey = Path.GetFileNameWithoutExtension(sceneName).ToUpperInvariant();
    var res = new ResourceMap(
        catalog, assetStore, Console.Error.WriteLine, assetOptions.AllowBmpAsAgf);
    var host = new AudioTraceHost(res);
    var vm = new VirtualMachine(ScriptByName(sceneName), table, host);
    // optional: seed globals, e.g. `audio SC0000.BIN 0xa57=1` to set Lily's form-A flag
    foreach (var s in args.Skip(2))
    {
        var kv = s.Split('=');
        int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
        long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
        vm.Globals[k] = v;
    }
    vm.Run();
    Console.WriteLine($"{sceneName}: {host.Events.Count} audio ops (halt: {vm.HaltReason})");
    foreach (var (kind, id, resolved) in host.Events)
        Console.WriteLine($"  {kind,-10} 0x{id:x2} -> {resolved}");
    return 0;
}

if (args[0] == "gfx")
{
    // gfx [--boot] <SCENE.BIN> [0xADDR=VAL ...] — run the scene and dump executed texture ops with resolved
    // file + computed geometry. --boot first runs SYSTEM4's state-setup prefix (INITCONFIG/INIT2/INIT,
    // skipping the UI scripts LOGO/OP/TITLE) through a GameSession, so scene-assumed boot state — chiefly
    // INIT2's gfx handle array 0x62455.. — is present. Diagnostic only.
    bool boot = args.Contains("--boot");
    var sceneName = args.First(a => a.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase));
    var sceneKey = Path.GetFileNameWithoutExtension(sceneName).ToUpperInvariant();
    var res = new ResourceMap(
        catalog, assetStore, Console.Error.WriteLine, assetOptions.AllowBmpAsAgf);
    var host = new GfxTraceHost(res);
    var session = new GameSession();
    foreach (var s in args.Where(a => a.Contains('=')))
    {
        var kv = s.Split('=');
        int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
        long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
        session.Seed(k, v);
    }
    if (boot && selectedProfile.Profile.DirectSceneDiagnostics == null)
    {
        Console.Error.WriteLine($"profile '{selectedProfile.Profile.Id}' has no proven direct-scene boot policy");
        return 2;
    }
    if (boot)
        foreach (string b in selectedProfile.Profile.DirectSceneDiagnostics!.DataBootstrapScripts)
        {
            var bs = session.RunScene(ScriptByName(b), table, new CaptureHost(), null, provider);
            Console.WriteLine($"[boot] {b}: {bs.Steps} steps (halt: {bs.Halt})");
        }
    var target = ScriptByName(sceneName);
    // With --boot, run the target like the real engine (call-scripts on) so subroutine-driven setup runs.
    var vm = boot ? new VirtualMachine(target, table, host, new VmOptions(MaxSteps: 20_000_000), provider)
                  : new VirtualMachine(target, table, host);
    foreach (var kv in session.Globals) vm.Globals[kv.Key] = kv.Value;
    foreach (var kv in session.GlobalStrings) vm.GlobalStrings[kv.Key] = kv.Value;
    vm.Run();
    Console.WriteLine($"{sceneName}: {host.Events.Count} texture ops (halt: {vm.HaltReason})");
    foreach (var line in host.Events) Console.WriteLine("  " + line);
    var gfxObjs = vm.Gfx.Objects.OrderBy(o => o.Slot).ToList();
    Console.WriteLine($"  gfx objects: {gfxObjs.Count} -> " +
        string.Join(", ", gfxObjs.Select(o => $"0x{o.Handle:x}=slot{o.Slot}")));
    var vis = vm.Gfx.SnapshotVisibleObjects();
    Console.WriteLine($"  visible objects ({vis.Count}, ascending-handle = z-order):");
    foreach (var v in vis)
        Console.WriteLine($"    h=0x{v.Handle:x} surf=0x{v.SurfaceResId:x} ({res.ResolveTexture(v.SurfaceResId)?.Name ?? "?"}) src=({v.SrcX},{v.SrcY} {v.W}x{v.H}) dst=({v.DstX},{v.DstY})");
    return 0;
}

if (args[0] == "init-image")
{
    // init-image <out.json> <SCRIPT.BIN...> — run data-table scripts in order through one session and
    // record each script's global-bank delta (new or changed cells). Names match catalog records
    // exactly, so append fragments are addressed by their literal `$n$NAME.BIN` record names.
    // tools/init_image.py drives this and projects the deltas through the profile's global registry.
    if (args.Length < 3) { Console.WriteLine("usage: init-image <out.json> <SCRIPT.BIN...>"); return 1; }
    string output = args[1];
    var byName = provider.Catalog.EnumerateScripts()
        .GroupBy(entry => entry.Asset.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last().PackedId, StringComparer.OrdinalIgnoreCase);
    var session = new GameSession();
    var steps = new List<object>();
    var initOptions = new VmOptions(HaltAtWaitForInput: false);
    foreach (string name in args.Skip(2).Where(a => a.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)))
    {
        if (!byName.TryGetValue(name, out long packedId))
        {
            Console.Error.WriteLine($"not in catalog: {name}");
            return 2;
        }
        Script script = provider.GetById(packedId)
                        ?? throw new InvalidOperationException($"not a script: {name}");
        var ints = new Dictionary<int, long>(session.Globals);
        var strings = new Dictionary<int, string>(session.GlobalStrings);
        var result = session.RunScene(script, table, new CaptureHost(), initOptions, provider);
        var intDelta = session.Globals
            .Where(kv => !ints.TryGetValue(kv.Key, out long old) || old != kv.Value)
            .OrderBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        var stringDelta = session.GlobalStrings
            .Where(kv => !strings.TryGetValue(kv.Key, out string? old) || old != kv.Value)
            .OrderBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        steps.Add(new
        {
            script = name,
            packed_id = $"0x{packedId:x}",
            halt = result.Halt,
            steps = result.Steps,
            ints = intDelta,
            strings = stringDelta,
        });
        Console.WriteLine($"  {name,-16} 0x{packedId:x8} {result.Steps,8} steps (halt: {result.Halt}) " +
                          $"{intDelta.Count,7} int / {stringDelta.Count,5} string cells");
    }
    File.WriteAllText(output, JsonSerializer.Serialize(
        new { profile = selectedProfile.Profile.Id, game_root = gameRoot, scripts = steps },
        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"[init-image] {steps.Count} scripts -> {output}");
    return 0;
}

if (args[0] == "play")
{
    // play [--boot] [--show N] <SCENE.BIN...> [0xADDR=VAL ...] — run a sequence of scenes carrying persistent global
    // state across them (optional up-front seeds). --boot first runs the data-table *INIT scripts so scenes
    // see the real skill/item/unit/etc. state. The state substrate for cross-scene flow; headless.
    // The *INIT boot set — all run clean (halt: exit) and populate the game's data tables into globals.
    IReadOnlyList<string> bootScripts =
        selectedProfile.Profile.DirectSceneDiagnostics?.DataTableBootstrapScripts ?? [];
    bool boot = args.Contains("--boot");
    if (boot && selectedProfile.Profile.DirectSceneDiagnostics == null)
    {
        Console.Error.WriteLine($"profile '{selectedProfile.Profile.Id}' has no proven data-table boot policy");
        return 2;
    }
    var userScenes = args.Skip(1).Where(a => a.ToUpperInvariant().EndsWith(".BIN")).ToList();
    if (userScenes.Count == 0) { Console.WriteLine("usage: play [--boot] <SCENE.BIN...> [0xADDR=VAL ...]"); return 1; }
    var scenes = (boot ? bootScripts.Concat(userScenes) : userScenes).ToList();
    // --state <file>: start from a saved snapshot (e.g. a pre-booted state) instead of booting fresh.
    string? StateArg(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    var loadState = StateArg("--state");
    var saveState = StateArg("--save-state");
    var session = loadState != null ? GameSession.FromJson(File.ReadAllText(loadState)) : new GameSession();
    if (loadState != null) Console.WriteLine($"[state] loaded {session.Globals.Count} globals from {loadState}");
    foreach (var s in args.Skip(1).Where(a => a.Contains('=')))
    {
        var kv = s.Split('=');
        int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
        long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
        session.Seed(k, v);
    }
    long totalLines = 0;
    using var trace = TraceSetup.Build(args, table);   // one sink across the sequence (histogram aggregates)
    var playOpts = new VmOptions(HaltAtWaitForInput: !args.Contains("--plow"));   // faithful by default
    foreach (var name in scenes)
    {
        var script = ScriptByName(name);
        var r = session.RunScene(script, table, new CaptureHost(), playOpts, provider, trace.Sink);
        totalLines += r.Emitted.Count;
        Console.WriteLine($"  {name,-14} {r.Emitted.Count,4} lines, {r.Steps,7} steps (halt: {r.Halt})");
        // --show N prints each scene's first N emitted lines.
        foreach (var line in r.Emitted.Take(int.TryParse(StateArg("--show"), out int shown) ? shown : 0))
            Console.WriteLine($"      [{line.Script} 0x{line.Offset:x}] {line.Text}");
    }
    trace.Report();
    Console.WriteLine($"total: {totalLines} lines across {scenes.Count} scene(s); {session.Globals.Count} globals carried");
    if (saveState != null) { File.WriteAllText(saveState, session.ToJson()); Console.WriteLine($"[state] saved -> {saveState}"); }
    return 0;
}

if (args[0] == "sweep")
{
    // sweep [--boot] — run every SC/SP scene through GameSession (each from a fresh or booted-from-snapshot
    // baseline) and report halt distribution + line counts. Validates the VM + state substrate at scale and
    // surfaces how booted real data affects the corpus. Headless.
    var sceneRe = new Regex(@"^S[CP]\d{4}\.BIN$");
    var names = provider.ScriptNames.Where(n => sceneRe.IsMatch(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
    bool boot = args.Contains("--boot");
    // Sweep DEFAULTS to plow (walk every page) — it's the dialogue-coverage oracle. --halt-at-wait opts into
    // the faithful "stop at the first prompt" semantics (VmOptions.HaltAtWaitForInput).
    var sweepOpts = new VmOptions(HaltAtWaitForInput: args.Contains("--halt-at-wait"));
    string? baseline = null;
    if (boot)
    {
        var bootSession = new GameSession();
        DirectSceneDiagnosticPolicy? diagnosticPolicy = selectedProfile.Profile.DirectSceneDiagnostics;
        if (diagnosticPolicy == null)
        {
            Console.Error.WriteLine($"profile '{selectedProfile.Profile.Id}' has no proven data-table boot policy");
            return 2;
        }
        foreach (string s in diagnosticPolicy.DataTableBootstrapScripts)
            bootSession.RunScene(ScriptByName(s), table, new CaptureHost(), null, provider);
        baseline = bootSession.ToJson();
        Console.WriteLine($"[boot] baseline = {bootSession.Globals.Count} globals; running {names.Count} scenes from it.");
    }
    // Optional seeds turn sweep into a story-state explorer: run each scene with AND without the seeds
    // (from the same baseline) and report which scenes' dialogue changes — i.e. what a story flag affects.
    var seeds = new List<(int, long)>();
    foreach (var s in args.Skip(1).Where(a => a.Contains('=')))
    {
        var kv = s.Split('=');
        int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
        long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
        seeds.Add((k, v));
    }
    GameSession Fresh() => baseline != null ? GameSession.FromJson(baseline) : new GameSession();
    int RunLines(string name, bool seeded)
    {
        var session = Fresh();
        if (seeded) foreach (var (k, v) in seeds) session.Seed(k, v);
        return session.RunScene(ScriptByName(name), table, new CaptureHost(), sweepOpts, provider).Emitted.Count;
    }

    if (seeds.Count > 0)
    {
        var changed = new List<string>();
        foreach (var name in names)
        {
            int baseLines = RunLines(name, false), seededLines = RunLines(name, true);
            if (baseLines != seededLines) changed.Add($"{name}: {baseLines} -> {seededLines} lines ({seededLines - baseLines:+#;-#;0})");
        }
        var seedStr = string.Join(" ", seeds.Select(s => $"0x{s.Item1:x}={s.Item2}"));
        Console.WriteLine($"seed [{seedStr}]{(boot ? " (booted)" : "")}: {changed.Count}/{names.Count} scenes change dialogue");
        foreach (var c in changed) Console.WriteLine("  " + c);
        return 0;
    }

    var haltDist = new SortedDictionary<string, int>(StringComparer.Ordinal);
    long totalLines = 0; var anomalies = new List<string>();
    using var trace = TraceSetup.Build(args, table);   // one sink across the corpus (histogram aggregates)
    foreach (var name in names)
    {
        var session = Fresh();
        var r = session.RunScene(ScriptByName(name), table, new CaptureHost(), sweepOpts, provider, trace.Sink);
        var halt = r.Halt ?? "null";
        haltDist[halt] = haltDist.GetValueOrDefault(halt) + 1;
        totalLines += r.Emitted.Count;
        if (halt != "exit") anomalies.Add($"{name}: {r.Emitted.Count} lines, halt={halt}");
    }
    trace.Report();
    Console.WriteLine($"swept {names.Count} scenes{(boot ? " (booted)" : "")}: {totalLines} total lines");
    Console.WriteLine("halt distribution: " + string.Join(", ", haltDist.Select(kv => $"{kv.Key}={kv.Value}")));
    if (anomalies.Count > 0) { Console.WriteLine($"non-exit halts ({anomalies.Count}):"); foreach (var a in anomalies) Console.WriteLine("  " + a); }
    return 0;
}

if (args[0] == "trace")
{
    // --trace-json <path>: single-scene per-op offset trace for the differential oracle (docs/engine-re.md).
    // Emits EVERY instruction offset the scene executes, in order (not just show-text lines), filtered to the
    // scene's own frame so call-script subroutines into other scripts are excluded — matching the Frida engine
    // tracer's per-codebase filter. --boot first runs SYSTEM4's state-setup prefix (INITCONFIG/INIT2/INIT) so
    // the scene sees real boot state, exactly like `gfx --boot`. Offsets are bytecode WORD indices, the same
    // unit the engine emits as (pc-codebase)/4. Observe-only (a step sink) — the sweep path below is untouched.
    int tji = Array.IndexOf(args, "--trace-json");
    if (tji >= 0)
    {
        if (tji + 1 >= args.Length) { Console.WriteLine("usage: trace <SCENE.BIN> [--boot] --trace-json <out.json>"); return 1; }
        var outPath = args[tji + 1];
        var sceneName = args.First(a => a.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase));
        bool boot = args.Contains("--boot");
        var target = ScriptByName(sceneName);
        // --state <file>: start from a captured scene-entry snapshot (Frida global-write log →
        // capture_global_writes.py) — the real engine's full pre-scene state, superseding the partial
        // --boot. Otherwise fresh + optional --boot.
        int si = Array.IndexOf(args, "--state");
        var session = (si >= 0 && si + 1 < args.Length)
            ? GameSession.FromJson(File.ReadAllText(args[si + 1]))
            : new GameSession();
        // optional 0xADDR=VAL seeds — inject pre-scene state by hand (e.g. 0x6c1=1). Applied on top.
        foreach (var s in args.Where(a => a.Contains('=') && a != outPath))
        {
            var kv = s.Split('=');
            int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
            long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
            session.Seed(k, v);
        }
        if (boot && si < 0 && selectedProfile.Profile.DirectSceneDiagnostics == null)
        {
            Console.Error.WriteLine($"profile '{selectedProfile.Profile.Id}' has no proven direct-scene boot policy");
            return 2;
        }
        if (boot && si < 0)   // --state already carries boot state; don't re-run the *INIT prefix
            foreach (string b in selectedProfile.Profile.DirectSceneDiagnostics!.DataBootstrapScripts)
                session.RunScene(ScriptByName(b), table, new CaptureHost(), null, provider);
        var sink = new JsonOffsetTraceSink(target.Name);
        var vm = new VirtualMachine(target, table, new CaptureHost(),
                                    new VmOptions(HaltAtWaitForInput: true, MaxSteps: 20_000_000), provider, sink);
        foreach (var kv in session.Globals) vm.Globals[kv.Key] = kv.Value;
        foreach (var kv in session.GlobalStrings) vm.GlobalStrings[kv.Key] = kv.Value;
        vm.Run();
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(outPath, JsonSerializer.Serialize(
            new { scene = Path.GetFileNameWithoutExtension(sceneName).ToUpperInvariant(), offsets = sink.Offsets },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine($"{sceneName}{(boot ? " (booted)" : "")}: {sink.Offsets.Count} offsets -> {outPath} (halt: {vm.HaltReason})");
        return 0;
    }

    var scene = new Regex(@"^S[CP]\d{4}\.BIN$");
    var trace = new SortedDictionary<string, object>(StringComparer.Ordinal);
    foreach (var name in provider.ScriptNames.Where(n => scene.IsMatch(n)).OrderBy(n => n, StringComparer.Ordinal))
    {
        var vm = new VirtualMachine(ScriptByName(name), table, new CaptureHost());
        vm.Run();
        trace[name] = new { offsets = vm.Emitted.Select(e => e.Offset).ToArray(), halt = vm.HaltReason, steps = vm.Steps };
    }
    File.WriteAllText(args[1], JsonSerializer.Serialize(trace,
        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"trace: {trace.Count} scenes -> {args[1]}");
    return 0;
}
Console.WriteLine("unknown command"); return 1;

// Assembles the diagnostic sink from CLI flags and owns the file writer + the post-run histogram dump.
// Inert (NullTraceSink) unless a --trace* flag is present, so normal runs are untouched (parity).
sealed class TraceSetup : IDisposable
{
    public ITraceSink Sink { get; private init; } = NullTraceSink.Instance;
    private HistogramTraceSink? _hist;
    private TextWriter? _file;              // owned file writer; null => console
    private OpcodeTable? _table;

    public static TraceSetup Build(string[] a, OpcodeTable tbl)
    {
        bool text = a.Contains("--trace");
        bool steps = a.Contains("--trace-steps");
        bool hist = a.Contains("--trace-histogram");
        string? opsCsv = ArgVal(a, "--trace-ops");
        if (!text && !hist && opsCsv == null) return new TraceSetup();   // inert

        int fi = Array.IndexOf(a, "--trace-file");
        TextWriter? file = null;
        if (fi >= 0 && fi + 1 < a.Length)
        {
            var path = a[fi + 1];
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);   // robust: create parent dir
            file = new StreamWriter(path) { AutoFlush = true };
        }
        TextWriter w = file ?? Console.Out;

        HashSet<int>? filter = null;
        if (opsCsv != null)
        {
            filter = new HashSet<int>();
            foreach (var tok in opsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int? op = tok.StartsWith("0x") ? Convert.ToInt32(tok, 16) : tbl.ByLabel(tok);
                if (op is int o) filter.Add(o);
                else Console.Error.WriteLine($"--trace-ops: unknown op '{tok}' (ignored)");
            }
        }

        var sinks = new List<ITraceSink>();
        if (text || filter != null) sinks.Add(new TextTraceSink(w, tbl, steps, filter));
        HistogramTraceSink? h = null;
        if (hist) { h = new HistogramTraceSink(); sinks.Add(h); }

        return new TraceSetup
        {
            Sink = sinks.Count == 1 ? sinks[0] : new CompositeTraceSink(sinks.ToArray()),
            _hist = h, _file = file, _table = tbl,
        };
    }

    /// <summary>After the run, dump the histogram (if enabled). Text/filter output already streamed live.</summary>
    public void Report() => _hist?.WriteReport(_file ?? Console.Out, _table);

    public void Dispose() { _file?.Flush(); _file?.Dispose(); }

    private static string? ArgVal(string[] a, string key)
    { int i = Array.IndexOf(a, key); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
}

sealed class AudioTraceHost : IHost
{
    private readonly ResourceMap _res;
    public List<(string Kind, long Id, string Resolved)> Events { get; } = new();
    public AudioTraceHost(ResourceMap res) { _res = res; }
    public void PlayBgm(long id)                        // BGM: direct name, not the manifest
    {
        var entry = _res.ResolveBgm(id);
        Events.Add(("play-bgm", id, entry != null ? $"{entry.Archive} {entry.Name}" : $"BGM{id:D3}.OGG <missing>"));
    }
    public void RestartBgm(long id, int startMode)
    {
        var entry = _res.ResolveBgm(id);
        string resolved = entry != null ? $"{entry.Archive} {entry.Name}" : $"BGM{id:D3}.OGG <missing>";
        Events.Add((startMode == 0 ? "restart-bgm-once" : "restart-bgm-loop", id, resolved));
    }
    public void StopBgm() => Events.Add(("stop-bgm", 0, ""));
    public void PlayVoice(long id)                      // voice: universal packed catalog id
    {
        var e = _res.ResolveVoice(id);
        Events.Add(("play-voice", id, e == null ? "<unresolved>" : $"{e.Archive} {e.Name}"));
    }
    public void ShowText(int offset, string text) { }
    public void WaitForInput() { }
    public void Sleep(long duration) { }
    public void FrameYield() { }
    public void CreateTexture(int slot, int width, int height) { }
    public void SetTexture(long resourceId, int slot) { }
    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
}

sealed class GfxTraceHost : IHost
{
    private readonly ResourceMap _res;
    private readonly Dictionary<int, string?> _slotAsset = new(); // slot -> resolved AGF name (or null)
    // slot -> dimensions of the currently allocated surface. Slot 0 starts as the engine's primary surface.
    private readonly Dictionary<int, (int W, int H)> _slotDims = new() { { 0, (800, 600) } };
    public List<string> Events { get; } = new();
    public GfxTraceHost(ResourceMap res) { _res = res; }

    public (int Width, int Height) GetTextureSize(int slot)
    {
        var d = _slotDims.TryGetValue(slot, out var v) ? v : (0, 0);
        Events.Add($"get-tex-size slot={slot} -> {d.Item1}x{d.Item2}");
        return d;
    }

    public void SetTexture(long resId, int slot)
    {
        var e = _res.ResolveTexture(resId);
        RgbaImage? image = e != null ? _res.DecodeTexture(e) : null;
        _slotAsset[slot] = e?.Name;
        _slotDims[slot] = image != null ? (image.Width, image.Height) : (0, 0);
        Events.Add($"set-texture slot={slot} res=0x{resId:x} -> {(e?.Name ?? "<unresolved>")}"
                   + (image == null ? " [NO AGF]" : ""));
    }

    public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy)
    {
        _slotAsset.TryGetValue(slot, out var asset);
        Events.Add($"draw-texture slot={slot} src=({sx},{sy} {w}x{h}) dst=({dx},{dy}) "
                   + $"file={(asset ?? "<none>")}");
    }

    public void CreateTexture(int slot, int width, int height)
    {
        _slotDims[slot] = (width, height);
        Events.Add($"create-texture slot={slot} {width}x{height}");
    }
    public void ReleaseSurface(int slot)
    {
        _slotAsset.Remove(slot);
        _slotDims.Remove(slot);
        Events.Add($"release-surface slot={slot}");
    }
    public void ShowText(int offset, string text) { }
    public void WaitForInput() { }
    public void Sleep(long duration) { }
    public void FrameYield() { }
    public void PlayBgm(long id) { }
    public void PlayVoice(long id) { }
}
