namespace Ome.Launcher.ViewModels;

/// <summary>What the view models ask of the window and the desktop (launcher plan §6.3): the window's state while a
/// game runs, the launcher's end, and opening a file or folder with the OS. The window implements it; the tests record
/// the calls.</summary>
public interface IWindowHost
{
    /// <summary>Minimizes the window (<c>while_running = "minimize"</c>).</summary>
    void Minimize();

    /// <summary>Hides the window while the launcher's process stays (<c>while_running = "close"</c>, spec §8.5).</summary>
    void Hide();

    /// <summary>Shows the window again, restored and in front: a failed start comes forward (spec §8.5).</summary>
    void Restore();

    /// <summary>Ends the launcher.</summary>
    void Quit();

    /// <summary>Opens <paramref name="path"/> with the OS: a folder in the file manager, a file in its program, a
    /// web address in the browser.</summary>
    void Open(string path);

    /// <summary>Asks the player for a folder; null when they cancel.</summary>
    Task<string?> PickFolderAsync(string title);
}
