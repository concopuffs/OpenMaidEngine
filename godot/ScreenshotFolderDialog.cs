using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Godot;

/// <summary>
/// AGERC's 画像の保存場所 (menu 0x9c47, agerc_choose_screenshot_folder@0x10002bc0; docs/engine-re.md, "Native menu
/// bar"). Native opens the shell's own folder picker, SHBrowseForFolder rooted at the desktop with
/// BIF_RETURNONLYFSDIRS, a prompt (the shell's <c>folder.prompt</c>; AGERC's 画像を保存するフォルダを選択してください) and
/// the folder screenshots would go to preselected. OK stores the folder as system:SaveBMPPath; Cancel changes nothing.
/// <para>On Windows the port opens the same dialog, owned by the game window, on a thread of its own: the game window
/// is disabled while it is open, as native's is, but Godot keeps presenting the frozen frame and the driver can list
/// and answer it as it does a native dialog. Elsewhere it uses the platform's folder dialog.</para>
/// </summary>
public sealed class ScreenshotFolderDialog
{
    private readonly string _prompt;
    private readonly Action<string?> _completed;
    private IntPtr _hwnd;

    private ScreenshotFolderDialog(string prompt, Action<string?> completed)
    {
        _prompt = prompt;
        _completed = completed;
    }

    /// <summary>Open the dialog. <paramref name="completed"/> runs on the main thread with the chosen folder, or
    /// null for Cancel or a failure.</summary>
    public static ScreenshotFolderDialog Open(string prompt, string initialFolder, Action<string?> completed)
    {
        var dialog = new ScreenshotFolderDialog(prompt, completed);
        if (OperatingSystem.IsWindows())
        {
            long owner = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
            var thread = new Thread(() => dialog.RunWin32(new IntPtr(owner), initialFolder))
            {
                IsBackground = true,
                Name = "screenshot folder dialog",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        else if (DisplayServer.HasFeature(DisplayServer.Feature.NativeDialogFile))
        {
            DisplayServer.FileDialogShow(prompt, initialFolder, "", false, DisplayServer.FileDialogMode.OpenDir,
                Array.Empty<string>(),
                Callable.From((bool ok, string[] paths, long _) =>
                    dialog._completed(ok && paths.Length > 0 ? paths[0] : null)));
        }
        else
        {
            GD.PushWarning("[menu] this platform has no folder dialog");
            Callable.From(() => completed(null)).CallDeferred();
        }
        return dialog;
    }

    /// <summary>The dialog as the driver's <c>dialogs</c> step reports it (title, static text, buttons), or null
    /// before its window exists.</summary>
    public System.Text.Json.Nodes.JsonObject? Describe()
    {
        IntPtr hwnd = Volatile.Read(ref _hwnd);
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return null;
        var texts = new List<string>();
        var buttons = new System.Text.Json.Nodes.JsonArray();
        foreach ((IntPtr _, string kind, string text) in Children(hwnd))
        {
            if (kind == "Static" && text.Length != 0) texts.Add(text);
            else if (kind == "Button") buttons.Add(text);
        }
        return new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = WindowText(hwnd), ["text"] = string.Join("\n", texts), ["buttons"] = buttons,
        };
    }

    /// <summary>Click the button labelled <paramref name="button"/> (ignoring access-key ampersands and case), as
    /// the native driver's <c>answer</c> does. The button's label, or null when there is none.</summary>
    public string? Press(string button)
    {
        IntPtr hwnd = Volatile.Read(ref _hwnd);
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return null;
        string wanted = button.Replace("&", "").ToLowerInvariant();
        foreach ((IntPtr handle, string kind, string text) in Children(hwnd))
        {
            if (kind != "Button" || text.Replace("&", "").ToLowerInvariant() != wanted) continue;
            PostMessageW(handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            return text;
        }
        return null;
    }

    // ---- Win32 ----

    private const uint BIF_RETURNONLYFSDIRS = 0x1;
    private const uint BFFM_INITIALIZED = 1;
    private const uint BFFM_SETSELECTIONW = 0x467;   // WM_USER + 103; AGERC sends the ANSI 0x466
    private const uint BM_CLICK = 0xF5;
    private const int MaxPath = 260;

    private void RunWin32(IntPtr owner, string initialFolder)
    {
        string? chosen = null;
        IntPtr displayName = Marshal.AllocCoTaskMem(MaxPath * sizeof(char));
        BrowseCallbackProc callback = (hwnd, message, _, _) =>
        {
            if (message == BFFM_INITIALIZED)
            {
                Volatile.Write(ref _hwnd, hwnd);
                if (initialFolder.Length != 0) SendMessageW(hwnd, BFFM_SETSELECTIONW, new IntPtr(1), initialFolder);
            }
            return 0;
        };
        try
        {
            var info = new BROWSEINFOW
            {
                hwndOwner = owner,
                pszDisplayName = displayName,
                lpszTitle = _prompt,
                ulFlags = BIF_RETURNONLYFSDIRS,
                lpfn = callback,
            };
            IntPtr pidl = SHBrowseForFolderW(ref info);
            if (pidl != IntPtr.Zero)
            {
                var path = new StringBuilder(MaxPath);
                if (SHGetPathFromIDListW(pidl, path)) chosen = path.ToString();
                CoTaskMemFree(pidl);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            GD.PushWarning($"[menu] the folder dialog failed: {error.Message}");
        }
        finally
        {
            Marshal.FreeCoTaskMem(displayName);
            GC.KeepAlive(callback);
            Volatile.Write(ref _hwnd, IntPtr.Zero);
        }
        Callable.From(() => _completed(chosen)).CallDeferred();
    }

    private static List<(IntPtr Handle, string Kind, string Text)> Children(IntPtr hwnd)
    {
        var children = new List<(IntPtr, string, string)>();
        EnumChildProc visit = (handle, _) =>
        {
            children.Add((handle, ClassName(handle), WindowText(handle)));
            return true;
        };
        EnumChildWindows(hwnd, visit, IntPtr.Zero);
        GC.KeepAlive(visit);
        return children;
    }

    private static string WindowText(IntPtr hwnd)
    {
        var text = new StringBuilder(512);
        GetWindowTextW(hwnd, text, text.Capacity);
        return text.ToString();
    }

    private static string ClassName(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        GetClassNameW(hwnd, name, name.Capacity);
        return name.ToString();
    }

    private delegate int BrowseCallbackProc(IntPtr hwnd, uint message, IntPtr lParam, IntPtr data);

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFOW
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszTitle;
        public uint ulFlags;
        public BrowseCallbackProc lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolderW(ref BROWSEINFOW info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder path);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, string lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc visit, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int capacity);
}
