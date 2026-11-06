using Godot;

/// <summary>
/// AGE's window-close policy (age_main_window_proc@0x486320, WM_CLOSE).
/// <list type="bullet">
/// <item>Forced close (wParam 1: op 0x1's Command_Exit_Exception catch; menu command 0x9c6b, which only the
/// debug menu 124 holds and no code loads) quits without asking. The port's op-0x1 path ends the VM with halt
/// reason "exit-request" and quits here.</item>
/// <item>An unforced close (the window's close button, ESC, the menu bar's ｹﾞｰﾑ > 終了 command 0x9c41) runs
/// wm_close_confirm_exit_or_title@0x405740: the screen dims to alpha 0x80 and the modal 終了 dialog (AGERC host
/// command 3) asks. Quit (1) blackens the screen and closes; タイトルに戻る (2) restores the screen, waits 250 ms
/// and calls op_0x9_reset_scene_and_reload_root; Cancel (0) restores the screen.</item>
/// <item>Run-state 0x1000000 skips the dialog and quits. For a windowed game WM_SYSCOMMAND SC_MINIMIZE sets it
/// and SC_RESTORE clears it, so closing the minimized game (from the taskbar) quits at once. The port reads the
/// window's minimized state when the close arrives.</item>
/// </list>
/// The port's AGERC menu bar (Main.MenuBar.cs) sends 0x9c41 here as an unforced close.
/// An accepted close saves the shared profile unless set:NoSaveDat (_ExitTree's shutdown flush).
/// <para>Native's dialogs are modal: their message loop runs inside AGE's main loop, so the game window keeps the
/// last presented frame until the dialog closes. The port shows a snapshot of that frame meanwhile
/// (<see cref="FreezePresentedFrame"/>), since Godot itself keeps presenting.</para>
/// </summary>
public partial class Main
{
    private const double ReturnToTitleDelaySeconds = 0.25;   // FUN_00405740: Sleep(250) before op 0x9

    private ExitConfirmationDialog? _exitConfirmation;
    private ScriptErrorDialog? _scriptErrorDialog;
    private CanvasLayer? _exitDimLayer;
    private ColorRect? _exitDim;
    private CanvasLayer? _frozenFrameLayer;
    private TextureRect? _frozenFrame;
    /// <summary>The frame the first hold froze: what native's back buffer still holds while a modal loop runs.</summary>
    private Image? _frozenFrameImage;
    private int _frozenFrameHolds;

    private void InitializeExitPolicy()
    {
        GetTree().AutoAcceptQuit = false;
    }

    public override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationWMCloseRequest:
                RequestUnforcedClose();
                break;
            case NotificationApplicationFocusIn:
                SetMenuBarActive(true);
                break;
            case NotificationApplicationFocusOut:
                SetMenuBarActive(false);   // an inactive window grays its bar; deactivation ends a menu loop
                HideMenuStrip("the window lost focus");   // WM_ACTIVATE(WA_INACTIVE) on the strip
                break;
        }
    }

    /// <summary>Native WM_CLOSE with wParam 0: confirm through the 終了 dialog while a game is running.</summary>
    private void RequestUnforcedClose()
    {
        // A modal message box disables AGE's window, so neither its close button nor ESC can ask meanwhile.
        if (_exitConfirmation != null || _scriptErrorDialog != null || _agercDialog != null) return;
        if (_vm == null || _host == null || _done || _selftest)
        {
            GetTree().Quit(0);
            return;
        }
        if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Minimized)
        {
            // Run-state 0x1000000: wm_close_confirm_exit_or_title returns 1 before it dims or asks.
            GD.Print("[exit] close while minimized: quitting without the 終了 dialog (AGE run-state 0x1000000)");
            GetTree().Quit(0);   // _ExitTree runs the shared-profile shutdown flush, as native saves SAVE.DAT
            return;
        }

        _host.SetFrontendModalHold(true);
        FreezePresentedFrame(true);
        SetExitDim(128 / 255.0f);
        var dialog = new ExitConfirmationDialog();
        _exitConfirmation = dialog;
        dialog.Completed += choice =>
        {
            _exitConfirmation = null;
            dialog.QueueFree();
            CompleteUnforcedClose(choice);
        };
        AddChild(dialog);
        dialog.Open(new Vector2I(_screenWidth, _screenHeight));
    }

    private void CompleteUnforcedClose(ExitConfirmationDialog.Choice choice)
    {
        switch (choice)
        {
            case ExitConfirmationDialog.Choice.Quit:
                SetExitDim(1.0f);
                GetTree().Quit(0);   // _ExitTree stops the VM and runs the shared-profile shutdown flush
                break;
            case ExitConfirmationDialog.Choice.ReturnToTitle:
                SetExitDim(0.0f);
                GetTree().CreateTimer(ReturnToTitleDelaySeconds).Timeout += () =>
                {
                    // FUN_00405740 sleeps 250 ms inside the modal handler, so the frame stays frozen until then.
                    FreezePresentedFrame(false);
                    _vm?.RequestRootReload();
                    _host?.SetFrontendModalHold(false);
                };
                break;
            default:
                SetExitDim(0.0f);
                FreezePresentedFrame(false);
                _host?.SetFrontendModalHold(false);
                break;
        }
    }

    private void SetExitDim(float alpha)
    {
        if (_exitDim == null)
        {
            _exitDimLayer = new CanvasLayer { Layer = 100 };
            _exitDim = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
            PlaceOverGame(_exitDim);   // AGE dims its client area, not the menu bar
            _exitDimLayer.AddChild(_exitDim);
            AddChild(_exitDimLayer);
        }
        _exitDim.Color = new Color(0, 0, 0, alpha);
        _exitDim.Visible = alpha > 0;
    }

    /// <summary>While held, show the frame presented when the first hold began instead of the live game, as
    /// native's modal dialog loops do. Holds nest; each true needs a false.</summary>
    private void FreezePresentedFrame(bool hold)
    {
        if (!hold)
        {
            if (_frozenFrameHolds > 0 && --_frozenFrameHolds == 0 && _frozenFrame != null)
            {
                _frozenFrame.Visible = false;
                _frozenFrame.Texture = null;
                _frozenFrameImage = null;
            }
            return;
        }
        if (_frozenFrameHolds++ > 0) return;
        if (_frozenFrame == null)
        {
            _frozenFrameLayer = new CanvasLayer { Layer = 99 };   // under the exit dim (100) and the dialogs
            _frozenFrame = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Stop,
            };
            PlaceOverGame(_frozenFrame);
            _frozenFrameLayer.AddChild(_frozenFrame);
            AddChild(_frozenFrameLayer);
        }
        Image? presented = CaptureGameImage();
        if (presented == null) return;
        _frozenFrameImage = presented;
        _frozenFrame.Texture = ImageTexture.CreateFromImage(presented);
        _frozenFrame.Visible = true;
    }

    /// <summary>Native forced close after op 0x1 (Command_Exit_Exception) or an interpreter error answered 中止:
    /// both leave through continuation 0x412961 and quit without the dialog.</summary>
    private void QuitAfterExitRequest(string reason = "exit request")
    {
        GD.Print($"[vm] {reason}: closing like AGE's forced WM_CLOSE");
        GetTree().Quit(0);
    }
}
