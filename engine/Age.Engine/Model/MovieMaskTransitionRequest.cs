namespace Age.Engine.Model;

/// <summary>Opcode 0x24d's captured-range movie-mask transition. The movie's decoded green channel
/// becomes a byte-per-pixel alpha mask over SourceRange in the scratch surface.</summary>
public readonly record struct MovieMaskTransitionRequest(
    long CommandKey, int SurfaceSlot, long SourceRangeStart, int SourceRangeCount,
    int X, int Y, int Width, int Height, long Mode, long ResourceId,
    long StartDelayMs, long DurationMs);
