using System.Globalization;
using System.Text;
using Ome.Engine.Frontend.Shell;
using Ome.Mods.Launch;

namespace Ome.Launcher.Core;

/// <summary>A profile root the launcher knows (launcher spec §4.1): a folder of <c>profiles/</c>, or a
/// <c>[[roots]]</c> entry of <c>launcher.toml</c> (<see cref="Elsewhere"/>). <see cref="Loaded"/> is null, and
/// <see cref="Problem"/> says why, when its file cannot be read.</summary>
public sealed record ProfileEntry(string Id, string Root, bool Elsewhere, LoadedProfile? Loaded, string? Problem)
{
    public bool Usable => Loaded != null && Problem == null;
    public string DisplayName => Loaded?.Document.Name ?? Id;
}

/// <summary>A profile the launcher made, and the settings that now list it.</summary>
public sealed record ProfileChange(ProfileEntry Profile, LauncherSettings Settings);

/// <summary>The profile's lock is held elsewhere: the game is running it, or another launcher is changing it.</summary>
public sealed class ProfileBusyException(string message) : Exception(message);

/// <summary>
/// The launcher's profiles (launcher spec §4.1, §4.2, §4.4, §8.4): the list, ids, and create, duplicate, rename and
/// delete. Every write to a profile's files happens while the launcher holds that profile's lock
/// (<see cref="RunLock"/>, without marking a start), so a shortcut cannot start the engine in the middle of a write,
/// and none happens while the game runs the profile. Each operation that changes which roots
/// <c>launcher.toml</c> lists returns the new settings; the caller saves them.
/// </summary>
public static class ProfileStore
{
    /// <summary>The folders a duplicate never copies: the engine's link cache, which only this root's machine may use
    /// (spec §5.3), and the source's own logs.</summary>
    private static readonly string[] NeverCopied = [Path.Combine("mods", ".cache"), "diagnostics"];

    private static OmePlatform Platform => LauncherEnvironment.Current.Platform;

    /// <summary>Takes <paramref name="root"/>'s lock for a write. Throws <see cref="ProfileBusyException"/> while the
    /// game runs the profile, and <see cref="IOException"/> when the root cannot hold the lock file.</summary>
    public static RunLock Lock(string root)
    {
        if (RunLock.TryAcquire(root, markStart: false, out RunLock? held, out RunLockRefusal? refusal)) return held;
        if (refusal.HeldElsewhere)
            throw new ProfileBusyException($"{root} is in use: the game is running this profile, or another launcher is "
                                           + "changing it; close the game first");
        throw new IOException(refusal.Message);
    }

    /// <summary>The profiles: <c>profiles/*</c> by folder name, then the roots elsewhere in their order. A root on a
    /// network path this machine's player has not approved is listed and not opened (spec §12); the machine's
    /// state is read only when there is such a root.</summary>
    public static IReadOnlyList<ProfileEntry> List(OmeHome home, LauncherSettings settings, Func<MachineState> machine)
    {
        var entries = new List<ProfileEntry>();
        if (Directory.Exists(home.ProfilesDirectory))
            foreach (DirectoryInfo folder in new DirectoryInfo(home.ProfilesDirectory).EnumerateDirectories("*", Folders.Entries)
                         .OrderBy(folder => folder.Name, StringComparer.Ordinal))
                entries.Add(ProfileId.IsValid(folder.Name)
                    ? Load(folder.Name, folder.FullName, elsewhere: false)
                    : new ProfileEntry(folder.Name, folder.FullName, false, null,
                                       $"the folder name '{folder.Name}' is not a profile id (lowercase letters, digits "
                                       + "and -), so the launcher does not use it"));
        MachineState? state = null;
        foreach (ExternalRoot root in settings.Roots)
        {
            if (entries.Any(entry => OmeHome.PathsEqual(entry.Root, root.Path, Platform))) continue;
            if (entries.Any(entry => entry.Id == root.Id))
                entries.Add(new ProfileEntry(root.Id, root.Path, true, null,
                                             $"its id '{root.Id}' is also the name of a folder in profiles/; add it again "
                                             + "with another id"));
            else if (RootSafety.IsNetworkPath(root.Path) && !(state ??= machine()).IsApproved(root.Path))
                entries.Add(new ProfileEntry(root.Id, root.Path, true, null,
                                             $"{root.Path} is on another computer, and this machine's player has not "
                                             + "approved that location"));
            else entries.Add(Load(root.Id, root.Path, elsewhere: true));
        }
        return entries;
    }

    /// <summary>The listed profile with <paramref name="id"/> (a folder of <c>profiles/</c> before a root elsewhere),
    /// or null.</summary>
    public static ProfileEntry? Find(IEnumerable<ProfileEntry> profiles, string id)
        => profiles.FirstOrDefault(entry => entry.Id == id);

    /// <summary>Whether <paramref name="id"/> names a known root, or a folder or file in <c>profiles/</c>.</summary>
    public static bool IsTaken(string id, OmeHome home, LauncherSettings settings)
        => settings.Roots.Any(root => root.Id == id) || Path.Exists(Path.Combine(home.ProfilesDirectory, id));

    /// <summary>A free id made from <paramref name="name"/>: its ASCII letters and digits, lowercase, the rest as
    /// <c>-</c>; else from <paramref name="fallback"/> (the game's id); then <c>-2</c>, <c>-3</c>, … while taken.</summary>
    public static string SuggestId(string? name, string fallback, OmeHome home, LauncherSettings settings)
    {
        string stem = Slug(name ?? "");
        if (stem.Length == 0) stem = Slug(fallback);
        if (stem.Length == 0) stem = "profile";
        for (int number = 1; ; number++)
        {
            string suffix = number == 1 ? "" : $"-{number.ToString(CultureInfo.InvariantCulture)}";
            string id = stem[..Math.Min(stem.Length, ProfileId.MaxLength - suffix.Length)].TrimEnd('-') + suffix;
            if (!IsTaken(id, home, settings)) return id;
        }
    }

    /// <summary>Makes a profile: its <c>profile.toml</c> and an <c>OME.INI</c> from the launcher's defaults for a new
    /// profile (spec §4.4). It goes in <c>profiles/&lt;id&gt;</c>, or in <paramref name="elsewhere"/>, which must be
    /// new or empty and is then listed in <c>launcher.toml</c> as a root the launcher made (no review, spec §12).</summary>
    /// <param name="fill">What the new profile starts from (spec §8.3), given the root and run while the launcher
    /// still holds its lock: saves copied in, the bare engine's <c>OME.INI</c>. A failure removes the whole
    /// profile.</param>
    public static ProfileChange Create(OmeHome home, LauncherSettings settings, string id, ProfileDocument document,
                                       string? elsewhere = null, Action<string>? fill = null)
    {
        string root = NewRoot(home, settings, id, elsewhere);
        bool created = !Directory.Exists(root);
        try
        {
            using RunLock held = Lock(root);
            ProfileDocuments.Write(document, root, home);
            new OmeIniStore(Path.Combine(root, PortOptions.FileName)).Save(settings.NewProfile);
            fill?.Invoke(root);
        }
        catch
        {
            Discard(root, created);
            throw;
        }
        return Added(settings, id, root, elsewhere != null);
    }

    /// <summary>Copies a profile under a new id (spec §8.4). The copy is the whole folder but <c>run.lock</c>, the
    /// engine's link cache and the logs, with the profile's paths that point inside the source moved onto the copy.
    /// The saves and <c>mods/selectors.toml</c> go together or not at all: <paramref name="leaveOutSaves"/> is the
    /// source's save folder (<see cref="SaveDirectory"/>) to leave out, or null to copy both. Refused while the source
    /// runs, and when it holds a link (spec §12).</summary>
    public static ProfileChange Duplicate(OmeHome home, LauncherSettings settings, ProfileEntry source, string id,
                                          string? name, string? leaveOutSaves, string? elsewhere = null)
    {
        if (!source.Usable)
            throw new ArgumentException($"the profile '{source.Id}' cannot be read, so it is not copied: {source.Problem}");
        string from = source.Root;
        string? saves = leaveOutSaves == null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(leaveOutSaves));
        if (saves != null && (!HomePaths.IsInside(saves, from) || OmeHome.PathsEqual(saves, from, Platform)))
            throw new ArgumentException($"the save folder {saves} is not a folder inside {from}");
        string root = NewRoot(home, settings, id, elsewhere);
        string? cleanName = CleanName(name);

        using RunLock sourceLock = Lock(from);
        if (RootSafety.FindLink(from) is { } link)
            throw new IOException($"{link} is a link, which a profile root never holds (spec §12); the profile is not copied");
        LoadedProfile current = ProfileDocuments.Read(from)
                                ?? throw new IOException($"{Path.Combine(from, ProfileFile.FileName)} is gone");
        var leftOut = new List<string>
        {
            Path.Combine(from, RunLock.FileName),
            Path.Combine(from, ProfileFile.FileName),
        };
        leftOut.AddRange(NeverCopied.Select(folder => Path.Combine(from, folder)));
        if (saves != null) leftOut.AddRange([saves, Path.Combine(from, "mods", "selectors.toml")]);

        bool created = !Directory.Exists(root);
        try
        {
            using RunLock held = Lock(root);
            Folders.Copy(from, root, path => leftOut.Any(skip => OmeHome.PathsEqual(skip, path, Platform)));
            ProfileDocument document = Rebase(current.Document, from, root) with { Name = cleanName ?? current.Document.Name };
            ProfileDocuments.Write(document, root, home);
        }
        catch
        {
            Discard(root, created);
            throw;
        }
        return Added(settings, id, root, elsewhere != null);
    }

    /// <summary>Changes a profile's name, never its id (spec §4.1). An empty name leaves the profile shown by its
    /// id.</summary>
    public static void Rename(ProfileEntry profile, string? name, OmeHome home)
    {
        string? cleanName = CleanName(name);
        using RunLock held = Lock(profile.Root);
        LoadedProfile current = ProfileDocuments.Read(profile.Root)
                                ?? throw new IOException($"{Path.Combine(profile.Root, ProfileFile.FileName)} is gone");
        ProfileDocuments.Write(current.Document with { Name = cleanName }, profile.Root, home);
    }

    /// <summary>Deletes a profile's folder (spec §4.2: a profile is the unit of removal) and forgets it. Refused while
    /// the game runs it. <c>profile.toml</c> goes first, so a start that slips in finds no profile. A link, inside the
    /// root or the root itself, is removed itself, never what it names.</summary>
    public static LauncherSettings Delete(OmeHome home, LauncherSettings settings, ProfileEntry profile)
    {
        string root = profile.Root;
        var info = new DirectoryInfo(root);
        if (info.LinkTarget != null)
            Folders.RemoveLink(info);
        else if (Directory.Exists(root))
        {
            using (RunLock held = Lock(root))
            {
                string file = Path.Combine(root, ProfileFile.FileName);
                if (File.Exists(file)) File.Delete(file);
                Folders.DeleteContents(root, keep: held.Path);
            }
            File.Delete(Path.Combine(root, RunLock.FileName));
            Directory.Delete(root);
        }
        return Forget(settings, profile);
    }

    /// <summary>The save folder of a profile root, from the game's <c>SAVEPATH</c> beneath its <c>REGFILEPATH</c>, as
    /// the engine resolves it under <c>--data-root</c> (<c>Sys4PersistencePaths.ResolveProfileOverride</c>).</summary>
    public static string SaveDirectory(Ome.Engine.Sys4.Sys4StartupSettings startup, string root)
        => Ome.Engine.Persistence.Sys4PersistencePaths.ResolveProfileOverride(startup, root).SaveDirectory;

    private static ProfileEntry Load(string id, string root, bool elsewhere)
    {
        if (!Directory.Exists(root))
            return new ProfileEntry(id, root, elsewhere, null, $"{root} is gone; add the profile again where it is now");
        try
        {
            LoadedProfile? loaded = ProfileDocuments.Read(root);
            return loaded == null
                ? new ProfileEntry(id, root, elsewhere, null, $"{root} has no {ProfileFile.FileName}")
                : new ProfileEntry(id, root, elsewhere, loaded, null);
        }
        catch (ProfileFileException error)
        {
            return new ProfileEntry(id, root, elsewhere, null, error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new ProfileEntry(id, root, elsewhere, null, $"{root}: {error.Message}");
        }
    }

    /// <summary>The root a new profile goes in, checked: a free id, and a new or empty folder that is not inside or
    /// around another profile.</summary>
    private static string NewRoot(OmeHome home, LauncherSettings settings, string id, string? elsewhere)
    {
        if (!ProfileId.IsValid(id))
            throw new ArgumentException($"'{id}' is not a profile id: lowercase letters, digits and -, at most "
                                        + $"{ProfileId.MaxLength}, with no - at either end");
        if (IsTaken(id, home, settings)) throw new ArgumentException($"the profile id '{id}' is taken");
        string root = elsewhere == null
            ? Path.Combine(home.ProfilesDirectory, id)
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(elsewhere));
        if (elsewhere != null)
        {
            if (HomePaths.IsInside(root, home.ProfilesDirectory) || HomePaths.IsInside(home.ProfilesDirectory, root))
                throw new ArgumentException($"{root} is in or around {home.ProfilesDirectory}; choose a folder outside it");
            foreach (ExternalRoot other in settings.Roots)
                if (HomePaths.IsInside(root, other.Path) || HomePaths.IsInside(other.Path, root))
                    throw new ArgumentException($"{root} is in or around the profile '{other.Id}' ({other.Path})");
        }
        if (File.Exists(root)) throw new IOException($"{root} is a file");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root, "*", Folders.Entries).Any())
            throw new IOException($"{root} is not empty; a new profile needs a new or empty folder");
        return root;
    }

    private static ProfileChange Added(LauncherSettings settings, string id, string root, bool elsewhere)
    {
        LauncherSettings changed = elsewhere
            ? settings with
            {
                Roots = [.. settings.Roots, new ExternalRoot(root, id)],
                Reviewed = [.. settings.Reviewed, root],
            }
            : settings;
        return new ProfileChange(Load(id, root, elsewhere), changed);
    }

    private static LauncherSettings Forget(LauncherSettings settings, ProfileEntry profile)
        => settings with
        {
            Roots = profile.Elsewhere ? settings.Roots.Where(root => root.Id != profile.Id).ToList() : settings.Roots,
            Reviewed = settings.Reviewed.Where(root => !OmeHome.PathsEqual(root, profile.Root, Platform)).ToList(),
            LastProfile = settings.LastProfile == profile.Id ? null : settings.LastProfile,
        };

    /// <summary>A name as the file keeps it: trimmed, one line, null when empty.</summary>
    private static string? CleanName(string? name)
    {
        string? trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Any(char.IsControl)) throw new ArgumentException("a profile's name is one line of text");
        return trimmed;
    }

    private static string Slug(string text)
    {
        var slug = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormKD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        return slug.ToString().Trim('-');
    }

    /// <summary>The document with each path inside <paramref name="from"/> moved to the same place in
    /// <paramref name="to"/>, so a copy uses its own files.</summary>
    private static ProfileDocument Rebase(ProfileDocument document, string from, string to)
    {
        string Move(string path)
            => HomePaths.IsInside(path, from) ? Path.Join(to, Path.GetRelativePath(from, path)) : path;
        ProfileInstall install = document.Install;
        return document with
        {
            Install = install with { Root = Move(install.Root), Overlays = install.Overlays.Select(Move).ToList() },
            Mods = document.Mods is { } mods ? mods with { Folders = mods.Folders.Select(Move).ToList() } : null,
            Launcher = document.Launcher with { Cover = document.Launcher.Cover is { } cover ? Move(cover) : null },
        };
    }

    /// <summary>Removes a root a failed create or duplicate left: the whole folder when it made it, else what it
    /// put in it.</summary>
    private static void Discard(string root, bool created)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            Folders.DeleteContents(root, null);
            if (created) Directory.Delete(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The first failure is the one to report; what is left is a folder the player can delete.
        }
    }
}
