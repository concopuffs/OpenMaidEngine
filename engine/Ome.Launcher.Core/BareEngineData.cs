using Ome.Engine.Frontend.Shell;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

namespace Ome.Launcher.Core;

/// <summary>
/// The data the engine keeps for a game when it runs without a profile (launcher spec §8.3): Godot's
/// <c>user://games/&lt;storage namespace&gt;/</c>, with its saves, <c>SYS4REG.INI</c> and <c>mods/</c> folder
/// (<c>load-order.txt</c>, <c>selectors.toml</c>), and the one <c>user://OME.INI</c> the bare engine shares across
/// games (shell §5.6).
/// </summary>
public sealed record BareEngineData(string Root, string GodotUserFolder)
{
    /// <param name="godotUserFolder">Godot's user folder for the engine; the machine's own
    /// (<see cref="MachineState.DefaultFolder()"/>) when null.</param>
    public static BareEngineData For(GameProfileManifest game, string? godotUserFolder = null)
    {
        string user = Path.GetFullPath(godotUserFolder ?? MachineState.DefaultFolder());
        return new BareEngineData(Path.Combine(user, "games", game.PersistenceNamespace), user);
    }

    public bool Exists => Directory.Exists(Root);
    public string ModsDirectory => Path.Combine(Root, ModLaunch.ModsFolder);
    public string SharedOmeIni => Path.Combine(GodotUserFolder, PortOptions.FileName);
    public SaveSource Saves(Sys4StartupSettings startup) => SaveSource.FromProfile(Root, startup);
}

/// <summary>What a profile adopted from the bare engine's data will be: its document, and why it starts in Extended
/// mode when it does (each package and its reason), with notes for the candidates that could not be read.</summary>
public sealed record AdoptionPlan(ProfileDocument Document, IReadOnlyDictionary<string, string> NeedsExtended,
                                  IReadOnlyList<string> Notes);

/// <summary>Starting a profile from the bare engine's data (launcher spec §8.3), so the profile runs the stack the bare
/// engine ran.</summary>
public static class Adoption
{
    /// <summary>
    /// The document for a profile adopted from <paramref name="data"/>, from the wizard's <paramref name="document"/>:
    /// <list type="bullet">
    /// <item>the bare engine's <c>mods/</c> folder joins the profile's library folders;</item>
    /// <item><c>order</c> lists what <c>load-order.txt</c> leaves enabled: its ids that are there, in order, then the
    /// folder's other packages by id, the user order the bare engine itself uses (<c>ModLaunch.Discover</c>);</item>
    /// <item>the mode is Extended when the native mode would refuse a package of that stack (<c>ModeCheck</c>).</item>
    /// </list>
    /// </summary>
    public static AdoptionPlan Plan(BareEngineData data, ProfileDocument document)
    {
        var notes = new List<string>();
        var entries = new List<PackageIndexEntry>();
        foreach (string candidate in ModLaunch.Candidates(data.ModsDirectory))
        {
            try
            {
                entries.Add(PackageIndex.Read(candidate));
            }
            catch (Exception error) when (error is ModPackageException or ModManifestException or IOException
                                              or InvalidDataException or UnauthorizedAccessException)
            {
                notes.Add($"{candidate} cannot be read, so it is not enabled: {error.Message}");
            }
        }
        var (listed, disabled) = ModLaunch.ReadLoadOrder(data.ModsDirectory);
        var enabled = entries.Where(entry => !disabled.Contains(entry.Id)).ToList();
        var ids = enabled.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        List<string> order = listed.Where(ids.Contains)
            .Concat(ids.Where(id => !listed.Contains(id)).Order(StringComparer.Ordinal))
            .Distinct().ToList();
        IReadOnlyDictionary<string, string> needsExtended = ModeCheck.NativeRefusals(enabled);

        ProfileMods mods = document.Mods ?? new ProfileMods([], [], SkipFailed: false);
        var folders = mods.Folders.ToList();
        string bareMods = Path.TrimEndingDirectorySeparator(Path.GetFullPath(data.ModsDirectory));
        if (!folders.Any(folder => OmeHome.PathsEqual(folder, bareMods, LauncherEnvironment.Current.Platform)))
            folders.Add(bareMods);
        ProfileDocument adopted = document with
        {
            Mode = needsExtended.Count > 0 ? ProfileMode.Extended : document.Mode,
            Mods = mods with { Folders = folders, Order = order.Select(id => new ProfileModEntry(id, true)).ToList() },
        };
        return new AdoptionPlan(adopted, needsExtended, notes);
    }

    /// <summary>Makes the profile of <paramref name="plan"/> and fills it from <paramref name="data"/>, under its lock:
    /// the saves with the selectors and <c>SYS4REG.INI</c>, and the shared <c>OME.INI</c> as the profile's own.</summary>
    public static ProfileChange Adopt(OmeHome home, LauncherSettings settings, string id, BareEngineData data,
                                      AdoptionPlan plan, Sys4StartupSettings startup, string? elsewhere = null)
        => ProfileStore.Create(home, settings, id, plan.Document, elsewhere, root =>
        {
            ProfileSaves.CopyInLocked(root, startup, data.Saves(startup), withSettings: true, replace: false);
            if (File.Exists(data.SharedOmeIni))
                Folders.CopyFile(data.SharedOmeIni, Path.Combine(root, PortOptions.FileName), overwrite: true);
        });
}
