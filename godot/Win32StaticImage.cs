using Godot;

/// <summary>
/// A Win32 SS_ICON static: the icon STM_SETICON gave it, drawn at the control's top-left. Without
/// SS_CENTERIMAGE or SS_REALSIZECONTROL the control takes the icon's size, as the static does when it gets one.
/// </summary>
public partial class Win32StaticImage : Control
{
    private Texture2D? _texture;

    public Win32StaticImage() => MouseFilter = MouseFilterEnum.Ignore;

    public Texture2D? Texture
    {
        get => _texture;
        set
        {
            _texture = value;
            if (value != null) Size = value.GetSize();
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_texture != null) DrawTexture(_texture, Vector2.Zero);
    }
}
