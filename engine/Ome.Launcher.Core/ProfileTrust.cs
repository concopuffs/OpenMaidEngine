using Ome.Engine.Profiles;
using Ome.Mods.Launch;

namespace Ome.Launcher.Core;

/// <summary>A path a profile file names, as its review shows it: its key, the path as text, whether it is on another
/// computer, and whether this machine's player approved that location.</summary>
public sealed record ReviewedPath(string Key, string Path, bool Network, bool Approved);

/// <summary>
/// The review of a root the launcher did not create (launcher spec §12). It comes from <c>profile.toml</c>'s text
/// alone, the text <see cref="ProfileStore.List"/> already read, and the walk of the root for links; it opens no path
/// the file names. A root on another computer that this machine has not approved is not read at all
/// (<see cref="Read"/> false): accepting that review approves the location, and the next review shows the profile.
/// </summary>
public sealed record RootReview(string Root, bool Read, string? Name, string? Game, string? GameTitle, string? Mode,
                                IReadOnlyList<ReviewedPath> Paths, IReadOnlyList<string> EnabledMods, bool OwnModsFolder,
                                string? Link, string? Problem)
{
    public IEnumerable<ReviewedPath> NetworkPaths => Paths.Where(path => path.Network);

    /// <summary>A root with a link can never start (spec §12), and one that does not read has nothing to accept.</summary>
    public bool CanAccept => Problem == null && Link == null;
}

/// <summary>A root the launcher did not create, used before the player accepted its review (spec §12).</summary>
public sealed class ProfileNeedsReviewException(string message) : Exception(message);

/// <summary>
/// The launcher's side of spec §12. A root the launcher did not create is reviewed once, and nothing opens its paths
/// until then; <c>launcher.toml</c>'s reviewed roots record the acceptance. Network locations are approved on this
/// machine (<see cref="MachineState"/>), never in OME home or a profile, so neither brings an approval with it. A root
/// that holds a link is refused at the review and before each start.
/// </summary>
public static class ProfileTrust
{
    private static OmePlatform Platform => LauncherEnvironment.Current.Platform;

    /// <summary>Whether <paramref name="root"/> is one the player accepted, or the launcher made elsewhere.</summary>
    public static bool IsReviewed(LauncherSettings settings, string root)
        => settings.Reviewed.Any(reviewed => OmeHome.PathsEqual(reviewed, root, Platform));

    /// <summary>Adds a folder that already holds a profile as a root elsewhere, under <paramref name="id"/>. It is not
    /// reviewed: its page shows the review before anything else. A network folder the player <paramref name="picked"/>
    /// in the launcher is approved by that (spec §12); nothing here opens the folder.</summary>
    public static LauncherSettings AddExisting(OmeHome home, LauncherSettings settings, string root, string id,
                                               Func<MachineState> machine, bool picked)
    {
        if (!ProfileId.IsValid(id)) throw new ArgumentException($"'{id}' is not a profile id");
        if (ProfileStore.IsTaken(id, home, settings)) throw new ArgumentException($"the id '{id}' is taken");
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (HomePaths.IsInside(full, home.ProfilesDirectory) || HomePaths.IsInside(home.ProfilesDirectory, full))
            throw new ArgumentException($"{full} is in or around {home.ProfilesDirectory}");
        foreach (ExternalRoot other in settings.Roots)
            if (HomePaths.IsInside(full, other.Path) || HomePaths.IsInside(other.Path, full))
                throw new ArgumentException($"{full} is in or around the profile '{other.Id}' ({other.Path})");
        if (picked && RootSafety.IsNetworkPath(full)) machine().Approve(full);
        return settings with { Roots = [.. settings.Roots, new ExternalRoot(full, id)] };
    }

    /// <summary>The review of <paramref name="profile"/>: what its file names, each network location marked, and
    /// whether the root holds a link.</summary>
    public static RootReview Review(ProfileEntry profile, Func<MachineState> machine)
    {
        MachineState? state = null;
        bool Approved(string path) => (state ??= machine()).IsApproved(path);
        bool rootNetwork = RootSafety.IsNetworkPath(profile.Root);
        var root = new ReviewedPath("root", profile.Root, rootNetwork, !rootNetwork || Approved(profile.Root));
        if (!root.Approved)
            return new RootReview(profile.Root, false, null, null, null, null, [root], [], false, null, null);
        if (profile.Loaded is not { } loaded)
            return new RootReview(profile.Root, false, null, null, null, null, [root], [], false, null,
                                  profile.Problem ?? $"{profile.Root} has no readable {ProfileFile.FileName}");

        ProfileFile file = loaded.File;
        var paths = new List<ReviewedPath> { root };
        void Named(string key, string path)
        {
            bool network = RootSafety.IsNetworkPath(path) && !RootSafety.IsSameOrUnder(path, file.ProfileRoot);
            paths.Add(new ReviewedPath(key, path, network, !network || Approved(path)));
        }
        Named("install.root", file.Install.Root);
        foreach (string overlay in file.Install.Overlays) Named("install.overlays", overlay);
        foreach (string folder in file.Mods?.Folders ?? []) Named("mods.folders", folder);

        string? link = null, problem = null;
        try
        {
            link = RootSafety.FindLink(profile.Root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            problem = $"{profile.Root} cannot be checked for links: {error.Message}";
        }
        return new RootReview(profile.Root, true, loaded.Document.Name, file.Game,
                              GameProfileRegistry.BuiltIn.Find(file.Game)?.DisplayTitle, file.ModeName, paths,
                              file.Mods?.Enabled.ToList() ?? [], file.Mods == null,
                              link == null ? null : $"{link} is a symbolic link or junction; a profile root may hold none",
                              problem);
    }

    /// <summary>Accepts a review: approves the network locations it shows on this machine, then, for a review that
    /// read the profile, records the root as reviewed. Returns the settings to save.</summary>
    public static LauncherSettings Accept(LauncherSettings settings, RootReview review, Func<MachineState> machine)
    {
        if (!review.CanAccept)
            throw new InvalidOperationException(review.Problem ?? review.Link);
        MachineState? state = null;
        foreach (ReviewedPath path in review.NetworkPaths.Where(path => !path.Approved))
            state = (state ?? machine()).Approve(path.Path);
        return review.Read && !IsReviewed(settings, review.Root)
            ? settings with { Reviewed = [.. settings.Reviewed, review.Root] }
            : settings;
    }

    /// <summary>Declines a review (spec §12: the player accepts or declines): the launcher forgets the root, and its
    /// folder is left as it is. Returns the settings to save.</summary>
    public static LauncherSettings Decline(LauncherSettings settings, ProfileEntry profile)
        => ProfileStore.Forget(settings, profile);

    /// <summary>The check before each start (spec §8.5, §12): a root that holds a link stops the start, with the
    /// reason; null lets it go.</summary>
    public static string? BeforeStart(string root)
    {
        try
        {
            return RootSafety.FindLink(root) is { } link
                ? $"{link} is a symbolic link or junction; a profile root may hold none, since the engine writes into it"
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"{root} cannot be checked for links: {error.Message}";
        }
    }
}
