namespace Age.Engine.Model;

[Flags]
public enum AdvTextOverflowFlags
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
}

/// <summary>
/// Native AGE's 20-byte live ADV glyph record. Rectangle values are edges, matching the
/// retained-object bind ABI; they are not x/y/width/height values.
/// </summary>
public readonly record struct AdvRetainedGlyphRecord(
    int PublicationChainFlag,
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool PublishesNextInSameTick => PublicationChainFlag == 1;
}

/// <summary>The script-owned retained-object binding associated with one live ADV layout.</summary>
public readonly record struct AdvTextLayoutPresentationBinding(
    int LayoutSlot,
    int SourceSurfaceSlot,
    long FirstObjectHandle,
    long ObjectCapacity,
    long WaitIndicatorObjectHandle,
    int ResetCursorX,
    int ResetCursorY);

/// <summary>
/// Transient publication crop. Native records retain measured cell edges, but a backend mask/effect can
/// carry ink outside that cell. Keeping the expanded crop separate preserves the native layout ABI.
/// </summary>
public readonly record struct AdvRetainedGlyphPresentationRect(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public readonly record struct AdvRetainedGlyphPlacement(
    AdvRetainedGlyphRecord Record,
    AdvRetainedGlyphPresentationRect PresentationRect,
    int LayoutOriginX,
    int LayoutOriginY);

public readonly record struct AdvRetainedTextRunResult(
    int LayoutSlot,
    int FirstGlyphIndex,
    int GlyphCount,
    int CursorX,
    int CursorY);

/// <summary>Platform-neutral rules established from AGE's native retained-glyph workers.</summary>
public static class AdvRetainedTextContract
{
    public static AdvTextOverflowFlags CheckOverflow(
        int rightBound, int bottomBound, int glyphRight, int glyphBottom)
    {
        var result = AdvTextOverflowFlags.None;
        if (rightBound < glyphRight) result |= AdvTextOverflowFlags.Horizontal;
        if (bottomBound < glyphBottom) result |= AdvTextOverflowFlags.Vertical;
        return result;
    }

    /// <summary>
    /// CP932 closing punctuation which AGE keeps on the preceding horizontal line even when the
    /// current glyph crosses the right bound.
    /// </summary>
    public static bool PreventsHorizontalWrapBefore(ushort cp932)
        => cp932 is 0x8141 or 0x8142 or 0x8176; // 、 。 」

    public static bool TimedPublicationHandleExists(int revealIndex, long objectCapacity)
        => revealIndex >= 0 && revealIndex < objectCapacity;
}
