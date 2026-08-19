namespace Age.Engine.Model;

/// <summary>The sampled native one-shot channels carried to the compositor: op 0x21e scale, op 0x21f
/// axis-angle rotation, and op 0x220 translation. Z is retained through full 4x4 composition.</summary>
public readonly record struct TransformState(double ScaleX, double ScaleY, double ScaleZ,
                                             double TranslateX, double TranslateY, double TranslateZ,
                                             double AnchorX, double AnchorY, double AnchorZ,
                                             double RotationAxisX = 0, double RotationAxisY = 0,
                                             double RotationAxisZ = 0, double RotationAngleDegrees = 0);

public readonly record struct RotationCycleState(bool Enabled, long PeriodMs,
                                                 double AxisX, double AxisY, double AxisZ,
                                                 double AngleDegrees = 0);

public readonly record struct ScaleCycleState(bool Enabled, long PeriodMs,
                                              double ScaleX, double ScaleY, double ScaleZ);

[System.Flags]
public enum GfxPresentationReason
{
    None = 0,
    RetainedMutation = 1,
    ContinuousChannel = 2,
    DiscreteSourceCell = 4,
}

/// <summary>Sampled op-0x223 type-0 surface transition. Range A is already present in normal z-order;
/// the compositor draws range B over it with <paramref name="Progress"/> to form the native crossfade.</summary>
public readonly record struct SurfaceTransitionState(long CommandKey, int TargetSlot,
    long RangeAStart, int RangeACount, long RangeBStart, int RangeBCount,
    long DelayMs, long DurationMs, long StartMs, double Progress, bool Forced);

/// <summary>Sampled SYS4433 op-0x251 radial-blur command. The selected retained-object range is
/// rendered through a center/length post-effect at the command handle's z position.</summary>
public readonly record struct RadialBlurRangeTransitionState(
    long CommandKey, int TargetSlot, long RangeStart, int RangeCount,
    double CenterX, double CenterY, double Length,
    long DelayMs, long DurationMs, long StartMs, double Progress, bool Forced)
{
    public bool Contains(long handle)
        => RangeCount > 0 && handle >= RangeStart &&
           (ulong)(handle - RangeStart) < (ulong)RangeCount;
}

public readonly record struct MovieMaskTransitionState(
    MovieMaskTransitionRequest Request, bool Completed);

/// <summary>One synchronized sample of op 0x202's native one-shot packed-color channel.</summary>
public readonly record struct ColorTransitionState(long Current, long Target,
    long DelayMs, long DurationMs, long StartMs, double Progress, bool Active);

/// <summary>One EngineCtx numeric-glyph style registered by opcode 0x13a or SYS4433 opcode 0x2da.</summary>
public readonly record struct NumericGlyphStyle(int SurfaceSlot, int AtlasX, int AtlasY,
                                                int DigitWidth, int DigitHeight,
                                                int SourceCellSpacing, int DestinationCellSpacing)
{
    public bool Registered => SurfaceSlot != 0;
}

public sealed record BlockingGfxObjectDiagnostic(
    long Handle, int SourceSlot, long StartMs, long ControlFlags,
    bool ColorEnabled, long ColorDelayMs, long ColorDurationMs,
    bool ScaleEnabled, long ScaleDelayMs, long ScaleDurationMs,
    bool RotationEnabled, long RotationDelayMs, long RotationDurationMs,
    bool TranslationEnabled, long TranslationDelayMs, long TranslationDurationMs);

public sealed record GfxDiagnosticSnapshot(
    long NowMs, bool HasActiveTimedPresentation, int ObjectCount, int VisibleObjectCount,
    int ActiveSurfaceTransitionCount, long AnimationServiceFlags,
    long AnimClockDurationTicks, long AnimClockGeneration,
    uint PreviousFrameTimeMilliseconds, uint CurrentFrameTimeMilliseconds,
    long RangeTransformFirst, long RangeTransformCount,
    BlockingGfxObjectDiagnostic? BlockingRangeTransform,
    IReadOnlyList<BlockingGfxObjectDiagnostic> BlockingObjects);

public readonly record struct GfxSurfacePersistenceState(
    int Slot, long ResourceId, long ColorKey, bool Created, bool ReloadOnRestore);

public sealed record GfxPersistenceSnapshot(
    IReadOnlyList<GfxSurfacePersistenceState> Surfaces,
    IReadOnlyList<(long Handle, GfxState.GfxObject Object)> Objects,
    long RangeFirst,
    long RangeCount,
    GfxState.GfxObject RangeTransform);

/// <summary>A renderable view of one visible gfx object — the host composites these in ascending-handle order
/// (= the engine's z-order) each frame. Built by <see cref="GfxState.SnapshotVisibleObjects"/>; the surface
/// resId/colorkey are resolved from the object's live source slot at snapshot time (see docs/engine-re.md,
/// "The full gfx render model").</summary>
/// <summary>The packed-color channel is mode-dependent. Textured mode 0 ignores packed alpha and
/// multiplicatively modulates by <paramref name="Tint"/>; surfaceless mode 0 uses <paramref name="TintStrength"/>.
/// A mode-0 color which has passed through op 0x202, and mode 1, use <paramref name="Alpha"/> as opacity.
/// <paramref name="MultiplyTint"/> selects the latter compositor path.</summary>
public readonly record struct RenderObject(long Handle, long SurfaceResId, long ColorKey,
                                           int SrcX, int SrcY, int W, int H, int DstX, int DstY,
                                           TransformState Transform, RotationCycleState Rotation,
                                           int Alpha, long Tint, int TintStrength, BlendKind Blend,
                                           bool MultiplyTint,
                                           SurfaceTransitionState? SurfaceTransition = null,
                                           ColorTransitionState? ColorTransition = null,
                                           Affine2D? RangeTransform = null,
                                           bool TimeVarying = false,
                                           ScaleCycleState ScaleCycle = default,
                                           RadialBlurRangeTransitionState? RadialBlurTransition = null);

/// <summary>The retained handle interval selected by an op-0x222 backbuffer publication.</summary>
public readonly record struct GfxHandleRange(long First, long Count)
{
    public static GfxHandleRange All => new(0, long.MaxValue);

    public bool Contains(long handle)
        => Count > 0 && handle >= First && (ulong)(handle - First) < (ulong)Count;
}
