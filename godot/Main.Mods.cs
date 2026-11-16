using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Mods.Linking;

/// <summary>Startup linking of the profile's mods (mod loader spec §6.1, §6.5, §8, §9).</summary>
public partial class Main
{
    private MountedModLayer? _modLayer;

    /// <summary>
    /// Links the profile's mods and the <c>--mod</c> packages. Returns false when the link fails (startup stops with the
    /// report); on success <paramref name="layer"/> is the layer to mount, or null when no mods are enabled, and
    /// <paramref name="unpackedLua"/> the unpacked mods that ship Lua, by id with their folders (for --lua-reload).
    /// <paramref name="run"/> is the resolver's: a profile file's <c>[mods]</c> table names the packages in place of
    /// the root's <c>mods/</c> folder (launcher spec §5.3), and its mode joins the cache key of a link that drops
    /// failed mods (§5.4).
    /// </summary>
    private static bool LinkMods(IReadOnlyList<string> userArgs, string workingDirectory, SelectedGameProfile selected,
                                 ResolvedGamePersistence persistence, ResolvedRun run, string sys4IniPath,
                                 Sys4AssetCatalog catalog,
                                 IAssetStore baseStore, Ome.Engine.Modding.LazyModRuntime luaRuntime,
                                 out MountedModLayer? layer, out IReadOnlyDictionary<string, string> unpackedLua)
    {
        layer = null;
        unpackedLua = new Dictionary<string, string>();
        ModLaunchOptions options = ModLaunchOptions.Resolve(userArgs, workingDirectory);
        ProfileMods? modsTable = run.Mods;
        string modsDirectory = Path.Combine(persistence.ProfileRoot, ModLaunch.ModsFolder);
        if (options.NoMods && options.ForgetMods.Count == 0)
        {
            GD.Print("[mods] --no-mods: base only");
            return true;
        }
        if (!options.NoMods && options.ExtraPackages.Count == 0 && !ModLaunch.MayHavePackages(modsDirectory, modsTable)
            && options.ForgetMods.Count == 0)
        {
            GD.Print(modsTable == null ? $"[mods] none ({modsDirectory})" : "[mods] none: profile.toml enables no mod");
            return true;
        }

        string profile = selected.Profile.Id;
        OpcodeTable table = OmeRuntimeMetadata.LoadOpcodeTable(selected.Profile.EngineAbiId);
        var (globals, modules, points) = ModToolchainFiles(profile);
        if (modules == null)
            GD.PushWarning("[mods] no repository toolchain: fragments compile without the profile's OMS modules, "
                           + "globals and patch points");
        // The Lua runtime checks each lua/ file; it is created only when a mod ships Lua (Lua scripting spec §14.2).
        // Launcher spec §5.3: this machine's value joins every cache key, so an entry another machine wrote is not used.
        string machineValue = MachineState.CacheValueFor(ProjectSettings.GlobalizePath("user://"), out string? machineWarning);
        if (machineWarning != null) GD.PushWarning($"[mods] {machineWarning}");
        ModLinkBase linkBase = ModLinkBase.ForInstall(profile, sys4IniPath, catalog, baseStore, table,
            OmeRuntimeMetadata.LoadOpcodeContracts(), globals, modules, points) with
        {
            Lua = luaRuntime,
            MachineValue = machineValue,
            ProfileMode = run.ProfileFile?.ModeName,
        };
        ModLaunchResult result = ModLaunch.Run(linkBase, persistence.ProfileRoot, persistence.DiagnosticsDirectory, options,
                                               modsTable);
        foreach (ModReportLine line in result.Report.Lines.Where(line => line.Class == ModReportClass.Note
                                                                          && line.Message.Contains("--forget-mod")))
            GD.Print($"[mods] {line}");
        if (result.Failed)
        {
            GD.PushError($"[mods] the mod stack did not link: {result.Report.Summary()}");
            foreach (ModReportLine line in result.Report.Lines.Where(line => line.Stops)) GD.PushError($"[mods] {line}");
            GD.PushError($"[mods] full report: {result.ReportPath}; --skip-failed-mods drops failing mods, "
                         + "--no-mods starts without any");
            return false;
        }
        layer = result.Layer;
        unpackedLua = result.UnpackedLuaFolders;
        if (layer == null)
        {
            GD.Print("[mods] none enabled");
            return true;
        }
        GD.Print($"[mods] load order: {(result.Stack.Count == 0 ? "(none)" : string.Join(" -> ", result.Stack.Select(mod => $"{mod.Id} {mod.Version}")))}"
                 + (result.Report.Dropped.Count > 0 ? $"; dropped {string.Join(", ", result.Report.Dropped)}" : ""));
        GD.Print($"[mods] {(result.FromCache ? "cache hit" : "linked")} {layer.Key[..16]}: {result.Report.Summary()}; "
                 + $"report {result.ReportPath}");
        foreach (var (name, source) in layer.Manifest.Files)
            GD.Print($"[mods] layer {name}: {source.Kind} from {string.Join(", ", source.Mods)} ({source.Detail})");
        return true;
    }

    /// <summary>The profile's generated globals, OMS modules and patch points from the repository, when this build runs
    /// inside one; exports have none yet (a packaging follow-up).</summary>
    private static (string? Globals, string? Modules, string? Points) ModToolchainFiles(string profile)
    {
        try
        {
            return (Paths.GlobalsJson(profile), Paths.OmsModules(profile), Paths.PatchPointsJson(profile));
        }
        catch (Exception error) when (error is TypeInitializationException or DirectoryNotFoundException)
        {
            return (null, null, null);
        }
    }

    /// <summary>Wraps the numbered-save store so each save carries the stack stamp, which each load compares, and the
    /// mods' state, whose text the scripting native gives at each save (Lua scripting spec §11).</summary>
    private INativeDatStore StampSaves(INativeDatStore inner, ResolvedGamePersistence persistence)
    {
        ModSaveStamp? current = _modLayer == null ? null : ModSaveStamp.Of(_modLayer);
        string saves = persistence.NativePaths.SaveDirectory;
        var stamped = new SidecarNativeDatStore(inner, saves, ModSaveStamp.Suffix,
            current?.ToToml(), (slot, text) => CheckSaveStamp(slot, text, current, persistence.DiagnosticsDirectory));
        return new SidecarNativeDatStore(stamped, saves, Scripting.StateFileSuffix, currentSidecar: null);
    }

    /// <summary>The mod ids the stamp beside a loaded save names, for the scripting native's missing-state report.</summary>
    private static IReadOnlyCollection<string>? StampedMods(IReadOnlyDictionary<string, string> sidecars)
        => sidecars.TryGetValue(ModSaveStamp.Suffix, out string? text)
            ? ModSaveStamp.TryParse(text)?.Mods.Select(mod => mod.Id).ToArray()
            : null;

    private static void CheckSaveStamp(int slot, string? text, ModSaveStamp? current, string diagnosticsDirectory)
    {
        ModSaveStamp? saved = text == null ? null : ModSaveStamp.TryParse(text);
        IReadOnlyList<string> differences = text != null && saved == null
            ? ["its mod stamp is unreadable"]
            : ModSaveStamp.Compare(saved, current);
        if (differences.Count == 0) return;
        string message = $"save slot {slot} was made with a different mod stack: {string.Join("; ", differences)}";
        GD.PushWarning($"[mods] {message}");
        try
        {
            Directory.CreateDirectory(diagnosticsDirectory);
            File.AppendAllText(Path.Combine(diagnosticsDirectory, "mod-save-stamps.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[mods] could not record the stamp warning: {error.Message}");
        }
    }
}
