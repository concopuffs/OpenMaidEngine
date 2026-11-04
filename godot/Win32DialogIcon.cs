using Godot;

/// <summary>
/// A 32×32 system icon drawn as vectors, so no Windows resource is needed: IDI_ASTERISK (blue disc, white
/// "i") for AGERC's 終了 dialog and IDI_HAND (red disc, white ✕) for MB_ICONHAND message boxes, both in the
/// Windows 10 style: a light grey rim and a glossy top.
/// </summary>
public partial class Win32DialogIcon : Control
{
    public enum Kind { Asterisk, Hand }

    private readonly Kind _kind;

    public Win32DialogIcon(Kind kind)
    {
        _kind = kind;
        Size = new Vector2(32, 32);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var c = new Vector2(16, 16);
        (Color dark, Color light) = _kind == Kind.Asterisk
            ? (Color.Color8(0x1c, 0x3f, 0x9e), Color.Color8(0x5b, 0x8d, 0xe8))
            : (Color.Color8(0xa8, 0x1b, 0x1b), Color.Color8(0xe8, 0x5a, 0x50));
        DrawCircle(c, 15.5f, Color.Color8(0x8c, 0x8c, 0x8c), true, -1, true);
        DrawCircle(c, 14.6f, Color.Color8(0xe6, 0xe6, 0xe6), true, -1, true);
        DrawCircle(c, 12.8f, dark, true, -1, true);
        // Radial shading: lighter towards the upper middle.
        for (int i = 1; i <= 6; i++)
        {
            float t = i / 6f;
            DrawCircle(c + new Vector2(0, -2.5f * t), 12.8f * (1 - 0.13f * i), dark.Lerp(light, t), true, -1, true);
        }
        var gloss = new Color(1, 1, 1, 0.22f);
        DrawSetTransform(c + new Vector2(0, -6.5f), 0, new Vector2(1, 0.55f));
        DrawCircle(Vector2.Zero, 10f, gloss, true, -1, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        Color glyph = Colors.White;
        if (_kind == Kind.Asterisk)
        {
            DrawCircle(c + new Vector2(0, -6.5f), 2.1f, glyph, true, -1, true);
            DrawRect(new Rect2(c.X - 2, c.Y - 2.5f, 4, 11), glyph);
        }
        else
        {
            DrawLine(c + new Vector2(-5.5f, -5.5f), c + new Vector2(5.5f, 5.5f), glyph, 3.4f, true);
            DrawLine(c + new Vector2(5.5f, -5.5f), c + new Vector2(-5.5f, 5.5f), glyph, 3.4f, true);
        }
    }
}
