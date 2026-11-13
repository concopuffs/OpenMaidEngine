namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// The frontend services the engine shell's commands reach (docs/superpowers/specs/2026-11-09-engine-shell-design.md,
/// §4.1). These run on the frontend's main thread: the screenshot and its folder, quitting, returning to the title,
/// holding the VM, and opening the shell's own surfaces. What the engine owns is not here: the message toggles, the
/// 設定 values and the screen mode are the shell-services native's (<see cref="Ome.Engine.Natives.ShellServices"/>),
/// which queues the toggles for the VM thread, and the hide request is the interpreter's
/// (<see cref="Ome.Engine.Vm.IVmMenuServices"/>).
/// </summary>
public interface IShellHost
{
    /// <summary>An unforced close, as native's WM_CLOSE(0): the 終了 dialog asks, unless the game is minimized.</summary>
    void RequestClose();

    /// <summary>Open the options panel at <paramref name="section"/>; for <see cref="ShellSection.Info"/>,
    /// <paramref name="diagnostics"/> picks the diagnostics over the about view.</summary>
    void OpenPanel(ShellSection section, bool diagnostics = false);

    /// <summary>表示画面を保存: save the presented frame as a BMP in the image folder (IAGEService +0xa4,
    /// engine_save_screenshot_bmp).</summary>
    void SaveScreenshot();

    /// <summary>画像の保存場所: let the user choose the folder screenshots go to (system:SaveBMPPath). The VM holds while
    /// the folder picker is open.</summary>
    void ChooseImageFolder();

    /// <summary>Quit, as the 終了 dialog's quit does. The shutdown flush still writes the settings and the shared
    /// profile.</summary>
    void Quit();

    /// <summary>Return to the title, as the 終了 dialog's タイトルに戻る does: op 0x9's reset and root reload at the VM's
    /// next safe point.</summary>
    void ReturnToTitle();

    /// <summary>Hold the VM at its next opcode boundary while a shell surface is open, or release one hold. Holds
    /// nest, as native's modal loops do.</summary>
    void HoldVm(bool hold);
}
