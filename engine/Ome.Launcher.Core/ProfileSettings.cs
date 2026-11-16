using Ome.Engine.Frontend.Shell;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;

namespace Ome.Launcher.Core;

/// <summary>
/// The settings a profile's Settings page edits (launcher spec §8.4): <c>OME.INI</c>'s five <see cref="PortOptions"/>
/// fields, and <c>SYS4REG.INI</c>'s <c>display:ScreenMode</c>. Both files are the profile root's, read as the engine
/// reads them (<see cref="OmeIniStore"/>, <see cref="Sys4RegIniStore"/>): a key <c>SYS4REG.INI</c> lacks takes the game's
/// <c>SYS4INI.BIN</c> value, else the engine's default.
/// <para>The game starts in a window when <c>ScreenMode</c> is not 0, and fullscreen by default
/// (<c>engine_initialize_graphics_from_settings</c>, docs/engine-re.md). <c>display:ForceScreen</c> 1 does not change
/// the start: it refuses every switch out of a window (<c>ShellServices.SwitchScreenMode</c>).</para>
/// </summary>
public sealed record ProfileSettings(PortOptions Port, bool Windowed, bool ForceScreen)
{
    public const string DisplaySection = "display", ScreenModeKey = "ScreenMode", ForceScreenKey = "ForceScreen";

    /// <param name="startup">The install's <c>SYS4INI.BIN</c> settings, whose values a missing key takes; empty when
    /// the install cannot be read.</param>
    public static ProfileSettings Load(string root, Sys4StartupSettings startup)
    {
        PortOptions port = new OmeIniStore(Path.Combine(root, PortOptions.FileName)).Load();
        Sys4RegIniStore registry = Sys4RegIniStore.ForPath(startup, Path.Combine(root, Sys4RegIniStore.FileName));
        return new ProfileSettings(port, registry.LoadInt(DisplaySection, ScreenModeKey, 0) != 0,
                                   registry.LoadInt(DisplaySection, ForceScreenKey, 0) == 1);
    }

    /// <summary>
    /// Changes the profile's <c>OME.INI</c> options by <paramref name="port"/>, applied to the file as it is once the
    /// lock is held (so two changes in a row both land), and writes its start mode to <c>SYS4REG.INI</c>
    /// (<c>ScreenMode</c> 1 for a window, 0 for fullscreen, as the engine writes it); each only when given, under the
    /// profile's lock. The files are written as the engine writes them: <c>OME.INI</c> keeps the keys it does not
    /// know, and <c>SYS4REG.INI</c> is rewritten in the loader's order with the keys AGE does not bind kept.
    /// <para>A <c>SYS4REG.INI</c> write fills every bound key the file lacks from <paramref name="startup"/>, the
    /// install's <c>SYS4INI.BIN</c> settings, so the start mode is written only with them: without the install, the
    /// write would put the engine's defaults where the game's belong.</para>
    /// </summary>
    /// <exception cref="ProfileBusyException">The game runs the profile; nothing is written.</exception>
    /// <exception cref="ArgumentException">A start mode without the install's settings.</exception>
    public static void Save(string root, Func<PortOptions, PortOptions>? port = null, bool? windowed = null,
                            Sys4StartupSettings? startup = null)
    {
        if (windowed != null && startup == null)
            throw new ArgumentException("the start mode is written with the install's SYS4INI settings", nameof(startup));
        using RunLock held = ProfileStore.Lock(root);
        if (port != null)
        {
            var store = new OmeIniStore(Path.Combine(root, PortOptions.FileName));
            store.Save(port(store.Load()));
        }
        if (windowed is { } value)
            Sys4RegIniStore.ForPath(startup!, Path.Combine(root, Sys4RegIniStore.FileName))
                .SaveInt(DisplaySection, ScreenModeKey, value ? 1 : 0);
    }

    /// <summary>The rows the port's menu bar adds above the game in a window (<c>NativeMenuBar.BarHeight</c> in
    /// <c>godot/NativeMenuBar.cs</c>); the port has the bar unless it runs with <c>--no-menu-bar</c>.</summary>
    public const int MenuBarHeight = 20;

    /// <summary>The window sizes the Settings page offers, as the port's options panel offers them
    /// (<see cref="ShellPanel.WindowSizes"/>): the game's canvas at 100 % to 200 %, those that fit
    /// <paramref name="usable"/> with the menu bar, and the saved size when it is none of them. Null
    /// <paramref name="usable"/> (a screen the window cannot measure) offers all five.</summary>
    public static IReadOnlyList<(int Width, int Height)> WindowSizes(Sys4LogicalCanvas canvas, PortOptions port,
                                                                       (int Width, int Height)? usable)
        => ShellPanel.WindowSizes(canvas.Width, canvas.Height, usable ?? (int.MaxValue, int.MaxValue),
                                  port.CanvasWindowSize ? null : (port.WindowWidth, port.WindowHeight), MenuBarHeight);
}
