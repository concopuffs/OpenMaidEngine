using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Profiles;
using Age.Engine.Sys4;
using Age.Mods.Launch;
using Age.Mods.Linking;

/// <summary>Startup linking of the profile's mods (mod loader spec §6.1, §6.5, §8, §9).</summary>
public partial class Main
{
    private MountedModLayer? _modLayer;

    /// <summary>
    /// Links the profile's mods and the <c>--mod</c> packages. Returns false when the link fails (startup stops with the
    /// report); on success <paramref name="layer"/> is the layer to mount, or null when no mods are enabled.
    /// </summary>
    private static bool LinkMods(IReadOnlyList<string> userArgs, string workingDirectory, SelectedGameProfile selected,
                                 ResolvedGamePersistence persistence, string sys4IniPath, Sys4AssetCatalog catalog,
                                 IAssetStore baseStore, out MountedModLayer? layer)
    {
        layer = null;
        ModLaunchOptions options = ModLaunchOptions.Resolve(userArgs, workingDirectory);
        string modsDirectory = Path.Combine(persistence.ProfileRoot, ModLaunch.ModsFolder);
        if (options.NoMods && options.ForgetMods.Count == 0)
        {
            GD.Print("[mods] --no-mods: base only");
            return true;
        }
        if (!options.NoMods && options.ExtraPackages.Count == 0 && ModLaunch.Candidates(modsDirectory).Count == 0
            && options.ForgetMods.Count == 0)
        {
            GD.Print($"[mods] none ({modsDirectory})");
            return true;
        }

        string profile = selected.Profile.Id;
        OpcodeTable table = OmeRuntimeMetadata.LoadOpcodeTable(selected.Profile.EngineAbiId);
        var (globals, modules, points) = ModToolchainFiles(profile);
        if (modules == null)
            GD.PushWarning("[mods] no repository toolchain: fragments compile without the profile's OMS modules, "
                           + "globals and patch points");
        ModLinkBase linkBase = ModLinkBase.ForInstall(profile, sys4IniPath, catalog, baseStore, table,
            OmeRuntimeMetadata.LoadOpcodeContracts(), globals, modules, points);
        ModLaunchResult result = ModLaunch.Run(linkBase, persistence.ProfileRoot, persistence.DiagnosticsDirectory, options);
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

    /// <summary>Wraps the numbered-save store so each save carries the stack stamp and each load compares it.</summary>
    private INativeDatStore StampSaves(INativeDatStore inner, ResolvedGamePersistence persistence)
    {
        ModSaveStamp? current = _modLayer == null ? null : ModSaveStamp.Of(_modLayer);
        return new SidecarNativeDatStore(inner, persistence.NativePaths.SaveDirectory, ModSaveStamp.Suffix,
            current?.ToToml(), (slot, text) => CheckSaveStamp(slot, text, current, persistence.DiagnosticsDirectory));
    }

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
