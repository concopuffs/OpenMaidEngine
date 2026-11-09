using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Model;
using Ome.Engine.Persistence;
using Ome.Engine.Vm;
using Godot;

/// <summary>
/// The engine shell's options panel (<see cref="OptionsPanel"/>; docs/superpowers/specs/2026-11-09-engine-shell-design.md
/// §5.3, §5.5, §5.6 and §7.1), which replaces AGERC's 設定, ﾊﾞｰｼﾞｮﾝ情報 and 動作環境. The shell's section commands open
/// it (<see cref="IShellHost.OpenPanel"/>). While it is open the VM holds and the frame stays frozen, as native's modal
/// dialog loop holds AGE's thread. 決定 commits the native options as 設定's OK does, CALLBACK_SETTING.BIN included,
/// and the port options to OME.INI (<see cref="OmeIniStore"/>); ｷｬﾝｾﾙ restores the live ｻｳﾝﾄﾞ values.
/// </summary>
public partial class Main
{
    private OptionsPanel? _optionsPanel;
    private CanvasLayer? _optionsPanelLayer;
    private OmeIniStore? _omeIni;
    private PortOptionsRun _portOptions = new(PortOptions.Defaults, PortOptions.Defaults, PortOptionFlags.None);
    private Texture2D? _omeIcon;

    private Texture2D OmeIcon => _omeIcon ??= ShellAssets.IconTexture(128);

    /// <summary>OME.INI's port options and this run's flags, before the window is sized and the menu is built: a
    /// driver sandbox (<c>--data-root</c>) keeps its own file, and a self-test reads none.</summary>
    /// <exception cref="ArgumentException">A port-option flag has a missing or unknown value.</exception>
    private void LoadPortOptions(string[] userArgs)
    {
        PortOptions saved = PortOptions.Defaults;
        if (Array.IndexOf(userArgs, "--selftest") < 0)
        {
            _omeIni = OmeIniStore.For(ProjectSettings.GlobalizePath("user://"), ArgumentValue(userArgs, "--data-root"));
            saved = _omeIni.Load();
        }
        _portOptions = PortOptionsRun.Resolve(saved, userArgs);
        _shellStrings = ShellStrings.Load(_portOptions.Effective.Language);
        PortOptions effective = _portOptions.Effective;
        GD.Print($"[options] {(_omeIni?.FilePath ?? "no OME.INI (self-test)")}: language={effective.Language} " +
                 $"window={(effective.CanvasWindowSize ? "canvas" : $"{effective.WindowWidth}x{effective.WindowHeight}")} " +
                 $"fps-counter={effective.FpsCounter} locator-hud={effective.LocatorHud}" +
                 (_portOptions.Overridden != PortOptionFlags.None ? $" (flags: {_portOptions.Overridden})" : ""));
    }

    /// <summary>A <c>--window-width</c>/<c>--window-height</c> run shows its size as the current one; OME.INI keeps
    /// the file's unless the panel changes it.</summary>
    private void NoteWindowSizeFlags(Ome.Engine.Sys4.Sys4LogicalCanvas canvas)
    {
        if (!_windowOptions.IsOverridden) return;
        bool isCanvas = _windowOptions.Width == canvas.Width && _windowOptions.Height == canvas.Height;
        _portOptions = _portOptions with
        {
            Effective = _portOptions.Effective with
            {
                WindowWidth = isCanvas ? 0 : _windowOptions.Width, WindowHeight = isCanvas ? 0 : _windowOptions.Height,
            },
        };
    }

    private string WindowSizeSource() => _windowOptions.IsOverridden ? "boot-arguments"
        : _portOptions.Effective.CanvasWindowSize ? "logical-canvas" : "OME.INI";

    /// <summary>Open the panel at <paramref name="section"/>, unless a modal window is already up.</summary>
    private void OpenOptionsPanel(ShellSection section, bool diagnostics)
    {
        if (_optionsPanel != null || _vm == null || _exitConfirmation != null || _scriptErrorDialog != null
            || _screenshotFolderDialog != null)
            return;
        // While op 0x142's gate is 0 the script-shared sections are read-only; native 設定 cannot open then.
        bool readOnly = !_vm.MenuState.SystemMenuEnabled;
        var draft = new ShellOptionsDraft(new SettingsHost(this), _catalog.StartupSettings, readOnly);
        PortOptions opened = _portOptions.Effective;
        // 設定's OK runs agerc_settings_apply and CALLBACK_SETTING.BIN even when nothing changed; ﾊﾞｰｼﾞｮﾝ情報's OK
        // runs neither, so the about view applies only an edit.
        bool fromSettings = section != ShellSection.Info || diagnostics;
        var context = new OptionsPanel.Context(
            _shellStrings, draft, opened, new Vector2I(_screenWidth, _screenHeight), PanelWindowSizes(opened),
            () => _windowed, toWindow => SwitchScreenMode(toWindow, "the options panel"), _forceScreen, About(),
            Diagnostics(), OmeIcon);
        var panel = new OptionsPanel(context, section);
        panel.Committed += edited => CloseOptionsPanel(panel, draft, opened, edited, fromSettings);
        panel.Cancelled += () => CloseOptionsPanel(panel, draft, opened, null, fromSettings);

        Shell.HoldVm(true);
        FreezePresentedFrame(true);
        _optionsPanel = panel;
        _optionsPanelLayer = new CanvasLayer { Layer = 100 };   // over the frozen frame (99), below the bar (101)
        _optionsPanelLayer.AddChild(panel);
        AddChild(_optionsPanelLayer);
        PlaceOverGame(panel);
        panel.Open(diagnostics);
        GD.Print($"[options] the panel opens at {section}{(diagnostics ? " (diagnostics)" : "")}" +
                 (readOnly ? "; op 0x142's gate is 0: the script-shared sections are read-only" : ""));
    }

    /// <summary>決定 (<paramref name="edited"/> set) or ｷｬﾝｾﾙ.</summary>
    private void CloseOptionsPanel(OptionsPanel panel, ShellOptionsDraft draft, PortOptions opened, PortOptions? edited,
                                   bool fromSettings)
    {
        if (_optionsPanel != panel) return;
        if (edited == null)
        {
            draft.Cancel();
            GD.Print("[options] cancelled: the live sound values are restored");
        }
        else
        {
            if (!draft.ReadOnly && (draft.HasChanges || fromSettings))
            {
                draft.Commit();
                GD.Print("[options] committed: the settings apply and CALLBACK_SETTING.BIN runs");
            }
            CommitPortOptions(opened, edited);
        }
        _optionsPanel = null;
        _optionsPanelLayer?.QueueFree();
        _optionsPanelLayer = null;
        FreezePresentedFrame(false);
        Shell.HoldVm(false);
    }

    /// <summary>The port options the player changed: OME.INI takes them (a flag's value never reaches it), and this
    /// run applies them at once.</summary>
    private void CommitPortOptions(PortOptions opened, PortOptions edited)
    {
        if (edited == opened) return;
        PortOptions saved = _portOptions.SavedAfter(opened, edited);
        _portOptions = _portOptions with { Saved = saved, Effective = edited };
        if (_omeIni != null)
        {
            try
            {
                _omeIni.Save(saved);
                GD.Print($"[options] {_omeIni.FilePath} saved");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"[options] saving {_omeIni.FilePath} failed: {error.Message}");
            }
        }
        if (edited.Language != opened.Language) ApplyShellLanguage(edited.Language);
        if (edited.FpsCounter != opened.FpsCounter) _fpsHud.Visible = edited.FpsCounter;
        if (edited.LocatorHud != opened.LocatorHud)
        {
            _locatorHudVisible = edited.LocatorHud;
            _locatorHud.Visible = _locatorHudVisible;
            if (_locatorHudVisible) _locatorHud.Text = _locator.CurrentDisplay;
        }
        if (edited.WindowWidth != opened.WindowWidth || edited.WindowHeight != opened.WindowHeight)
            ApplyWindowSize(edited);
    }

    /// <summary>ui:Language: the shell's texts, the bar and the strip switch tables. The 終了 dialog and the folder
    /// picker read the table when they open.</summary>
    private void ApplyShellLanguage(string language)
    {
        _shellStrings = ShellStrings.Load(language);
        _shellMenu = new ShellMenu(_shellStrings, _catalog.StartupSettings);
        if (_menuItems != null) _shellMenu.Relabel(_menuItems);
        if (_menuStrip != null) _shellMenu.Relabel(_menuStrip.Bar.Items);
        RefreshMenu();
        _menuBar?.LayoutBar((int)_menuBar.Size.X);
        _menuStrip?.LayoutStrip(new Vector2I(_screenWidth, _screenHeight));
        GD.Print($"[options] the shell's language is {language}");
    }

    /// <summary>display:WindowWidth/WindowHeight: the game's area in a window, the bar's rows scaled with it.</summary>
    private void ApplyWindowSize(PortOptions options)
    {
        Vector2I game = options.CanvasWindowSize ? new Vector2I(_screenWidth, _screenHeight)
                                                 : new Vector2I(options.WindowWidth, options.WindowHeight);
        int bar = _menuItems != null ? NativeMenuBar.BarHeight : 0;
        _windowedSize = new Vector2I(game.X, game.Y + (int)Math.Round(bar * (double)game.Y / _screenHeight));
        if (_windowed) DisplayServer.WindowSetSize(_windowedSize);
        GD.Print($"[options] window size {game.X}x{game.Y} (window {_windowedSize.X}x{_windowedSize.Y})");
    }

    /// <summary>画面's sizes: the canvas scaled to fit the screen the window is on, and the current size.</summary>
    private IReadOnlyList<(int Width, int Height)> PanelWindowSizes(PortOptions current)
    {
        int screen = DisplayServer.WindowGetCurrentScreen();
        Vector2I usable = DisplayServer.ScreenGetUsableRect(screen).Size;
        Vector2I frame = DisplayServer.WindowGetSizeWithDecorations() - DisplayServer.WindowGetSize();
        (int, int)? saved = current.CanvasWindowSize ? null : (current.WindowWidth, current.WindowHeight);
        IReadOnlyList<(int Width, int Height)> sizes = ShellPanel.WindowSizes(
            _screenWidth, _screenHeight, (usable.X - frame.X, usable.Y - frame.Y), saved,
            _menuItems != null ? NativeMenuBar.BarHeight : 0);
        GD.Print($"[options] window sizes for a {usable.X}x{usable.Y} screen area less a {frame.X}x{frame.Y} frame: " +
                 string.Join(", ", System.Linq.Enumerable.Select(sizes, s => $"{s.Width}x{s.Height}")));
        return sizes;
    }

    // ---- 情報 ----

    private ShellAbout About() => new(EngineVersion(), _catalog.Title, GameVersion(),
                                      _catalog.StartupSettings.GetValueOrDefault("COPYRIGHT") ?? "");

    /// <summary>What a bug report needs (spec §5.5).</summary>
    private ShellDiagnostics Diagnostics()
    {
        var version = Engine.GetVersionInfo();
        string Join(params string[] parts) => string.Join(" ", Array.FindAll(parts, p => p.Length > 0));
        return new ShellDiagnostics(
        [
            ("diag.os", Join(OS.GetName(), OS.GetVersion(), $"({Engine.GetArchitectureName()})")),
            ("diag.godot", (string)version["string"]),
            ("diag.renderer", Join(RenderingServer.GetCurrentRenderingDriverName(),
                                   $"({RenderingServer.GetCurrentRenderingMethod()})")),
            ("diag.gpu", Join(RenderingServer.GetVideoAdapterName(), "·", RenderingServer.GetVideoAdapterVendor(),
                              "·", RenderingServer.GetVideoAdapterApiVersion())),
            ("diag.audio", Join(AudioServer.GetDriverName(), "·", AudioServer.OutputDevice, "·",
                                $"{AudioServer.GetMixRate():0} Hz")),
            ("diag.engine", Join(EngineIdentity.Name, EngineVersion())),
            ("diag.profile", _selectedProfile.Profile.Id),
            ("diag.game-root", _gameExecutableDirectory),
            ("diag.data-root", _profilePersistence.ProfileRoot),
        ]);
    }

    /// <summary>This engine's version, where ﾊﾞｰｼﾞｮﾝ情報 showed agerc_exe_file_version_string (AGE.EXE's
    /// FileVersion): what godot/OME.csproj's OmeVersion target stored in the assembly.</summary>
    private static string EngineVersion()
    {
        foreach (var attribute in typeof(Main).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            if (attribute.Key == EngineIdentity.VersionMetadataKey) return EngineIdentity.DisplayVersion(attribute.Value);
        return EngineIdentity.DisplayVersion(null);
    }

    /// <summary>set:GameVersion. With VERREGPOS, registration_read_install_display_version@0x46ae80 reads the
    /// installer's DisplayVersion from HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\InstallShield_{GUID}
    /// (a 32-bit process, so the WOW6432Node view), "1.00.0000" when it has none; without, SYS4INI GAMEVERSION.</summary>
    private string GameVersion()
    {
        var settings = _catalog.StartupSettings;
        string guid = settings.GetValueOrDefault("VERREGPOS") ?? "";
        if (guid.Length == 0) return settings.GetValueOrDefault("GAMEVERSION") ?? "";
        const string Default = "1.00.0000";
        if (!OperatingSystem.IsWindows()) return Default;
        try
        {
            using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                         Microsoft.Win32.RegistryView.Registry32);
            using var key = machine.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\InstallShield_{{{guid}}}");
            return key?.GetValue("DisplayVersion") as string ?? Default;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException
                                          or IOException)
        {
            return Default;
        }
    }

    /// <summary>The engine side of the panel's draft: AGE:IAGEService on the audio mixer, the VM and SYS4REG.INI.
    /// </summary>
    private sealed class SettingsHost(Main main) : IShellSettingsHost
    {
        private Sys4RegIniStore? Store => main._sys4RegIniStore;

        public VmMessageSettings MessageSettings => main._vm?.MessageSettings ?? new VmMessageSettings(
            Sys4RegIniStore.DefaultMessageSpeed, Sys4RegIniStore.DefaultMesWinAlpha,
            Sys4RegIniStore.DefaultAutoMessageTime0, Sys4RegIniStore.DefaultAutoMessageTime1);

        public int LoadInt(string section, string key, int defaultValue) =>
            Store?.LoadInt(section, key, defaultValue) ?? defaultValue;

        public void SaveInt(string section, string key, int value)
        {
            try
            {
                Store?.SaveInt(section, key, value);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"[settings] saving {section}:{key} failed: {error.Message}");
            }
        }

        public void SetTextAntialias(bool enabled)
        {
            SaveInt("message", Sys4RegIniStore.UseAntiFontKey, enabled ? 1 : 0);
            main._host?.SetTextAntialias(enabled);
        }

        // The sound object starts every level at 10000; sound:VolumeN = -1 never replaces it.
        public int Volume(int category) =>
            main._audioMixerSettings.TryGetVolume(category, out int level) && level >= 0
                ? level
                : AudioMixerSettings.MaximumVolume;

        public void ApplyVolume(int category, int basisPoints) => main.ApplyAudioVolume(category, basisPoints);

        public void SaveVolume(int category, int basisPoints) =>
            main._audioMixerSettings.TrySetVolume(category, basisPoints);

        public bool RouteEnabled(int category) =>
            main._audioMixerSettings.TryGetRouteEnabled(category, out bool enabled) && enabled;

        public void SetRouteEnabled(int category, bool enabled)
        {
            if (main._audioMixerSettings.TrySetRouteEnabled(category, enabled, out bool changed) && changed)
                main.ApplyAudioRouteEnabled(category, enabled);
        }

        public void ApplyMessageSettings(VmMessageSettings settings) => main._vm?.RequestMessageSettings(settings);
    }
}
