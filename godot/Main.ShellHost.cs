using Ome.Engine.Frontend.Shell;

/// <summary>
/// The engine shell's frontend services (<see cref="IShellHost"/>; docs/superpowers/specs/2026-11-09-engine-shell-design.md
/// §4.1). The shell's commands (<see cref="ShellCommands"/>) and the 終了 dialog's outcomes reach the screen mode, the
/// screenshot, quitting, returning to the title, the VM hold and the shell's own surfaces through it.
/// </summary>
public partial class Main : IShellHost
{
    /// <summary>The shell's text, in OME.INI's ui:Language (spec §5.6; Main.OptionsPanel.cs).</summary>
    private ShellStrings _shellStrings = ShellStrings.Japanese;

    private IShellHost Shell => this;

    bool IShellHost.Windowed => _windowed;

    void IShellHost.RequestClose() => RequestUnforcedClose();   // PostMessage(WM_CLOSE, 0): the 終了 dialog

    /// <summary>The options panel (Main.OptionsPanel.cs), which replaces AGERC's 設定 and ﾊﾞｰｼﾞｮﾝ情報.</summary>
    void IShellHost.OpenPanel(ShellSection section, bool diagnostics) => OpenOptionsPanel(section, diagnostics);

    bool IShellHost.SwitchScreenMode(bool windowed, string source) => SwitchScreenMode(windowed, source);

    void IShellHost.SaveScreenshot() => SaveScreenshotFromMenu();

    void IShellHost.ChooseImageFolder() => ChooseScreenshotFolder();

    void IShellHost.Quit() => GetTree().Quit(0);   // _ExitTree stops the VM and runs the shutdown flush

    void IShellHost.ReturnToTitle() => _vm?.RequestRootReload();

    void IShellHost.HoldVm(bool hold) => _host?.SetFrontendModalHold(hold);
}
