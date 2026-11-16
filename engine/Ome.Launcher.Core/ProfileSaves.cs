using System.Text.RegularExpressions;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Mods.Linking;

namespace Ome.Launcher.Core;

/// <summary>What the Saves page shows of a save folder (spec §8.4): how many numbered saves it holds, and when the
/// last was written.</summary>
public sealed record SaveCounts(int Saves, DateTime? LastWrittenUtc);

/// <summary>Where saves are copied from (spec §8.3, §8.4): a save folder, the <c>selectors.toml</c> that goes with it,
/// the <c>SYS4REG.INI</c> that may go with it, and the profile root to hold the lock of while copying.</summary>
public sealed record SaveSource(string SaveDirectory, string? Selectors, string? Sys4RegIni, string? ProfileRoot)
{
    /// <summary>A profile of the same game: its save folder by the game's <c>SAVEPATH</c>, and its selectors.</summary>
    public static SaveSource FromProfile(string profileRoot, Sys4StartupSettings startup)
    {
        Sys4PersistencePaths paths = Sys4PersistencePaths.ResolveProfileOverride(startup, profileRoot);
        return new SaveSource(paths.SaveDirectory,
                              SelectorRelease.PathFor(profileRoot),
                              paths.Sys4RegIniPath, Path.GetFullPath(profileRoot));
    }

    /// <summary>The original game's saves, where native AGE keeps them (<c>Sys4PersistencePaths.ResolveNative</c>;
    /// Himegari's are under <c>%LOCALAPPDATA%</c>). Native AGE runs no mod, so there are no selectors.</summary>
    public static SaveSource Native(Sys4StartupSettings startup, string gameRoot, string? localApplicationData = null)
    {
        Sys4PersistencePaths paths = Sys4PersistencePaths.ResolveNative(startup, gameRoot, localApplicationData);
        return new SaveSource(paths.SaveDirectory, null, paths.Sys4RegIniPath, null);
    }
}

/// <summary>What a copy did: the saves it copied and those of the profile it replaced, each named as a unit
/// (<c>SAVE03</c>, <c>SAVE.DAT</c>).</summary>
public sealed record SaveCopyResult(IReadOnlyList<string> Copied, IReadOnlyList<string> Replaced);

/// <summary>A copy of saves that was refused before anything was written: the selectors clash, or saves would be
/// replaced without leave.</summary>
public sealed class SaveCopyRefusedException(string message, IReadOnlyList<string> details) : Exception(message)
{
    public IReadOnlyList<string> Details { get; } = details;
}

/// <summary>
/// A profile's saves (launcher spec §8.3, §8.4). Saves travel with <c>mods/selectors.toml</c>: a save made inside a
/// mod's script holds that mod's selector (mod loader §6.2). Copying into a profile that has selectors of its own
/// merges the two files when they agree and is refused, with the ids named, when they clash. A numbered save moves as
/// a whole: <c>SAVEnn.DAT</c> with its thumbnail and its sidecars (the stack stamp and the mods' state), so a replaced
/// slot keeps none of the old one's files. The launcher copies nothing out of a profile.
/// </summary>
public static partial class ProfileSaves
{
    /// <summary>The save folder of a profile root (<see cref="ProfileStore.SaveDirectory"/>).</summary>
    public static string Directory(Sys4StartupSettings startup, string root) => ProfileStore.SaveDirectory(startup, root);

    public static SaveCounts Count(string saveDirectory)
    {
        if (!System.IO.Directory.Exists(saveDirectory)) return new SaveCounts(0, null);
        var saves = new DirectoryInfo(saveDirectory).EnumerateFiles("*", Folders.Entries)
            .Where(file => NumberedSave().IsMatch(file.Name)).ToList();
        return new SaveCounts(saves.Count, saves.Count == 0 ? null : saves.Max(file => file.LastWriteTimeUtc));
    }

    /// <summary>The units of <paramref name="source"/> that copying into <paramref name="targetSaves"/> would
    /// replace.</summary>
    public static IReadOnlyList<string> WouldReplace(SaveSource source, string targetSaves)
    {
        var target = Units(targetSaves);
        return Units(source.SaveDirectory).Keys.Where(target.ContainsKey).ToList();
    }

    /// <summary>Copies saves into the profile at <paramref name="profileRoot"/>, holding its lock and the source
    /// profile's (so neither runs meanwhile). Refused, before anything is written, when the selectors clash, or when
    /// a save would be replaced and <paramref name="replace"/> is not set.</summary>
    /// <param name="withSettings">Also copies the source's <c>SYS4REG.INI</c> over the profile's.</param>
    public static SaveCopyResult CopyIn(string profileRoot, Sys4StartupSettings startup, SaveSource source,
                                        bool withSettings, bool replace)
    {
        using RunLock target = ProfileStore.Lock(profileRoot);
        using RunLock? sourceLock = source.ProfileRoot is { } sourceRoot ? ProfileStore.Lock(sourceRoot) : null;
        return CopyInLocked(profileRoot, startup, source, withSettings, replace);
    }

    /// <summary><see cref="CopyIn"/> for a caller that holds the profile's lock (a new profile's
    /// <c>fill</c>).</summary>
    public static SaveCopyResult CopyInLocked(string profileRoot, Sys4StartupSettings startup, SaveSource source,
                                              bool withSettings, bool replace)
    {
        string root = Path.GetFullPath(profileRoot);
        string targetSaves = ProfileStore.SaveDirectory(startup, root);
        if (OmeHome.PathsEqual(targetSaves, source.SaveDirectory, LauncherEnvironment.Current.Platform))
            throw new ArgumentException($"{source.SaveDirectory} is the profile's own save folder");

        // Everything that can refuse the copy is checked before the first write.
        string selectorsPath = SelectorRelease.PathFor(root);
        SelectorStore? merged = null;
        if (source.Selectors is { } sourceSelectors && File.Exists(sourceSelectors))
        {
            var clashes = new List<string>();
            merged = SelectorStore.Merge(SelectorStore.Load(selectorsPath), SelectorStore.Load(sourceSelectors), clashes);
            if (merged == null)
                throw new SaveCopyRefusedException(
                    $"the saves are not copied: their {SelectorStore.FileName} and the profile's clash ("
                    + string.Join("; ", clashes) + "), and a save made inside a mod's script needs its own selector",
                    clashes);
        }
        var from = Units(source.SaveDirectory);
        var into = Units(targetSaves);
        var replaced = from.Keys.Where(into.ContainsKey).ToList();
        if (replaced.Count > 0 && !replace)
            throw new SaveCopyRefusedException(
                $"the saves are not copied: the profile already has {string.Join(", ", replaced)}", replaced);
        if (System.IO.Directory.Exists(source.SaveDirectory) && RootSafety.FindLink(source.SaveDirectory) is { } link)
            throw new IOException($"{link} is a link, which is not copied");

        foreach (var (unit, files) in from)
        {
            if (into.TryGetValue(unit, out var old))
                foreach (FileSystemInfo file in old) Folders.Delete(file);
            foreach (FileSystemInfo file in files)
            {
                string target = Path.Combine(targetSaves, file.Name);
                if (file is DirectoryInfo folder) Folders.Copy(folder.FullName, target, _ => false);
                else Folders.CopyFile(file.FullName, target, overwrite: false);
            }
        }
        merged?.Save(selectorsPath);
        if (withSettings && source.Sys4RegIni is { } settings && File.Exists(settings))
            Folders.CopyFile(settings, Path.Combine(root, Sys4RegIniStore.FileName), overwrite: true);
        return new SaveCopyResult(from.Keys.ToList(), replaced);
    }

    /// <summary>A save folder's entries by the unit they move in, sorted by unit.</summary>
    private static SortedDictionary<string, List<FileSystemInfo>> Units(string saveDirectory)
    {
        var units = new SortedDictionary<string, List<FileSystemInfo>>(StringComparer.Ordinal);
        if (!System.IO.Directory.Exists(saveDirectory)) return units;
        foreach (FileSystemInfo entry in new DirectoryInfo(saveDirectory).EnumerateFileSystemInfos("*", Folders.Entries))
        {
            string unit = UnitOf(entry.Name);
            if (!units.TryGetValue(unit, out var files)) units[unit] = files = [];
            files.Add(entry);
        }
        return units;
    }

    /// <summary><c>SAVEnn</c> for a numbered save's files; <c>SAVE.DAT</c> and <c>RT.DAT</c> with their backups and
    /// temporaries (<c>DirectoryNativeDatStore</c>); any other entry by its own name.</summary>
    private static string UnitOf(string name)
    {
        string upper = name.ToUpperInvariant();
        Match numbered = SlotPrefix().Match(upper);
        if (numbered.Success) return "SAVE" + numbered.Groups[1].Value;
        if (upper is DirectoryNativeDatStore.SharedFileName or DirectoryNativeDatStore.SharedBackupFileName
                  or DirectoryNativeDatStore.SharedTemporaryFileName)
            return DirectoryNativeDatStore.SharedFileName;
        if (upper is DirectoryNativeDatStore.ReadTextFileName or DirectoryNativeDatStore.ReadTextBackupFileName
                  or DirectoryNativeDatStore.ReadTextTemporaryFileName)
            return DirectoryNativeDatStore.ReadTextFileName;
        return upper;
    }

    [GeneratedRegex(@"^SAVE([0-9]+)\.", RegexOptions.CultureInvariant)]
    private static partial Regex SlotPrefix();

    [GeneratedRegex(@"^SAVE[0-9]+\.DAT$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NumberedSave();
}
