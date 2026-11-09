namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// The frontend services the engine shell's commands reach (docs/superpowers/specs/2026-11-09-engine-shell-design.md,
/// §4.1). These run on the frontend's main thread: the screen mode, the screenshot and its folder, quitting, returning
/// to the title, and holding the VM. VM services such as the message toggles and the message settings are not here:
/// they keep their own queues on the VM, so a toggle still applies on the VM thread.
/// </summary>
public interface IShellHost
{
    /// <summary>True while the game runs in a window (IAGEService +0x08).</summary>
    bool Windowed { get; }

    /// <summary>Switch to a window or to fullscreen as IAGEService +0x1c does (display_toggle_screen_mode_and_menu_bar),
    /// which queues CALLBACK_WINDOW.BIN. False when the game is already in that mode or display:ForceScreen keeps the
    /// window.</summary>
    /// <param name="source">What asked, for the log.</param>
    bool SwitchScreenMode(bool windowed, string source);

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
