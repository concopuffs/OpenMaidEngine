using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Age.Engine.Diagnostics;
using Age.Engine.Persistence;
using Age.Engine.Profiles;
using Age.Engine.Vm;

public partial class Main
{
    private ImageTexture? _ageCursorTexture;
    private Label _locatorHud = null!;
    private FullwidthTextEditorDialog? _fullwidthTextEditor;
    private DebugSceneLauncher? _debugSceneLauncher;
    private IReadOnlyList<DebugSceneEntry> _debugSceneEntries = System.Array.Empty<DebugSceneEntry>();
    private PageLocatorState _locator = null!;
    private bool _locatorHudVisible;
    /// <summary>SYS4INI CLICKONUP: mouse clicks trigger on release (see Sys4StartupSettings.ClickOnUp).</summary>
    private bool _clickOnUp;

    // _Input (not _UnhandledInput): the root Control consumes mouse clicks as GUI input before they
    // reach _UnhandledInput, so clicks were swallowed while keyboard ui_accept still got through.
    public override void _Input(InputEvent e)
    {
        if (_selftest) return;
        NoteInputSource(e);
        if (DropForExclusiveDrive(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        // The menu bar is the window's non-client area: its own input never reaches the game, and an open menu's
        // loop takes every key and button.
        if (RouteMenuBarInput(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        // The fullscreen strip is a window of its own: while it shows it has the keyboard, and a click elsewhere
        // hides it and reaches the game. Alt+Enter and fullscreen's Alt belong to the window procedure.
        if (RouteMenuStripInput(e) || RouteScreenModeKey(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F2)
        {
            _locatorHudVisible = !_locatorHudVisible;
            _locatorHud.Visible = _locatorHudVisible;
            if (_locatorHudVisible) _locatorHud.Text = _locator.CurrentDisplay;
            return;
        }
        if (e is InputEventKey copy && copy.Pressed && !copy.Echo && copy.Keycode == Key.F3)
        {
            DisplayServer.ClipboardSet(_locator.CurrentDisplay);
            _locatorHud.Text = _locator.CurrentDisplay + " · copied";
            return;
        }
        if (e is InputEventKey debugKey && debugKey.Pressed && !debugKey.Echo && debugKey.Keycode == Key.F4)
        {
            ToggleDebugSceneLauncher();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventKey diagnosticKey && diagnosticKey.Keycode == Key.F6)
        {
            if (diagnosticKey.Pressed && !diagnosticKey.Echo) CaptureStallDiagnostic();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_debugSceneLauncher?.Visible == true)
        {
            if (e is InputEventKey escape && escape.Pressed && !escape.Echo && escape.Keycode == Key.Escape)
            {
                _debugSceneLauncher.Hide();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        // WM_KEYDOWN VK_F1 saves a screenshot BMP, and the key still reaches the game's bindings below.
        TrySaveScreenshotFromKey(e);
        if (e is InputEventMouseMotion motion)
        {
            if (!_windowed) NoteTopEdgePointer(motion.Position);   // the strip's top-edge dwell
            var p = ToNativeScreen(motion.Position);
            _vm.UpdatePointer(p.X, p.Y);
            return;
        }
        if (e is InputEventMouseButton wheel
            && wheel.Pressed
            && (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown))
        {
            // WM_MOUSEWHEEL supplies signed multiples of WHEEL_DELTA (120). At an ADV text wait its sign raises
            // the WHEELKEYUP/WHEELKEYDOWN action (wheel-up opens History); otherwise AGE accumulates it until op
            // 0x10d reads and clears it (HISTORY scrolls by its sign). VirtualMachine.ApplyMouseWheel has the rules.
            int direction = wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
            int steps = System.Math.Max(1, (int)System.Math.Round(wheel.Factor));
            bool advanceOnWheel = _sys4RegIniStore?.LoadMessageInt(Sys4RegIniStore.AdvanceMesOnWheelKey, 0) == 1;
            MouseWheelOutcome outcome = _vm.ApplyMouseWheel(direction * 120 * steps, advanceOnWheel,
                                                            _host.IsTextRevealing);
            if (outcome == MouseWheelOutcome.Advance
                && !_host.IsAdvPagePresentationSuspended && !_vm.IsRawInputCallbackActive)
                _host.SignalInput();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventMouseButton mb
            && (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right))
        {
            var p = ToNativeScreen(mb.Position);
            bool advPageSuspended = _host.IsAdvPagePresentationSuspended;
            bool rawInputCallbackActive = _vm.IsRawInputCallbackActive;
            _vm.UpdatePointer(p.X, p.Y);
            int nativeButtonBit = mb.ButtonIndex == MouseButton.Left ? 0x1 : 0x2;
            int physicalButton = mb.ButtonIndex == MouseButton.Left ? 0 : 1;
            _vm.UpdateMouseButtonState(nativeButtonBit, mb.Pressed);
            int action = _vm.UpdatePhysicalMouseButtonState(physicalButton, mb.Pressed);
            // Native AGE latches the click that every trigger below consumes on button-up under CLICKONUP
            // (Himegari), on button-down otherwise. Menus read the polled state above, so a menu opened by this
            // click starts with the button already up, as natively.
            if (mb.Pressed == _clickOnUp) return;
            if (_host.IsModalMovieWaiting)
            {
                _host.SignalInput();
                GetViewport().SetInputAsHandled();
                return;
            }
            // While an effect service parks the VM, the trigger goes to the effect's poll or to the input latch,
            // never to a hotspot under the effect or to a page wait (RouteEffectTrigger).
            if (_host.IsTransitionWaiting)
            {
                if (action >= 0) RouteEffectTrigger(1 << action);
                GetViewport().SetInputAsHandled();
                return;
            }
            // An ADV coroutine yield (hide window) runs with no input service: the click only latches for the
            // hidden-window wait, which it ends. It reaches no hotspot and never advances the page.
            if (action >= 0 && _vm.TryLatchAdvCoroutineYieldTrigger(1 << action))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            // CANCELMESSKIPONCLICK: during active fast-forward native withholds hotspot dispatch and feeds the
            // action-4 click to adv_interpreter_tick's cancel state machine instead.
            if (action == 4 && _vm.TryLatchMessageSkipCancelClick())
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            // Only a polling text wait dispatches hotspots and op-0x97 bindings (adv_input_service_poll). Elsewhere the
            // click goes to a running reveal's poll or stays in native's input latch (RouteUnpolledTrigger).
            if (action >= 0 && !_vm.IsAdvTextWaitPolling)
            {
                RouteUnpolledTrigger(1 << action);
                GetViewport().SetInputAsHandled();
                return;
            }
            // AGE exposes mouse buttons twice: op 0x108 reads the raw bitmask while op 0xff translates
            // the held physical button through the script-configured logical action map.
            if (action >= 0 && _vm.TryActivateInputActions(1 << action))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            if (mb.ButtonIndex == MouseButton.Left && _vm.TryActivatePointer(p.X, p.Y))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            // Modal callback scripts return through their own bytecode. Signaling the enclosing ADV wait
            // here would also advance the restored dialogue page after HISTORY/HIDEWIN exits.
            if (mb.ButtonIndex == MouseButton.Left
                && !advPageSuspended && !rawInputCallbackActive) _host.SignalInput();
            return;
        }
        // Native WM_KEYDOWN VK_ESCAPE posts an unforced WM_CLOSE (the 終了 dialog) and still delivers the key.
        if (e is InputEventKey escapeKey && escapeKey.Pressed && !escapeKey.Echo && escapeKey.Keycode == Key.Escape)
            RequestUnforcedClose();
        if (e is InputEventKey gameplayKey && !gameplayKey.Echo
            && Win32VirtualKeyTranslator.TryTranslate(gameplayKey, out int virtualKey))
        {
            int action = _vm.UpdateKeyboardVirtualKeyState(virtualKey, gameplayKey.Pressed);
            if (gameplayKey.Pressed && action >= 0 && _host.IsTransitionWaiting)
            {
                RouteEffectTrigger(1 << action);
                GetViewport().SetInputAsHandled();
            }
            else if (gameplayKey.Pressed && action >= 0 && _vm.TryLatchAdvCoroutineYieldTrigger(1 << action))
            {
                GetViewport().SetInputAsHandled();
            }
            else if (gameplayKey.Pressed && action == 4 && _vm.TryLatchMessageSkipCancelClick())
            {
                GetViewport().SetInputAsHandled();
            }
            else if (gameplayKey.Pressed && action >= 0 && !_host.IsModalMovieWaiting && !_vm.IsAdvTextWaitPolling)
            {
                RouteUnpolledTrigger(1 << action);
                GetViewport().SetInputAsHandled();
            }
            else if (gameplayKey.Pressed && action >= 0 && _vm.TryActivateInputActions(1 << action))
            {
                GetViewport().SetInputAsHandled();
            }
            else if (gameplayKey.Pressed && action == HideWindowAction && _vm.TryRequestAdvWaitCoroutineYield())
            {
                GetViewport().SetInputAsHandled();
            }
            else if (gameplayKey.Pressed && IsAdvanceAction(action))
            {
                if (_host.IsModalMovieWaiting)
                {
                    _host.SignalInput();
                    GetViewport().SetInputAsHandled();
                }
                else if (!_host.IsAdvPagePresentationSuspended && !_vm.IsRawInputCallbackActive)
                    _host.SignalInput();
            }
            return;
        }
        if (e is InputEventJoypadButton joyButton)
        {
            int actionMask = _vm.UpdateJoystickButtonState((int)joyButton.ButtonIndex, joyButton.Pressed);
            if (joyButton.Pressed && actionMask != 0 && _host.IsTransitionWaiting)
            {
                RouteEffectTrigger(actionMask);
                GetViewport().SetInputAsHandled();
            }
            else if (joyButton.Pressed && _vm.TryLatchAdvCoroutineYieldTrigger(actionMask))
            {
                GetViewport().SetInputAsHandled();
            }
            else if (joyButton.Pressed && actionMask != 0 && !_host.IsModalMovieWaiting && !_vm.IsAdvTextWaitPolling)
            {
                RouteUnpolledTrigger(actionMask);
                GetViewport().SetInputAsHandled();
            }
            else if (joyButton.Pressed && _vm.TryActivateInputActions(actionMask))
            {
                GetViewport().SetInputAsHandled();
            }
            else if (joyButton.Pressed && !HasAdvanceAction(actionMask)
                     && (actionMask & (1 << HideWindowAction)) != 0 && _vm.TryRequestAdvWaitCoroutineYield())
            {
                GetViewport().SetInputAsHandled();
            }
            else if (joyButton.Pressed && HasAdvanceAction(actionMask))
            {
                if (_host.IsModalMovieWaiting)
                {
                    _host.SignalInput();
                    GetViewport().SetInputAsHandled();
                }
                else if (!_host.IsAdvPagePresentationSuspended && !_vm.IsRawInputCallbackActive)
                    _host.SignalInput();
            }
            return;
        }
        if (e is InputEventJoypadMotion joyMotion && (int)joyMotion.Axis is 0 or 1)
            _vm.UpdateJoystickAxisState((int)joyMotion.Axis, joyMotion.AxisValue);
    }

    // Native services advance, finish a reveal, skip an effect or cancel a movie on logical action 4 only. At a text
    // wait, action 5 (Space in both games) enters the coroutine yield instead: hide window (adv_input_service_poll).
    private const int HideWindowAction = VirtualMachine.AdvWaitCoroutineYieldAction;
    private static bool IsAdvanceAction(int action) => action == VirtualMachine.AdvanceAction;
    private static bool HasAdvanceAction(int mask) => (mask & (1 << VirtualMachine.AdvanceAction)) != 0;

    /// <summary>system:EffectSkipOnClick, read on each trigger: the 設定 dialog's その他 page writes it.</summary>
    private bool EffectSkipOnClick => _sys4RegIniStore?.LoadInt("system", "EffectSkipOnClick", 0) == 1;

    /// <summary>A trigger while an effect service parks the VM. With EffectSkipOnClick 1 the effect polls it (action 4
    /// skips the effect, anything else is dropped); otherwise native's input latch holds it for the next service that
    /// polls, such as the next text reveal, which a held click finishes (docs/engine-re.md, "Effect skipping and the
    /// input latch").</summary>
    private void RouteEffectTrigger(int actionMask)
    {
        if (!_host.TryPollEffectTriggers(actionMask, EffectSkipOnClick)) _vm.HoldTriggers(actionMask);
    }

    /// <summary>A trigger while no text wait polls (native run-state 0x80000000 clear), so no hotspot or op-0x97 binding
    /// can take it. A running reveal polls on every tick (adv_text_reveal_service_tick@0x406fa0): action 4 finishes it
    /// and the poll drops the rest. Otherwise nothing polls: a sleep (run-state 1), the ops between waits, a callback
    /// script or a timed callback sequence. age_main_window_proc latches every mapped click and key whatever the run
    /// state, so native's input latch keeps the trigger for the next service that polls: the next reveal's first tick,
    /// the timed sequence's tick, or op 0x72's entry, which discards it (docs/engine-re.md, "Effect skipping and the
    /// input latch").</summary>
    private void RouteUnpolledTrigger(int actionMask)
    {
        if (_host.IsTextRevealing)
        {
            if (HasAdvanceAction(actionMask)) _host.SignalInput();
            return;
        }
        _vm.HoldTriggers(actionMask);
    }

    private void ToggleDebugSceneLauncher()
    {
        if (_debugSceneLauncher == null) return;
        if (_debugSceneLauncher.Visible)
        {
            _debugSceneLauncher.Hide();
            return;
        }
        if (!TryGetTitleDebugFrame(out var frame, out string reason))
        {
            _status.Text = reason;
            GD.Print($"[debug-launcher] unavailable: {reason}");
            return;
        }
        _debugSceneLauncher.Open(_debugSceneEntries, string.Join(" > ", frame.CallStack));
    }

    private void LaunchDebugScene(DebugSceneEntry entry)
    {
        if (_debugSceneLauncher == null || _scripts == null) return;
        DebugSceneLaunchPolicy? policy = _selectedProfile.Profile.DebugSceneLaunch;
        if (policy == null) return;
        if (!TryGetTitleDebugFrame(out var frame, out string reason))
        {
            _debugSceneLauncher.SetStatus(reason);
            return;
        }
        if (!entry.Launchable || _scripts.GetById(entry.PackedId) == null)
        {
            _debugSceneLauncher.SetStatus("The selected packed script could not be parsed; no state was changed.");
            return;
        }

        var coordinatorWrites = policy.CoordinatorWrites
            .ToDictionary(seed => seed.Address, seed => seed.Value);
        coordinatorWrites[policy.PackedScriptIdAddress] = entry.PackedId;
        if (!_vm.TryRequestDebugFrameReturn(frame.FrameId, coordinatorWrites))
        {
            _debugSceneLauncher.SetStatus("TITLE changed frames before launch; reopen the launcher and try again.");
            return;
        }

        _timeline?.Event("debug-scene-launch-request", new()
        {
            ["script"] = entry.Name,
            ["packed_id"] = entry.PackedId,
        });
        GD.Print($"[debug-launcher] {policy.RootScript} dispatch requested: "
                 + $"{entry.Name} (0x{entry.PackedId:x8})");
        _debugSceneLauncher.Hide();
        // ADV waits need an explicit wake; TITLE's actual menu is a 1 ms sleep/poll loop and will consume
        // the request at its next opcode boundary without leaving a stale input signal for the child scene.
        if (_host.IsWaiting) _host.SignalInput();
    }

    private bool TryGetTitleDebugFrame(out DebugFrameSnapshot frame, out string reason)
    {
        frame = _vm.DebugFrame!;
        DebugSceneLaunchPolicy? policy = _selectedProfile.Profile.DebugSceneLaunch;
        if (policy == null)
        {
            reason = "The selected profile has no debug-scene coordinator policy.";
            return false;
        }
        if (_done || frame == null)
        {
            reason = "Available only while TITLE is the active SYSTEM4 child.";
            return false;
        }
        if (frame.CallStack.Count != 2
            || !frame.CallStack[0].Equals(policy.RootScript, System.StringComparison.OrdinalIgnoreCase)
            || !frame.CallStack[1].Equals(policy.CoordinatorScript, System.StringComparison.OrdinalIgnoreCase)
            || !frame.CurrentScript.Equals(
                policy.CoordinatorScript, System.StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Refused: the active stack is not {policy.RootScript} > {policy.CoordinatorScript}.";
            return false;
        }
        reason = "";
        return true;
    }

    private (int X, int Y) ToNativeScreen(Vector2 position)
    {
        // Godot reports input in viewport coordinates after content scaling. This ratio is therefore
        // normally identity, while remaining correct for any viewport expansion policy.
        // The game's client area starts below the menu bar.
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (size.X <= 0 || size.Y <= 0) return (0, 0);
        return ((int)System.Math.Floor(position.X * _screenWidth / size.X),
                (int)System.Math.Floor(position.Y * (_screenHeight + _menuBarHeight) / size.Y) - _menuBarHeight);
    }

    public void WarpAgeCursor(int virtualX, int virtualY)
    {
        if (RedirectCursorWarpToDrive(virtualX, virtualY)) return;
        // Viewport.WarpMouse expects viewport coordinates. This is the inverse of ToNativeScreen and
        // lets Godot account for the platform window origin before moving the desktop pointer.
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (size.X <= 0 || size.Y <= 0 || _screenWidth <= 0 || _screenHeight <= 0) return;
        GetViewport().WarpMouse(new Vector2(
            virtualX * size.X / _screenWidth,
            (virtualY + _menuBarHeight) * size.Y / (_screenHeight + _menuBarHeight)));
    }

    public void SetAgeCursor(byte[] rgba, int width, int height, int hotspotX, int hotspotY)
    {
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        _ageCursorTexture = ImageTexture.CreateFromImage(image);
        Input.SetCustomMouseCursor(_ageCursorTexture, Input.CursorShape.Arrow,
                                   new Vector2(hotspotX, hotspotY));
    }

    public void ClearAgeCursor()
    {
        Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
        _ageCursorTexture = null;
    }

    public void ShowAgeDiagnostic(string text, string caption)
    {
        try
        {
            // A driven run (--control) has nobody to dismiss a modal box, and OS.Alert would block the main thread
            // that serves the control channel; log the box instead (the timeline records it either way).
            if (_driveServer != null) GD.Print($"[diagnostic] {caption}: {text.Replace("\n", " / ")}");
            else OS.Alert(text, caption);
        }
        catch (System.Exception error)
        {
            GD.PushError($"[diagnostic] native alert failed: {error.Message}");
        }
        finally
        {
            _host?.CompleteDiagnosticMessage();
        }
    }

    /// <summary>AGE's interpreter error box (中止/再試行/無視), shown for driven runs too: the driver answers it with
    /// `answer`, as it answers the native box.</summary>
    public void ShowAgeScriptError(string text, string caption)
    {
        if (_scriptErrorDialog != null)
        {
            GD.PushWarning("[vm] replaced an already-open script error box");
            _scriptErrorDialog.QueueFree();
            FreezePresentedFrame(false);
        }
        GD.Print($"[vm] script error box: {text.Split('\n')[0]}");
        var dialog = new ScriptErrorDialog(caption, text);
        _scriptErrorDialog = dialog;
        dialog.Completed += choice =>
        {
            if (_scriptErrorDialog == dialog) _scriptErrorDialog = null;
            GD.Print($"[vm] script error answered: {choice}");
            FreezePresentedFrame(false);
            _host?.CompleteScriptError(choice);
            dialog.QueueFree();
        };
        FreezePresentedFrame(true);
        AddChild(dialog);
        dialog.Open(new Vector2I(_screenWidth, _screenHeight));
    }

    public void ShowAgeFullwidthTextEditor(string initialText)
    {
        if (_fullwidthTextEditor != null)
        {
            GD.PushWarning("[inputname] replaced an already-open full-width text editor");
            _fullwidthTextEditor.QueueFree();
        }

        var editor = new FullwidthTextEditorDialog();
        _fullwidthTextEditor = editor;
        editor.EditCompleted += (accepted, text) =>
        {
            if (_fullwidthTextEditor == editor) _fullwidthTextEditor = null;
            _host?.CompleteFullwidthTextEdit(accepted, text);
            editor.QueueFree();
        };
        AddChild(editor);
        editor.Open(initialText);
    }

}
