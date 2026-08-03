namespace Age.Engine.Model;

public readonly record struct AdvWaitIndicatorConfig(
    int LayoutSlot, int X, int Y, int SurfaceSlot,
    int SourceX, int SourceY, int CellWidth, int CellHeight,
    int TerminalFrame, long FramePeriodMs)
{
    /// <summary>Select the current atlas frame. TerminalFrame is the exclusive native upper bound,
    /// so SYSTEM4's value 12 addresses the twelve cells 0 through 11.</summary>
    public int FrameAt(long elapsedMs)
    {
        int frameCount = System.Math.Max(1, TerminalFrame);
        long period = System.Math.Max(1, FramePeriodMs);
        return (int)(System.Math.Max(0, elapsedMs) / period % frameCount);
    }
}
