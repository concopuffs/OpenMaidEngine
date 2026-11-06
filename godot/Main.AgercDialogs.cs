using System;
using System.Collections.Generic;
using System.IO;
using Age.Engine.Frontend.NativeMenu;
using Age.Engine.Frontend.NativeResources;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Vm;
using Godot;

/// <summary>
/// AGERC's own dialogs (docs/engine-re.md, "Native menu bar"), built from the game's AGERC.DLL templates
/// (<see cref="Win32TemplateDialog"/>). The dialog's modal loop runs on AGE's thread, so the game holds and its frame
/// stays frozen while it is open, with no dim (fullscreen also brackets it in IAGEService +0x14/+0x18). The port
/// opens ﾍﾙﾌﾟ > ﾊﾞｰｼﾞｮﾝ情報 (0x9c45) and 設定 (0x9c6c, 0x9c68, 0x9c6e, 0x9c6d), whose ﾊﾞｰｼﾞｮﾝ情報 button opens that
/// dialog over it; the open dialogs form a stack, the newest on top.
/// </summary>
public partial class Main
{
    private string? _agercPath;
    private readonly List<Win32TemplateDialog> _agercDialogs = new();
    /// <summary>Whether the last key or button input came from the keyboard, which decides a new dialog's keyboard
    /// cues (WM_CHANGEUISTATE UIS_INITIALIZE).</summary>
    private bool _lastInputFromKeyboard;

    /// <summary>The topmost open AGERC dialog.</summary>
    private Win32TemplateDialog? _agercDialog => _agercDialogs.Count > 0 ? _agercDialogs[^1] : null;

    private void NoteInputSource(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true }) _lastInputFromKeyboard = true;
        else if (e is InputEventMouseButton { Pressed: true }) _lastInputFromKeyboard = false;
    }

    private byte[]? ReadAgerc()
    {
        if (_agercPath == null) return null;
        try
        {
            return File.ReadAllBytes(_agercPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[menu] reading {_agercPath} failed: {error.Message}");
            return null;
        }
    }

    private DialogTemplate? AgercTemplate(byte[] agerc, int id)
    {
        DialogTemplate? template = DialogTemplateResource.Load(agerc, id);
        if (template == null) GD.PushWarning($"[menu] {_agercPath} has no dialog {id}");
        return template;
    }

    /// <summary>Menu 0x9c45: agerc_dispatch_host_command's command 2, DialogBoxParamA(2, agerc_version_dialog_proc),
    /// owned by AGE's window, or by the 設定 dialog when its ﾊﾞｰｼﾞｮﾝ情報 button asks.</summary>
    private void OpenVersionDialog(Win32TemplateDialog? owner = null)
    {
        if (_agercDialog != owner || ReadAgerc() is not { } agerc
            || AgercTemplate(agerc, AgercVersionDialog.ResourceId) is not { } template)
            return;
        Texture2D? icon = IconResource.Load(agerc, "IDI_ICON1") is { } image ? IconTexture(image) : null;
        var settings = _catalog.StartupSettings;
        var dialog = new AgercVersionDialog(template, AgeExeFileVersion(), icon, _catalog.Title, GameVersion(),
                                            settings.GetValueOrDefault("COPYRIGHT") ?? "");
        OpenAgercDialog(dialog, owner);
    }

    /// <summary>Menus 0x9c6c, 0x9c68, 0x9c6e and 0x9c6d: agerc_dispatch_host_command's command 9,
    /// DialogBoxParamA(9, agerc_settings_dialog_proc) on tab 0..3.</summary>
    private void OpenSettingsDialog(int menuCommand)
    {
        if (_agercDialog != null || ReadAgerc() is not { } agerc
            || AgercTemplate(agerc, AgercSettingsDialog.ResourceId) is not { } template)
            return;
        AgercSettingsDialog dialog;
        try
        {
            dialog = new AgercSettingsDialog(template, id => AgercTemplate(agerc, id), new AgercSettingsHost(this),
                                             AgercSettingsText.StartTab(menuCommand));
        }
        catch (InvalidOperationException error)
        {
            GD.PushWarning($"[menu] {error.Message}");
            return;
        }
        OpenAgercDialog(dialog, null);
    }

    private void OpenAgercDialog(Win32TemplateDialog dialog, Win32TemplateDialog? owner)
    {
        if (_agercDialogs.Count == 0)
        {
            _host?.SetFrontendModalHold(true);
            FreezePresentedFrame(true);
        }
        _agercDialogs.Add(dialog);
        dialog.Ended += result =>
        {
            GD.Print($"[menu] dialog {dialog.Title} ended with {result}");
            _agercDialogs.Remove(dialog);
            dialog.QueueFree();
            if (_agercDialogs.Count > 0) return;
            FreezePresentedFrame(false);
            _host?.SetFrontendModalHold(false);
        };
        // A dialog owned by another is that window's child, so it stays above it and takes its input.
        if (owner != null) owner.AddChild(dialog);
        else AddChild(dialog);
        dialog.Open(new Vector2I(_screenWidth, _screenHeight), _lastInputFromKeyboard, owner);
        GD.Print($"[menu] dialog {dialog.Title} opens");
    }

    /// <summary>The engine side of the 設定 dialog: AGE:IAGEService on the audio mixer, the VM and SYS4REG.INI.</summary>
    private sealed class AgercSettingsHost(Main main) : IAgercSettingsHost
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

        public int NoSetMusic => main._catalog.StartupSettings.NoSetMusic;

        public string InstallDirectory => main._gameExecutableDirectory;

        public void OpenVersionDialog(Win32TemplateDialog owner) => main.OpenVersionDialog(owner);
    }

    private static Texture2D? IconTexture(IconImage icon)
    {
        var image = new Image();
        if (icon.Png != null)
        {
            if (image.LoadPngFromBuffer(icon.Png) != Error.Ok) return null;
        }
        else
        {
            image = Image.CreateFromData(icon.Width, icon.Height, false, Image.Format.Rgba8, icon.Rgba);
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>agerc_exe_file_version_string: the running EXE's \StringFileInfo\041103a4\FileVersion. The port stands
    /// in for the game's AGE.EXE, so it reads that file's.</summary>
    private string AgeExeFileVersion()
    {
        string? exe = FindFileIgnoringCase(_gameExecutableDirectory, "AGE.EXE");
        if (exe == null) return "";
        try
        {
            return VersionResource.QueryString(File.ReadAllBytes(exe), "041103a4", "FileVersion") ?? "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "";
        }
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
}
