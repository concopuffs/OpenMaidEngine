using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Mods.Launch;

/// <summary>
/// The Saves tab (launcher spec §8.4, plan §6.7) on synthetic installs whose saves sit in <c>SAVE</c>, as both games'
/// do: the profile's folder and its count, where the original game keeps its saves, copying in from there and from
/// another profile of the game (the replaced saves confirmed first, a selector clash refused with both ids named),
/// the page waiting for the install, and the page locked while the game runs.
/// </summary>
public sealed class SavesTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly string _install;
    private readonly ProfileEntry _profile;
    private readonly ProfileEntry _other;

    public SavesTests()
    {
        _install = _home.Install("kamidori", settings: [("SAVEPATH", "SAVE"), ("REGFILEPATH", "")]);
        _profile = _home.Profile("kamidori", "kamidori", _install, "Kamidori");
        _other = _home.Profile("kamidori-two", "kamidori", _install, "Kamidori two");
        _home.Profile("himegari", "himegari", _home.Install("himegari"), "Himegari");
        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings
                             with { Art = ArtConsent.Never, LastProfile = _profile.Id });
    }

    public void Dispose() => _home.Dispose();

    private async Task<MainViewModel> Open()
    {
        var model = new MainViewModel(_home.Session(), new ArtOptions(Handler: new HttpClientHandler()));
        model.Host = new RecordingHost();
        await model.StartAsync();
        Assert.Equal(_profile.Id, model.Selected!.Id);
        model.Page!.ShowTab.Execute(ProfileTab.Saves);
        return model;
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Saves => Path.Combine(_profile.Root, "SAVE");
    private string Native => Path.Combine(_install, "SAVE");

    [Fact]
    public void TheOriginalGamesSavesCopyInAndAReplacedSaveIsAskedFirst() => Headless.Run(async () =>
    {
        Write(Path.Combine(Native, "SAVE01.DAT"), "native one");
        Write(Path.Combine(Native, "SAVE01.STH"), "thumbnail");
        Write(Path.Combine(Native, "SAVE.DAT"), "shared");
        using MainViewModel model = await Open();
        SavesViewModel saves = model.Page!.Saves;
        Assert.True(saves.Known && saves.CanWrite);
        Assert.Equal(Path.GetFullPath(Saves), Path.GetFullPath(saves.Folder!));
        Assert.Equal("No saves yet", saves.Contents);
        Assert.Equal(Path.GetFullPath(Native), Path.GetFullPath(saves.NativeFolder!));
        Assert.False(saves.HasNoNativeSaves);
        Assert.True(saves.OpenNative.CanExecute(null));

        // Only profiles of this game are offered.
        Assert.Equal(["Kamidori two"], saves.Sources.Select(choice => choice.Label));

        saves.CopyFromNative.Execute(null);
        await Headless.Until(() => saves.HasResult, "the copy");
        Assert.False(saves.ResultBad);
        Assert.Equal("Copied in from the original game: SAVE.DAT, SAVE01.", saves.Result);
        Assert.Equal("native one", File.ReadAllText(Path.Combine(Saves, "SAVE01.DAT")));
        Assert.True(File.Exists(Path.Combine(Saves, "SAVE01.STH")));
        Assert.StartsWith("1 save · last ", saves.Contents);
        Assert.False(File.Exists(Path.Combine(_profile.Root, "SYS4REG.INI")));

        // Copying again would replace both: the question names them, and No leaves the profile's saves.
        File.WriteAllText(Path.Combine(Saves, "SAVE01.DAT"), "played on");
        Task again = saves.CopyAsync(SaveSource.Native(model.Page.Checks!.Install!.Catalog!.StartupSettings, _install),
                                     "the original game");
        await Headless.Until(() => model.HasConfirm, "the question");
        Assert.Equal("Replace 2 saves?", model.Confirm!.Title);
        Assert.Equal(["SAVE.DAT", "SAVE01"], model.Confirm.Items);
        Assert.True(model.Confirm.Danger);
        model.Confirm.No.Execute(null);
        await again;
        Assert.Equal("played on", File.ReadAllText(Path.Combine(Saves, "SAVE01.DAT")));

        saves.CopyFromNative.Execute(null);
        await Headless.Until(() => model.HasConfirm, "the question again");
        model.Confirm!.Yes.Execute(null);
        await Headless.Until(() => saves.Result!.Contains("Replaced"), "the replacing copy");
        Assert.Equal("Copied in from the original game: SAVE.DAT, SAVE01. Replaced: SAVE.DAT, SAVE01.", saves.Result);
        Assert.Equal("native one", File.ReadAllText(Path.Combine(Saves, "SAVE01.DAT")));
    });

    [Fact]
    public void AnotherProfilesSavesComeWithItsSelectorsAndAClashIsRefused() => Headless.Run(async () =>
    {
        Write(Path.Combine(_other.Root, "SAVE", "SAVE02.DAT"), "two");
        Write(Path.Combine(_other.Root, "SAVE", "SAVE02.mods.toml"), "stamp");
        Write(Path.Combine(_other.Root, "mods", "selectors.toml"), "[selectors]\n\"example.alpha\" = 1\n");
        Write(Path.Combine(_profile.Root, "mods", "selectors.toml"), "[selectors]\n\"example.alpha\" = 4\n");
        using MainViewModel model = await Open();
        SavesViewModel saves = model.Page!.Saves;
        Assert.True(saves.HasNoNativeSaves);
        Assert.False(saves.CopyFromNative.CanExecute(null));
        Assert.Equal("Kamidori two", saves.Source!.Label);

        saves.CopyFromProfile.Execute(null);
        await Headless.Until(() => saves.HasResult, "the refusal");
        Assert.True(saves.ResultBad);
        Assert.StartsWith("The saves were not copied.", saves.Result);
        Assert.Equal(["example.alpha has selector 4 in one and 1 in the other"], saves.Details);
        Assert.False(Directory.Exists(Saves));

        File.WriteAllText(Path.Combine(_profile.Root, "mods", "selectors.toml"), "[selectors]\n\"example.bravo\" = 2\n");
        saves.CopyFromProfile.Execute(null);
        await Headless.Until(() => !saves.ResultBad, "the copy");
        Assert.Equal("Copied in from Kamidori two: SAVE02.", saves.Result);
        Assert.False(saves.HasDetails);
        Assert.Equal("stamp", File.ReadAllText(Path.Combine(Saves, "SAVE02.mods.toml")));
        Assert.Contains("\"example.alpha\" = 1", File.ReadAllText(Path.Combine(_profile.Root, "mods", "selectors.toml")));
    });

    [Fact]
    public void TheFoldersWaitForTheInstallAndTheRunningGameLocksThePage() => Headless.Run(async () =>
    {
        Write(Path.Combine(Native, "SAVE01.DAT"), "native one");
        using (MainViewModel model = await Open())
        {
            SavesViewModel saves = model.Page!.Saves;
            Assert.True(RunLock.TryAcquire(_profile.Root, markStart: true, out RunLock? engine, out _));
            using (engine)
            {
                await model.RefreshRunningAsync();
                Assert.True(saves.IsLocked);
                Assert.False(saves.CopyFromNative.CanExecute(null));
                Assert.False(saves.CopyFromProfile.CanExecute(null));
                // Looking stays open.
                Assert.True(saves.OpenFolder.CanExecute(null));
                Assert.True(saves.OpenNative.CanExecute(null));
            }
        }

        Directory.Move(_install, _install + "-moved");
        using (MainViewModel model = await Open())
        {
            SavesViewModel saves = model.Page!.Saves;
            Assert.True(saves.Unknown);
            Assert.Null(saves.Folder);
            Assert.Equal("Known once the install is found", saves.FolderText);
            Assert.False(saves.OpenFolder.CanExecute(null));
            Assert.False(saves.CopyFromNative.CanExecute(null));
            Assert.Empty(saves.Sources);
        }
    });
}
