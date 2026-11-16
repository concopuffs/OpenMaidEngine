using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher.ViewModels;

/// <summary>A value of one of <c>profile.toml</c>'s typed keys, with what the page calls it.</summary>
public sealed record OptionChoice(string Value, string Label);

/// <summary>One overlay of the profile, or a subfolder of the install the launcher offers as one (spec §8.2).</summary>
public sealed class OverlayRowViewModel : ViewModel
{
    public OverlayRowViewModel(string name, string path, string note, bool on, Command toggle)
    {
        Name = name;
        Path = path;
        Note = note;
        On = on;
        Toggle = toggle;
    }

    public string Name { get; }
    public string Path { get; }
    public string Note { get; }
    public bool On { get; }
    public Command Toggle { get; }
}

/// <summary>
/// The Advanced tab (launcher spec §8.4, plan §6.8): the install's overlays, chosen from the subfolders that hold files
/// the catalog names (<see cref="Installs.OverlayCandidates"/>), and the BMP switch; the engine's startup options in
/// <c>profile.toml</c> (the renderer, the text backend, the Lua error policy, what a failed link does, and Direct3D 12
/// on Windows); and the diagnostics folder with a summary to copy (<see cref="DiagnosticsSummary"/>). Each change is
/// written at once, under the profile's lock.
/// </summary>
public sealed class AdvancedViewModel : ViewModel
{
    private readonly ProfilePageViewModel _page;
    private IReadOnlyList<OverlayRowViewModel> _overlays = [];
    private IReadOnlyList<OverlayCandidate> _candidates = [];
    private string? _result;
    private bool _resultBad;
    private bool _refreshing;

    public AdvancedViewModel(ProfilePageViewModel page)
    {
        _page = page;
        ToggleBmp = new Command(() => _ = ChangeAsync(document => document with
        {
            Install = document.Install with { AllowBmpAsAgf = !document.Install.AllowBmpAsAgf },
        }), () => CanWrite);
        ToggleDirect3D12 = new Command(() => _ = ChangeAsync(document => document with
        {
            Launcher = document.Launcher with
            {
                GraphicsApi = document.Launcher.GraphicsApi == ProfileLauncherTable.Direct3D12
                    ? ProfileLauncherTable.DefaultApi
                    : ProfileLauncherTable.Direct3D12,
            },
        }), () => CanWrite);
        OpenDiagnostics = new Command(() =>
        {
            string folder = Path.Combine(_page.Entry.Root, EngineLaunch.DiagnosticsFolder);
            Directory.CreateDirectory(folder);
            _page.Main.Host?.Open(folder);
        }, () => _page.Entry.Loaded != null && !_page.Entry.NeedsReview);
        CopySummary = new Command(() => _ = CopySummaryAsync(), () => _page.Entry.Loaded != null);
    }

    public LauncherStrings S => _page.S;
    private OmeHome Home => _page.Main.Session.Home;
    private ProfileDocument? Document => _page.Entry.Loaded?.Document;
    public bool HasDocument => Document != null && !_page.Entry.NeedsReview;
    public bool CanWrite => HasDocument && _page.IsUnlocked;
    public bool IsLocked => _page.IsLocked;
    public static bool OnWindows => OperatingSystem.IsWindows();

    public void Refresh()
    {
        _refreshing = true;
        try
        {
            _candidates = Candidates();
            Overlays = BuildOverlays();
            ChangedAll();
        }
        finally
        {
            _refreshing = false;
        }
        foreach (Command command in new[] { ToggleBmp, ToggleDirect3D12, OpenDiagnostics, CopySummary }) command.Refresh();
    }

    // ---- the install ----

    /// <summary>The install's subfolders that hold files the catalog names, once the install is found.</summary>
    private IReadOnlyList<OverlayCandidate> Candidates()
    {
        if (Document is not { } document || _page.Catalog is not { } catalog) return [];
        try
        {
            return Installs.OverlayCandidates(document.Install.Root, catalog);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The profile's overlays in their order, then the candidates it does not use.</summary>
    private IReadOnlyList<OverlayRowViewModel> BuildOverlays()
    {
        if (Document is not { } document) return [];
        var rows = new List<OverlayRowViewModel>();
        foreach (string overlay in document.Install.Overlays)
        {
            OverlayCandidate? candidate = _candidates.FirstOrDefault(each => SamePath(each.Path, overlay));
            string note = candidate != null ? Note(candidate)
                : Directory.Exists(overlay) ? S["advanced.overlay-other"] : S["advanced.overlay-gone"];
            rows.Add(Row(overlay, note, on: true));
        }
        foreach (OverlayCandidate candidate in _candidates
                     .Where(each => !document.Install.Overlays.Any(overlay => SamePath(overlay, each.Path))))
            rows.Add(Row(candidate.Path, Note(candidate), on: false));
        return rows;

        OverlayRowViewModel Row(string path, string note, bool on)
            => new(Shown(path, document.Install.Root), path, note, on,
                   new Command(() => _ = ToggleOverlayAsync(path), () => CanWrite));
    }

    private string Note(OverlayCandidate candidate)
        => S.Format(candidate.Files == 1 ? "advanced.overlay-file" : "advanced.overlay-files", candidate.Files)
           + (candidate.BmpAsAgf ? " · " + S["advanced.overlay-bmp"] : "");

    /// <summary>A folder in the install by its path there, as <c>--overlay-root</c> takes it; another in full.</summary>
    private static string Shown(string path, string install)
    {
        string relative = System.IO.Path.GetRelativePath(install, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative) ? path : relative;
    }

    private static bool SamePath(string first, string second)
        => OmeHome.PathsEqual(first, second, LauncherEnvironment.Current.Platform);

    public IReadOnlyList<OverlayRowViewModel> Overlays
    {
        get => _overlays;
        private set => Set(ref _overlays, value);
    }
    public bool HasOverlays => _overlays.Count > 0;
    public bool HasNoOverlays => HasDocument && _overlays.Count == 0;

    /// <summary>Turns an overlay on, after those the profile has, or off. A folder with BMP files under AGF names turns
    /// the BMP switch on with it (spec §8.2).</summary>
    public Task ToggleOverlayAsync(string path)
    {
        OverlayCandidate? candidate = _candidates.FirstOrDefault(each => SamePath(each.Path, path));
        return ChangeAsync(document =>
        {
            ProfileInstall install = document.Install;
            if (install.Overlays.Any(overlay => SamePath(overlay, path)))
                return document with
                {
                    Install = install with { Overlays = install.Overlays.Where(overlay => !SamePath(overlay, path)).ToList() },
                };
            return document with
            {
                Install = install with
                {
                    Overlays = [.. install.Overlays, path],
                    AllowBmpAsAgf = install.AllowBmpAsAgf || candidate?.BmpAsAgf == true,
                },
            };
        });
    }

    public bool AllowBmpAsAgf => Document?.Install.AllowBmpAsAgf == true;
    public Command ToggleBmp { get; }

    /// <summary>An overlay in use holds BMP files under AGF names while the switch is off: the engine would refuse
    /// them.</summary>
    public bool BmpNeeded => !AllowBmpAsAgf && Document is { } document
                             && _candidates.Any(each => each.BmpAsAgf
                                                        && document.Install.Overlays.Any(overlay => SamePath(overlay, each.Path)));

    // ---- the engine ----

    public IReadOnlyList<OptionChoice> Renderers =>
        [new("gpu", S["advanced.renderer-gpu"]), new("software", S["advanced.renderer-software"])];
    public IReadOnlyList<OptionChoice> TextBackends =>
    [
        new("auto", S["advanced.text-auto"]), new("gdi", S["advanced.text-gdi"]), new("portable", S["advanced.text-portable"]),
    ];
    public IReadOnlyList<OptionChoice> LuaErrorModes =>
        [new("continue", S["advanced.lua-continue"]), new("halt", S["advanced.lua-halt"])];
    public IReadOnlyList<OptionChoice> FailureModes =>
        [new("stop", S["advanced.failure-stop"]), new("skip", S["advanced.failure-skip"])];

    /// <summary>A key the file leaves out shows the engine's default, the first choice.</summary>
    public OptionChoice? Renderer
    {
        get => Pick(Renderers, Document?.Launch.Renderer);
        set => Choose(value, Document?.Launch.Renderer ?? "gpu",
                      choice => document => document with { Launch = document.Launch with { Renderer = choice } });
    }
    public bool SoftwareRenderer => Renderer?.Value == "software";

    public OptionChoice? TextBackend
    {
        get => Pick(TextBackends, Document?.Launch.TextBackend);
        set => Choose(value, Document?.Launch.TextBackend ?? "auto",
                      choice => document => document with { Launch = document.Launch with { TextBackend = choice } });
    }

    public OptionChoice? LuaErrors
    {
        get => Pick(LuaErrorModes, Document?.Launch.LuaErrors);
        set => Choose(value, Document?.Launch.LuaErrors ?? "continue",
                      choice => document => document with { Launch = document.Launch with { LuaErrors = choice } });
    }

    public OptionChoice? OnFailure
    {
        get => Document?.Mods is { } mods ? FailureModes[mods.SkipFailed ? 1 : 0] : null;
        set => Choose(value, Document?.Mods?.SkipFailed == true ? "skip" : "stop",
                      choice => document => document.Mods is { } mods
                          ? document with { Mods = mods with { SkipFailed = choice == "skip" } }
                          : document);
    }
    public bool HasMods => Document?.Mods != null;

    public bool Direct3D12 => Document?.Launcher.GraphicsApi == ProfileLauncherTable.Direct3D12;
    public Command ToggleDirect3D12 { get; }

    private static OptionChoice? Pick(IReadOnlyList<OptionChoice> choices, string? value)
        => choices.FirstOrDefault(choice => choice.Value == value) ?? choices[0];

    private void Choose(OptionChoice? value, string current, Func<string, Func<ProfileDocument, ProfileDocument>> change)
    {
        if (_refreshing || value == null || !CanWrite || value.Value == current) return;
        _ = ChangeAsync(change(value.Value));
    }

    // ---- diagnostics ----

    public Command OpenDiagnostics { get; }
    public Command CopySummary { get; }

    /// <summary>Copies the profile's diagnostics summary, with its checks as they last ran, to the clipboard.</summary>
    public async Task CopySummaryAsync()
    {
        if (_page.Main.Host is not { } host) return;
        ProfileEntry entry = _page.Entry;
        ProfileCheckResult? checks = _page.Checks;
        string summary = await Task.Run(() => DiagnosticsSummary.Build(entry, checks, App.Version, Home));
        await host.CopyTextAsync(summary);
        Say(S["advanced.summary-copied"], bad: false);
    }

    // ---- the outcome ----

    public string? Result
    {
        get => _result;
        private set
        {
            Set(ref _result, value);
            Changed(nameof(HasResult));
        }
    }
    public bool HasResult => _result != null;
    public bool ResultBad
    {
        get => _resultBad;
        private set => Set(ref _resultBad, value);
    }

    private void Say(string text, bool bad)
    {
        ResultBad = bad;
        Result = text;
    }

    /// <summary>Changes <c>profile.toml</c> under the profile's lock; the page then shows the file as it is.</summary>
    public async Task ChangeAsync(Func<ProfileDocument, ProfileDocument> change)
    {
        if (!CanWrite) return;
        Result = null;
        await _page.WriteAsync(entry => ProfileStore.Update(entry, change, Home),
                               message => Say(S["advanced.write-failed"] + " " + message, bad: true));
        Refresh();
    }
}
