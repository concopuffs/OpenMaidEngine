using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

namespace Ome.Launcher.Core;

/// <summary>Why a package cannot run as it is, from the index, the mode and the install (launcher spec §8.4). An
/// enabled package's other states (ready, conflicts) come from the link report.</summary>
public enum PackageState
{
    Off,
    Enabled,
    /// <summary>The profile's Compatibility mode refuses it (<c>ModeCheck</c>).</summary>
    NeedsExtended,
    /// <summary>A hard dependency that no folder holds in a version it accepts.</summary>
    NeedsDependency,
    /// <summary>Its <c>[game].revisions</c> do not list this install's catalog fingerprint.</summary>
    UnsupportedRevision,
    /// <summary>The profile lists it, and no folder holds it.</summary>
    Missing,
}

/// <summary>One row of the Mods page: a package of the profile's folders for its game, or an id the profile lists
/// that no folder holds (<see cref="Entry"/> null). <see cref="State"/> is the first of <see cref="Reasons"/>' kinds,
/// else on or off. <see cref="Problems"/> are other copies of its id.</summary>
public sealed record LibraryPackage(string Id, PackageIndexEntry? Entry, bool Enabled, bool Listed, PackageState State,
                                    IReadOnlyList<string> Reasons, IReadOnlyList<string> Problems);

/// <summary>The Mods page's list, in the profile's order (then the packages it never listed, by id), with the notes of
/// the candidates that could not be read.</summary>
public sealed record LibraryView(IReadOnlyList<LibraryPackage> Packages, IReadOnlyList<PackageIndexNote> Notes);

/// <summary>What adding a package did, or would do: its place, the copies of its id it replaces, and whether it was
/// copied (not when it would replace one and the caller did not say to).</summary>
public sealed record LibraryAddResult(string Destination, PackageIndexEntry Package, IReadOnlyList<PackageIndexEntry> Replaced,
                                      bool Copied);

/// <summary>Who uses a package: the profiles that enable it from its folder, and those that hold a selector for it
/// (spec §7).</summary>
public sealed record PackageUsage(IReadOnlyList<string> Enabling, IReadOnlyList<string> HoldingSelector);

/// <summary>A changed document and the other packages the change touched: the dependencies it enabled, those it
/// could not, the packages it disabled, or those that still need a disabled one.</summary>
public sealed record LibraryChange(ProfileDocument Document, IReadOnlyList<string> Changed, IReadOnlyList<string> Unmet);

/// <summary>
/// The library (launcher spec §7, §8.4 Mods): the folders a profile's packages come from, each read by its manifests
/// alone (<see cref="PackageIndex"/>). Edits return the changed document; the caller writes it under the profile's
/// lock (<see cref="ProfileStore.Update"/>).
/// </summary>
public static class Library
{
    private static OmePlatform Platform => LauncherEnvironment.Current.Platform;

    /// <summary>Every folder of the profile's <c>[mods]</c> table, read.</summary>
    public static IReadOnlyList<PackageFolderIndex> Index(ProfileDocument document)
        => (document.Mods?.Folders ?? []).Select(folder => PackageIndex.ReadFolder(folder)).ToList();

    /// <param name="fingerprint">The install's catalog fingerprint; null leaves the revision check out.</param>
    public static LibraryView View(ProfileDocument document, IReadOnlyList<PackageFolderIndex> folders, string? fingerprint)
    {
        var copies = Copies(document, folders);
        IReadOnlyDictionary<string, string> refusals = document.Mode == ProfileMode.Native
            ? ModeCheck.NativeRefusals(copies.Values.Select(list => list[0]))
            : new Dictionary<string, string>();
        var order = document.Mods?.Order ?? [];
        var rows = new List<LibraryPackage>();
        foreach (ProfileModEntry listed in order)
            rows.Add(Row(listed.Id, listed.Enabled, true));
        foreach (string id in copies.Keys.Where(id => order.All(entry => entry.Id != id)).Order(StringComparer.Ordinal))
            rows.Add(Row(id, false, false));
        return new LibraryView(rows, folders.SelectMany(folder => folder.Notes).ToList());

        LibraryPackage Row(string id, bool enabled, bool listed)
        {
            if (!copies.TryGetValue(id, out var found))
                return new LibraryPackage(id, null, enabled, listed, PackageState.Missing,
                                          ["the profile lists it, and none of its folders holds it"], []);
            PackageIndexEntry entry = found[0];
            var reasons = new List<(PackageState State, string Text)>();
            if (refusals.TryGetValue(id, out string? refusal))
                reasons.Add((PackageState.NeedsExtended, $"Compatibility mode refuses it: {refusal}"));
            foreach (ModRequirement requirement in entry.Requires)
                if (!Satisfied(requirement, copies))
                    reasons.Add((PackageState.NeedsDependency,
                                 $"it requires {requirement.Id} {requirement.Version.Text}, which no folder holds"));
            if (fingerprint != null && !entry.Supports(fingerprint))
                reasons.Add((PackageState.UnsupportedRevision,
                             "it does not support this install's revision (its [game] revisions do not list it)"));
            var problems = found.Skip(1).Select(other => $"another copy of {id} is {other.Path}; the engine refuses an "
                                                         + "id it finds twice once it is enabled").ToList();
            PackageState state = reasons.Count > 0 ? reasons[0].State : enabled ? PackageState.Enabled : PackageState.Off;
            return new LibraryPackage(id, entry, enabled, listed, state, reasons.Select(reason => reason.Text).ToList(),
                                      problems);
        }
    }

    /// <summary>Copies a package into the library: <c>mods/&lt;game&gt;/</c> by its <c>[game] profile</c>, or
    /// <c>mods/common/</c> without one (spec §7), or <paramref name="folder"/>. Its manifest is read with the engine's
    /// reader first (spec §12). It is named by its id. When the folder already holds its id, nothing is copied unless
    /// <paramref name="replace"/> says to, and then the old copies go.</summary>
    public static LibraryAddResult Add(OmeHome home, string package, bool replace, string? folder = null)
    {
        PackageIndexEntry entry = PackageIndex.Read(package);
        string target = Path.GetFullPath(folder ?? (entry.Game == null ? home.CommonModsDirectory : home.ModsDirectory(entry.Game)));
        bool file = File.Exists(package);
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package));
        if (OmeHome.PathsEqual(Path.GetDirectoryName(source)!, target, Platform))
            throw new ArgumentException($"{source} is already in {target}");
        string destination = Path.Combine(target, entry.Id + (file ? ModPackage.PackageExtension : ""));
        var replaced = PackageIndex.ReadFolder(target).Entries.Where(other => other.Id == entry.Id).ToList();
        if (replaced.Count > 0 && !replace) return new LibraryAddResult(destination, entry, replaced, false);
        if (Path.Exists(destination) && !replaced.Any(other => OmeHome.PathsEqual(other.Path, destination, Platform)))
            throw new IOException($"{destination} exists and is not a copy of {entry.Id}");

        // Copied under a dot-name first, which discovery skips, so a failed copy leaves no half package.
        Directory.CreateDirectory(target);
        string temporary = Path.Combine(target, $".adding-{Guid.NewGuid():N}");
        try
        {
            if (file) Folders.CopyFile(source, temporary, overwrite: false);
            else Folders.Copy(source, temporary, _ => false);
            foreach (PackageIndexEntry old in replaced) Folders.Delete(Info(old.Path));
            if (file) File.Move(temporary, destination);
            else Directory.Move(temporary, destination);
        }
        catch
        {
            if (Path.Exists(temporary)) Folders.Delete(Info(temporary));
            throw;
        }
        return new LibraryAddResult(destination, entry, replaced, true);
    }

    /// <summary>The profiles that enable <paramref name="package"/> from its folder, and those that hold a selector for
    /// its id, before it is removed (spec §7).</summary>
    public static PackageUsage Usage(PackageIndexEntry package, IEnumerable<ProfileEntry> profiles)
    {
        string folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(package.Path))!;
        var enabling = new List<string>();
        var holding = new List<string>();
        foreach (ProfileEntry profile in profiles)
        {
            if (profile.Loaded?.Document.Mods is { } mods && mods.Enabled.Contains(package.Id)
                && mods.Folders.Any(other => OmeHome.PathsEqual(other, folder, Platform)))
                enabling.Add(profile.Id);
            try
            {
                if (SelectorStore.Load(Path.Combine(profile.Root, ModLaunch.ModsFolder, SelectorStore.FileName))
                    .TryGet(package.Id, out _))
                    holding.Add(profile.Id);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // An unreadable selectors file holds nothing the launcher can show.
            }
        }
        return new PackageUsage(enabling, holding);
    }

    /// <summary>Removes a package from its folder. Its selectors stay reserved until the player releases them (spec
    /// §7, §8.4).</summary>
    public static void Remove(PackageIndexEntry package) => Folders.Delete(Info(package.Path));

    /// <summary>Enables <paramref name="id"/> and the hard dependencies its folders hold, each before what needs it;
    /// never an engine base mod, which mounts by itself (spec §8.4). A disabled package keeps its place; one never
    /// listed goes at the end. <see cref="LibraryChange.Changed"/> are the dependencies it enabled,
    /// <see cref="LibraryChange.Unmet"/> those no folder holds.</summary>
    public static LibraryChange Enable(ProfileDocument document, string id, IReadOnlyList<PackageFolderIndex> folders)
    {
        ProfileMods mods = document.Mods ?? throw new InvalidOperationException("the profile has no [mods] table");
        var copies = Copies(document, folders);
        if (!copies.ContainsKey(id)) throw new ArgumentException($"none of the profile's folders holds {id}");
        var order = mods.Order.ToList();
        var changed = new List<string>();
        var unmet = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Visit(id);
        return new LibraryChange(document with { Mods = mods with { Order = order } }, changed, unmet);

        void Visit(string current)
        {
            if (!visited.Add(current)) return;
            foreach (ModRequirement requirement in copies[current][0].Requires)
            {
                if (ShippedPackages.Ids.Contains(requirement.Id)) continue;
                if (Satisfied(requirement, copies)) Visit(requirement.Id);
                else if (!unmet.Contains(requirement.Id)) unmet.Add(requirement.Id);
            }
            int at = order.FindIndex(entry => entry.Id == current);
            if (at >= 0 && order[at].Enabled) return;
            if (at >= 0) order[at] = order[at] with { Enabled = true };
            else order.Add(new ProfileModEntry(current, true));
            if (current != id) changed.Add(current);
        }
    }

    /// <summary>Disables <paramref name="id"/>, keeping its place (spec §4.3). <see cref="LibraryChange.Unmet"/> are
    /// the enabled packages that still require it, which the engine would then refuse to start.</summary>
    public static LibraryChange Disable(ProfileDocument document, string id, IReadOnlyList<PackageFolderIndex> folders)
    {
        ProfileMods mods = document.Mods ?? throw new InvalidOperationException("the profile has no [mods] table");
        var order = mods.Order.Select(entry => entry.Id == id ? entry with { Enabled = false } : entry).ToList();
        var copies = Copies(document, folders);
        var needing = order.Where(entry => entry.Enabled && copies.TryGetValue(entry.Id, out var found)
                                           && found[0].Requires.Any(requirement => requirement.Id == id))
            .Select(entry => entry.Id).ToList();
        return new LibraryChange(document with { Mods = mods with { Order = order } }, [], needing);
    }

    /// <summary>Switches the profile to Compatibility mode and disables the enabled packages that mode refuses
    /// (spec §6), keeping their places. <see cref="LibraryChange.Changed"/> are the packages it disabled.</summary>
    public static LibraryChange ToCompatibility(ProfileDocument document, IReadOnlyList<PackageFolderIndex> folders)
    {
        var copies = Copies(document, folders);
        ProfileMods? mods = document.Mods;
        var enabled = mods?.Enabled.Where(copies.ContainsKey).Select(id => copies[id][0]).ToList() ?? [];
        IReadOnlyDictionary<string, string> refused = ModeCheck.NativeRefusals(enabled);
        ProfileDocument changed = document with { Mode = ProfileMode.Native };
        if (mods != null && refused.Count > 0)
            changed = changed with
            {
                Mods = mods with
                {
                    Order = mods.Order.Select(entry => refused.ContainsKey(entry.Id) ? entry with { Enabled = false } : entry)
                        .ToList(),
                },
            };
        return new LibraryChange(changed, mods?.Order.Where(entry => entry.Enabled && refused.ContainsKey(entry.Id))
                                              .Select(entry => entry.Id).ToList() ?? [], []);
    }

    /// <summary>The profile's packages for its game, by id, each id's copies in folder order.</summary>
    private static Dictionary<string, List<PackageIndexEntry>> Copies(ProfileDocument document,
                                                                      IReadOnlyList<PackageFolderIndex> folders)
    {
        var copies = new Dictionary<string, List<PackageIndexEntry>>(StringComparer.Ordinal);
        foreach (PackageIndexEntry entry in folders.SelectMany(folder => folder.Entries).Where(entry => entry.IsFor(document.Game)))
        {
            if (!copies.TryGetValue(entry.Id, out var list)) copies[entry.Id] = list = [];
            if (!list.Any(other => OmeHome.PathsEqual(other.Path, entry.Path, Platform))) list.Add(entry);
        }
        return copies;
    }

    /// <summary>Whether a folder holds the requirement in a version it accepts, or the engine carries it (a base
    /// mod).</summary>
    private static bool Satisfied(ModRequirement requirement, Dictionary<string, List<PackageIndexEntry>> copies)
        => copies.TryGetValue(requirement.Id, out var found)
            ? found.Any(entry => requirement.Version.Contains(entry.Version))
            : ShippedPackages.Ids.Contains(requirement.Id);

    private static FileSystemInfo Info(string path)
        => Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
}
