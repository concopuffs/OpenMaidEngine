using System;
using System.IO;
using Age.Engine.Frontend.NativeResources;
using Godot;

/// <summary>
/// AGERC's own dialogs (docs/engine-re.md, "Native menu bar"), built from the game's AGERC.DLL templates
/// (<see cref="Win32TemplateDialog"/>). The dialog's modal loop runs on AGE's thread, so the game holds and its frame
/// stays frozen while it is open, with no dim (fullscreen also brackets it in IAGEService +0x14/+0x18). The port
/// opens ﾍﾙﾌﾟ > ﾊﾞｰｼﾞｮﾝ情報 (0x9c45).
/// </summary>
public partial class Main
{
    private string? _agercPath;
    private Win32TemplateDialog? _agercDialog;
    /// <summary>Whether the last key or button input came from the keyboard, which decides a new dialog's keyboard
    /// cues (WM_CHANGEUISTATE UIS_INITIALIZE).</summary>
    private bool _lastInputFromKeyboard;

    private void NoteInputSource(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true }) _lastInputFromKeyboard = true;
        else if (e is InputEventMouseButton { Pressed: true }) _lastInputFromKeyboard = false;
    }

    /// <summary>Menu 0x9c45: agerc_dispatch_host_command's command 2, DialogBoxParamA(2, agerc_version_dialog_proc).
    /// </summary>
    private void OpenVersionDialog()
    {
        if (_agercDialog != null || _agercPath == null) return;
        byte[] agerc;
        try
        {
            agerc = File.ReadAllBytes(_agercPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[menu] reading {_agercPath} failed: {error.Message}");
            return;
        }
        if (DialogTemplateResource.Load(agerc, AgercVersionDialog.ResourceId) is not { } template)
        {
            GD.PushWarning($"[menu] {_agercPath} has no dialog {AgercVersionDialog.ResourceId}");
            return;
        }
        Texture2D? icon = IconResource.Load(agerc, "IDI_ICON1") is { } image ? IconTexture(image) : null;
        var settings = _catalog.StartupSettings;
        var dialog = new AgercVersionDialog(template, AgeExeFileVersion(), icon, _catalog.Title, GameVersion(),
                                            settings.GetValueOrDefault("COPYRIGHT") ?? "");
        OpenAgercDialog(dialog);
    }

    private void OpenAgercDialog(Win32TemplateDialog dialog)
    {
        _host?.SetFrontendModalHold(true);
        FreezePresentedFrame(true);
        _agercDialog = dialog;
        dialog.Ended += result =>
        {
            GD.Print($"[menu] dialog {dialog.Title} ended with {result}");
            _agercDialog = null;
            dialog.QueueFree();
            FreezePresentedFrame(false);
            _host?.SetFrontendModalHold(false);
        };
        AddChild(dialog);
        dialog.Open(new Vector2I(_screenWidth, _screenHeight), _lastInputFromKeyboard);
        GD.Print($"[menu] dialog {dialog.Title} opens");
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
