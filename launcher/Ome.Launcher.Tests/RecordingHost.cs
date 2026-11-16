using Ome.Launcher.ViewModels;

/// <summary>Stands in for the window (<see cref="IWindowHost"/>): records what the view models asked of it, and answers
/// a folder pick with <see cref="Folder"/>.</summary>
internal sealed class RecordingHost : IWindowHost
{
    public List<string> Calls { get; } = [];

    /// <summary>The folder the next pick returns; null is a cancelled pick.</summary>
    public string? Folder { get; set; }

    public void Minimize() => Calls.Add("Minimize");
    public void Hide() => Calls.Add("Hide");
    public void Restore() => Calls.Add("Restore");
    public void Quit() => Calls.Add("Quit");
    public void Open(string path) => Calls.Add($"Open {path}");

    public Task<string?> PickFolderAsync(string title)
    {
        Calls.Add($"Pick {title}");
        return Task.FromResult(Folder);
    }
}
