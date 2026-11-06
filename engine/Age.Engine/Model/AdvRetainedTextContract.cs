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
/// A glyph's retained record and the layout origin it is published at. Like
/// adv_text_publish_next_glyph@0x451220, publication crops the layout surface to the record's measured
/// cell; ink placed at the font's ascent stays inside it.
/// </summary>
public readonly record struct AdvRetainedGlyphPlacement(
    AdvRetainedGlyphRecord Record,
    int LayoutOriginX,
    int LayoutOriginY);

public readonly record struct AdvRetainedTextRunResult(
    int LayoutSlot,
    int FirstGlyphIndex,
    int GlyphCount,
    int CursorX,
    int CursorY)
{
    /// <summary>The run's drawn rows; empty when the host did not lay the text out.</summary>
    public IReadOnlyList<AdvTextRow> Rows { get; init; } = [];

    /// <summary>Op 0x196's ruby characters in drawing order, each with the row it was drawn over.</summary>
    public IReadOnlyList<AdvRubyGlyph> Ruby { get; init; } = [];
}

/// <summary>One drawn ruby character: its text, the base row it sits over, and its retained record.</summary>
public readonly record struct AdvRubyGlyph(int Row, string Text, AdvRetainedGlyphRecord Record);

/// <summary>
/// One drawn row of a show-text run: <see cref="GlyphCount"/> glyphs (one per Unicode scalar of the run's
/// text) from <see cref="FirstGlyph"/>, with rectangle edges from the first glyph's left/top to the last
/// glyph's right/bottom. <see cref="WrapsAfter"/> marks a horizontal wrap, which AGE records as a line
/// break (adv_text_build_glyph_records@0x4576c0).
/// </summary>
public readonly record struct AdvTextRow(
    int FirstGlyph, int GlyphCount, int Left, int Top, int Right, int Bottom, bool WrapsAfter);

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
