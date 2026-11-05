using Godot;

/// <summary>
/// GDI DrawEdge's classic edges in the system colours of <see cref="Win32DialogStyle"/>. Each edge is two 1 px
/// bevels: the top row and left column in one colour, the bottom row and right column (which own the corners they
/// share with the first two) in the other. Captured from AGE's dialogs on Windows 10 (2026-11-05): the push button
/// and the trackbar thumb are raised, the check box, trackbar channel and bordered edit are sunken, and the group box
/// is etched.
/// </summary>
public static class Win32Edges
{
    public static void Bevel(CanvasItem canvas, Rect2I r, Color topLeft, Color bottomRight)
    {
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;
        canvas.DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X - 1, 1), topLeft);
        canvas.DrawRect(new Rect2(r.Position.X, r.Position.Y, 1, r.Size.Y - 1), topLeft);
        canvas.DrawRect(new Rect2(r.Position.X, r.End.Y - 1, r.Size.X, 1), bottomRight);
        canvas.DrawRect(new Rect2(r.End.X - 1, r.Position.Y, 1, r.Size.Y), bottomRight);
    }

    /// <summary>EDGE_RAISED: highlight over dark shadow, then light over shadow.</summary>
    public static void Raised(CanvasItem canvas, Rect2I r)
    {
        Bevel(canvas, r, Win32DialogStyle.Highlight, Win32DialogStyle.DarkShadow);
        Bevel(canvas, r.Grow(-1), Win32DialogStyle.Light, Win32DialogStyle.Shadow);
    }

    /// <summary>EDGE_SUNKEN: shadow over highlight, then dark shadow over light.</summary>
    public static void Sunken(CanvasItem canvas, Rect2I r)
    {
        Bevel(canvas, r, Win32DialogStyle.Shadow, Win32DialogStyle.Highlight);
        Bevel(canvas, r.Grow(-1), Win32DialogStyle.DarkShadow, Win32DialogStyle.Light);
    }

    /// <summary>EDGE_ETCHED: shadow over highlight, then highlight over shadow.</summary>
    public static void Etched(CanvasItem canvas, Rect2I r)
    {
        Bevel(canvas, r, Win32DialogStyle.Shadow, Win32DialogStyle.Highlight);
        Bevel(canvas, r.Grow(-1), Win32DialogStyle.Highlight, Win32DialogStyle.Shadow);
    }
}
