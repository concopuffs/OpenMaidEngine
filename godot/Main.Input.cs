using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ome.Engine.Diagnostics;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Profiles;
using Ome.Engine.Vm;

public partial class Main
{
    private ImageTexture? _ageCursorTexture;
    private Label _locatorHud = null!;
    private FullwidthTextEditorDialog? _fullwidthTextEditor;
    private DebugSceneLauncher? _debugSceneLauncher;
    private IReadOnlyList<DebugSceneEntry> _debugSceneEntries = System.Array.Empty<DebugSceneEntry>();
    private PageLocatorState _locator = null!;
    private bool _locatorHudVisible;

    // _Input (not _UnhandledInput): the root Control consumes mouse clicks as GUI input before they
    // reach _UnhandledInput, so clicks were swallowed while keyboard ui_accept still got through.
    public override void _Input(InputEvent e)
    {
        // _Ready returns before creating the VM when startup fails (no game root, a bad flag); the window stays up
        // until the quit lands, and input meanwhile has nothing to drive.
        if (_selftest || _vm == null) return;
        if (DropForExclusiveDrive(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        // The options panel is modal: its keys first, then its controls as GUI input; nothing reaches the game.
        if (_optionsPanel != null)
        {
            if (_optionsPanel.HandleInput(e)) GetViewport().SetInputAsHandled();
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
            if (diagnosticKey.Pressed && !diagnosticKey.Echo) CaptureStallDiagnostic(copyToClipboard: true);
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
        // What an input does in the game is the input and waits native's (InputAndWaits.Events.cs): this queues each
        // device event, in native client pixels and Win32 virtual keys, for the engine thread, and stops it here; the
        // game area has no controls of its own.
        if (e is InputEventMouseMotion motion)
        {
            if (!Windowed) NoteTopEdgePointer(motion.Position);   // the strip's top-edge dwell
            var p = ToNativeScreen(motion.Position);
            _waits.PointerMoved(p.X, p.Y);
            return;
        }
        if (e is InputEventMouseButton wheel
            && wheel.Pressed
            && (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown))
        {
            // WM_MOUSEWHEEL supplies signed multiples of WHEEL_DELTA (120).
            int direction = wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
            int steps = System.Math.Max(1, (int)System.Math.Round(wheel.Factor));
            _waits.MouseWheel(direction * 120 * steps);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventMouseButton mb
            && (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right))
        {
            var p = ToNativeScreen(mb.Position);
            _waits.MouseButton(mb.ButtonIndex == MouseButton.Left ? 0 : 1, mb.Pressed, p.X, p.Y);
            GetViewport().SetInputAsHandled();
            return;
        }
        // Native WM_KEYDOWN VK_ESCAPE posts an unforced WM_CLOSE (the 終了 dialog) and still delivers the key.
        if (e is InputEventKey escapeKey && escapeKey.Pressed && !escapeKey.Echo && escapeKey.Keycode == Key.Escape)
            RequestUnforcedClose();
        if (e is InputEventKey gameplayKey && !gameplayKey.Echo
            && Win32VirtualKeyTranslator.TryTranslate(gameplayKey, out int virtualKey))
        {
            _waits.Key(virtualKey, gameplayKey.Pressed);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventJoypadButton joyButton)
        {
            _waits.JoystickButton((int)joyButton.ButtonIndex, joyButton.Pressed);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventJoypadMotion joyMotion)
            _waits.JoystickAxis((int)joyMotion.Axis, joyMotion.AxisValue);
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
        if (!_waits.Invoke(() => _vm.TryRequestDebugFrameReturn(frame.FrameId, coordinatorWrites)))
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
        if (_waits.IsWaiting) _waits.SignalInput();
    }

    private bool TryGetTitleDebugFrame(out DebugFrameSnapshot frame, out string reason)
    {
        frame = _waits.Invoke(() => _vm.DebugFrame)!;
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

    /// <summary>Op 0x1b4's box, in the shell's language (the driven run's log keeps AGE's Japanese).</summary>
    private void ShowAgeDiagnostic(DiagnosticBox box)
    {
        DiagnosticMessage message = box.Message;
        try
        {
            // A driven run (--control) has nobody to dismiss a modal box, and OS.Alert would block the main thread
            // that serves the control channel; log the box instead (the timeline records it either way).
            if (_driveServer != null) GD.Print($"[diagnostic] {message.Caption}: {message.Text.Replace("\n", " / ")}");
            else
            {
                (string caption, string text) = ShellBoxes.Present(message, _shellStrings);
                OS.Alert(text, caption);
            }
        }
        catch (System.Exception error)
        {
            GD.PushError($"[diagnostic] native alert failed: {error.Message}");
        }
        finally
        {
            box.Close();
        }
    }

    /// <summary>AGE's interpreter error box (中止/再試行/無視) in the shell's language, shown for driven runs too: the
    /// driver answers it with `answer`, as it answers the native box, and reads it in Japanese.</summary>
    private void ShowAgeScriptError(ScriptErrorBox box)
    {
        DiagnosticMessage message = box.Message;
        if (_scriptErrorDialog != null)
        {
            GD.PushWarning("[vm] replaced an already-open script error box");
            _scriptErrorDialog.QueueFree();
            FreezePresentedFrame(false);
        }
        GD.Print($"[vm] script error box: {message.Text.Split('\n')[0]}");
        var dialog = new ScriptErrorDialog(message, _shellStrings);
        _scriptErrorDialog = dialog;
        dialog.Completed += choice =>
        {
            if (_scriptErrorDialog == dialog) _scriptErrorDialog = null;
            GD.Print($"[vm] script error answered: {choice}");
            FreezePresentedFrame(false);
            box.Answer(choice);
            dialog.QueueFree();
        };
        FreezePresentedFrame(true);
        AddChild(dialog);
        dialog.Open(new Vector2I(_screenWidth, _screenHeight));
    }

    private void ShowAgeFullwidthTextEditor(TextEntry entry)
    {
        if (_fullwidthTextEditor != null)
        {
            GD.PushWarning("[inputname] replaced an already-open full-width text editor");
            _fullwidthTextEditor.QueueFree();
        }

        var editor = new FullwidthTextEditorDialog(_shellStrings);
        _fullwidthTextEditor = editor;
        editor.EditCompleted += (accepted, text) =>
        {
            if (_fullwidthTextEditor == editor) _fullwidthTextEditor = null;
            entry.Answer(accepted, text);
            editor.QueueFree();
        };
        AddChild(editor);
        editor.Open(entry.Request.InitialText);
    }

}
