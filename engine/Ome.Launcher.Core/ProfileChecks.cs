using Ome.Engine.Profiles;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Engine.Frontend.Shell;

namespace Ome.Launcher.Core;

/// <summary>The rows of the checks table (launcher spec §8.5), in its order.</summary>
public enum ProfileCheckKind
{
    /// <summary>The install folder exists and holds the profile's game.</summary>
    Install,
    /// <summary>The catalog fingerprint equals the recorded one.</summary>
    Fingerprint,
    /// <summary>The profile root can be written.</summary>
    Root,
    /// <summary>Every enabled package is found, and the mode admits it.</summary>
    Packages,
    /// <summary>The stack links.</summary>
    Link,
    /// <summary>No engine holds the profile's lock.</summary>
    Lock,
}

public enum CheckStatus { Passed, Warning, Failed, NotRun }

/// <summary>One check's outcome and its message: what passed, or what is wrong and where it is fixed.</summary>
public sealed record ProfileCheck(ProfileCheckKind Kind, CheckStatus Status, string Message);

/// <summary>The link check (spec §5.9): the text <c>omp check</c> prints for the profile, the discovery's and the
/// link's reports, the resolved order, the packages the resolver moved and why, and what a skipped link drops.</summary>
public sealed record LinkCheck(string Text, ModLinkReport Discovery, ModLinkReport? Report, IReadOnlyList<string> Order,
                               IReadOnlyList<OrderMove> Moves, IReadOnlyList<string> Dropped);

/// <summary>Every check of a profile. Play is on when none failed; a failed lock check is "running".</summary>
public sealed record ProfileCheckResult(IReadOnlyList<ProfileCheck> Checks, InstallRecognition Install,
                                        LibraryView? Library, LinkCheck? Link)
{
    public ProfileCheck this[ProfileCheckKind kind] => Checks.Single(check => check.Kind == kind);
    public bool CanPlay => Checks.All(check => check.Status != CheckStatus.Failed);
    public bool Running => this[ProfileCheckKind.Lock].Status == CheckStatus.Failed;

    /// <summary>The overlays the profile names that are not there, which fail the install's check.</summary>
    public IReadOnlyList<string> MissingOverlays { get; init; } = [];
}

/// <param name="Registry">The games the engine runs; the built-in ones when null.</param>
/// <param name="MachineRegistry">The registry read of the game version rule; the machine's own when null.</param>
/// <param name="MachineFolder">Where the network approvals are kept (<see cref="MachineState"/>); Godot's user folder
/// by its rule when null, as the engine reads it.</param>
public sealed record CheckOptions(GameProfileRegistry? Registry = null, MachineRegistryReader? MachineRegistry = null,
                                  string? MachineFolder = null);

/// <summary>
/// The checks of launcher spec §8.5, when a profile's page opens and again at Play. They write nothing: the link is
/// the inspection (<see cref="ModInspection"/>), and the lock is probed without taking it. The engine links again
/// when it starts, and writes the cache and the selectors then.
/// </summary>
public static class ProfileChecks
{
    public static ProfileCheckResult Run(ProfileEntry profile, CheckOptions? options = null)
    {
        options ??= new CheckOptions();
        if (profile.NeedsReview)
            throw new ProfileNeedsReviewException(
                $"{profile.Root} is not checked before the player accepts its review: nothing opens its paths until then");
        GameProfileRegistry registry = options.Registry ?? GameProfileRegistry.BuiltIn;
        LoadedProfile loaded = profile.Loaded ?? throw new ArgumentException(
            $"{profile.Id} cannot be checked: {profile.Problem ?? "its profile.toml is not read"}");
        ProfileDocument document = loaded.Document;
        var checks = new List<ProfileCheck>();
        void Add(ProfileCheckKind kind, CheckStatus status, string message) => checks.Add(new(kind, status, message));

        // Spec §12: a network path this machine's player has not approved is not opened, so the checks that would
        // open one fail and name it instead.
        IReadOnlyList<(string Key, string Path)> unapproved;
        try
        {
            unapproved = RootSafety.UnapprovedNetworkPaths(
                loaded.File, () => MachineState.Load(options.MachineFolder ?? MachineState.DefaultFolder()),
                OperatingSystem.IsWindows());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new IOException($"this machine's state cannot be read: {error.Message}", error);
        }
        string Unapproved(IEnumerable<(string Key, string Path)> entries)
            => string.Join("; ", entries.Select(entry => $"{entry.Key} names the network path {entry.Path}"))
               + ", which this machine's player has not approved; approve the location";
        var installUnapproved = unapproved.Where(entry => entry.Key.StartsWith("install.", StringComparison.Ordinal)).ToList();
        var foldersUnapproved = unapproved.Where(entry => entry.Key == "mods.folders").ToList();

        InstallRecognition install = installUnapproved.Count > 0
            ? new InstallRecognition(document.Install.Root, InstallKind.NotAnInstall, Unapproved(installUnapproved))
            : Installs.Recognize(document.Install.Root, registry, options.MachineRegistry);
        bool installed = install is { Kind: InstallKind.Supported, Problem: null }
                         && install.Game!.Id == document.Game;
        // The engine refuses to start without an overlay the profile names (--overlay-root), so a folder that is gone
        // fails the install's check, by name, and the link waits for it.
        List<string> missingOverlays = installed
            ? document.Install.Overlays.Where(overlay => !Directory.Exists(Path.GetFullPath(overlay, document.Install.Root)))
                .ToList()
            : [];
        Add(ProfileCheckKind.Install, installed && missingOverlays.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed, installed
            ? missingOverlays.Count == 0
                ? $"{install.Root} holds {install.Game!.DisplayTitle}"
                : $"the overlay folder{(missingOverlays.Count == 1 ? "" : "s")} {string.Join(", ", missingOverlays)} "
                  + $"{(missingOverlays.Count == 1 ? "is" : "are")} not there, and the engine does not start without "
                  + "its overlays; put the folder back or turn it off"
            : installUnapproved.Count > 0
                ? install.Problem!
                : (install.Kind == InstallKind.Supported && install.Problem == null
                    ? $"{install.Root} holds {install.Game!.DisplayTitle}, not the profile's game ({document.Game})"
                    : install.Problem!) + "; locate the install");

        string? recorded = document.Install.Fingerprint;
        if (!installed)
            Add(ProfileCheckKind.Fingerprint, CheckStatus.NotRun, "needs the install");
        else if (recorded == null || recorded == install.Fingerprint)
            Add(ProfileCheckKind.Fingerprint, CheckStatus.Passed, recorded == null
                ? "no fingerprint is recorded: the profile takes the install as it is"
                : "the catalog is the one the profile accepted");
        else
            Add(ProfileCheckKind.Fingerprint, CheckStatus.Warning,
                $"the install's SYS4INI.BIN changed since the profile accepted it ({recorded} is recorded, "
                + $"{install.Fingerprint} is installed); accept the install as it is now");

        bool held = RunLock.IsHeld(profile.Root, out string? lockProblem);
        string? rootProblem = lockProblem
                              ?? (held || File.Exists(Path.Combine(profile.Root, RunLock.FileName)) ? null : WriteProblem(profile.Root));
        Add(ProfileCheckKind.Root, rootProblem == null ? CheckStatus.Passed : CheckStatus.Failed,
            rootProblem ?? $"{profile.Root} can be written");

        LibraryView? view = null;
        if (foldersUnapproved.Count > 0)
            Add(ProfileCheckKind.Packages, CheckStatus.Failed, Unapproved(foldersUnapproved));
        else if (document.Mods == null)
            Add(ProfileCheckKind.Packages, CheckStatus.Passed,
                "the profile has no [mods] table: the engine reads the packages of its own mods/ folder");
        else
        {
            view = Library.View(document, Library.Index(document), install.Fingerprint);
            var refused = view.Packages.Where(package => package.Enabled
                                                         && package.State is PackageState.Missing or PackageState.NeedsExtended)
                .ToList();
            Add(ProfileCheckKind.Packages, refused.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed, refused.Count == 0
                ? $"{document.Mods.Enabled.Count()} enabled packages are found and the mode admits them"
                : string.Join("; ", refused.Select(package => $"{package.Id}: {package.Reasons[0]}")));
        }

        LinkCheck? link = null;
        if (!installed)
            Add(ProfileCheckKind.Link, CheckStatus.NotRun, "needs the install");
        else if (unapproved.Count > 0)
            Add(ProfileCheckKind.Link, CheckStatus.NotRun, "needs the network locations approved");
        else if (missingOverlays.Count > 0)
            Add(ProfileCheckKind.Link, CheckStatus.NotRun, "needs the overlay folders");
        else
        {
            try
            {
                link = LinkComposition.Link(profile.Root, registry, options.MachineFolder);
                if (link.Report is not { } report)
                    Add(ProfileCheckKind.Link, CheckStatus.Failed, "a package does not open: "
                        + string.Join("; ", link.Discovery.Lines.Where(line => line.Stops)));
                else if (!report.Stops && link.Dropped.Count > 0)
                    Add(ProfileCheckKind.Link, CheckStatus.Warning,
                        $"the stack links without {string.Join(", ", link.Dropped)}, which on_failure = \"skip\" drops");
                else
                    Add(ProfileCheckKind.Link, report.Stops ? CheckStatus.Failed : CheckStatus.Passed,
                        $"the stack {(report.Stops ? "does not link" : "links")}: {report.Summary()}");
            }
            catch (Exception error) when (error is RunResolutionException or IOException or InvalidDataException
                                              or UnauthorizedAccessException)
            {
                Add(ProfileCheckKind.Link, CheckStatus.Failed, error.Message);
            }
        }

        Add(ProfileCheckKind.Lock, held ? CheckStatus.Failed : CheckStatus.Passed,
            held ? "running: an engine holds the profile's lock" : "no engine holds the profile's lock");
        return new ProfileCheckResult(checks, install, view, link) { MissingOverlays = missingOverlays };
    }

    /// <summary>"Accept the install as it is now": records the install's fingerprint in <c>profile.toml</c>, under the
    /// profile's lock.</summary>
    public static LoadedProfile AcceptInstall(ProfileEntry profile, string fingerprint, OmeHome home)
        => ProfileStore.Update(profile, document => document with
        {
            Install = document.Install with { Fingerprint = fingerprint },
        }, home);

    /// <summary>Whether a root without a <c>run.lock</c> can be written: a file that is gone when it closes. A root the
    /// launcher made always has the lock file, which the probe opens for writing instead.</summary>
    private static string? WriteProblem(string root)
    {
        string probe = Path.Combine(root, $".ome-write-check-{Guid.NewGuid():N}");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                                              FileOptions.DeleteOnClose);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"{root} cannot be written: {error.Message}";
        }
    }
}
