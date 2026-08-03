using Age.Engine.Model;

namespace Age.Engine.Text;

/// <summary>
/// Transient retained-object projection of one ADV layout's animated input-wait atlas.
/// The script owns the source surface and object handle; this object owns only the current binding.
/// </summary>
public sealed class RetainedAdvWaitIndicatorPresentation
{
    private int _publishedFrame = -1;

    public RetainedAdvWaitIndicatorPresentation(
        AdvWaitIndicatorConfig config,
        AdvTextLayoutPresentationBinding binding,
        int layoutOriginX,
        int layoutOriginY)
    {
        if (config.LayoutSlot != binding.LayoutSlot)
            throw new ArgumentException(
                "Wait-indicator configuration and retained binding must select the same layout.");
        if (binding.WaitIndicatorObjectHandle < 0)
            throw new ArgumentException(
                "A retained ADV wait indicator requires a nonnegative object handle.",
                nameof(binding));
        if (config.SurfaceSlot < 0 || config.CellWidth <= 0 || config.CellHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(config));

        Config = config;
        Binding = binding;
        LayoutOriginX = layoutOriginX;
        LayoutOriginY = layoutOriginY;
    }

    public AdvWaitIndicatorConfig Config { get; }
    public AdvTextLayoutPresentationBinding Binding { get; }
    public int LayoutOriginX { get; }
    public int LayoutOriginY { get; }
    public int PublishedFrame => _publishedFrame;
    public bool IsPublished => _publishedFrame >= 0;

    /// <summary>Publish a changed source cell, or erase the retained handle when inactive.</summary>
    public bool Update(GfxState gfx, long elapsedMs, bool visible)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        if (!visible)
        {
            if (!IsPublished) return false;
            Erase(gfx);
            return true;
        }

        int frame = Config.FrameAt(elapsedMs);
        if (frame == _publishedFrame) return false;
        Bind(gfx, frame);
        return true;
    }

    /// <summary>Recreate the current frame after script-owned range teardown/publication.</summary>
    public bool Republish(GfxState gfx, long elapsedMs, bool visible)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        if (!visible)
        {
            if (!IsPublished) return false;
            Erase(gfx);
            return true;
        }

        Bind(gfx, Config.FrameAt(elapsedMs));
        return true;
    }

    public void Erase(GfxState gfx)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        gfx.EraseRange(Binding.WaitIndicatorObjectHandle, 1);
        _publishedFrame = -1;
    }

    private void Bind(GfxState gfx, int frame)
    {
        gfx.BindDraw(
            Binding.WaitIndicatorObjectHandle,
            Config.SurfaceSlot,
            checked(Config.SourceX + frame * Config.CellWidth),
            Config.SourceY,
            Config.CellWidth,
            Config.CellHeight,
            checked(LayoutOriginX + Config.X),
            checked(LayoutOriginY + Config.Y));
        _publishedFrame = frame;
    }
}
