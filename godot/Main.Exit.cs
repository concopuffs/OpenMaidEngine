using Godot;

/// <summary>
/// AGE's window-close policy (age_main_window_proc@0x486320, WM_CLOSE).
/// <list type="bullet">
/// <item>Forced close (wParam 1: op 0x1's Command_Exit_Exception catch, system-menu command 0x9c6b) quits
/// without asking. The port's op-0x1 path ends the VM with halt reason "exit-request" and quits here.</item>
/// <item>An unforced close (the window's close button, ESC, menu command 0x9c41) runs FUN_00405740: the
/// screen dims to alpha 0x80 and the modal 終了 dialog (AGERC host command 3) asks. Quit (1) blackens the
/// screen and closes; タイトルに戻る (2) restores the screen, waits 250 ms and calls
/// op_0x9_reset_scene_and_reload_root; Cancel (0) restores the screen. Run-state 0x1000000 (set while a
/// windowed game is minimized) skips the dialog and quits.</item>
/// </list>
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
    private int _frozenFrameHolds;

    private void InitializeExitPolicy()
    {
        GetTree().AutoAcceptQuit = false;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) RequestUnforcedClose();
    }

    /// <summary>Native WM_CLOSE with wParam 0: confirm through the 終了 dialog while a game is running.</summary>
    private void RequestUnforcedClose()
    {
        // A modal message box disables AGE's window, so neither its close button nor ESC can ask meanwhile.
        if (_exitConfirmation != null || _scriptErrorDialog != null) return;
        if (_vm == null || _host == null || _done || _selftest)
        {
            GetTree().Quit(0);
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
            _exitDim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
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
            _frozenFrame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _frozenFrameLayer.AddChild(_frozenFrame);
            AddChild(_frozenFrameLayer);
        }
        Image? presented = GetViewport().GetTexture()?.GetImage();
        if (presented == null) return;
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
