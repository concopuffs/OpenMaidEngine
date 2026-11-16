using System.Diagnostics;
using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>
/// Launcher spec §12, gate 14 (launcher plan §5.10): the launcher's side of trust. A root the launcher did not create is
/// reviewed before anything opens its paths; a network path is opened only once this machine approved it; a root that
/// holds a link is refused at the review and before a start; an arguments key makes a profile unusable; a cover outside
/// the root is refused. The engine's side of the gate, which M1 built, is tested where it lives:
/// <list type="bullet">
/// <item>a cache entry another machine wrote is not used: <c>ProfileModsTests.AnEntryAnotherMachineWroteIsNotUsed</c>;</item>
/// <item>an <c>[arguments]</c> or <c>[environment]</c> key stops the engine as an unknown key:
/// <c>RunResolverTests.AProblemStopsWithItsReason</c>;</item>
/// <item>an unapproved network path stops the engine, and a profile or an OME home from elsewhere brings no approval:
/// <c>RootSafetyTests.ANetworkPathThePlayerDidNotApproveStopsBeforeItIsOpened</c> and
/// <c>AProfileRootOrAnOmeHomeFromElsewhereBringsNoApproval</c>;</item>
/// <item>a root that holds a link stops the engine: <c>RootSafetyTests.AProfileRootWithALinkStopsBeforeItsFileIsRead</c>.</item>
/// </list>
/// </summary>
public sealed class ProfileTrustTests : IDisposable
{
    private const string Share = @"\\ome-gate14\share";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-trust-" + Guid.NewGuid().ToString("N"));
    private readonly OmeHome _home;
    private readonly string _machineFolder;

    public ProfileTrustTests()
    {
        _home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
        _machineFolder = Path.Combine(_root, "machine");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private MachineState Machine() => MachineState.Load(_machineFolder);

    private CheckOptions Options => new(MachineRegistry: (_, _) => null, MachineFolder: _machineFolder);

    /// <summary>A profile folder from someone else: its file, written by hand, names paths that do not exist here.</summary>
    private string Foreign(string name, string install, string modsFolder, string extra = "")
    {
        string root = Path.Combine(_root, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ProfileFile.FileName),
            $"ome_profile = 1\nname = \"Theirs\"\ngame = \"kamidori\"\nmode = \"native\"\n\n[install]\nroot = '{install}'\n\n"
            + $"[mods]\nfolders = ['{modsFolder}']\norder = [\"their.mod\", \"-their.other\"]\n" + extra);
        return root;
    }

    private IReadOnlyList<ProfileEntry> List(LauncherSettings settings) => ProfileStore.List(_home, settings, Machine);

    [Fact]
    public void ARootFromElsewhereIsReviewedBeforeAnyOfItsPathsIsOpened()
    {
        string install = Path.Combine(_root, "their-install"), mods = Path.Combine(_root, "their-mods");
        string root = Foreign("received", install, mods);
        LauncherSettings settings = ProfileTrust.AddExisting(_home, LauncherSettings.Defaults, root, "theirs", Machine,
                                                             picked: true);
        ProfileEntry entry = ProfileStore.Find(List(settings), "theirs")!;
        Assert.True(entry.NeedsReview);
        Assert.Throws<ProfileNeedsReviewException>(() => ProfileChecks.Run(entry, Options));

        RootReview review = ProfileTrust.Review(entry, Machine);
        Assert.True(review.Read);
        Assert.True(review.CanAccept);
        Assert.Equal(("Theirs", "kamidori", "native"), (review.Name, review.Game, review.Mode));
        Assert.Equal(["root", "install.root", "mods.folders"], review.Paths.Select(path => path.Key));
        Assert.Equal([root, install, mods], review.Paths.Select(path => path.Path));
        Assert.Empty(review.NetworkPaths);
        Assert.Equal(["their.mod"], review.EnabledMods);
        Assert.False(Directory.Exists(install) || Directory.Exists(mods));

        // Accepted, the root is checked like any other, and its install is now looked at.
        settings = ProfileTrust.Accept(settings, review, Machine);
        entry = ProfileStore.Find(List(settings), "theirs")!;
        Assert.False(entry.NeedsReview);
        ProfileCheckResult result = ProfileChecks.Run(entry, Options);
        Assert.EndsWith("holds no SYS4INI.BIN; locate the install", result[ProfileCheckKind.Install].Message);

        // A root the launcher makes elsewhere is its own, and needs no review.
        ProfileDocument document = ProfileDocument.New(null, "kamidori", install, null, _home);
        ProfileChange made = ProfileStore.Create(_home, settings, "mine", document, elsewhere: Path.Combine(_root, "mine"));
        Assert.False(ProfileStore.Find(List(made.Settings), "mine")!.NeedsReview);
    }

    [Fact]
    public void ANetworkPathIsOpenedOnlyOnceThisMachineApprovedIt()
    {
        if (!OperatingSystem.IsWindows()) return;   // network paths are a Windows notion (spec §12)
        string install = Share + @"\games\kamidori", mods = Share + @"\mods";
        string root = Foreign("received", install, mods);
        LauncherSettings settings = ProfileTrust.AddExisting(_home, LauncherSettings.Defaults, root, "theirs", Machine,
                                                             picked: true);
        RootReview review = ProfileTrust.Review(ProfileStore.Find(List(settings), "theirs")!, Machine);
        Assert.Equal([("install.root", false), ("mods.folders", false)],
                     review.NetworkPaths.Select(path => (path.Key, path.Approved)));

        // A profile the launcher made names the same share: its checks name the paths and open neither.
        ProfileDocument document = ProfileDocument.New(null, "kamidori", install, null, _home);
        document = document with { Mods = document.Mods! with { Folders = [mods] } };
        ProfileEntry mine = ProfileStore.Create(_home, settings, "mine", document).Profile;
        ProfileCheckResult result = ProfileChecks.Run(mine, Options);
        Assert.Contains($"install.root names the network path {install}, which this machine's player has not approved",
                        result[ProfileCheckKind.Install].Message);
        Assert.Contains($"mods.folders names the network path {mods}", result[ProfileCheckKind.Packages].Message);
        Assert.Equal(CheckStatus.NotRun, result[ProfileCheckKind.Link].Status);

        // Accepting the review approves its locations on this machine, which the engine then lets through.
        ProfileTrust.Accept(settings, review, Machine);
        Assert.Empty(RootSafety.UnapprovedNetworkPaths(ProfileStore.Find(List(settings), "theirs")!.Loaded!.File,
                                                       Machine, windows: true));
        Assert.True(Machine().IsApproved(install) && Machine().IsApproved(mods));

        // A network root only named is not approved, and is not read; one the player picked is approved by that. The
        // picked one is not listed here, since listing an approved share would reach for it.
        string picked = Share + @"\picked", named = @"\\ome-gate14-other\share\named";
        settings = ProfileTrust.AddExisting(_home, settings, named, "named", Machine, picked: false);
        Assert.False(Machine().IsApproved(named));
        ProfileTrust.AddExisting(_home, settings, picked, "picked", Machine, picked: true);
        Assert.True(Machine().IsApproved(picked));
        ProfileEntry unread = ProfileStore.Find(List(settings), "named")!;
        Assert.True(unread.NeedsReview);
        Assert.Contains("has not approved", unread.Problem);
        RootReview first = ProfileTrust.Review(unread, Machine);
        Assert.False(first.Read);
        Assert.Equal([named], first.NetworkPaths.Select(path => path.Path));
        Assert.Equal(settings, ProfileTrust.Accept(settings, first, Machine));
        Assert.True(Machine().IsApproved(named));
    }

    [Fact]
    public void ARootThatHoldsALinkIsRefusedAtTheReviewAndBeforeAStart()
    {
        string root = Foreign("linked", Path.Combine(_root, "i"), Path.Combine(_root, "m"));
        string target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(target);
        Junction(Path.Combine(root, "SAVE"), target);
        LauncherSettings settings = ProfileTrust.AddExisting(_home, LauncherSettings.Defaults, root, "linked", Machine,
                                                             picked: true);
        RootReview review = ProfileTrust.Review(ProfileStore.Find(List(settings), "linked")!, Machine);
        Assert.Contains("is a symbolic link or junction", review.Link);
        Assert.False(review.CanAccept);
        Assert.Throws<InvalidOperationException>(() => ProfileTrust.Accept(settings, review, Machine));
        Assert.Contains("is a symbolic link or junction", ProfileTrust.BeforeStart(root));
        Assert.Null(ProfileTrust.BeforeStart(Foreign("plain", "i", "m")));
        Directory.Delete(Path.Combine(root, "SAVE"));   // the link itself, so the cleanup never walks through it
    }

    [Fact]
    public void AnArgumentsKeyMakesAProfileUnusableAndACoverOutsideTheRootIsRefused()
    {
        string root = Foreign("arguments", "i", "m", "\n[arguments]\nextra = [\"--log-file\", \"C:/x\"]\n");
        LauncherSettings settings = ProfileTrust.AddExisting(_home, LauncherSettings.Defaults, root, "arguments", Machine,
                                                             picked: true);
        ProfileEntry entry = ProfileStore.Find(List(settings), "arguments")!;
        Assert.False(entry.Usable);
        Assert.Contains("arguments: unknown key", entry.Problem);
        Assert.False(ProfileTrust.Review(entry, Machine).CanAccept);

        string covered = Foreign("covered", "i", "m", "\n[launcher]\ncover = \"../cover.png\"\n");
        File.WriteAllBytes(Path.Combine(_root, "cover.png"), [0x89, 0x50]);
        settings = ProfileTrust.AddExisting(_home, settings, covered, "covered", Machine, picked: true);
        LoadedProfile loaded = ProfileStore.Find(List(settings), "covered")!.Loaded!;
        Assert.Null(loaded.Document.Launcher.Cover);
        Assert.Contains(loaded.Warnings, warning => warning.Contains("is not a file inside the profile root"));
    }

    private static void Junction(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using Process process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
}
