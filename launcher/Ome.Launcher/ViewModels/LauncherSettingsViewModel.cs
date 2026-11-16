using Ome.Engine.Frontend.Shell;
using Ome.Launcher.Core;

namespace Ome.Launcher.ViewModels;

/// <summary>
/// The launcher's own settings (launcher spec §8.8, plan §6.11), each written to <c>launcher.toml</c> at once: the
/// language (the operating system's by default), the theme, what the window does while a game runs, whether to
/// download art (§8.7), and the defaults a new profile's <c>OME.INI</c> starts from (the game's language, the FPS
/// counter, the page locator). A new language and a new theme show at once.
/// <para>Each list is built once and kept: a list read anew at each change would hand its dropdown other objects, and
/// the dropdown would lose what it shows (plan §10, item 5). Only a new language builds them again, once the pick
/// that changed it has finished: a list replaced while its dropdown still writes the pick made the dropdown write
/// its first option back, which undid the pick. The lists are then announced before what is picked in them.</para>
/// </summary>
public sealed class LauncherSettingsViewModel : ViewModel
{
    private readonly MainViewModel _main;
    private string? _error;
    private IReadOnlyList<OptionChoice> _languages = [], _themes = [], _whileRunning = [], _art = [], _newLanguages = [];
    private string _listsLanguage = "";

    public LauncherSettingsViewModel(MainViewModel main)
    {
        _main = main;
        ToggleNewFps = new Command(() => Save(settings => settings with
        {
            NewProfile = settings.NewProfile with { FpsCounter = !settings.NewProfile.FpsCounter },
        }));
        ToggleNewLocator = new Command(() => Save(settings => settings with
        {
            NewProfile = settings.NewProfile with { LocatorHud = !settings.NewProfile.LocatorHud },
        }));
        Close = new Command(() => _main.CloseDialog(this));
        BuildLists();
    }

    private void BuildLists()
    {
        _listsLanguage = S.Language;
        _languages =
        [
            new("", S.Format("lsettings.language-system", LanguageName(LauncherStrings.Choose(null, _main.Session.OsCulture)))),
            .. LauncherStrings.Languages.Select(tag => new OptionChoice(tag, LanguageName(tag))),
        ];
        _themes =
        [
            new(nameof(LauncherTheme.System), S["lsettings.theme-system"]),
            new(nameof(LauncherTheme.Light), S["lsettings.theme-light"]),
            new(nameof(LauncherTheme.Dark), S["lsettings.theme-dark"]),
        ];
        _whileRunning =
        [
            new(nameof(WhileRunning.Stay), S["lsettings.running-stay"]),
            new(nameof(WhileRunning.Minimize), S["lsettings.running-minimize"]),
            new(nameof(WhileRunning.Close), S["lsettings.running-close"]),
        ];
        _art =
        [
            new(nameof(ArtConsent.Ask), S["lsettings.art-ask"]),
            new(nameof(ArtConsent.Always), S["lsettings.art-always"]),
            new(nameof(ArtConsent.Never), S["lsettings.art-never"]),
        ];
        _newLanguages = [.. ShellStrings.Languages.Select(tag => new OptionChoice(tag, LanguageName(tag)))];
    }

    /// <summary>The lists in the language now shown.</summary>
    private void Relabel()
    {
        BuildLists();
        Refresh();
    }

    /// <summary>Announces every property: the text and the lists first, then what is picked in them.</summary>
    private void Refresh()
    {
        foreach (string name in new[]
                 {
                     nameof(S), nameof(Languages), nameof(Themes), nameof(WhileRunningChoices), nameof(ArtChoices),
                     nameof(NewLanguages), nameof(SelectedLanguage), nameof(SelectedTheme), nameof(SelectedWhileRunning),
                     nameof(SelectedArt), nameof(SelectedNewLanguage), nameof(NewFps), nameof(NewLocator),
                 })
            Changed(name);
    }

    public LauncherStrings S => _main.S;
    private LauncherSettings Settings => _main.Session.Settings;

    private string LanguageName(string tag) => S.Has($"language.{tag}") ? S[$"language.{tag}"] : tag;

    // ---- the launcher's language ----

    /// <summary>The operating system's language (named with what it resolves to), then each table the launcher
    /// has.</summary>
    public IReadOnlyList<OptionChoice> Languages => _languages;

    public OptionChoice? SelectedLanguage
    {
        get => Languages.FirstOrDefault(choice => choice.Value == (Settings.Language ?? ""));
        set
        {
            if (value == null || value.Value == (Settings.Language ?? "")) return;
            if (!Save(settings => settings with { Language = value.Value.Length == 0 ? null : value.Value })) return;
            _main.ApplyLanguage();
            if (S.Language == _listsLanguage) return;
            if (SynchronizationContext.Current is { } ui) ui.Post(_ => Relabel(), null);
            else Relabel();
        }
    }

    // ---- the theme ----

    public IReadOnlyList<OptionChoice> Themes => _themes;

    public OptionChoice? SelectedTheme
    {
        get => Themes.FirstOrDefault(choice => choice.Value == Settings.Theme.ToString());
        set
        {
            if (value == null || !Enum.TryParse(value.Value, out LauncherTheme theme) || theme == Settings.Theme) return;
            if (Save(settings => settings with { Theme = theme })) _main.Host?.ApplyTheme(theme);
        }
    }

    // ---- while a game runs ----

    public IReadOnlyList<OptionChoice> WhileRunningChoices => _whileRunning;

    public OptionChoice? SelectedWhileRunning
    {
        get => WhileRunningChoices.FirstOrDefault(choice => choice.Value == Settings.WhileRunning.ToString());
        set
        {
            if (value == null || !Enum.TryParse(value.Value, out WhileRunning running) || running == Settings.WhileRunning)
                return;
            Save(settings => settings with { WhileRunning = running });
        }
    }

    // ---- art ----

    public IReadOnlyList<OptionChoice> ArtChoices => _art;

    /// <summary><c>always</c> downloads what is missing at once, as answering the window's question does.</summary>
    public OptionChoice? SelectedArt
    {
        get => ArtChoices.FirstOrDefault(choice => choice.Value == Settings.Art.ToString());
        set
        {
            if (value == null || !Enum.TryParse(value.Value, out ArtConsent art) || art == Settings.Art) return;
            _ = AnswerAsync(art);
        }
    }

    private async Task AnswerAsync(ArtConsent art)
    {
        try
        {
            await _main.AnswerArtAsync(art);
            Error = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Error = error.Message;
        }
        Changed(nameof(SelectedArt));
    }

    // ---- a new profile's OME.INI ----

    public IReadOnlyList<OptionChoice> NewLanguages => _newLanguages;

    public OptionChoice? SelectedNewLanguage
    {
        get => NewLanguages.FirstOrDefault(choice => choice.Value == Settings.NewProfile.Language);
        set
        {
            if (value == null || value.Value == Settings.NewProfile.Language) return;
            Save(settings => settings with { NewProfile = settings.NewProfile with { Language = value.Value } });
        }
    }

    public bool NewFps => Settings.NewProfile.FpsCounter;
    public bool NewLocator => Settings.NewProfile.LocatorHud;
    public Command ToggleNewFps { get; }
    public Command ToggleNewLocator { get; }

    public string? Error
    {
        get => _error;
        private set
        {
            Set(ref _error, value);
            Changed(nameof(HasError));
        }
    }
    public bool HasError => _error != null;

    public Command Close { get; }

    /// <summary>Writes the change to <c>launcher.toml</c>; a failed write is shown and changes nothing.</summary>
    private bool Save(Func<LauncherSettings, LauncherSettings> change)
    {
        try
        {
            _main.Session.Save(change(Settings));
            Error = null;
            Refresh();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Error = error.Message;
            Refresh();
            return false;
        }
    }
}
