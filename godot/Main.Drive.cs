using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Ome.Engine.Frontend.Control;

// The driving protocol's port backend (docs/superpowers/specs/2026-11-03-game-driving-design.md §5). With
// --control, a loopback server accepts the shared step protocol; requests run here on the main thread. Input is
// injected as Godot input events tagged with DriveInputDevice, and every other input event is dropped, so the
// player's own mouse and keyboard cannot disturb a driven run.
public partial class Main
{
    /// <summary>InputEvent.Device of injected events.</summary>
    public const int DriveInputDevice = 0x7d21;

    private DriveServer? _driveServer;
    private string? _driveSessionPath;
    private bool _driveExclusiveInput;
    private MouseButtonMask _driveButtonMask;
    private (int X, int Y)? _driveCursorWarp;
    private (int X, int Y) _drivePointer;

    private static string? ArgumentValue(string[] userArgs, string name)
    {
        int index = Array.LastIndexOf(userArgs, name);
        return index >= 0 && index + 1 < userArgs.Length ? userArgs[index + 1] : null;
    }

    /// <summary>Start the control server after the VM exists. Returns false (after reporting why) on failure.</summary>
    private bool StartDriveServer(string? sessionPath)
    {
        string token = System.Environment.GetEnvironmentVariable(DriveProtocol.TokenEnvironmentVariable) is
            { Length: > 0 } supplied ? supplied : DriveServer.NewToken();
        try
        {
            _driveServer = new DriveServer(new PortDriveBackend(this), token);
            _driveSessionPath = Path.GetFullPath(
                sessionPath ?? Path.Combine(_profilePersistence.DiagnosticsDirectory, "drive-session.json"));
            _driveServer.WriteSessionFile(_driveSessionPath, OS.GetProcessId(), token);
            _driveServer.Start();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                          or System.Net.Sockets.SocketException)
        {
            GD.PushError($"[drive] control server failed to start: {error.Message}");
            _driveServer?.Dispose();
            _driveServer = null;
            return false;
        }
        _driveExclusiveInput = true;
        GD.Print($"[drive] control on 127.0.0.1:{_driveServer.Port} session={_driveSessionPath} " +
                 "input=exclusive");
        return true;
    }

    private void PumpDriveServer() => _driveServer?.Pump(System.Environment.TickCount64);

    private void StopDriveServer()
    {
        if (_driveServer == null) return;
        _driveServer.Dispose();
        _driveServer = null;
        if (_driveSessionPath != null)
        {
            try { File.Delete(_driveSessionPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>True when a real (not injected) input event must be dropped.</summary>
    private bool DropForExclusiveDrive(InputEvent e)
        => _driveExclusiveInput && e.Device != DriveInputDevice
           && e is InputEventMouse or InputEventKey or InputEventJoypadButton or InputEventJoypadMotion
               or InputEventScreenTouch or InputEventScreenDrag or InputEventGesture;

    /// <summary>A driven run does not move the player's cursor: the warp becomes an injected motion event and is
    /// reported with the next click.</summary>
    private bool RedirectCursorWarpToDrive(int virtualX, int virtualY)
    {
        if (_driveServer == null) return false;
        _driveCursorWarp = (virtualX, virtualY);
        InjectDrivePointer(virtualX, virtualY);
        return true;
    }

    /// <summary>Where a native client pixel is in the window: Input.ParseInputEvent takes window coordinates, which
    /// the root viewport maps through its stretch (fullscreen scales the canvas and centres it).</summary>
    private Vector2 NativeToWindow(int x, int y)
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        Vector2 viewport;
        if (size.X <= 0 || size.Y <= 0 || _screenWidth <= 0 || _screenHeight <= 0)
            viewport = new Vector2(x, y + _menuBarHeight);
        else
            // The centre of the native pixel, so ToNativeScreen's floor maps it back exactly. Client coordinates
            // start below the menu bar.
            viewport = new Vector2((x + 0.5f) * size.X / _screenWidth,
                                   (y + _menuBarHeight + 0.5f) * size.Y / (_screenHeight + _menuBarHeight));
        return GetViewport().GetScreenTransform() * viewport;
    }

    private void InjectDrivePointer(int x, int y)
    {
        _drivePointer = (x, y);
        Vector2 position = NativeToWindow(x, y);
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Device = DriveInputDevice,
            Position = position,
            GlobalPosition = position,
            ButtonMask = _driveButtonMask,
        });
    }

    private void InjectDriveButton(MouseButton button, bool pressed, int x, int y, float factor = 1)
    {
        MouseButtonMask bit = button switch
        {
            MouseButton.Left => MouseButtonMask.Left,
            MouseButton.Right => MouseButtonMask.Right,
            _ => 0,
        };
        if (bit != 0) _driveButtonMask = pressed ? _driveButtonMask | bit : _driveButtonMask & ~bit;
        Vector2 position = NativeToWindow(x, y);
        Input.ParseInputEvent(new InputEventMouseButton
        {
            Device = DriveInputDevice,
            ButtonIndex = button,
            Pressed = pressed,
            Position = position,
            GlobalPosition = position,
            ButtonMask = _driveButtonMask,
            Factor = factor,
        });
    }

    private sealed class PortDriveBackend(Main main) : IDriveBackend
    {
        private static readonly string[] PortCapabilities =
            { "speed", "waiting", "dialogs", "answer", "window", "menu", "rand" };

        public string Target => "port";
        public string Profile { get; } = main._selectedProfile.Profile.Id;
        public IReadOnlyList<string> Capabilities => PortCapabilities;

        public DriveState CaptureState()
        {
            IReadOnlyList<DriveFrame> frames = DriveFrame.FromVm(main._vm.CaptureFrameLocations());
            GodotHostDiagnosticSnapshot host = main._host.CaptureDiagnosticSnapshot();
            string? waiting =
                host.IsModalMovieWaiting ? DriveWaiting.Movie
                : host.IsTransitionWaiting ? DriveWaiting.Transition
                : host.IsInputWaiting ? DriveWaiting.Input
                : host.IsSleeping && main._vm.IsRawInputCallbackActive ? DriveWaiting.Menu
                : host.IsSleeping ? DriveWaiting.Sleep
                : null;
            return new DriveState(frames, waiting, host.IsTextRevealing || host.IsScreenTransitionActive);
        }

        public void Pointer(int x, int y) => main.InjectDrivePointer(x, y);

        public void Button(DriveButton button, bool down, int x, int y)
            => main.InjectDriveButton(button == DriveButton.Left ? MouseButton.Left : MouseButton.Right, down, x, y);

        private bool _altDown;

        /// <summary>A key event as Godot reports a real one: Alt held (the driver's Alt chords, or Alt itself while
        /// pressed) sets the event's Alt modifier.</summary>
        public void Key(int virtualKey, bool down)
        {
            if (!Win32VirtualKeyTranslator.TryCreateKey(virtualKey, out Key key))
                throw new DriveProtocolException($"vk 0x{virtualKey:x} has no Godot key");
            if (key == Godot.Key.Alt) _altDown = down;
            Input.ParseInputEvent(new InputEventKey
            {
                Device = DriveInputDevice,
                Keycode = key,
                PhysicalKeycode = key,
                Pressed = down,
                AltPressed = _altDown,
            });
        }

        public void Wheel(int notches, int x, int y)
        {
            if (notches == 0) return;
            MouseButton button = notches > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            main.InjectDriveButton(button, true, x, y, Math.Abs(notches));
            main.InjectDriveButton(button, false, x, y, Math.Abs(notches));
        }

        public (int Width, int Height) Screenshot(string path)
        {
            Image? image = main.CaptureGameImage();
            if (image == null || image.IsEmpty())
                throw new DriveProtocolException("no rendered frame to capture (a headless or minimized window)");
            if (image.GetWidth() != main._screenWidth || image.GetHeight() != main._screenHeight)
                image.Resize(main._screenWidth, main._screenHeight, Image.Interpolation.Bilinear);
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            Error error = image.SavePng(full);
            if (error != Error.Ok) throw new DriveProtocolException($"saving {full} failed: {error}");
            return (image.GetWidth(), image.GetHeight());
        }

        public void SetSpeed(double factor) => main._clock.Speed = factor;

        public System.Text.Json.Nodes.JsonObject RandSeed(uint? seed)
            => DriveRandomReport.ToJson(seed, main._vm.SeedDeterministicRandom(seed));

        public void Close() => main.GetTree().Quit(0);

        /// <summary>Deferred to the end of the frame, so the reply is on its way before a close that quits at once
        /// (a minimized window) ends the process.</summary>
        public void RequestClose() => Callable.From(main.RequestUnforcedClose).CallDeferred();

        /// <summary>The window manager's minimize and restore, which brings back the screen mode the game was in;
        /// restoring leaves the window's z-order to the launcher (drive_port.py sends it back to the bottom).</summary>
        public System.Text.Json.Nodes.JsonObject Window(bool minimize)
        {
            DisplayServer.WindowSetMode(minimize ? DisplayServer.WindowMode.Minimized
                                        : main._windowed ? DisplayServer.WindowMode.Windowed
                                        : DisplayServer.WindowMode.Fullscreen);
            return new System.Text.Json.Nodes.JsonObject
            {
                ["minimized"] = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Minimized,
                ["windowed"] = main._windowed,
            };
        }

        public System.Text.Json.Nodes.JsonObject Menu(int command) => main.ChooseMenuCommand(command);

        public System.Text.Json.Nodes.JsonArray Dialogs()
        {
            var dialogs = new System.Text.Json.Nodes.JsonArray();
            if (main._exitConfirmation is { } exit) dialogs.Add(exit.Describe());
            if (main._scriptErrorDialog is { } error) dialogs.Add(error.Describe());
            if (main._screenshotFolderDialog?.Describe() is { } folder) dialogs.Add(folder);
            if (main._optionsPanel is { } panel) dialogs.Add(panel.Describe());
            return dialogs;
        }

        public System.Text.Json.Nodes.JsonObject Answer(string button)
        {
            if (main._screenshotFolderDialog is { } folderDialog && folderDialog.Describe() is { } folder
                && folderDialog.Press(button) is { } folderButton)
                return new System.Text.Json.Nodes.JsonObject
                {
                    ["text"] = (string?)folder["text"], ["answered"] = folderButton,
                };
            if (main._optionsPanel is { } panel)
            {
                // The section the press happened on, before a section button or 決定 changes it.
                string? section = (string?)panel.Describe()["text"];
                if (panel.Press(button) is { } panelButton)
                    return new System.Text.Json.Nodes.JsonObject { ["text"] = section, ["answered"] = panelButton };
            }
            if (main._scriptErrorDialog is { } error && error.Press(button) is { } errorButton)
                return new System.Text.Json.Nodes.JsonObject
                {
                    ["text"] = (string?)error.Describe()["text"], ["answered"] = errorButton,
                };
            if (main._exitConfirmation is not { } dialog || dialog.Press(button) is not { } pressed)
                throw new DriveProtocolException($"no open dialog has a '{button}' button");
            return new System.Text.Json.Nodes.JsonObject { ["text"] = dialog.Text, ["answered"] = pressed };
        }

        public (int X, int Y)? TakeCursorWarp()
        {
            var warp = main._driveCursorWarp;
            main._driveCursorWarp = null;
            return warp;
        }

        public (int X, int Y) PointerPosition => main._drivePointer;
    }
}
