namespace Ome.Launcher.ViewModels;

/// <summary>The main window (launcher spec §9): the profile list beside the selected profile's page.</summary>
public sealed class MainViewModel(LauncherSession session) : ViewModel
{
    public LauncherSession Session { get; } = session;

    /// <summary>The launcher's strings, bound as <c>S[key]</c>; a new language tells every binding at once.</summary>
    public LauncherStrings S => Session.Strings;
}
