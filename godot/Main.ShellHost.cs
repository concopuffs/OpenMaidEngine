using Ome.Engine.Frontend.Shell;

/// <summary>
/// The engine shell's frontend services (<see cref="IShellHost"/>; docs/superpowers/specs/2026-11-09-engine-shell-design.md
/// §4.1). The menu bar's commands and the 終了 dialog's outcomes reach the screen mode, the screenshot, quitting,
/// returning to the title and the VM hold through it.
/// </summary>
public partial class Main : IShellHost
{
    private IShellHost Shell => this;

    bool IShellHost.Windowed => _windowed;

    bool IShellHost.SwitchScreenMode(bool windowed, string source) => SwitchScreenMode(windowed, source);

    void IShellHost.SaveScreenshot() => SaveScreenshotFromMenu();

    void IShellHost.ChooseImageFolder() => ChooseScreenshotFolder();

    void IShellHost.Quit() => GetTree().Quit(0);   // _ExitTree stops the VM and runs the shutdown flush

    void IShellHost.ReturnToTitle() => _vm?.RequestRootReload();

    void IShellHost.HoldVm(bool hold) => _host?.SetFrontendModalHold(hold);
}
