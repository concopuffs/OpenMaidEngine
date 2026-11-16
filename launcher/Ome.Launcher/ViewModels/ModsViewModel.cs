using Ome.Engine.Profiles;
using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

namespace Ome.Launcher.ViewModels;

/// <summary>One row of the Mods list (the mock's <c>.mrow</c>): a library package with its switch and state, or a base
/// mod the engine mounted (<see cref="IsEngine"/>), which has neither switch nor place in the order.</summary>
public sealed class ModRowViewModel : ViewModel
{
    private bool _isSelected;
    private bool _dropBefore;

    public ModRowViewModel(string id, string name, string version, bool enabled, bool isEngine, string chip,
                           string chipClass, string? note, LibraryPackage? package)
    {
        Id = id;
        Name = name;
        Version = version;
        Enabled = enabled;
        IsEngine = isEngine;
        Chip = chip;
        ChipClass = chipClass;
        Note = note;
        Package = package;
    }

    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public bool Enabled { get; }
    public bool IsOff => !Enabled && !IsEngine;
    public bool IsEngine { get; }
    public bool IsPackage => !IsEngine;
    public string Chip { get; }
    public string ChipClass { get; }
    public bool ChipOk => ChipClass == "ok";
    public bool ChipWarn => ChipClass == "warn";
    public bool ChipBad => ChipClass == "bad";

    /// <summary>Where the resolver moved it, or why the engine mounted it.</summary>
    public string? Note { get; }
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public LibraryPackage? Package { get; }

    /// <summary>A package no folder holds cannot be switched on.</summary>
    public bool CanToggle => IsPackage && (Enabled || Package?.Entry != null);

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>A package being dragged would land just above this row.</summary>
    public bool DropBefore
    {
        get => _dropBefore;
        set => Set(ref _dropBefore, value);
    }

    /// <summary>The switch.</summary>
    public Command? Toggle { get; set; }

    /// <summary>A click on the row: selects it.</summary>
    public Command? Choose { get; set; }
}

/// <summary>A line of the link report card: its tag, the text, and the packages it names (a click selects the
/// first).</summary>
public sealed record ReportLine(string Tag, string Text, bool Bad, IReadOnlyList<string> Packages, Command Show);

/// <summary>A selector the profile holds (the Mods page's "Advanced"); one whose package is gone can be released.</summary>
public sealed record SelectorLine(string Text, bool Gone, Command Release);

/// <summary>
/// The Mods tab (launcher spec §8.4, plan §6.5): the library's packages for the profile's game, each with a switch and
/// a state, in the order the stack loads, with a base mod the engine mounts where it loads. Dragging sets the preferred order
/// (<see cref="Library.Move"/>), and a package the resolver moved says what moved it. Enabling enables the dependencies
/// the library holds and says which; a package that needs Extended asks to switch first. The detail pane shows the
/// selected package: its manifest, what it supplies, the report lines that name it, and its README on request. The link
/// report follows every change, since every change runs the checks again. The bar adds a package to the library (from
/// a folder or an <c>.omp</c>), and the detail pane removes one after naming the profiles that use it. Under
/// "Advanced", the selectors the profile holds, with Release for a package that is gone.
/// </summary>
public sealed class ModsViewModel : ViewModel
{
    private readonly ProfilePageViewModel _page;
    private IReadOnlyList<ModRowViewModel> _rows = [];
    private IReadOnlyList<ReportLine> _report = [];
    private IReadOnlyList<SelectorLine> _selectors = [];
    private string? _selectedId;
    private string? _result;
    private bool _resultBad;
    private string? _readme;
    private bool _dropAtEnd;
    private bool _showAdvanced;

    public ModsViewModel(ProfilePageViewModel page)
    {
        _page = page;
        AddFolder = new Command(() => _ = AddAsync(folder: true), () => CanWrite);
        AddFile = new Command(() => _ = AddAsync(folder: false), () => CanWrite);
        OpenLibrary = new Command(OpenLibraryFolders, () => Document?.Mods != null);
        ShowReadme = new Command(() => _ = ReadmeAsync(), () => Selected?.Package?.Entry != null);
        HideReadme = new Command(() => Readme = null);
        ShowFile = new Command(() =>
        {
            if (Selected?.Package?.Entry is { } entry) _page.Main.Host?.Open(Folder(entry));
        }, () => Selected?.Package?.Entry != null);
        Remove = new Command(() => _ = RemoveAsync(), () => CanWrite && Selected?.Package?.Entry != null);
        ToggleAdvanced = new Command(() =>
        {
            _showAdvanced = !_showAdvanced;
            Changed(nameof(ShowAdvanced));
        });
        ToggleDoes = new Command(() =>
        {
            _page.Main.ModsDoesOpen = !DoesOpen;
            Changed(nameof(DoesOpen));
            Changed(nameof(DoesHeading));
        });
        ToggleReport = new Command(() =>
        {
            _page.Main.ModsReportOpen = !ReportOpen;
            Changed(nameof(ReportOpen));
            Changed(nameof(ReportHeading));
        });
    }

    public LauncherStrings S => _page.S;
    private ProfileCheckResult? Checks => _page.Checks;
    private ProfileDocument? Document => _page.Entry.Loaded?.Document;
    private OmeHome Home => _page.Main.Session.Home;
    /// <summary>Whether the page's changes can be used: not while the game runs, and not while the page works on a
    /// change, so a second click does not queue the same change again (launcher plan §10, item 2).</summary>
    private bool CanWrite => Document?.Mods != null && !_page.Entry.NeedsReview && _page.IsUnlocked && !_page.IsWorking;

    /// <summary>While the game runs the page can be looked at, and nothing on it changes the profile (spec §8.4).</summary>
    public bool IsUnlocked => _page.IsUnlocked;
    public bool IsLocked => _page.IsLocked;

    // ---- the list ----

    public IReadOnlyList<ModRowViewModel> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }
    public bool HasRows => _rows.Count > 0;
    public bool HasNoRows => Checks?.Library != null && !HasRows;

    /// <summary>The page's checks ran again, or the lock changed: the list, the report and the detail are rebuilt from
    /// them, keeping the selection.</summary>
    public void Refresh()
    {
        string? wanted = _selectedId;
        Rows = BuildRows();
        Report = BuildReport();
        Selectors = BuildSelectors();
        _selectedId = wanted;
        ModRowViewModel? selected = _rows.FirstOrDefault(row => row.Id == _selectedId)
                                    ?? _rows.FirstOrDefault(row => row.Enabled && row.IsPackage)
                                    ?? _rows.FirstOrDefault(row => row.IsPackage);
        Select(selected, keepReadme: selected?.Id == _selectedId);
        ChangedAll();
        OpenLibrary.Refresh();
        RefreshCommands();
    }

    /// <summary>Asks again whether each change can be used: the page started or ended its work.</summary>
    public void RefreshCommands()
    {
        AddFolder.Refresh();
        AddFile.Refresh();
        Remove.Refresh();
        foreach (ModRowViewModel row in _rows) row.Toggle?.Refresh();
        foreach (SelectorLine line in Selectors) line.Release.Refresh();
    }

    private IReadOnlyList<ModRowViewModel> BuildRows()
    {
        if (Checks?.Library is not { } view) return [];
        LinkCheck? link = Checks.Link;
        ModLinkReport? report = link?.Report;
        var rows = new List<ModRowViewModel>();

        // The packages in the order the stack loads: the enabled ones in the resolved order, each disabled one kept at
        // its place in the profile's order (the order the player drags).
        var packages = view.Packages.ToList();
        var resolved = link?.Order.Where(id => !ShippedPackages.Ids.Contains(id)).ToList();
        var enabledSlots = packages.Where(package => package.Enabled).Select(package => package.Id).ToList();
        if (resolved != null && resolved.Count == enabledSlots.Count && resolved.ToHashSet().SetEquals(enabledSlots))
        {
            var byId = packages.ToDictionary(package => package.Id);
            int next = 0;
            for (int at = 0; at < packages.Count; at++)
                if (packages[at].Enabled) packages[at] = byId[resolved[next++]];
        }

        var moves = link?.Moves.ToDictionary(move => move.Id) ?? [];
        var enabled = view.Packages.Where(package => package.Enabled).Select(package => package.Id).ToHashSet();
        foreach (LibraryPackage package in packages)
        {
            (string chip, string cls) = ChipOf(package, enabled, report, link);
            string? note = moves.TryGetValue(package.Id, out OrderMove? move)
                ? S.Format("mods.moved", string.Join(", ", move.Overtaken), string.Join("; ", move.Edges))
                : null;
            var row = new ModRowViewModel(package.Id, package.Entry?.Manifest.Name ?? package.Id,
                                          package.Entry?.Version.ToString() ?? "", package.Enabled, false, chip, cls, note,
                                          package);
            row.Toggle = new Command(() => _ = ToggleAsync(row), () => CanWrite && row.CanToggle);
            row.Choose = new Command(() => Select(row));
            rows.Add(row);
        }

        // A base mod the engine mounted goes where the stack loads it: before the package that follows it in the resolved
        // order, else after the one before it. (Spec §8.4 has it first, "since it loads first"; the resolver places an
        // unranked base after every ranked package that is ready, so first would misstate the order the list shows.)
        IReadOnlyList<string> order = link?.Order ?? [];
        for (int at = 0; at < order.Count; at++)
        {
            string id = order[at];
            if (!ShippedPackages.Ids.Contains(id)) continue;
            var needing = view.Packages.Where(package => package.Enabled
                                                         && package.Entry?.Requires.Any(requirement => requirement.Id == id) == true)
                .Select(package => package.Entry!.Manifest.Name).ToList();
            string version = "";
            using (ModPackage? shipped = ShippedPackages.Open(id)) version = shipped?.Manifest.Version.ToString() ?? "";
            var engine = new ModRowViewModel(id, id, version, true, true, S["chip.engine"], "",
                                             needing.Count > 0 ? S.Format("mods.engine-note", string.Join(", ", needing))
                                                               : S["mods.engine-note-any"], null);
            string? next = order.Skip(at + 1).FirstOrDefault(other => !ShippedPackages.Ids.Contains(other));
            string? previous = order.Take(at).LastOrDefault(other => !ShippedPackages.Ids.Contains(other));
            int before = next == null ? -1 : rows.FindIndex(row => row.Id == next);
            int after = previous == null ? -1 : rows.FindIndex(row => row.Id == previous);
            rows.Insert(before >= 0 ? before : after >= 0 ? after + 1 : 0, engine);
        }
        return rows;
    }

    /// <summary>A report line as the engine's report writes it, without its class: the packages it names in brackets,
    /// then its script, then the message.</summary>
    private static string Words(ModReportLine line)
        => (line.Mods.Count > 0 ? $"[{string.Join(", ", line.Mods)}] " : "") + (line.Script != null ? line.Script + ": " : "")
           + line.Message;

    /// <summary>The row's chip: why it cannot run, else what the link said of it, else on or off. An enabled package
    /// whose dependency the library holds but the profile has off needs that dependency too.</summary>
    private (string Text, string Class) ChipOf(LibraryPackage package, IReadOnlySet<string> enabled, ModLinkReport? report,
                                               LinkCheck? link)
    {
        string level = package.Enabled ? "bad" : "warn";
        switch (package.State)
        {
            case PackageState.Missing: return (S["chip.missing"], "bad");
            case PackageState.NeedsExtended: return (S["chip.needs-extended"], level);
            case PackageState.NeedsDependency: return (S["chip.needs-dependency"], level);
            case PackageState.UnsupportedRevision: return (S["chip.unsupported"], level);
        }
        if (!package.Enabled) return (S["chip.off"], "");
        if (package.Entry?.Requires.Any(requirement => !ShippedPackages.Ids.Contains(requirement.Id)
                                                       && !enabled.Contains(requirement.Id)) == true)
            return (S["chip.needs-dependency"], "bad");
        bool Names(ModReportClass kind) => report?.Lines.Any(line => line.Class == kind && line.Mods.Contains(package.Id)) == true
                                           || link?.Discovery.Lines.Any(line => line.Class == kind && line.Mods.Contains(package.Id)) == true;
        if (Names(ModReportClass.HardConflict)) return (S["chip.conflict"], "bad");
        if (Names(ModReportClass.Error)) return (S["chip.error"], "bad");
        if (link?.Dropped.Contains(package.Id) == true) return (S["chip.skipped"], "warn");
        return (S["chip.ready"], "ok");
    }

    // ---- the selection and its detail ----

    public ModRowViewModel? Selected => _rows.FirstOrDefault(row => row.IsSelected);
    public bool HasSelected => Selected?.Package != null;
    public bool HasNoSelected => !HasSelected;

    /// <summary>Selects <paramref name="row"/> (null selects nothing), closing a README shown for another.</summary>
    public void Select(ModRowViewModel? row, bool keepReadme = false)
    {
        foreach (ModRowViewModel each in _rows) each.IsSelected = each == row;
        _selectedId = row?.Id;
        if (!keepReadme) _readme = null;
        Changed(nameof(Selected));
        Changed(nameof(HasSelected));
        Changed(nameof(HasNoSelected));
        ChangedDetail();
    }

    /// <summary>Selects the row of <paramref name="id"/>.</summary>
    public void Select(string id) => Select(_rows.FirstOrDefault(row => row.Id == id));

    private ModManifest? Manifest => Selected?.Package?.Entry?.Manifest;

    public string DetailName => Selected?.Name ?? "";
    public string? DetailDescription => Manifest?.Description;
    public bool HasDetailDescription => !string.IsNullOrEmpty(DetailDescription);
    public string DetailId => Selected?.Id ?? "";
    public string DetailVersion => Selected?.Version ?? "";
    public string DetailAuthors => Manifest == null ? "" : string.Join(", ", Manifest.Authors);
    public bool HasDetailAuthors => Manifest?.Authors.Count > 0;

    /// <summary>The mode it needs and what it requires.</summary>
    public string DetailNeeds
    {
        get
        {
            if (Selected?.Package?.Entry is not { } entry) return "";
            bool extended = ModeCheck.NativeRefusals([entry]).Count > 0;
            string mode = extended ? S["mods.needs-extended"] : S["mods.needs-both"];
            return entry.Requires.Count == 0
                ? mode
                : mode + "; " + string.Join(", ", entry.Requires.Select(requirement => $"{requirement.Id} {requirement.Version.Text}"));
        }
    }

    /// <summary>What it supplies (its manifest) and what the link report says of it (Core's lines).</summary>
    public IReadOnlyList<string> DetailDoes
    {
        get
        {
            if (Manifest is not { } manifest || Selected is not { } row) return [];
            var lines = new List<string>();
            if (manifest.ScriptNames.Count > 0) lines.Add(S.Format("mods.supplies-scripts", string.Join(", ", manifest.ScriptNames)));
            if (manifest.AssetNames.Count > 0) lines.Add(S.Format("mods.supplies-assets", string.Join(", ", manifest.AssetNames)));
            if (manifest.Languages.Count > 0)
                lines.Add(S.Format("mods.defines-languages", string.Join(", ", manifest.Languages.Select(language => language.Tag))));
            if (manifest.Lua != null) lines.Add(S["mods.runs-lua"]);
            IEnumerable<ModReportLine> named = (Checks?.Link?.Report?.Lines ?? Checks?.Link?.Discovery.Lines ?? [])
                .Where(line => line.Mods.Contains(row.Id));
            lines.AddRange(named.Select(Words));
            lines.AddRange(row.Package!.Reasons);
            lines.AddRange(row.Package.Problems);
            return lines;
        }
    }
    public bool HasDetailDoes => DetailDoes.Count > 0;
    /// <summary>The heading of the card that lists <see cref="DetailDoes"/>, under the list (plan §10, item 3).</summary>
    public string DoesTitle => S.Format("mods.does-title", DetailName);

    // ---- the cards that open and close (launcher plan §10, item 7) ----

    /// <summary>The longest "What it does" list that opens by itself. A longer one (a translation's notes, one for each
    /// text file with entries that match nothing) starts closed, so the link report stays near the list.</summary>
    public const int DoesOpenLines = 8;

    /// <summary>Whether the "What it does" card shows its list: the player's choice, else whether the list is short.</summary>
    public bool DoesOpen => _page.Main.ModsDoesOpen ?? DetailDoes.Count <= DoesOpenLines;

    /// <summary>The card's heading, which opens and closes it: a closed one says how many lines it holds.</summary>
    public string DoesHeading => DoesOpen
        ? DoesTitle + " ▾"
        : (DetailDoes.Count == 1
              ? S.Format("mods.does-title-closed-one", DetailName)
              : S.Format("mods.does-title-closed", DetailName, DetailDoes.Count)) + " ▸";

    public Command ToggleDoes { get; }

    /// <summary>Whether the link report shows its lines; its heading keeps the summary either way.</summary>
    public bool ReportOpen => _page.Main.ModsReportOpen;
    public string ReportHeading => ReportTitle + (ReportOpen ? " ▾" : " ▸");
    public Command ToggleReport { get; }

    public string? Readme
    {
        get => _readme;
        private set
        {
            _readme = value;
            Changed(nameof(Readme));
            Changed(nameof(HasReadme));
            Changed(nameof(ShowReadmeLink));
        }
    }
    public bool HasReadme => _readme != null;
    public bool ShowReadmeLink => !HasReadme;
    public Command ShowReadme { get; }
    public Command HideReadme { get; }
    public Command ShowFile { get; }
    public Command Remove { get; }

    private void ChangedDetail()
    {
        foreach (string name in new[]
                 {
                     nameof(DetailName), nameof(DetailDescription), nameof(HasDetailDescription), nameof(DetailId),
                     nameof(DetailVersion), nameof(DetailAuthors), nameof(HasDetailAuthors), nameof(DetailNeeds),
                     nameof(DetailDoes), nameof(HasDetailDoes), nameof(DoesTitle), nameof(DoesOpen), nameof(DoesHeading),
                     nameof(Readme), nameof(HasReadme), nameof(ShowReadmeLink),
                 })
            Changed(name);
        ShowReadme.Refresh();
        ShowFile.Refresh();
        Remove.Refresh();
    }

    /// <summary>Reads the selected package's README (<see cref="Library.Readme"/>) off the UI thread.</summary>
    public async Task ReadmeAsync()
    {
        if (Selected?.Package?.Entry is not { } entry) return;
        string id = entry.Id;
        string text;
        try
        {
            text = await Task.Run(() => Library.Readme(entry)) is { } readme
                ? readme.Text + (readme.Truncated ? Environment.NewLine + Environment.NewLine + S["mods.readme-truncated"] : "")
                : S["mods.no-readme"];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ModPackageException
                                          or InvalidDataException)
        {
            text = error.Message;
        }
        if (_selectedId == id) Readme = text;
    }

    private static string Folder(PackageIndexEntry entry)
        => Directory.Exists(entry.Path) ? entry.Path : Path.GetDirectoryName(entry.Path)!;

    // ---- the link report ----

    public IReadOnlyList<ReportLine> Report
    {
        get => _report;
        private set => Set(ref _report, value);
    }

    public string ReportTitle
    {
        get
        {
            if (Checks?.Link is not { } link)
                return S.Format("report.title", S["report.not-linked"]);
            int problems = (link.Report?.Lines ?? link.Discovery.Lines).Count(line => line.Stops);
            if (problems > 0)
                return S.Format("report.title", problems == 1 ? S["report.problem"] : S.Format("report.problems", problems));
            int linked = link.Order.Count(id => !ShippedPackages.Ids.Contains(id));
            return S.Format("report.title", S.Format("report.linked", linked));
        }
    }

    private IReadOnlyList<ReportLine> BuildReport()
    {
        if (Checks is not { } checks) return [];
        ReportLine Line(string tag, string text, bool bad, IReadOnlyList<string> packages)
            => new(tag, text, bad, packages, new Command(() => { if (packages.Count > 0) Select(packages[0]); },
                                                         () => packages.Count > 0));
        if (checks.Link is not { } link)
        {
            ProfileCheck check = checks[ProfileCheckKind.Link];
            return [Line(S["report.tag.note"], check.Message, false, [])];
        }
        var lines = new List<ReportLine>();
        foreach (ModReportLine line in (link.Report?.Lines ?? link.Discovery.Lines).OrderBy(line => line.Class))
        {
            lines.Add(Line(S[$"report.tag.{line.Class.ToString().ToLowerInvariant()}"], Words(line), line.Stops, line.Mods));
        }
        foreach (OrderMove move in link.Moves)
            lines.Add(Line(S["report.tag.order"],
                           S.Format("report.moved", move.Id, string.Join(", ", move.Overtaken), string.Join("; ", move.Edges)),
                           false, [move.Id, .. move.Overtaken]));
        if (link.Dropped.Count > 0)
            lines.Add(Line(S["report.tag.skipped"], S.Format("report.dropped", string.Join(", ", link.Dropped)), false,
                           link.Dropped));
        if (lines.Count == 0)
            lines.Add(Line(S["report.tag.note"],
                           link.Order.Count == 0 ? S["report.unmodded"] : S["report.no-interactions"], false, []));
        return lines;
    }

    // ---- what the player changes ----

    /// <summary>What the last change did, or why it could not.</summary>
    public string? Result
    {
        get => _result;
        private set
        {
            Set(ref _result, value);
            Changed(nameof(HasResult));
        }
    }
    public bool HasResult => !string.IsNullOrEmpty(_result);
    public bool ResultBad
    {
        get => _resultBad;
        private set => Set(ref _resultBad, value);
    }

    private void Say(string text, bool bad = false)
    {
        ResultBad = bad;
        Result = text;
    }

    private void Failed(string message) => Say(S["overview.write-failed"] + " " + message, bad: true);

    /// <summary>Turns <paramref name="row"/>'s package on or off (spec §8.4): on, with the dependencies the library
    /// holds, saying which; off, saying which enabled packages still require it. A package Compatibility refuses asks to
    /// switch the profile to Extended first (spec §6).</summary>
    public async Task ToggleAsync(ModRowViewModel row)
    {
        if (!CanWrite || !row.CanToggle || Document is not { } document) return;
        _selectedId = row.Id;
        string name = row.Name;
        LibraryChange? change = null;
        if (row.Enabled)
        {
            Result = null;
            if (!await _page.WriteAsync(entry => ProfileStore.Update(entry, current =>
                    (change = Library.Disable(current, row.Id, Library.Index(current))).Document, Home), Failed,
                    S.Format("mods.disabling", name)))
                return;
            Say(change!.Unmet.Count == 0
                ? S.Format("mods.disabled", name)
                : S.Format("mods.disabled-needed", name, string.Join(", ", change.Unmet)), bad: change.Unmet.Count > 0);
            return;
        }

        bool toExtended = document.Mode == ProfileMode.Native && row.Package?.State == PackageState.NeedsExtended;
        if (toExtended && !await _page.Main.ConfirmAsync(new ConfirmViewModel(
                S.Format("confirm.extended-for.title", name), S.Format("confirm.extended-for.text", name),
                S["confirm.extended-for.yes"], S["confirm.cancel"])))
            return;
        Result = null;
        if (!await _page.WriteAsync(entry => ProfileStore.Update(entry, current =>
            {
                if (toExtended) current = current with { Mode = ProfileMode.Extended };
                change = Library.Enable(current, row.Id, Library.Index(current));
                return change.Document;
            }, Home), Failed, S.Format("mods.enabling", name)))
            return;
        string said = change!.Changed.Count == 0
            ? S.Format("mods.enabled", name)
            : S.Format("mods.enabled-deps", name, string.Join(", ", change.Changed));
        if (toExtended) said = S["mods.switched-extended"] + " " + said;
        if (change.Unmet.Count > 0) said += " " + S.Format("mods.unmet", name, string.Join(", ", change.Unmet));
        Say(said, bad: change.Unmet.Count > 0);
    }

    // ---- dragging (spec §8.4: "drag to set the order") ----

    /// <summary>Whether <paramref name="id"/> can be dragged: a package of the list, not the engine's.</summary>
    public bool CanDrag(string id) => CanWrite && _rows.Any(row => row.Id == id && row.IsPackage);

    /// <summary>Where a package dropped on the row at <paramref name="index"/> lands: the id it goes before, or null for
    /// the end. The lower half of a row means below it; a base mod's row means the top of the packages.</summary>
    public string? BeforeAt(int index, bool lowerHalf)
    {
        int at = Math.Clamp(lowerHalf ? index + 1 : index, 0, _rows.Count);
        return _rows.Skip(at).FirstOrDefault(row => row.IsPackage)?.Id;
    }

    /// <summary>Marks where a drag would land (null clears the mark).</summary>
    public void MarkDrop(string? before, bool active)
    {
        foreach (ModRowViewModel row in _rows) row.DropBefore = active && row.Id == before;
        DropAtEnd = active && before == null;
    }

    /// <summary>A drag would land after the last row.</summary>
    public bool DropAtEnd
    {
        get => _dropAtEnd;
        private set => Set(ref _dropAtEnd, value);
    }

    /// <summary>Moves <paramref name="id"/> to just before <paramref name="before"/> in the preferred order, or to the
    /// end (dragging, spec §8.4). The list then shows the order the stack resolves to.</summary>
    public async Task MoveAsync(string id, string? before)
    {
        if (!CanWrite || id == before) return;
        _selectedId = id;
        string name = _rows.FirstOrDefault(row => row.Id == id)?.Name ?? id;
        if (await _page.WriteAsync(entry => ProfileStore.Update(entry, current => Library.Move(current, id, before), Home),
                                   Failed, S.Format("mods.moving", name)))
            Result = null;
    }

    /// <summary>Adds a package to the library (<see cref="Library.Add"/>) from a folder or an <c>.omp</c> file the
    /// player picks. One whose id the library already holds asks before replacing it.</summary>
    public async Task AddAsync(bool folder)
    {
        if (!CanWrite || _page.Main.Host is not { } host) return;
        string? picked = folder
            ? await host.PickFolderAsync(S["mods.add-folder-title"])
            : await host.PickFileAsync(S["mods.add-file-title"], ModPackage.PackageExtension);
        if (picked == null) return;
        OmeHome home = Home;
        string adding = S.Format("mods.adding", Path.GetFileName(Path.TrimEndingDirectorySeparator(picked)));
        LibraryAddResult added;
        try
        {
            added = await _page.WorkAsync(adding, () => Task.Run(() => Library.Add(home, picked, replace: false)));
            if (!added.Copied)
            {
                PackageIndexEntry old = added.Replaced[0];
                if (!await _page.Main.ConfirmAsync(new ConfirmViewModel(
                        S.Format("confirm.replace.title", added.Package.Manifest.Name),
                        S.Format("confirm.replace.text", $"{old.Id} {old.Version}", old.Path, added.Package.Version),
                        S["confirm.replace.yes"], S["confirm.cancel"], danger: true)))
                    return;
                added = await _page.WorkAsync(adding, () => Task.Run(() => Library.Add(home, picked, replace: true)));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                          or InvalidDataException or ModPackageException or ModManifestException)
        {
            Say(S["mods.add-failed"] + " " + error.Message, bad: true);
            return;
        }
        _selectedId = added.Package.Id;
        await _page.WorkAsync(adding, async () =>
        {
            await _page.CheckAsync();
            return true;
        });
        Say(S.Format("mods.added", added.Package.Manifest.Name, added.Package.Version, Path.GetDirectoryName(added.Destination)!));
    }

    /// <summary>Removes the selected package from its library folder after the player confirms, naming the profiles
    /// that enable it or hold its selector (<see cref="Library.Usage"/>).</summary>
    public async Task RemoveAsync()
    {
        if (!CanWrite || Selected?.Package?.Entry is not { } entry) return;
        var profiles = _page.Main.Rows.Select(row => row.Entry).Where(profile => profile.Usable).ToList();
        PackageUsage usage = await Task.Run(() => Library.Usage(entry, profiles));
        // Named as the sidebar names them.
        string Name(string id) => profiles.FirstOrDefault(profile => profile.Id == id)?.DisplayName ?? id;
        IReadOnlyList<string> used = [.. usage.Enabling.Select(id => S.Format("mods.usage-enables", Name(id))),
                                      .. usage.HoldingSelector.Except(usage.Enabling)
                                          .Select(id => S.Format("mods.usage-selector", Name(id)))];
        if (!await _page.Main.ConfirmAsync(new ConfirmViewModel(
                S.Format("confirm.remove.title", entry.Manifest.Name), S.Format("confirm.remove.text", entry.Path),
                S["confirm.remove.yes"], S["confirm.cancel"], used, S["confirm.remove.used"], danger: true)))
            return;
        string removing = S.Format("mods.removing", entry.Manifest.Name);
        try
        {
            await _page.WorkAsync(removing, () => Task.Run(() =>
            {
                Library.Remove(entry);
                return true;
            }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Say(S["mods.remove-failed"] + " " + error.Message, bad: true);
            return;
        }
        _selectedId = null;
        await _page.WorkAsync(removing, async () =>
        {
            await _page.CheckAsync();
            return true;
        });
        Say(S.Format("mods.removed", entry.Manifest.Name));
    }

    public Command AddFolder { get; }
    public Command AddFile { get; }
    public Command OpenLibrary { get; }

    /// <summary>Opens each of the profile's library folders; the shared ones inside OME home are made first when they
    /// are not there yet.</summary>
    private void OpenLibraryFolders()
    {
        if (Document?.Mods is not { } mods) return;
        foreach (string folder in mods.Folders)
        {
            bool shared = ProfileDocument.LibraryFolders(Home, Document.Game)
                .Any(library => OmeHome.PathsEqual(library, folder, _page.Main.Session.Environment.Platform));
            if (shared) Directory.CreateDirectory(folder);
            if (Directory.Exists(folder)) _page.Main.Host?.Open(folder);
        }
    }

    // ---- Advanced: the selectors ----

    public IReadOnlyList<SelectorLine> Selectors
    {
        get => _selectors;
        private set => Set(ref _selectors, value);
    }
    public bool HasSelectors => _selectors.Count > 0;
    public bool HasNoSelectors => !HasSelectors;
    public bool ShowAdvanced => _showAdvanced;
    public Command ToggleAdvanced { get; }

    private IReadOnlyList<SelectorLine> BuildSelectors()
    {
        if (Checks?.Library is not { } view || !_page.Entry.Usable) return [];
        try
        {
            return ProfileSelectors.List(_page.Entry, view).Select(held => new SelectorLine(
                S.Format("mods.selector-line", held.Id, held.Selector) + (held.Gone ? " · " + S["mods.gone"] : ""),
                held.Gone, new Command(() => _ = ReleaseAsync(held.Id), () => held.Gone && CanWrite))).ToList();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Releases the selector of a package that is gone (spec §5.9): one action, done at once under the
    /// profile's lock.</summary>
    public async Task ReleaseAsync(string id)
    {
        if (!CanWrite) return;
        if (await _page.WriteAsync(entry =>
            {
                ProfileSelectors.Release(entry, [id]);
                return null;
            }, Failed))
            Say(S.Format("mods.released", id));
    }
}
