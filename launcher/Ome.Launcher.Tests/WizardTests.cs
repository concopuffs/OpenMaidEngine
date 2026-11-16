using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Ome.Engine.Profiles;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Launcher.Views;
using Ome.Mods.Launch;

/// <summary>
/// The new-profile wizard (launcher spec §8.3, plan §6.9) on synthetic installs whose saves sit in <c>SAVE</c>: the
/// first step's install (known, browsed, dropped on the window) with its overlays and its refusals; each start (nothing,
/// a copy with and without its saves, the bare engine's data with the Extended notice, the original game's saves); and
/// a refused Create, which leaves nothing behind.
/// </summary>
public sealed class WizardTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly string _install;
    private readonly ProfileEntry _source;

    public WizardTests()
    {
        _install = Path.Combine(_home.Root, "installs", "kamidori");
        Directory.CreateDirectory(_install);
        TestHome.Catalog(_install, "kamidori", extra: "TITLE.AGF", settings: [("SAVEPATH", "SAVE"), ("REGFILEPATH", "")]);
        Write(Path.Combine(_install, "patch", "MENU.BIN"), "");
        File.WriteAllBytes(Path.Combine(_install, "patch", "TITLE.AGF"), "BM\0\0"u8.ToArray());
        _source = _home.Profile("kamidori", "kamidori", _install, "Kamidori");
        Write(Path.Combine(_source.Root, "SAVE", "SAVE01.DAT"), "source save");
        Write(Path.Combine(_source.Root, "mods", "selectors.toml"), "[selectors]\n\"example.alpha\" = 1\n");
        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings
                             with { Art = ArtConsent.Never, LastProfile = _source.Id });
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

    /// <summary>Opens the wizard on the known install and goes to the second step.</summary>
    private static async Task<WizardViewModel> AtProfileStep(MainViewModel model)
    {
        model.NewProfile.Execute(null);
        WizardViewModel wizard = model.Wizard!;
        wizard.Known.Single().Pick.Execute(null);
        await Headless.Until(() => wizard.CanContinue, "the install recognized");
        wizard.Next.Execute(null);
        Assert.True(wizard.OnProfile);
        return wizard;
    }

    private static async Task<ProfileEntry> CreateAndOpen(MainViewModel model, WizardViewModel wizard)
    {
        Assert.True(wizard.Create.CanExecute(null));
        await wizard.CreateAsync();
        Assert.Null(wizard.Error);
        Assert.False(model.HasWizard);
        return model.Selected!.Entry;
    }

    private static ProfileDocument DocumentOf(ProfileEntry entry) => ProfileDocuments.Read(entry.Root)!.Document;

    [Fact]
    public void TheFirstStepShowsTheInstallAndNothingStartsAnEmptyProfile() => Headless.Run(async () =>
    {
        var (model, _) = await Open();
        using (model)
        {
            Assert.True(model.NewProfile.CanExecute(null));
            model.NewProfile.Execute(null);
            WizardViewModel wizard = model.Wizard!;
            Assert.False(model.NewProfile.CanExecute(null));
            Assert.True(wizard.OnGame);
            Assert.False(wizard.Next.CanExecute(null));
            KnownInstallChoice known = Assert.Single(wizard.Known);
            Assert.Equal((Path.GetFullPath(_install), "Kamidori"), (Path.GetFullPath(known.Root), known.Profiles));

            known.Pick.Execute(null);
            await Headless.Until(() => wizard.CanContinue, "the install recognized");
            Assert.Equal(GameProfileRegistry.BuiltIn.Find("kamidori")!.DisplayTitle, wizard.FoundTitle);
            Assert.EndsWith("supported", wizard.FoundSub);
            Assert.Contains(" · catalog ", wizard.FoundPath);
            WizardOverlayViewModel patch = Assert.Single(wizard.Overlays);
            Assert.Equal(("Use patch as an overlay", true), (patch.Label, patch.On));
            Assert.True(wizard.AllowBmp);
            patch.Toggle.Execute(null);
            Assert.False(patch.On);
            patch.Toggle.Execute(null);

            wizard.Next.Execute(null);
            Assert.Equal(wizard.FoundTitle, wizard.Name);
            Assert.True(wizard.FromNothing && wizard.Compatibility);
            Assert.True(wizard.HasCopySources);
            Assert.False(wizard.HasNativeSaves);
            Assert.False(wizard.HasBareData);
            Assert.False(wizard.StartBare.CanExecute(null));
            wizard.Name = "Kamidori (new)";
            Assert.Equal("kamidori-new", wizard.Id);
            Assert.Equal(Path.Combine(_home.Home.ProfilesDirectory, "kamidori-new"), wizard.LocationText);
            Assert.Equal(ProfileDocument.LibraryFolders(_home.Home, "kamidori"), wizard.Folders.Select(folder => folder.Path));

            ProfileEntry made = await CreateAndOpen(model, wizard);
            Assert.Equal("kamidori-new", made.Id);
            Assert.Equal(made.Id, model.Page!.Entry.Id);
            ProfileDocument document = DocumentOf(made);
            Assert.Equal(("Kamidori (new)", ProfileMode.Native), (document.Name, document.Mode));
            Assert.Equal([Path.Combine(_install, "patch")], document.Install.Overlays);
            Assert.True(document.Install.AllowBmpAsAgf);
            Assert.True(File.Exists(Path.Combine(made.Root, "OME.INI")));
            Assert.False(Directory.Exists(Path.Combine(made.Root, "SAVE")));
            Assert.Equal("kamidori-new", LauncherSettingsStore.Load(_home.Home).Settings.LastProfile);
            Assert.True(model.NewProfile.CanExecute(null));
        }
    });

    [Fact]
    public void ACopyTakesTheSourcesSavesOrLeavesThem() => Headless.Run(async () =>
    {
        var (model, host) = await Open();
        using (model)
        {
            WizardViewModel wizard = await AtProfileStep(model);
            wizard.Name = "With saves";
            wizard.StartCopy.Execute(null);
            Assert.Equal("Kamidori", wizard.CopySource!.Label);
            Assert.True(wizard.CopySaves);
            // The copy's mods come from its source's folders, which the Advanced list starts with and the copy keeps.
            string extra = Path.Combine(_home.Root, "extra-mods");
            Directory.CreateDirectory(extra);
            Assert.Equal(DocumentOf(_source).Mods!.Folders, wizard.Folders.Select(folder => folder.Path));
            host.Folder = extra;
            wizard.AddFolder.Execute(null);
            await Headless.Until(() => wizard.Folders.Count == DocumentOf(_source).Mods!.Folders.Count + 1, "the folder");
            ProfileEntry with = await CreateAndOpen(model, wizard);
            Assert.Equal([.. DocumentOf(_source).Mods!.Folders, extra], DocumentOf(with).Mods!.Folders);
            Assert.Equal("source save", File.ReadAllText(Path.Combine(with.Root, "SAVE", "SAVE01.DAT")));
            Assert.True(File.Exists(Path.Combine(with.Root, "mods", "selectors.toml")));
            Assert.Equal("With saves", DocumentOf(with).Name);
            Assert.Equal([Path.Combine(_install, "patch")], DocumentOf(with).Install.Overlays);

            wizard = await AtProfileStep(model);
            wizard.Name = "Without saves";
            wizard.StartCopy.Execute(null);
            wizard.ToggleCopySaves.Execute(null);
            ProfileEntry without = await CreateAndOpen(model, wizard);
            Assert.False(Directory.Exists(Path.Combine(without.Root, "SAVE")));
            Assert.False(File.Exists(Path.Combine(without.Root, "mods", "selectors.toml")));
        }
    });

    [Fact]
    public void TheBareEnginesDataSaysWhenItNeedsExtended() => Headless.Run(async () =>
    {
        GameProfileManifest kamidori = GameProfileRegistry.BuiltIn.Find("kamidori")!;
        string machine = _home.CheckOptions.MachineFolder!;
        string bare = Path.Combine(machine, "games", kamidori.PersistenceNamespace);
        Write(Path.Combine(bare, "SAVE", "SAVE03.DAT"), "bare save");
        _home.Package("kamidori", "test.lua", target: "extended", parent: Path.Combine(bare, "mods"));
        Write(Path.Combine(machine, "OME.INI"), "[ui]\nLanguage=en\n");

        var (model, _) = await Open();
        using (model)
        {
            WizardViewModel wizard = await AtProfileStep(model);
            Assert.True(wizard.HasBareData);
            Assert.Equal("(1 saves, 1 mods)", wizard.BareCounts);
            wizard.StartBare.Execute(null);
            await Headless.Until(() => wizard.StartForcesExtended, "the plan");
            Assert.True(wizard.Extended);
            Assert.False(wizard.ChooseCompatibility.CanExecute(null));
            Assert.StartsWith("This profile starts in Extended mode, which the mods the engine ran need: test.lua: ",
                              wizard.ExtendedNote);

            ProfileEntry made = await CreateAndOpen(model, wizard);
            ProfileDocument document = DocumentOf(made);
            Assert.Equal(ProfileMode.Extended, document.Mode);
            Assert.Equal(["test.lua"], document.Mods!.Enabled);
            Assert.Contains(document.Mods.Folders, folder => OmeHome.PathsEqual(
                folder, Path.Combine(bare, "mods"), LauncherEnvironment.Current.Platform));
            Assert.Equal("bare save", File.ReadAllText(Path.Combine(made.Root, "SAVE", "SAVE03.DAT")));
            Assert.Contains("Language=en", File.ReadAllText(Path.Combine(made.Root, "OME.INI")));
        }
    });

    [Fact]
    public void TheOriginalGamesSavesComeWithItsSettings() => Headless.Run(async () =>
    {
        Write(Path.Combine(_install, "SAVE", "SAVE02.DAT"), "native save");
        Write(Path.Combine(_install, "SYS4REG.INI"), "[display]\r\nScreenMode=1\r\n");
        var (model, _) = await Open();
        using (model)
        {
            WizardViewModel wizard = await AtProfileStep(model);
            Assert.True(wizard.HasNativeSaves);
            Assert.Equal("(1 save, with the game's settings)", wizard.NativeCounts);
            wizard.Name = "From the game";
            wizard.StartNative.Execute(null);
            ProfileEntry made = await CreateAndOpen(model, wizard);
            Assert.Equal("native save", File.ReadAllText(Path.Combine(made.Root, "SAVE", "SAVE02.DAT")));
            Assert.Contains("ScreenMode=1", File.ReadAllText(Path.Combine(made.Root, "SYS4REG.INI")));
        }
    });

    [Fact]
    public void AFolderThatIsNotASupportedGameCannotContinueAndSaysWhy() => Headless.Run(async () =>
    {
        string empty = Path.Combine(_home.Root, "empty");
        Directory.CreateDirectory(empty);
        string other = Path.Combine(_home.Root, "other-game");
        Directory.CreateDirectory(other);
        File.WriteAllBytes(Path.Combine(other, Installs.CatalogFile),
                           SyntheticCatalog.Build(["SC0000.BIN"], "S4IC999 ", "Another game"));
        var (model, host) = await Open();
        using (model)
        {
            model.NewProfile.Execute(null);
            WizardViewModel wizard = model.Wizard!;
            host.Folder = empty;
            wizard.Browse.Execute(null);
            await Headless.Until(() => wizard.Refused, "the empty folder");
            Assert.StartsWith("This folder is not a game install. ", wizard.RefusalText);
            Assert.False(wizard.Next.CanExecute(null));

            await wizard.ChooseAsync(other);
            Assert.True(wizard.Refused);
            Assert.Equal("This is an AGE game this engine does not support: catalog S4IC999, \"Another game\".",
                         wizard.RefusalText);
            Assert.False(wizard.Next.CanExecute(null));

            wizard.Cancel.Execute(null);
            Assert.False(model.HasWizard);
            Assert.Single(model.Rows);
        }
    });

    [Fact]
    public void AFolderDroppedOnTheWindowIsTheInstall() => Headless.Run(async () =>
    {
        var (model, _) = await Open();
        using (model)
        {
            model.NewProfile.Execute(null);
            WizardViewModel wizard = model.Wizard!;
            var window = new MainWindow { DataContext = model };
            Headless.Render(window, ThemeVariant.Light, "6.9-wizard-drop-light");
            var point = new Point(40, 40);
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(_install));
            window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Link, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Link, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Link, RawInputModifiers.None);
            await Headless.Until(() => wizard.CanContinue, "the dropped install");
            Assert.Equal(Path.GetFullPath(_install), Path.GetFullPath(wizard.FoundPath.Split(" · ")[0]));
            window.Close();
        }
    });

    [Fact]
    public void ARefusedCreateSaysWhyAndLeavesNothing() => Headless.Run(async () =>
    {
        string taken = Path.Combine(_home.Root, "taken");
        Write(Path.Combine(taken, "something.txt"), "not empty");
        var (model, host) = await Open();
        using (model)
        {
            WizardViewModel wizard = await AtProfileStep(model);
            host.Folder = taken;
            wizard.ToggleAdvanced.Execute(null);
            wizard.ChooseLocation.Execute(null);
            await Headless.Until(() => wizard.Location != null, "the location");
            Assert.Equal(taken, wizard.LocationText);
            await wizard.CreateAsync();
            Assert.True(model.HasWizard);
            Assert.Equal($"The profile was not made. {taken} is not empty; a new profile needs a new or empty folder",
                         wizard.Error);
            Assert.Single(model.Rows);
            Assert.Equal(["something.txt"], Directory.EnumerateFileSystemEntries(taken).Select(Path.GetFileName));
            Assert.Empty(LauncherSettingsStore.Load(_home.Home).Settings.Roots);

            wizard.DefaultLocation.Execute(null);
            Assert.Null(wizard.Location);
            await CreateAndOpen(model, wizard);
            Assert.Equal(2, model.Rows.Count());
        }
    });
}
