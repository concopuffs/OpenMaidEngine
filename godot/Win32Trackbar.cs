using System;
using Godot;

/// <summary>
/// A classic (unthemed) comctl32 trackbar (msctls_trackbar32) as AGERC's 設定 pages use it: horizontal (TBS_BOTTOM,
/// the default, or TBS_BOTH) or vertical (TBS_VERT | TBS_BOTH), with only the first and last ticks (no
/// TBS_AUTOTICKS). The geometry is comctl32's for a 22 px thumb length (captured 2026-11-05):
/// <list type="bullet">
/// <item>The channel is a 4 px sunken edge inset 8 px at both ends. Horizontally its top is 10 px down (19 with
/// TBS_BOTH); vertically its left is 19 px in.</item>
/// <item>The thumb is 11 px along the channel and 22 px across. It starts at
/// <c>channel start + (span × (pos − min) + range / 2) / range</c>, where span is the channel length less 11. It is
/// 2 px down (10 with TBS_BOTH) horizontally and 10 px in vertically. TBS_BOTTOM's thumb points down; TBS_BOTH's is
/// a raised rectangle.</item>
/// <item>Ticks are 4 px long and black, centred on the thumb's first and last positions. They sit 1 px past the
/// thumb's far side, and with TBS_BOTH also 2 px before its near side.</item>
/// </list>
/// Input follows comctl32. Arrow keys step one line (Left and Up decrease), Page Up and Page Down step a page of
/// range / 5, and Home and End go to the ends. A click on the thumb drags it; a click elsewhere on the control
/// pages toward the pointer, repeating while held. Each move raises <see cref="Scrolled"/> with its WM_HSCROLL or
/// WM_VSCROLL code, and the gesture's end raises TB_ENDTRACK. When focused, a dotted focus rectangle surrounds the
/// control.
/// </summary>
public partial class Win32Trackbar : Control
{
    public const int LineUp = 0, LineDown = 1, PageUp = 2, PageDown = 3, ThumbPosition = 4, ThumbTrack = 5,
                     Top = 6, Bottom = 7, EndTrack = 8;

    private const int ThumbLength = 22, ThumbBreadth = 11, ChannelInset = 8, ChannelThickness = 4, TickLength = 4;
    private const double RepeatDelay = 0.5, RepeatInterval = 0.1;

    // TBS_BOTTOM's pointed thumb as captured: w highlight, e light, a shadow, k dark shadow, f its face, '.' outside
    // it; rows 2..16 repeat row 2.
    private static readonly string[] PointedThumb =
    {
        "wwwwwwwwwwk",
        "weeeeeeeeak",
        "wefffffffak",
        ".wefffffak.",
        "..wefffak..",
        "...wefak...",
        "....wak....",
        ".....k.....",
    };

    private readonly bool _vertical;
    private readonly bool _both;
    private int _min, _max = 100, _position, _pageSize = 20;
    private bool _dragging;
    private int _pagingDirection;
    private double _repeatIn;
    private Vector2 _pointer;

    /// <summary>A position change with its scroll code (TB_*), then TB_ENDTRACK when the gesture ends.</summary>
    public event Action<int>? Scrolled;

    public Win32Trackbar(bool vertical, bool both)
    {
        _vertical = vertical;
        _both = both;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        SetProcess(false);
    }

    public int Minimum => _min;
    public int Maximum => _max;

    /// <summary>TBM_SETRANGEMAX: the position is clamped to the new range and the page becomes range / 5.</summary>
    public void SetRangeMax(int max)
    {
        _max = max;
        _pageSize = Math.Max(1, (_max - _min) / 5);
        _position = Math.Clamp(_position, _min, Math.Max(_min, _max));
        QueueRedraw();
    }

    /// <summary>TBM_GETPOS / TBM_SETPOS (clamped to the range, no notification).</summary>
    public int Value
    {
        get => _position;
        set
        {
            _position = Math.Clamp(value, _min, Math.Max(_min, _max));
            QueueRedraw();
        }
    }

    private int Range => Math.Max(1, _max - _min);

    private Rect2I Channel()
    {
        int w = (int)Size.X, h = (int)Size.Y;
        if (_vertical)
            return new Rect2I(ThumbLength / 2 - 1 + 9, ChannelInset, ChannelThickness, h - 2 * ChannelInset);
        int top = ThumbLength / 2 - 1 + (_both ? 9 : 0);
        return new Rect2I(ChannelInset, top, w - 2 * ChannelInset, ChannelThickness);
    }

    private int ChannelSpan(Rect2I channel) => (_vertical ? channel.Size.Y : channel.Size.X) - ThumbBreadth;

    // The thumb's offset along the channel for a position.
    private int Offset(Rect2I channel, int position) =>
        (ChannelSpan(channel) * (position - _min) + Range / 2) / Range;

    private Rect2I Thumb()
    {
        Rect2I channel = Channel();
        int along = Offset(channel, _position);
        return _vertical
            ? new Rect2I(10, channel.Position.Y + along, ThumbLength, ThumbBreadth)
            : new Rect2I(channel.Position.X + along, _both ? 10 : 2, ThumbBreadth, ThumbLength);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Win32DialogStyle.Face);
        Rect2I channel = Channel();
        Win32Edges.Sunken(this, channel);
        Rect2I thumb = Thumb();
        foreach (int position in new[] { _min, _max })
        {
            int centre = (_vertical ? channel.Position.Y : channel.Position.X) + ThumbBreadth / 2
                         + Offset(channel, position);
            if (_vertical)
            {
                Tick(new Rect2I(thumb.End.X + 1, centre, TickLength, 1));
                if (_both) Tick(new Rect2I(thumb.Position.X - 1 - TickLength, centre, TickLength, 1));
            }
            else
            {
                Tick(new Rect2I(centre, thumb.End.Y + 1, 1, TickLength));
                if (_both) Tick(new Rect2I(centre, thumb.Position.Y - 1 - TickLength, 1, TickLength));
            }
        }
        if (_both || _vertical)
        {
            DrawRect(thumb, Win32DialogStyle.Face);
            Win32Edges.Raised(this, thumb);
        }
        else
        {
            DrawPointedThumb(thumb);
        }

        if (HasFocus() && (GetWindow() as Win32Dialog)?.FocusCuesVisible != false)
            Win32DialogStyle.DrawFocusRect(this, new Rect2I(Vector2I.Zero, (Vector2I)Size));
    }

    private void Tick(Rect2I r) => DrawRect(r, Win32DialogStyle.Text);

    private void DrawPointedThumb(Rect2I thumb)
    {
        for (int y = 0; y < thumb.Size.Y; y++)
        {
            string row = PointedThumb[y < 2 ? y : y <= 16 ? 2 : y - 14];
            for (int x = 0; x < row.Length; x++)
            {
                Color? color = row[x] switch
                {
                    'w' => Win32DialogStyle.Highlight,
                    'e' => Win32DialogStyle.Light,
                    'a' => Win32DialogStyle.Shadow,
                    'k' => Win32DialogStyle.DarkShadow,
                    'f' => Win32DialogStyle.Face,
                    _ => null,
                };
                if (color is { } c) Win32DialogStyle.Pixel(this, thumb.Position.X + x, thumb.Position.Y + y, c);
            }
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                AcceptEvent();
                if (button.Pressed) Press(button.Position);
                else Release();
                break;
            case InputEventMouseMotion motion:
                _pointer = motion.Position;
                if (_dragging) Drag(motion.Position);
                break;
            case InputEventKey { Pressed: true } key:
                if (KeyStep(key.Keycode) is { } step)
                {
                    AcceptEvent();
                    MoveTo(step.Position, step.Code);
                }
                break;
            case InputEventKey { Pressed: false } key when KeyStep(key.Keycode) != null:
                AcceptEvent();
                Scrolled?.Invoke(EndTrack);
                break;
        }
    }

    private (int Position, int Code)? KeyStep(Key key) => key switch
    {
        Key.Left or Key.Up => (_position - 1, LineUp),
        Key.Right or Key.Down => (_position + 1, LineDown),
        Key.Pageup => (_position - _pageSize, PageUp),
        Key.Pagedown => (_position + _pageSize, PageDown),
        Key.Home => (_min, Top),
        Key.End => (_max, Bottom),
        _ => null,
    };

    private void Press(Vector2 at)
    {
        _pointer = at;
        if (Thumb().HasPoint((Vector2I)at))
        {
            _dragging = true;
            return;
        }
        _pagingDirection = Along(at) < Along(Thumb().GetCenter()) ? -1 : 1;
        PageTowardPointer();
        _repeatIn = RepeatDelay;
        SetProcess(true);
    }

    private void Release()
    {
        if (_dragging)
        {
            _dragging = false;
            Scrolled?.Invoke(ThumbPosition);
        }
        if (_pagingDirection != 0)
        {
            _pagingDirection = 0;
            SetProcess(false);
        }
        Scrolled?.Invoke(EndTrack);
    }

    private float Along(Vector2 point) => _vertical ? point.Y : point.X;

    // Page toward the pointer until the thumb reaches it.
    private void PageTowardPointer()
    {
        Rect2I thumb = Thumb();
        float pointer = Along(_pointer);
        bool reached = _pagingDirection < 0
            ? pointer >= Along(thumb.Position)
            : pointer < Along(thumb.End);
        if (reached) return;
        if (_pagingDirection < 0) MoveTo(_position - _pageSize, PageUp);
        else MoveTo(_position + _pageSize, PageDown);
    }

    public override void _Process(double delta)
    {
        if (_pagingDirection == 0) return;
        _repeatIn -= delta;
        if (_repeatIn > 0) return;
        _repeatIn = RepeatInterval;
        PageTowardPointer();
    }

    private void Drag(Vector2 at)
    {
        Rect2I channel = Channel();
        float start = (_vertical ? channel.Position.Y : channel.Position.X) + ThumbBreadth / 2f;
        int span = Math.Max(1, ChannelSpan(channel));
        int position = _min + (int)Math.Floor((Along(at) - start) * (_max - _min) / span + 0.5);
        MoveTo(position, ThumbTrack, notifyUnchanged: true);
    }

    private void MoveTo(int position, int code, bool notifyUnchanged = false)
    {
        position = Math.Clamp(position, _min, Math.Max(_min, _max));
        if (position == _position && !notifyUnchanged) return;
        _position = position;
        QueueRedraw();
        Scrolled?.Invoke(code);
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationFocusEnter or (int)NotificationFocusExit) QueueRedraw();
    }
}
