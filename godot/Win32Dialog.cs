using System;
using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// An embedded Godot window drawn like a native AGE dialog on Windows 10. AGE has no common-controls manifest,
/// so its dialogs have a Windows 10 frame (white caption, 1 px border) around classic controls. The values come
/// from PrintWindow captures of AGE's 終了 dialog and its interpreter error box (2026-11-04). The window rect has
/// a 2 px invisible border on the left, right and bottom, and the visible frame starts inside it. Only the
/// visible part is drawn here: a 1 px border, a 25 px caption, then the client area.
/// </summary>
public abstract partial class Win32Dialog : Window
{
    public const int InvisibleBorder = 2;            // left, right and bottom of the native window rect
    public const int CaptionHeight = 25;             // visible caption rows below the top border
    public static readonly Vector2I ClientOrigin = new(1, 1 + CaptionHeight);

    private readonly Win32DialogFrame _frame;

    /// <summary>The native dialog's client area; controls go here at their native client coordinates.</summary>
    protected Control Client { get; }

    protected Win32Dialog(string caption, Vector2I clientSize, bool closeEnabled)
    {
        Title = caption;
        Borderless = true;
        Exclusive = true;
        Transient = true;
        Unresizable = true;
        Size = clientSize + new Vector2I(2, CaptionHeight + 2);

        _frame = new Win32DialogFrame(this, caption, closeEnabled);
        AddChild(_frame);
        _frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Client = new Control { Position = ClientOrigin, Size = clientSize, MouseFilter = Control.MouseFilterEnum.Pass };
        _frame.AddChild(Client);
    }

    /// <summary>Native window size (the GetWindowRect size, with the invisible border) for a client size.</summary>
    public static Vector2I NativeWindowSize(Vector2I clientSize) =>
        clientSize + new Vector2I(2 + 2 * InvisibleBorder, CaptionHeight + 2 + InvisibleBorder);

    /// <summary>Show the dialog where native places a window whose GetWindowRect top-left is
    /// <paramref name="nativeWindowTopLeft"/> in the game's client coordinates.</summary>
    protected void PopupAtNativeWindow(Vector2I nativeWindowTopLeft)
    {
        NativeWindowTopLeft = nativeWindowTopLeft;
        Popup(new Rect2I(GameClientOrigin + nativeWindowTopLeft + new Vector2I(InvisibleBorder, 0), Size));
    }

    /// <summary>Where the dialog's native window rect starts, in the game's client coordinates.</summary>
    public Vector2I NativeWindowTopLeft { get; private set; }

    /// <summary>The dialog's native window size (GetWindowRect's, with the invisible border).</summary>
    public Vector2I NativeSize => NativeWindowSize(Size - new Vector2I(2, CaptionHeight + 2));

    /// <summary>Show the dialog as agerc_center_dialog_on_parent places a dialog owned by another dialog: centred on
    /// the owner's window rect.</summary>
    protected void PopupCenteredOnDialog(Win32Dialog owner, Vector2I clientSize)
    {
        Vector2I window = NativeWindowSize(clientSize);
        Vector2I ownerTopLeft = owner.NativeWindowTopLeft, ownerSize = owner.NativeSize;
        PopupAtNativeWindow(new Vector2I((2 * ownerTopLeft.X + ownerSize.X) / 2 - window.X / 2,
                                         (2 * ownerTopLeft.Y + ownerSize.Y) / 2 - window.Y / 2));
    }

    /// <summary>Where the game's client area starts in the port's viewport: below the menu bar, when there is one.</summary>
    public static Vector2I GameClientOrigin { get; set; }

    // AGE's windowed frame around its client on the capture machine (caption and menu bar on top):
    // agerc_center_dialog_on_parent centres a dialog on this outer window, not on the client.
    private const int OuterLeft = 3, OuterTop = 46, OuterRight = 3, OuterBottom = 3;

    /// <summary>Show the dialog as agerc_center_dialog_on_parent places it: centred on AGE's outer window around a
    /// game client of <paramref name="gameClient"/> pixels.</summary>
    protected void PopupCenteredOnAgeWindow(Vector2I gameClient, Vector2I clientSize)
    {
        Vector2I window = NativeWindowSize(clientSize);
        int outerWidth = OuterLeft + gameClient.X + OuterRight, outerHeight = OuterTop + gameClient.Y + OuterBottom;
        PopupAtNativeWindow(new Vector2I(outerWidth / 2 - window.X / 2 - OuterLeft,
                                         outerHeight / 2 - window.Y / 2 - OuterTop));
    }

    /// <summary>The dialog's UISF_HIDEFOCUS state cleared: focus rectangles are drawn. Windows hides them in a
    /// dialog opened by the mouse (a menu click included) until a navigation key is pressed.</summary>
    public bool FocusCuesVisible { get; private set; } = true;

    /// <summary>The dialog's UISF_HIDEACCEL state cleared: mnemonic underlines are drawn. Windows hides them in a
    /// dialog opened by the mouse until Alt is pressed.</summary>
    public bool AcceleratorCuesVisible { get; private set; } = true;

    /// <summary>Set the cues a dialog starts with (WM_CHANGEUISTATE UIS_INITIALIZE).</summary>
    protected void InitializeCues(bool focus, bool accelerators)
    {
        FocusCuesVisible = focus;
        AcceleratorCuesVisible = accelerators;
        RedrawControls(Client);
    }

    /// <summary>Show the cues a key reveals, as DefDlgProc does: navigation keys show focus rectangles, Alt shows
    /// both.</summary>
    protected void RevealCues(InputEventKey key)
    {
        bool alt = key.Keycode == Key.Alt || key.AltPressed;
        bool navigation = alt || key.Keycode is Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right;
        if (!navigation || (FocusCuesVisible && (AcceleratorCuesVisible || !alt))) return;
        FocusCuesVisible = true;
        if (alt) AcceleratorCuesVisible = true;
        RedrawControls(Client);
    }

    private static void RedrawControls(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is CanvasItem item) item.QueueRedraw();
            RedrawControls(child);
        }
    }

    /// <summary>Called by the frame's ✕ when it is enabled.</summary>
    protected virtual void OnCloseButton() => EmitSignal(Window.SignalName.CloseRequested);

    internal void PressCloseButton() => OnCloseButton();

    /// <summary>Redraw every classic push button, pages' included, since the default frame follows the focus.
    /// </summary>
    internal void RedrawButtons() => RedrawButtons(Client);

    private static void RedrawButtons(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Win32ClassicButton button) button.QueueRedraw();
            RedrawButtons(child);
        }
    }

    internal bool AnyPushButtonFocused() => Client.GetViewport().GuiGetFocusOwner() is Win32ClassicButton focus
                                            && Client.IsAncestorOf(focus);
}

/// <summary>Colours and fonts of the native dialogs, as captured.</summary>
public static class Win32DialogStyle
{
    public static readonly Color Frame = Color.Color8(0x19, 0x19, 0x19);
    public static readonly Color Caption = Colors.White;
    public static readonly Color CaptionText = Colors.Black;
    public static readonly Color CloseDisabled = Color.Color8(0xcc, 0xcc, 0xcc);
    public static readonly Color CloseHover = Color.Color8(0xe8, 0x11, 0x23);
    public static readonly Color ClosePressed = Color.Color8(0xf1, 0x70, 0x7a);
    public static readonly Color Face = Color.Color8(0xf0, 0xf0, 0xf0);       // COLOR_BTNFACE
    public static readonly Color Highlight = Colors.White;                    // COLOR_BTNHIGHLIGHT
    public static readonly Color Light = Color.Color8(0xe3, 0xe3, 0xe3);      // COLOR_3DLIGHT
    public static readonly Color Shadow = Color.Color8(0xa0, 0xa0, 0xa0);     // COLOR_BTNSHADOW
    public static readonly Color DarkShadow = Color.Color8(0x69, 0x69, 0x69); // COLOR_3DDKSHADOW
    public static readonly Color DefaultFrame = Color.Color8(0x64, 0x64, 0x64);
    public static readonly Color Window = Colors.White;                       // COLOR_WINDOW
    public static readonly Color Text = Colors.Black;

    // Substitutes for systems without the Windows fonts, so the dialogs stay readable everywhere.
    private static readonly string[] PortableJapanese =
    {
        "Noto Sans CJK JP", "Noto Sans JP", "Yu Gothic UI", "Meiryo UI", "Yu Gothic", "Hiragino Kaku Gothic ProN",
        "IPAGothic", "sans-serif",
    };

    private static Win32Font? _dialogFont, _messageFont, _captionFont, _menuFont;

    /// <summary>AGERC's dialog font, ＭＳ Ｐゴシック 12 px. GDI draws it from the font's 12 px bitmap strike
    /// without antialiasing; Godot does the same when the font is installed. Text sits 10 px above its
    /// baseline and centres as 13 px tall (measured; Godot reports an ascent of 11), but a multi-line static's
    /// lines are tmHeight, 12 px, apart (ﾊﾞｰｼﾞｮﾝ情報's Ogg Vorbis notice, captured 2026-11-05).</summary>
    public static Win32Font DialogFont => _dialogFont ??= WithLineHeight(
        Make("MS PGothic", 12, bitmapStrike: true, ascent: 10, height: 13), 12);

    private static Win32Font WithLineHeight(Win32Font font, int lineHeight)
    {
        font.LineHeight = lineHeight;
        return font;
    }

    /// <summary>The system message font, used by MessageBox: Segoe UI 11 px. Lines are 13 px apart (measured).
    /// Its Japanese comes from the Windows SystemLink chain's Meiryo UI, which GDI draws at 13 px.</summary>
    public static Win32Font MessageFont => _messageFont ??= Make("Segoe UI", 11, bitmapStrike: false, ascent: 10,
        height: 13, ("Meiryo UI", LinkedSize), ("Yu Gothic UI", LinkedSize));

    /// <summary>The caption font of the Windows 10 frame (Segoe UI 12 px), linked like the message font and drawn
    /// with ClearType, as every captured caption is.</summary>
    public static Win32Font CaptionFont => _captionFont ??= Make("Segoe UI", 12, bitmapStrike: false, ascent: 0,
        height: 0, lcd: true, ("Meiryo UI", LinkedSize), ("Yu Gothic UI", LinkedSize));

    /// <summary>The system menu font (lfMenuFont, Segoe UI -12), linked like the message font. GDI measures a line
    /// 15 px tall with its baseline 12 px down (GetTextMetrics on the capture machine, 2026-11-05).</summary>
    public static Win32Font MenuFont => _menuFont ??= Make("Segoe UI", 12, bitmapStrike: false, ascent: 12,
        height: 15, lcd: true, ("Meiryo UI", LinkedSize), ("Yu Gothic UI", LinkedSize));

    // GDI's font linking draws Segoe UI's Japanese as Meiryo UI 13 px for both the -11 and -12 heights: the
    // extents of エラーが発生しました (105 px) and 再試行：もう一度このコマンドを実行します． (233 px) match it.
    private const int LinkedSize = 13;

    // ascent/height 0: use the font's own metrics. Substitute fonts always do, since the measured values
    // belong to the Windows faces. The first installed linked face draws what the face lacks.
    private static Win32Font Make(string face, int size, bool bitmapStrike, int ascent, int height,
        params (string Face, int Size)[] linked) => Make(face, size, bitmapStrike, ascent, height, false, linked);

    // lcd: subpixel antialiasing, as ClearType draws the menus.
    private static Win32Font Make(string face, int size, bool bitmapStrike, int ascent, int height, bool lcd,
        params (string Face, int Size)[] linked)
    {
        var portable = new SystemFont { FontNames = PortableJapanese };
        if (LoadSystemFace(face) is not { } font) return new Win32Font(portable, size, 0, 0);
        Configure(font, bitmapStrike, lcd);
        font.Fallbacks = new Godot.Collections.Array<Font> { portable };
        foreach ((string name, int linkedSize) in linked)
            if (LoadSystemFace(name) is { } link)
                return new Win32Font(font, size, ascent, height, Configure(link, false, lcd), linkedSize)
                {
                    GdiFace = face,
                };
        return new Win32Font(font, size, ascent, height) { GdiFace = face };
    }

    private static FontFile Configure(FontFile font, bool bitmapStrike, bool lcd = false)
    {
        font.Antialiasing = bitmapStrike ? TextServer.FontAntialiasing.None
            : lcd ? TextServer.FontAntialiasing.Lcd : TextServer.FontAntialiasing.Gray;
        font.DisableEmbeddedBitmaps = !bitmapStrike;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        font.Hinting = TextServer.Hinting.Normal;
        return font;
    }

    /// <summary>The installed face named <paramref name="family"/>, or null. Many Windows fonts are collections
    /// (msgothic.ttc holds MS Gothic, MS PGothic and MS UI Gothic), and SystemFont loads a collection's first
    /// face, so the face is picked by name here. The index must be set before the data is.</summary>
    private static FontFile? LoadSystemFace(string family)
    {
        string path = OS.GetSystemFontPath(family);
        if (string.IsNullOrEmpty(path)) return null;
        byte[] data = Godot.FileAccess.GetFileAsBytes(path);
        if (data.Length == 0) return null;
        long faces = new FontFile { Data = data }.GetFaceCount();
        for (int i = 0; i < faces; i++)
        {
            var font = new FontFile();
            font.SetFaceIndex(0, i);
            font.Data = data;
            if (string.Equals(font.FontName, family, StringComparison.OrdinalIgnoreCase)) return font;
        }
        return null;
    }

    /// <summary>Draw a GDI DrawFocusRect: a 1 px dotted rectangle, every other pixel from the top-left.</summary>
    public static void DrawFocusRect(CanvasItem canvas, Rect2I rect)
    {
        int x0 = rect.Position.X, y0 = rect.Position.Y, x1 = rect.End.X - 1, y1 = rect.End.Y - 1;
        for (int x = x0; x <= x1; x++)
        {
            if ((x - x0) % 2 == 0) Pixel(canvas, x, y0, Text);
            if ((x - x0 + y1 - y0) % 2 == 0) Pixel(canvas, x, y1, Text);
        }
        for (int y = y0 + 1; y < y1; y++)
        {
            if ((y - y0) % 2 == 0) Pixel(canvas, x0, y, Text);
            if ((x1 - x0 + y - y0) % 2 == 0) Pixel(canvas, x1, y, Text);
        }
    }

    public static void Pixel(CanvasItem canvas, int x, int y, Color color) =>
        canvas.DrawRect(new Rect2(x, y, 1, 1), color);

}

/// <summary>A dialog font with the GDI metrics its text is laid out by.</summary>
public sealed class Win32Font
{
    public Font Font { get; }
    public int Size { get; }
    /// <summary>Pixels from a text line's top to its baseline (GDI tmAscent).</summary>
    public int Ascent { get; }
    /// <summary>A text line's height: what DT_VCENTER centres, and the line pitch unless
    /// <see cref="LineHeight"/> says otherwise.</summary>
    public int Height { get; }

    /// <summary>The pitch of DT_WORDBREAK lines.</summary>
    public int LineHeight { get; set; }

    private readonly Font? _linked;
    private readonly int _linkedSize;

    /// <summary>The Windows face this font stands in for: on Windows, GDI measures the advances with it.</summary>
    public string? GdiFace { get; init; }

    public Win32Font(Font font, int size, int ascent, int height, Font? linked = null, int linkedSize = 0)
    {
        Font = font;
        Size = size;
        _linked = linked;
        _linkedSize = linkedSize;
        Ascent = ascent > 0 ? ascent : (int)Mathf.Round(font.GetAscent(size));
        Height = height > 0 ? height : (int)Mathf.Round(font.GetHeight(size));
        LineHeight = Height;
    }

    /// <summary>GDI's text extent: the sum of the characters' advances (Godot's shaped string size can differ
    /// by a pixel). Mnemonic marks are not counted.</summary>
    public float Width(string text)
    {
        float width = 0;
        foreach (Rune rune in text.Replace("&", "").EnumerateRunes()) width += Advance(rune);
        return width;
    }

    // GDI's advance where GDI is available (it links the faces itself), else the drawing face's.
    private float Advance(Rune rune)
    {
        if (GdiFace != null && GdiTextMetrics.Advance(GdiFace, Size, rune.Value) is int gdi) return gdi;
        (Font font, int size) = FaceFor(rune);
        return font.GetCharSize(rune.Value, size).X;
    }

    // The linked face draws the characters the primary face lacks, at its own size, as GDI font linking does.
    // Font.HasChar also answers for the fallbacks (the portable Japanese chain), so coverage is asked of the
    // primary face's own data.
    private (Font, int) FaceFor(Rune rune) =>
        _linked != null && !PrimaryHasChar(rune.Value) && _linked.HasChar(rune.Value)
            ? (_linked, _linkedSize)
            : (Font, Size);

    private bool PrimaryHasChar(int codepoint)
    {
        if (_primaryRid is not { } rid)
        {
            Godot.Collections.Array<Rid> rids = Font.GetRids();
            rid = rids.Count > 0 ? rids[0] : default;
            _primaryRid = rid;
        }
        return rid.IsValid
            ? TextServerManager.GetPrimaryInterface().FontHasChar(rid, codepoint)
            : Font.HasChar(codepoint);
    }

    private Rid? _primaryRid;

    /// <summary>Draw one line from its top-left. With <paramref name="mnemonic"/>, "&amp;" marks the
    /// underlined character and is not drawn, as Win32 labels do; <paramref name="underline"/> false hides the
    /// underline, as Windows does for menus opened with the mouse while keyboard cues are off.</summary>
    public void Draw(CanvasItem canvas, Vector2 topLeft, string text, Color color, bool mnemonic = true,
                     bool underline = true)
    {
        int mark = mnemonic ? text.IndexOf('&') : -1;
        string shown = mnemonic ? text.Replace("&", "") : text;
        float baseline = topLeft.Y + Ascent;
        // GDI places each glyph at the sum of the advances before it, without kerning or shaping; so does this.
        float x = topLeft.X;
        foreach (Rune rune in shown.EnumerateRunes())
        {
            (Font font, int size) = FaceFor(rune);
            canvas.DrawChar(font, new Vector2(x, baseline), rune.ToString(), size, color);
            x += Advance(rune);
        }
        if (!underline || mark < 0 || mark >= shown.Length) return;
        float before = Width(shown[..mark]);
        float width = Width(shown[mark].ToString());
        canvas.DrawRect(new Rect2(Mathf.Round(topLeft.X + before), baseline + 1, Mathf.Round(width), 1), color);
    }

    /// <summary>Word-wrap like DrawText's DT_WORDBREAK: lines break at newlines, and a line too wide for
    /// <paramref name="width"/> breaks before the word that overflows. A word is a run of non-space characters
    /// below U+0800, or one wider character, so Japanese breaks between characters.</summary>
    public List<string> Wrap(string text, float width)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            bool wrapped = false;
            int i = 0;
            while (i < paragraph.Length)
            {
                int end = i + 1;
                if (paragraph[i] != ' ' && paragraph[i] < 0x800)
                    while (end < paragraph.Length && paragraph[end] != ' ' && paragraph[end] < 0x800) end++;
                string word = paragraph[i..end];
                if (line.Length > 0 && word != " " && Width(line + word) > width)
                {
                    lines.Add(line.ToString().TrimEnd(' '));
                    line.Clear();
                    wrapped = true;
                }
                // Spaces that start a wrapped line are dropped; a paragraph keeps its indent.
                if (!wrapped || line.Length > 0 || word != " ") line.Append(word);
                i = end;
            }
            lines.Add(line.ToString());
        }
        return lines;
    }
}
