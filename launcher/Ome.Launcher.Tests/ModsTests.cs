using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Ome.Engine.Profiles;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Launcher.Views;
using Ome.Mods.Launch;
using Ome.Mods.Linking;

/// <summary>
/// The Mods tab (launcher spec §8.4, plan §6.5) on synthetic packages: the list with each package's state in the order
/// the stack loads, the detail of the selected one, the link report after each change, the switch (with the
/// dependencies it enables and the mode a package needs), dragging to set the order, adding and removing a package,
/// the README, the selectors under "Advanced", and the page locked while the game runs.
/// </summary>
public sealed class ModsTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly string _install;
    private readonly ProfileEntry _profile;

    public ModsTests()
    {
        _install = _home.Install("kamidori");
        _profile = _home.Profile("kamidori", "kamidori", _install, "Kamidori");
        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { Art = ArtConsent.Never });
    }

    public void Dispose() => _home.Dispose();

    private async Task<(MainViewModel Model, RecordingHost Host)> Open()
    {
        var model = new MainViewModel(_home.Session(), new ArtOptions(Handler: new HttpClientHandler()));
        var host = new RecordingHost();
        model.Host = host;
        await model.StartAsync();
        model.Page!.ShowTab.Execute(ProfileTab.Mods);
        return (model, host);
    }

    private ProfileDocument Document => ProfileDocuments.Read(_profile.Root)!.Document;

    private static string Requires(string id) => $"[[requires]]\nid = \"{id}\"\nversion = \">=1.0\"\n";

    /// <summary>Enables <paramref name="ids"/> in this order, as the switch would, before the window opens.</summary>
    private void Enable(params string[] ids)
        => ProfileStore.Update(_profile, document =>
        {
            foreach (string id in ids) document = Library.Enable(document, id, Library.Index(document)).Document;
            return document;
        }, _home.Home);

    private static ModRowViewModel Row(ModsViewModel mods, string id) => mods.Rows.Single(row => row.Id == id);

    [Fact]
    public void TheListShowsEachPackagesStateAndTheSelectedOnesDetail() => Headless.Run(async () =>
    {
        _home.Package("kamidori", "test.plain");
        _home.Package("kamidori", "test.lua", target: "extended");
        var (model, _) = await Open();
        using (model)
        {
            ModsViewModel mods = model.Page!.Mods;
            Assert.Equal(["test.lua", "test.plain"], mods.Rows.Select(row => row.Id).Order());
            Assert.All(mods.Rows, row => Assert.True(row.IsPackage && row.IsOff));
            Assert.Equal(("Needs Extended", "warn"), (Row(mods, "test.lua").Chip, Row(mods, "test.lua").ChipClass));
            Assert.Equal(("Off", ""), (Row(mods, "test.plain").Chip, Row(mods, "test.plain").ChipClass));

            mods.Select("test.lua");
            Assert.Equal("test.lua", mods.DetailName);
            Assert.Equal("1.2.0", mods.DetailVersion);
            Assert.Equal("Extended mode", mods.DetailNeeds);
            Assert.Contains("Supplies the scripts STESTLUA", mods.DetailDoes);
            mods.Select("test.plain");
            Assert.Equal("Works in both modes", mods.DetailNeeds);

            Assert.Equal("Link report · 0 linked, no conflicts", mods.ReportTitle);
            ReportLine line = Assert.Single(mods.Report);
            Assert.Equal(("Note", "Nothing to link: the game runs unmodded."), (line.Tag, line.Text));
        }
    });

    [Fact]
    public void TurningOnEnablesWhatItRequiresAndTurningOffSaysWhatStillNeedsIt() => Headless.Run(async () =>
    {
        _home.Package("kamidori", "test.lib");
        _home.Package("kamidori", "test.dep", tables: Requires("test.lib"));
        var (model, _) = await Open();
        using (model)
        {
            ModsViewModel mods = model.Page!.Mods;
            await mods.ToggleAsync(Row(mods, "test.dep"));
            Assert.False(mods.ResultBad);
            Assert.Equal("test.dep is on, and so is what it requires: test.lib.", mods.Result);
            Assert.Equal(["test.dep", "test.lib"], Document.Mods!.Enabled.Order());
            Assert.Equal(ProfileState.Ready, model.Page.State);
            // The stack loads the dependency first, whatever the order the profile lists them in.
            Assert.Equal(["test.lib", "test.dep"], mods.Rows.Select(row => row.Id));
            Assert.All(mods.Rows, row => Assert.Equal(("Ready", "ok"), (row.Chip, row.ChipClass)));
            Assert.Equal("Link report · 2 linked, no conflicts", mods.ReportTitle);
            Assert.Equal("test.dep", mods.DetailId);

            await mods.ToggleAsync(Row(mods, "test.lib"));
            Assert.True(mods.ResultBad);
            Assert.Equal("test.lib is off. These enabled mods require it: test.dep.", mods.Result);
            Assert.Equal(["test.dep"], Document.Mods!.Enabled);
            Assert.Equal(("Needs a dependency", "bad"), (Row(mods, "test.dep").Chip, Row(mods, "test.dep").ChipClass));
            Assert.Equal(ProfileState.Problem, model.Page.State);
        }
    });

    [Fact]
    public void APackageThatNeedsExtendedAsksToSwitchTheProfileFirst() => Headless.Run(async () =>
    {
        _home.Package("kamidori", "test.lua", target: "extended");
        var (model, _) = await Open();
        using (model)
        {
            ModsViewModel mods = model.Page!.Mods;
            Task cancelled = mods.ToggleAsync(Row(mods, "test.lua"));
            await Headless.Until(() => model.HasConfirm, "the question");
            Assert.Equal("Turn on test.lua?", model.Confirm!.Title);
            model.Confirm.No.Execute(null);
            await cancelled;
            Assert.Equal(ProfileMode.Native, Document.Mode);
            Assert.Empty(Document.Mods!.Enabled);

            Task on = mods.ToggleAsync(Row(mods, "test.lua"));
            await Headless.Until(() => model.HasConfirm, "the question");
            model.Confirm!.Yes.Execute(null);
            await on;
            Assert.Equal(ProfileMode.Extended, Document.Mode);
            Assert.Equal(["test.lua"], Document.Mods!.Enabled);
            Assert.Equal("This profile is now in Extended mode. test.lua is on.", mods.Result);
            Assert.Equal("Ready", Row(mods, "test.lua").Chip);
        }
    });

    [Fact]
    public void DraggingSetsThePreferredOrderAndAMoveTheResolverMadeSaysWhy() => Headless.Run(async () =>
    {
        _home.Package("kamidori", "test.a");
        _home.Package("kamidori", "test.b");
        _home.Package("kamidori", "test.c", tables: "[order]\nafter = [\"test.b\"]\n");
        Enable("test.a", "test.b", "test.c");
        var (model, _) = await Open();
        using (model)
        {
            ModsViewModel mods = model.Page!.Mods;
            Assert.Equal(["test.a", "test.b", "test.c"], mods.Rows.Select(row => row.Id));
            Assert.All(mods.Rows, row => Assert.False(row.HasNote));
            // Where a drop lands: the upper half of a row is above it, the lower half below; past the last, the end.
            Assert.Equal("test.a", mods.BeforeAt(0, lowerHalf: false));
            Assert.Equal("test.b", mods.BeforeAt(0, lowerHalf: true));
            Assert.Null(mods.BeforeAt(2, lowerHalf: true));

            // Dragged to the top, test.c still waits for test.b: the list keeps the order the stack loads, and says why.
            await mods.MoveAsync("test.c", "test.a");
            Assert.Equal(["test.c", "test.a", "test.b"], Document.Mods!.Order.Select(entry => entry.Id));
            Assert.Equal(["test.a", "test.b", "test.c"], mods.Rows.Select(row => row.Id));
            Assert.Equal("Loads after test.a, test.b: test.c asks to load after test.b", Row(mods, "test.c").Note);
            ReportLine order = Assert.Single(mods.Report, line => line.Tag == "Order");
            Assert.Equal("test.c loads after test.a, test.b: test.c asks to load after test.b", order.Text);
            order.Show.Execute(null);
            Assert.Equal("test.c", mods.DetailId);

            // A drop through the list: test.b dragged onto the upper half of test.a's row goes before it.
            var window = new MainWindow { DataContext = model };
            Headless.Render(window, ThemeVariant.Light, "6.5-mods-drag-light");
            ModsView view = window.GetVisualDescendants().OfType<ModsView>().Single();
            ItemsControl list = view.FindControl<ItemsControl>("RowList")!;
            Control first = list.ContainerFromIndex(0)!;
            Point point = first.TranslatePoint(new Point(40, 4), window)!.Value;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(ModsView.DragPrefix + "test.b"));
            window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Move, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Move, RawInputModifiers.None);
            Assert.True(Row(mods, "test.a").DropBefore);
            window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Move, RawInputModifiers.None);
            await Headless.Until(() => Document.Mods!.Order[1].Id == "test.b", "the drop");
            // The move checks the page again once its write is done, in the same turn: wait for the turn to end, or
            // the checks outlive the test (on CI they finished after the window closed, launcher plan §9.7).
            await model.OneAtATime(() => Task.FromResult(true));
            Assert.Equal(["test.c", "test.b", "test.a"], Document.Mods!.Order.Select(entry => entry.Id));
            Assert.All(mods.Rows, row => Assert.False(row.DropBefore));

            // Text another program drags in is not a package of the list: nothing moves.
            var stranger = new DataTransfer();
            stranger.Add(DataTransferItem.CreateText(ModsView.DragPrefix + "test.unknown"));
            window.DragDrop(point, RawDragEventType.DragEnter, stranger, DragDropEffects.Move, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.Drop, stranger, DragDropEffects.Move, RawInputModifiers.None);
            Assert.Equal(["test.c", "test.b", "test.a"], Document.Mods!.Order.Select(entry => entry.Id));
            window.Close();
            // Work that ends after the close still refreshes the page, which asks for the screen: a closed window has
            // none, and Avalonia threw for it.
            Assert.Null(window.UsableArea());
        }
    });

    [Fact]
    public void AddingAsksBeforeReplacingAndRemovingNamesTheProfilesThatUseIt() => Headless.Run(async () =>
    {
        string downloads = Path.Combine(_home.Root, "downloads");
        string package = _home.Package("kamidori", "test.new", parent: downloads);
        var (model, host) = await Open();
        using (model)
        {
            ModsViewModel mods = model.Page!.Mods;
            string shared = _home.Home.ModsDirectory("kamidori");
            host.Folder = null;
            await mods.AddAsync(folder: true);
            Assert.Empty(mods.Rows);

            host.Folder = package;
            await mods.AddAsync(folder: true);
            Assert.Equal($"test.new 1.2.0 was added to the library in {shared}.", mods.Result);
            Assert.Equal("test.new", Assert.Single(mods.Rows).Id);
            Assert.Equal("test.new", mods.DetailId);
            Assert.True(File.Exists(Path.Combine(shared, "test.new", "manifest.toml")));

            // The library holds it now: adding it again asks first.
            Task again = mods.AddAsync(folder: true);
            await Headless.Until(() => model.HasConfirm, "the question");
            Assert.Equal("Replace test.new?", model.Confirm!.Title);
            Assert.True(model.Confirm.Danger);
            model.Confirm.No.Execute(null);
            await again;

            await mods.ToggleAsync(Row(mods, "test.new"));
            Task remove = mods.RemoveAsync();
            await Headless.Until(() => model.HasConfirm, "the question");
            Assert.Equal("Remove test.new?", model.Confirm!.Title);
            Assert.Equal(["Kamidori enables it"], model.Confirm.Items);
            model.Confirm.Yes.Execute(null);
            await remove;
            Assert.Equal("test.new was removed from the library.", mods.Result);
            Assert.False(Directory.Exists(Path.Combine(shared, "test.new")));
            // Still enabled, now missing: the profile cannot start until it is back or off.
            Assert.Equal(("Missing", "bad"), (Row(mods, "test.new").Chip, Row(mods, "test.new").ChipClass));
            Assert.Equal(ProfileState.Problem, model.Page.State);
        }
    });

    [Fact]
    public void TheReadmeAndTheSelectorsShowAndTheRunningGameLocksThePage() => Headless.Run(async () =>
    {
        string documented = _home.Package("kamidori", "test.doc");
        File.WriteAllText(Path.Combine(documented, "README.md"), "# Test\nA plain read me.\n");
        Directory.CreateDirectory(Path.Combine(_profile.Root, ModLaunch.ModsFolder));
        File.WriteAllText(SelectorRelease.PathFor(_profile.Root),
                          "[selectors]\n\"test.doc\" = 2\n\"test.gone\" = 3\n");
        var (model, host) = await Open();
        using (model)
        {
            ModsViewModel mods = model.Page!.Mods;
            Assert.Equal("test.doc", mods.DetailId);
            await mods.ReadmeAsync();
            Assert.Equal("# Test\nA plain read me.\n", mods.Readme);
            mods.HideReadme.Execute(null);
            Assert.False(mods.HasReadme);
            mods.ShowFile.Execute(null);
            Assert.Equal($"Open {documented}", host.Calls[^1]);

            Assert.Equal(["test.doc · selector 2", "test.gone · selector 3 · the package is gone"],
                         mods.Selectors.Select(line => line.Text));
            Assert.False(mods.Selectors[0].Release.CanExecute(null));
            await mods.ReleaseAsync("test.gone");
            Assert.Equal("The selector of test.gone was released.", mods.Result);
            Assert.Equal(["test.doc · selector 2"], mods.Selectors.Select(line => line.Text));

            Assert.True(RunLock.TryAcquire(_profile.Root, markStart: true, out RunLock? engine, out _));
            using (engine)
            {
                await model.RefreshRunningAsync();
                Assert.True(mods.IsLocked);
                Assert.False(Row(mods, "test.doc").Toggle!.CanExecute(null));
                Assert.False(mods.AddFolder.CanExecute(null));
                Assert.False(mods.Remove.CanExecute(null));
                Assert.False(mods.CanDrag("test.doc"));
                await mods.ToggleAsync(Row(mods, "test.doc"));
                Assert.Empty(Document.Mods!.Enabled);
                // Looking stays open: the detail and the README.
                await mods.ReadmeAsync();
                Assert.True(mods.HasReadme);
            }
        }
    });
}
