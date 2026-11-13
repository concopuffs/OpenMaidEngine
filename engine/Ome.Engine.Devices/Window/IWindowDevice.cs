namespace Ome.Engine.Devices;

/// <summary>
/// The game's window (engine layering spec §6.1, milestone 9): in a window, or fullscreen. The shell-services native
/// decides a switch (whether it happens, what it stores, what it launches) and tells the window to show it.
/// <para>Called on the front end's thread, where every switch is made: the menu, Alt+Enter, the maximize box, and the
/// frame service that takes op 0x2cf's request. Milestone 11 decides the threads.</para>
/// </summary>
public interface IWindowDevice
{
    /// <summary>Show the game in a window (with its menu bar) or fullscreen (without one).</summary>
    void ShowScreenMode(bool windowed);
}
