using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A classic (unthemed) comctl32 tab control (SysTabControl32) with one row of tabs, as AGERC's 設定 dialog draws it
/// (captured 2026-11-05).
/// <list type="bullet">
/// <item>Tabs start 2 px in and 2 px down. Each is its label's width plus 12 px wide, with the label 6 px in, and
/// reaches down to the body's top edge 19 px below the control's top.</item>
/// <item>A tab's outline is rounded at the top: a highlight top row from L+2 to R−2; then highlight at L+1, light
/// from L+2 to R−2 and dark shadow at R−1; then highlight at L, light at L+1, shadow at R−1 and dark shadow at
/// R.</item>
/// <item>The selected tab is drawn last, 2 px larger to the left, right and top, with its label 2 px higher. Its
/// sides run through the body's top row, and the body's top edge is left out under it, from L to R.</item>
/// <item>The body is a raised edge from the top row of the body to the control's bottom.</item>
/// </list>
/// A click on a tab selects it (TCN_SELCHANGING, then TCN_SELCHANGE) without taking the focus, since the control
/// lacks TCS_FOCUSONBUTTONDOWN. With the focus, Left and Right select the neighbouring tab.
/// </summary>
public partial class Win32TabControl : Control
{
    private const int StripLeft = 2, StripTop = 2, BodyTop = 19, Padding = 6, Inflate = 2;

    private readonly Win32Font _font;
    private readonly List<string> _labels = new();
    private int _selected = -1;
    private Control? _focusBeforeClick;

    /// <summary>TCN_SELCHANGING, before the selection moves off <see cref="Selected"/>.</summary>
    public event Action? SelectionChanging;

    /// <summary>TCN_SELCHANGE, after the selection moved.</summary>
    public event Action? SelectionChanged;

    public Win32TabControl(Win32Font font)
    {
        _font = font;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>TCM_INSERTITEM at the end.</summary>
    public void AddTab(string label)
    {
        _labels.Add(label);
        if (_selected < 0) _selected = 0;
        QueueRedraw();
    }

    /// <summary>TCM_GETCURSEL / TCM_SETCURSEL (no notifications).</summary>
    public int Selected
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _labels.Count) return;
            _selected = value;
            QueueRedraw();
        }
    }

    // The tab's inclusive left and right columns.
    private (int Left, int Right) Columns(int index)
    {
        int left = StripLeft;
        for (int i = 0; ; i++)
        {
            int right = left + (int)Mathf.Ceil(_font.Width(_labels[i])) + 2 * Padding - 1;
            if (i == index) return (left, right);
            left = right + 1;
        }
    }

    public override void _Draw()
    {
        int w = (int)Size.X, h = (int)Size.Y;
        DrawRect(new Rect2(0, 0, w, h), Win32DialogStyle.Face);

        // The body: a raised edge whose top rows leave out the selected tab's columns.
        (int cutLeft, int cutRight) = _selected >= 0 ? Columns(_selected) : (0, -1);
        cutLeft -= Inflate;
        cutRight += Inflate;
        var body = new Rect2I(0, BodyTop, w, h - BodyTop);
        Win32Edges.Raised(this, body);
        for (int x = Math.Max(cutLeft, 0); x <= Math.Min(cutRight, w - 1); x++)
        {
            bool outerColumn = x == 0, innerColumn = x == 1;
            if (!outerColumn) Win32DialogStyle.Pixel(this, x, BodyTop, Win32DialogStyle.Face);
            if (!outerColumn && !innerColumn) Win32DialogStyle.Pixel(this, x, BodyTop + 1, Win32DialogStyle.Face);
        }

        for (int i = 0; i < _labels.Count; i++)
            if (i != _selected) DrawTab(i, false);
        if (_selected >= 0) DrawTab(_selected, true);
    }

    private void DrawTab(int index, bool selected)
    {
        (int l, int r) = Columns(index);
        int t = StripTop, bottom = BodyTop - 1;
        if (selected)
        {
            l -= Inflate;
            r += Inflate;
            t -= Inflate;
            bottom = BodyTop;
            DrawRect(new Rect2(l, t, r - l + 1, BodyTop + 1 - t), Win32DialogStyle.Face);
        }
        Row(l + 2, r - 2, t, Win32DialogStyle.Highlight);
        Win32DialogStyle.Pixel(this, l + 1, t + 1, Win32DialogStyle.Highlight);
        Row(l + 2, r - 2, t + 1, Win32DialogStyle.Light);
        Win32DialogStyle.Pixel(this, r - 1, t + 1, Win32DialogStyle.DarkShadow);
        for (int y = t + 2; y <= bottom; y++)
        {
            Win32DialogStyle.Pixel(this, l, y, Win32DialogStyle.Highlight);
            Win32DialogStyle.Pixel(this, l + 1, y, Win32DialogStyle.Light);
            Win32DialogStyle.Pixel(this, r - 1, y, Win32DialogStyle.Shadow);
            Win32DialogStyle.Pixel(this, r, y, Win32DialogStyle.DarkShadow);
        }

        int textLeft = Columns(index).Left + Padding;
        int textTop = StripTop + 3 - (selected ? Inflate : 0);
        var dialog = GetWindow() as Win32Dialog;
        _font.Draw(this, new Vector2(textLeft, textTop), _labels[index], Win32DialogStyle.Text,
                   underline: dialog?.AcceleratorCuesVisible != false);
        if (selected && HasFocus() && dialog?.FocusCuesVisible != false)
            Win32DialogStyle.DrawFocusRect(this, new Rect2I(l + 3, t + 3, r - l - 5, BodyTop - t - 4));
    }

    private void Row(int from, int to, int y, Color color)
    {
        if (to >= from) DrawRect(new Rect2(from, y, to - from + 1, 1), color);
    }

    private int TabAt(Vector2 point)
    {
        if (point.Y < StripTop - Inflate || point.Y >= BodyTop) return -1;
        // The selected tab is on top, so it answers first.
        if (_selected >= 0)
        {
            (int l, int r) = Columns(_selected);
            if (point.X >= l - Inflate && point.X <= r + Inflate) return _selected;
        }
        if (point.Y < StripTop) return -1;
        for (int i = 0; i < _labels.Count; i++)
        {
            (int l, int r) = Columns(i);
            if (point.X >= l && point.X <= r) return i;
        }
        return -1;
    }

    /// <summary>Select a tab as a click or arrow key does: TCN_SELCHANGING, the move, TCN_SELCHANGE.</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= _labels.Count || index == _selected) return;
        SelectionChanging?.Invoke();
        _selected = index;
        QueueRedraw();
        SelectionChanged?.Invoke();
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button:
                AcceptEvent();
                // Without TCS_FOCUSONBUTTONDOWN a click leaves the focus where it was.
                if (_focusBeforeClick is { } previous && IsInstanceValid(previous) && previous != this)
                    previous.CallDeferred(Control.MethodName.GrabFocus);
                Select(TabAt(button.Position));
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Left }:
                AcceptEvent();
                Select(_selected - 1);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Right }:
                AcceptEvent();
                Select(_selected + 1);
                break;
        }
    }

    public override void _EnterTree()
    {
        GetViewport().GuiFocusChanged += OnFocusChanged;
    }

    public override void _ExitTree()
    {
        GetViewport().GuiFocusChanged -= OnFocusChanged;
    }

    // Remember the focus a click on the strip takes away (Godot focuses a FocusMode.All control on a click).
    private void OnFocusChanged(Control focus)
    {
        if (focus != this) _focusBeforeClick = focus;
        else if (!Input.IsMouseButtonPressed(MouseButton.Left)) _focusBeforeClick = null;
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationFocusEnter or (int)NotificationFocusExit) QueueRedraw();
    }
}
