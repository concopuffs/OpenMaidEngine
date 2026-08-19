namespace Age.Engine.Model;

public readonly record struct AdvWaitIndicatorConfig
{
    public AdvWaitIndicatorConfig(
        int LayoutSlot, int X, int Y, int SurfaceSlot,
        int SourceX, int SourceY, int CellWidth, int CellHeight,
        int TerminalFrame, long FramePeriodMs)
        : this(LayoutSlot, X, Y, SurfaceSlot, SourceX, SourceY, CellWidth, CellHeight,
               TerminalFrame, TerminalFrame, FramePeriodMs)
    {
    }

    public AdvWaitIndicatorConfig(
        int LayoutSlot, int X, int Y, int SurfaceSlot,
        int SourceX, int SourceY, int CellWidth, int CellHeight,
        int AtlasColumns, int TerminalFrame, long FramePeriodMs)
    {
        this.LayoutSlot = LayoutSlot;
        this.X = X;
        this.Y = Y;
        this.SurfaceSlot = SurfaceSlot;
        this.SourceX = SourceX;
        this.SourceY = SourceY;
        this.CellWidth = CellWidth;
        this.CellHeight = CellHeight;
        this.AtlasColumns = AtlasColumns;
        this.TerminalFrame = TerminalFrame;
        this.FramePeriodMs = FramePeriodMs;
    }

    public int LayoutSlot { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int SurfaceSlot { get; init; }
    public int SourceX { get; init; }
    public int SourceY { get; init; }
    public int CellWidth { get; init; }
    public int CellHeight { get; init; }
    public int AtlasColumns { get; init; }
    public int TerminalFrame { get; init; }
    public long FramePeriodMs { get; init; }

    /// <summary>Select the current atlas frame. TerminalFrame is the exclusive native upper bound,
    /// so SYSTEM4's value 12 addresses the twelve cells 0 through 11.</summary>
    public int FrameAt(long elapsedMs)
    {
        int frameCount = System.Math.Max(1, TerminalFrame);
        long period = System.Math.Max(1, FramePeriodMs);
        return (int)(System.Math.Max(0, elapsedMs) / period % frameCount);
    }

    public (int Column, int Row) AtlasCellAt(long elapsedMs)
    {
        int columns = System.Math.Max(1, AtlasColumns);
        int frame = FrameAt(elapsedMs);
        return (frame % columns, frame / columns);
    }
}
