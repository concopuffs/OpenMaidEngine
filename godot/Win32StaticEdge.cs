using Godot;

/// <summary>
/// A Win32 static frame: SS_SUNKEN's edge (the top row and left column COLOR_BTNSHADOW, the bottom row and right
/// column COLOR_BTNHIGHLIGHT, which owns the corners it shares), then, inside it, the 1 px frame of SS_BLACKFRAME
/// (COLOR_WINDOWFRAME), SS_GRAYFRAME (COLOR_BTNSHADOW) or SS_WHITEFRAME (COLOR_BTNHIGHLIGHT). AGERC's version dialog
/// draws its separator as a 2 px SS_BLACKFRAME | SS_SUNKEN static, whose edge leaves no room for the frame
/// (captured 2026-11-05).
/// </summary>
public partial class Win32StaticEdge : Control
{
    private readonly bool _sunken;
    private readonly Color? _frame;

    public Win32StaticEdge(bool sunken, Color? frame)
    {
        _sunken = sunken;
        _frame = frame;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var r = new Rect2I(Vector2I.Zero, (Vector2I)Size);
        if (_sunken)
        {
            Bevel(r, Win32DialogStyle.Shadow, Win32DialogStyle.Highlight);
            r = r.Grow(-1);
        }
        if (_frame is { } frame && r.Size.X > 0 && r.Size.Y > 0) Bevel(r, frame, frame);
    }

    private void Bevel(Rect2I r, Color topLeft, Color bottomRight)
    {
        DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X - 1, 1), topLeft);
        DrawRect(new Rect2(r.Position.X, r.Position.Y, 1, r.Size.Y - 1), topLeft);
        DrawRect(new Rect2(r.Position.X, r.End.Y - 1, r.Size.X, 1), bottomRight);
        DrawRect(new Rect2(r.End.X - 1, r.Position.Y, 1, r.Size.Y), bottomRight);
    }
}
