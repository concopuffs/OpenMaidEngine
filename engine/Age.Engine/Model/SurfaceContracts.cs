namespace Age.Engine.Model;

public readonly record struct SurfaceRectFill(
    int SurfaceSlot, int X, int Y, int Width, int Height, int Alpha, long Rgb);

public readonly record struct SurfaceRectCopy(
    int SourceSurface, int DestinationSurface, int SourceX, int SourceY,
    int Width, int Height, int DestinationX, int DestinationY);

public enum SurfaceBlackFadeDirection
{
    FromBlack,
    ToBlack,
}

public enum SurfacePatternTransitionMode
{
    WipeLeftToRight = 0,
    WipeRightToLeft = 1,
    WipeTopToBottom = 2,
    WipeBottomToTop = 3,
    VerticalStripsLeftToRight = 4,
    VerticalStripsRightToLeft = 5,
    HorizontalStripsTopToBottom = 6,
    HorizontalStripsBottomToTop = 7,
    StaggeredVerticalStripsLeftToRight = 8,
    StaggeredVerticalStripsRightToLeft = 9,
    StaggeredHorizontalStripsTopToBottom = 10,
    StaggeredHorizontalStripsBottomToTop = 11,
}

public readonly record struct SurfacePatternTransitionRequest(
    int Surface, long IntervalMilliseconds, int Divisions, SurfacePatternTransitionMode Mode);
