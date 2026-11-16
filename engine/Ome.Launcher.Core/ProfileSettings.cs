using Ome.Engine.Frontend.Shell;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;

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
}
