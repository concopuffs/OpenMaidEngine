using System.Diagnostics;
using Ome.Engine.Profiles;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Mods.Launch;

/// <summary>
/// The dialogs of plan §6.10 on a synthetic install whose saves sit in <c>SAVE</c>: Shortcut (both forms from the one
/// builder, each field copied, a shortcut written on the desktop and replaced only when the player says so), Duplicate
/// with and without the saves, Rename (the name, never the id), Delete (asked first, refused while the game runs), and
/// Add an existing profile with its review (spec §12): accepted, declined, a folder with no profile, a network path that
/// needs approval, and a link, which stops it.
/// </summary>
public sealed class DialogsTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly string _install;
    private readonly ProfileEntry _profile;

    public DialogsTests()
    {
        _install = Path.Combine(_home.Root, "installs", "kamidori");
        Directory.CreateDirectory(_install);
        TestHome.Catalog(_install, "kamidori", settings: [("SAVEPATH", "SAVE"), ("REGFILEPATH", "")]);
        _profile = _home.Profile("kamidori", "kamidori", _install, "Kamidori");
        Write(Path.Combine(_profile.Root, "SAVE", "SAVE01.DAT"), "save");
        Write(Path.Combine(_profile.Root, "mods", "selectors.toml"), "[selectors]\n\"example.alpha\" = 1\n");
        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings
                             with { Art = ArtConsent.Never, LastProfile = _profile.Id });
    }

    public void Dispose() => _home.Dispose();

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private async Task<(MainViewModel Model, RecordingHost Host)> Open()
    {
        var model = new MainViewModel(_home.Session(), new ArtOptions(Handler: new HttpClientHandler()));
        var host = new RecordingHost();
        model.Host = host;
        await model.StartAsync();
        return (model, host);
    }

    private static OmePlatform Platform => LauncherEnvironment.Current.Platform;

    [Fact]
    public void TheShortcutDialogGivesBothFormsAndWritesAShortcut() => Headless.Run(async () =>
    {
        var (model, host) = await Open();
        using (model)
        {
            model.Page!.Shortcut.Execute(null);
            ShortcutViewModel shortcut = Assert.IsType<ShortcutViewModel>(model.Dialog);
            ShortcutForms forms = EngineCommands.Shortcut(model.Session.LauncherProgram, _home.Home, model.Page.Entry,
                                                          model.Session.Engine);

            // Through the launcher is the default: --launch with the id, and --home since the home was named.
            Assert.True(shortcut.ThroughLauncher);
            Assert.Equal(forms.ThroughLauncher.Program, shortcut.Target);
            Assert.Equal(ShortcutFile.CommandLine(forms.ThroughLauncher, Platform), shortcut.CommandLine);
            Assert.StartsWith("--launch kamidori --home ", shortcut.Arguments);
            shortcut.CopyLine.Execute(null);
            Assert.Equal(shortcut.CommandLine, host.Clipboard);
            Assert.Equal("Copied.", shortcut.Status);

            // The engine directly is what Play runs.
            shortcut.ChooseDirect.Execute(null);
            Assert.Null(shortcut.Status);
            Assert.Equal((forms.Direct.Program, forms.Direct.WorkingDirectory), (shortcut.Target, shortcut.WorkingFolder));
            Assert.EndsWith(ShortcutFile.Quote(Path.GetFullPath(_profile.Root), Platform), shortcut.Arguments);
            shortcut.CopyArguments.Execute(null);
            Assert.Equal(shortcut.Arguments, host.Clipboard);

            string path = ShortcutFile.PathFor(_home.Desktop, "Kamidori", Platform);
            shortcut.CreateOnDesktop.Execute(null);
            await Headless.Until(() => shortcut.Status?.StartsWith("Created") == true, "the shortcut");
            Assert.Equal($"Created {path}", shortcut.Status);
            if (OperatingSystem.IsWindows())
                Assert.Equal(forms.Direct.Program, ShortcutFile.ReadWindowsLink(path).Target);

            // A shortcut already there is replaced only when the player says so.
            shortcut.ChooseLauncher.Execute(null);
            Task declined = shortcut.CreateAsync(_home.Desktop);
            await Headless.Until(() => model.HasConfirm, "the question");
            Assert.Equal("Replace the shortcut?", model.Confirm!.Title);
            model.Confirm.No.Execute(null);
            await declined;
            if (OperatingSystem.IsWindows())
                Assert.Equal(forms.Direct.Program, ShortcutFile.ReadWindowsLink(path).Target);
            Task replaced = shortcut.CreateAsync(_home.Desktop);
            await Headless.Until(() => model.HasConfirm, "the question");
            model.Confirm!.Yes.Execute(null);
            await replaced;
            if (OperatingSystem.IsWindows())
                Assert.Equal(forms.ThroughLauncher.Program, ShortcutFile.ReadWindowsLink(path).Target);

            shortcut.Close.Execute(null);
            Assert.False(model.HasDialog);
        }
    });

    [Fact]
    public void DuplicateCopiesTheProfileWithOrWithoutItsSaves() => Headless.Run(async () =>
    {
        var (model, _) = await Open();
        using (model)
        {
            model.Page!.Duplicate.Execute(null);
            DuplicateViewModel duplicate = Assert.IsType<DuplicateViewModel>(model.Dialog);
            Assert.Equal(("Kamidori (copy)", "kamidori-copy", true), (duplicate.Name, duplicate.Id, duplicate.WithSaves));
            Assert.True(duplicate.CanLeaveSaves);
            await duplicate.CreateAsync();
            Assert.False(model.HasDialog);
            ProfileEntry copy = model.Selected!.Entry;
            Assert.Equal(("kamidori-copy", "Kamidori (copy)"), (copy.Id, copy.DisplayName));
            Assert.Equal("save", File.ReadAllText(Path.Combine(copy.Root, "SAVE", "SAVE01.DAT")));
            Assert.Equal("kamidori-copy", LauncherSettingsStore.Load(_home.Home).Settings.LastProfile);

            // From the copy, while its page's checks have not found the install: the saves come along, since their
            // folder is not known. Once the checks find it they can stay behind, with the selectors.
            string catalog = Path.Combine(_install, Installs.CatalogFile);
            byte[] bytes = File.ReadAllBytes(catalog);
            File.Delete(catalog);
            await model.Page!.CheckAsync();
            model.Page.Duplicate.Execute(null);
            duplicate = Assert.IsType<DuplicateViewModel>(model.Dialog);
            Assert.True(duplicate.CannotLeaveSaves && duplicate.WithSaves);
            Assert.False(duplicate.ToggleSaves.CanExecute(null));
            File.WriteAllBytes(catalog, bytes);
            await model.Page.CheckAsync();
            Assert.True(duplicate.CanLeaveSaves && duplicate.ToggleSaves.CanExecute(null));
            duplicate.Name = "Fresh";
            duplicate.ToggleSaves.Execute(null);
            Assert.False(duplicate.WithSaves);
            await duplicate.CreateAsync();
            ProfileEntry fresh = model.Selected!.Entry;
            Assert.Equal("fresh", fresh.Id);
            Assert.False(Directory.Exists(Path.Combine(fresh.Root, "SAVE")));
            Assert.False(File.Exists(Path.Combine(fresh.Root, "mods", "selectors.toml")));
            Assert.Equal(3, model.Rows.Count());
        }
    });

    [Fact]
    public void RenameChangesTheNameNeverTheId() => Headless.Run(async () =>
    {
        var (model, _) = await Open();
        using (model)
        {
            Assert.True(model.Page!.Rename.CanExecute(null));
            model.Page.Rename.Execute(null);
            RenameViewModel rename = Assert.IsType<RenameViewModel>(model.Dialog);
            Assert.Equal(("Kamidori", "The id stays kamidori: shortcuts and scripts name it."), (rename.Name, rename.IdText));
            rename.Name = "  Main run  ";
            await rename.SaveAsync();
            Assert.False(model.HasDialog);
            Assert.Equal(("kamidori", "Main run"), (model.Selected!.Id, model.Selected.Name));
            Assert.Equal("Main run", model.Page!.Entry.Loaded!.Document.Name);

            // An empty name shows the profile by its id.
            model.Page.Rename.Execute(null);
            rename = Assert.IsType<RenameViewModel>(model.Dialog);
            rename.Name = "";
            await rename.SaveAsync();
            Assert.Equal("kamidori", model.Selected!.Name);
            Assert.Null(ProfileDocuments.Read(_profile.Root)!.Document.Name);
        }
    });

    [Fact]
    public void DeleteAsksFirstAndIsOffWhileTheGameRuns() => Headless.Run(async () =>
    {
        ProfileEntry other = _home.Profile("other", "kamidori", _install, "Other");
        var (model, _) = await Open();
        using (model)
        {
            Assert.True(model.Page!.Delete.CanExecute(null));
            Task asked = model.Page.DeleteAsync();
            await Headless.Until(() => model.HasConfirm, "the question");
            Assert.Equal("Delete Kamidori?", model.Confirm!.Title);
            Assert.EndsWith(" It holds 1 save.", model.Confirm.Text);
            Assert.Equal([_profile.Root], model.Confirm.Items);
            Assert.True(model.Confirm.Danger);
            model.Confirm.No.Execute(null);
            await asked;
            Assert.True(Directory.Exists(_profile.Root));

            asked = model.Page.DeleteAsync();
            await Headless.Until(() => model.HasConfirm, "the question");
            model.Confirm!.Yes.Execute(null);
            await asked;
            Assert.False(Directory.Exists(_profile.Root));
            Assert.Equal(["other"], model.Rows.Select(row => row.Id));
            Assert.Equal("other", model.Selected!.Id);

            // While the game runs, Delete (and Rename and Duplicate) are off.
            Assert.True(RunLock.TryAcquire(other.Root, markStart: true, out RunLock? engine, out _));
            using (engine)
            {
                await model.RefreshRunningAsync();
                await Headless.Until(() => model.Page!.IsRunning, "running");
                Assert.False(model.Page!.Delete.CanExecute(null));
                Assert.False(model.Page.Rename.CanExecute(null));
                Assert.False(model.Page.Duplicate.CanExecute(null));
                Assert.True(model.Page.Shortcut.CanExecute(null));
            }
        }
    });

    /// <summary>A profile folder from someone else: its file, written by hand.</summary>
    private string Foreign(string name, string modsFolder, string profileName = "Theirs")
    {
        string root = Path.Combine(_home.Root, "received", name);
        Write(Path.Combine(root, ProfileFile.FileName),
              $"ome_profile = 1\nname = \"{profileName}\"\ngame = \"kamidori\"\nmode = \"native\"\n\n"
              + $"[install]\nroot = '{_install}'\n\n[mods]\nfolders = ['{modsFolder}']\norder = [\"their.mod\"]\n");
        return root;
    }

    [Fact]
    public void AnExistingProfileIsReviewedThenAcceptedOrDeclined() => Headless.Run(async () =>
    {
        string mods = Path.Combine(_home.Root, "their-mods");
        string theirs = Foreign("Their Kamidori", mods);
        var (model, host) = await Open();
        using (model)
        {
            host.Folder = theirs;
            model.AddExisting.Execute(null);
            await Headless.Until(() => model.Dialog is ReviewViewModel { Loaded: true }, "the review");
            var review = (ReviewViewModel)model.Dialog!;
            Assert.Equal("their-kamidori", model.Selected!.Id);
            Assert.True(model.Selected.Entry.NeedsReview);
            Assert.True(review.Read);
            Assert.Equal(("Theirs", "Kamidori Alchemy Meister", "Compatibility", "their.mod"),
                         (review.NameText, review.GameText, review.ModeText, review.ModsText));
            Assert.Equal([("The profile", theirs), ("The install", _install), ("A mod folder", mods)],
                         review.Paths.Select(path => (path.Label, path.Path)));
            Assert.All(review.Paths, path => Assert.Null(path.Note));
            Assert.False(review.HasStop);

            // Later: the page keeps its notice, whose action opens the review again; Play stays off.
            review.Later.Execute(null);
            Assert.False(model.HasDialog);
            Assert.Equal("Review…", model.Page!.NoticeAction);
            Assert.False(model.Page.Play.CanExecute(null));
            Assert.Equal("Not checked until the review is accepted.", model.Page.Overview.ChecksPending);
            model.Page.NoticeCommand.Execute(null);
            await Headless.Until(() => model.Dialog is ReviewViewModel { Loaded: true }, "the review again");
            review = (ReviewViewModel)model.Dialog!;

            await review.AcceptAsync();
            Assert.False(model.HasDialog);
            Assert.False(model.Selected!.Entry.NeedsReview);
            Assert.Contains(theirs, LauncherSettingsStore.Load(_home.Home).Settings.Reviewed);

            // Declined, the root is forgotten and its folder stays.
            string declined = Foreign("declined", mods);
            host.Folder = declined;
            model.AddExisting.Execute(null);
            await Headless.Until(() => model.Dialog is ReviewViewModel { Loaded: true }, "the second review");
            await ((ReviewViewModel)model.Dialog!).DeclineAsync();
            Assert.False(model.HasDialog);
            Assert.DoesNotContain(model.Rows, row => row.Id == "declined");
            Assert.True(File.Exists(Path.Combine(declined, ProfileFile.FileName)));
            Assert.DoesNotContain(LauncherSettingsStore.Load(_home.Home).Settings.Roots, root => root.Id == "declined");

            // A folder with no profile, and one inside OME home's profiles, are refused with the reason.
            host.Folder = Path.Combine(_home.Root, "installs");
            Task told = model.AddExistingAsync();
            await Headless.Until(() => model.HasConfirm, "the refusal");
            Assert.Equal(("The profile was not added", $"{Path.Combine(_home.Root, "installs")} holds no profile.toml."),
                         (model.Confirm!.Title, model.Confirm.Text));
            Assert.False(model.Confirm.HasCancel);
            model.Confirm.Yes.Execute(null);
            await told;
            host.Folder = _profile.Root;
            told = model.AddExistingAsync();
            await Headless.Until(() => model.HasConfirm, "the second refusal");
            Assert.Contains("is in or around", model.Confirm!.Text);
            model.Confirm.Yes.Execute(null);
            await told;
        }
    });

    [Fact]
    public void ANetworkPathNeedsApprovalAndALinkStopsTheReview() => Headless.Run(async () =>
    {
        string linked = Foreign("linked", Path.Combine(_home.Root, "m"));
        string target = Path.Combine(_home.Root, "elsewhere");
        Directory.CreateDirectory(target);
        Junction(Path.Combine(linked, "SAVE"), target);
        const string share = @"\\ome-launcher-test\share\mods";
        string remote = Foreign("remote", OperatingSystem.IsWindows() ? share : Path.Combine(_home.Root, "m"));
        var (model, host) = await Open();
        using (model)
        {
            host.Folder = linked;
            model.AddExisting.Execute(null);
            await Headless.Until(() => model.Dialog is ReviewViewModel { Loaded: true }, "the review");
            var review = (ReviewViewModel)model.Dialog!;
            Assert.True(review.HasStop);
            Assert.Contains("is a symbolic link or junction", review.Stop);
            Assert.False(review.Accept.CanExecute(null));
            review.Later.Execute(null);

            if (!OperatingSystem.IsWindows()) return;   // network paths are a Windows notion (spec §12)
            host.Folder = remote;
            model.AddExisting.Execute(null);
            await Headless.Until(() => model.Dialog is ReviewViewModel { Loaded: true }, "the second review");
            review = (ReviewViewModel)model.Dialog!;
            ReviewPathRow path = Assert.Single(review.Paths, row => row.Path == share);
            Assert.Equal(("another computer · needs your approval", true), (path.Note, path.NeedsApproval));
            Assert.False(model.Session.Machine().IsApproved(share));
            await review.AcceptAsync();
            Assert.True(model.Session.Machine().IsApproved(share));
        }
        Directory.Delete(Path.Combine(linked, "SAVE"));   // the link itself, so the cleanup never walks through it
    });

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
