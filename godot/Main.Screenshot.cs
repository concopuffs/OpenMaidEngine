using System;
using System.IO;
using Ome.Engine.Model;
using Ome.Engine.Persistence;
using Godot;

/// <summary>
/// AGE's screenshot BMPs and their folder (docs/engine-re.md, "Native menu bar"): F1 and 表示画面を保存 (0x9c46) save the
/// presented frame through <see cref="ScreenshotBmpStore"/>; 画像の保存場所 (0x9c47) picks system:SaveBMPPath.
/// <c>--desktop-dir DIR</c> replaces the Desktop folder (the driver's sandbox), as native_drive redirects CSIDL 0x10.
/// </summary>
public partial class Main
{
    private ScreenshotBmpStore _screenshots = null!;
    private string? _desktopDirectoryOverride;
    private string _gameExecutableDirectory = "";
    private bool _useAppDataFolder;
    private ScreenshotFolderDialog? _screenshotFolderDialog;

    private void InitScreenshots(string[] userArgs, string gameRoot, bool useAppDataFolder)
    {
        _desktopDirectoryOverride = ArgumentValue(userArgs, "--desktop-dir");
        _gameExecutableDirectory = gameRoot;
        _useAppDataFolder = useAppDataFolder;
        _screenshots = new ScreenshotBmpStore(DesktopDirectory);
    }

    /// <summary>SHGetFolderPathA(CSIDL_DESKTOPDIRECTORY | CSIDL_FLAG_CREATE): the folder, created when missing.</summary>
    private string? DesktopDirectory()
    {
        string path = _desktopDirectoryOverride is { Length: > 0 } overridden
            ? Path.GetFullPath(overridden)
            : System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory,
                                        System.Environment.SpecialFolderOption.Create);
        if (path.Length == 0) return null;
        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string SaveBmpPath
        => _sys4RegIniStore?.LoadString(ScreenshotBmpStore.SaveBmpPathSection, ScreenshotBmpStore.SaveBmpPathKey, "")
           ?? "";

    /// <summary>engine_save_screenshot_bmp: write <paramref name="frame"/>, the game's presented 800×600 frame without
    /// the menu bar, as the next CG%6.6d.BMP. Native says nothing either way; the port only logs.</summary>
    private void SaveScreenshot(Image? frame)
    {
        if (frame == null || frame.IsEmpty()) return;
        Image image = (Image)frame.Duplicate();
        if (image.GetWidth() != _screenWidth || image.GetHeight() != _screenHeight)
            image.Resize(_screenWidth, _screenHeight, Image.Interpolation.Bilinear);
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        var rgba = new RgbaImage(image.GetWidth(), image.GetHeight(), image.GetData());
        string? path = _screenshots.Save(rgba, SaveBmpPath);
        GD.Print(path != null
            ? $"[screenshot] {path}"
            : $"[screenshot] nothing written (folder {_screenshots.TargetDirectory(SaveBmpPath) ?? "<none>"})");
    }

    /// <summary>WM_KEYDOWN VK_F1, auto-repeat included, unless op 0x10f's mask has bit 0. A modal dialog owns the
    /// keyboard while it is open, so F1 does nothing then.</summary>
    private bool TrySaveScreenshotFromKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Keycode: Key.F1 }) return false;
        if (_vm == null || (_vm.SystemKeyDisableMask & 1) != 0) return false;
        if (_exitConfirmation != null || _scriptErrorDialog != null || _fullwidthTextEditor != null
            || _screenshotFolderDialog != null || _agercDialog != null)
            return false;
        SaveScreenshot(CaptureGameImage());
        return true;
    }

    /// <summary>Menu 0x9c46. The menu loop presented nothing since it opened, so the frame is the one frozen then.
    /// </summary>
    private void SaveScreenshotFromMenu() => SaveScreenshot(_frozenFrameImage ?? CaptureGameImage());

    /// <summary>Menu 0x9c47: the folder picker, preselecting where the next screenshot goes (SaveBMPPath, else the
    /// game's directory, which set:UseAppDataFolder=1 replaces with the Desktop). The game holds and its frame stays
    /// frozen while the dialog is open, as native's thread is inside SHBrowseForFolder.</summary>
    private void ChooseScreenshotFolder()
    {
        if (_screenshotFolderDialog != null) return;
        string initial = SaveBmpPath;
        if (initial.Length == 0)
            initial = _useAppDataFolder ? DesktopDirectory() ?? _gameExecutableDirectory : _gameExecutableDirectory;
        _host?.SetFrontendModalHold(true);
        FreezePresentedFrame(true);
        GD.Print($"[menu] screenshot folder dialog opens at {initial}");
        _screenshotFolderDialog = ScreenshotFolderDialog.Open(_shellStrings["folder.prompt"], initial, OnScreenshotFolderChosen);
    }

    private void OnScreenshotFolderChosen(string? folder)
    {
        _screenshotFolderDialog = null;
        if (folder != null)
        {
            _sys4RegIniStore?.SaveString(ScreenshotBmpStore.SaveBmpPathSection, ScreenshotBmpStore.SaveBmpPathKey,
                                         folder);
            GD.Print($"[menu] system:SaveBMPPath = {folder}");
        }
        else
        {
            GD.Print("[menu] screenshot folder dialog cancelled");
        }
        FreezePresentedFrame(false);
        _host?.SetFrontendModalHold(false);
    }
}
