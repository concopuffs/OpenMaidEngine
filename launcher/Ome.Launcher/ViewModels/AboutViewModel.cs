namespace Ome.Launcher.ViewModels;

/// <summary>A licence file About opens: its name, and where it is, or null when it is not beside the launcher.</summary>
public sealed record LicenceFile(string Name, string? Path, Command Open);

/// <summary>
/// About (launcher spec §9, plan §6.11): the launcher's version, the engine its Play starts, the licences
/// (<c>LICENSE</c> and <c>THIRD_PARTY_NOTICES.md</c>, which ship beside the launcher), and the repository's address.
/// </summary>
public sealed class AboutViewModel : ViewModel
{
    /// <summary>The project's home (AGENTS.md, "One home: GitHub").</summary>
    public const string Repository = "https://github.com/concopuffs/OpenMaidEngine";

    private readonly MainViewModel _main;

    public AboutViewModel(MainViewModel main)
    {
        _main = main;
        string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(main.Session.LauncherProgram))!;
        Licences = new[] { "LICENSE", "THIRD_PARTY_NOTICES.md" }.Select(name =>
        {
            string path = System.IO.Path.Combine(folder, name);
            string? found = File.Exists(path) ? path : null;
            return new LicenceFile(name, found, new Command(() => _main.Host?.Open(found!), () => found != null));
        }).ToList();
        OpenRepository = new Command(() => _main.Host?.Open(Repository));
        Close = new Command(() => _main.CloseDialog(this));
    }

    public LauncherStrings S => _main.S;
    public string VersionText => S.Format("about.version", App.Version);

    /// <summary>The engine Play starts: the one beside the launcher, or the program <c>--engine</c> named.</summary>
    public string EngineText => string.Join(" ", [_main.Session.Engine.Program, .. _main.Session.Engine.Arguments]);
    public bool EngineMissing => !File.Exists(_main.Session.Engine.Program);

    public IReadOnlyList<LicenceFile> Licences { get; }
    public bool LicencesMissing => Licences.Any(licence => licence.Path == null);
    public string RepositoryText => Repository;
    public Command OpenRepository { get; }
    public Command Close { get; }
}
