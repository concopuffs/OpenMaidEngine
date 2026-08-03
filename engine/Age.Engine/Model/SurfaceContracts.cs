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
