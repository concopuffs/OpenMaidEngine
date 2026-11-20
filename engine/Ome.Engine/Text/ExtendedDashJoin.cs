using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Text;

/// <summary>
/// How AGE 4.38D joins consecutive dashes (docs/engine-re.md, "Kamidori against Arterial, triaged"). After its glyph
/// drawers (text_draw_glyph_string_plain@0x4692f0, text_draw_glyph_string_effect@0x46b1d0) draw a character whose
/// Shift-JIS code is 0xF042 or 0xF043, they copy a strip of the text surface onto the same surface with
/// gfx_copy_surface_rect@0x48d2f0, op 0x207's worker. With <c>x</c> the glyph's draw position, <c>y</c> the string's
/// top, <c>bbw</c> the glyph's black box width, <c>(ex, ey)</c> the style's effect offset, <c>size</c> the primary
/// font size and <c>next</c> the pen after the glyph's advance:
/// <list type="bullet">
/// <item>the strip is <c>[x + ex, x + bbw - 2)</c> wide and <c>[y, y + size + 2 * ey)</c> tall: the dash's ink
/// without its last two columns;</item>
/// <item>both codes copy it so that it ends at <c>x + 1</c>, which draws the dash again to the left, over the gap
/// before it;</item>
/// <item>0xF042 then copies it so that it ends at <c>next + ex</c>, which carries the dash to where the next glyph
/// starts.</item>
/// </list>
/// The worker clips the destination rectangle to the surface and moves the source by as much, then the source
/// likewise, and copies nothing when what is left is empty. It changes the caller's rectangles, and the second copy
/// starts from the first copy's clipped source, as here. A build that draws the user-defined area with its own face
/// (<see cref="TextRules.ExtendedGlyphFace"/>) joins; the others have no such code.
/// </summary>
public static class ExtendedDashJoin
{
    /// <summary>The dash that joins the glyph before it and the glyph after it.</summary>
    public const ushort BothSides = 0xf042;

    /// <summary>The dash that joins the glyph before it.</summary>
    public const ushort LeftOnly = 0xf043;

    public static bool Applies(ushort? code) => code is BothSides or LeftOnly;

    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>Join <paramref name="request"/>'s glyph, just drawn into <paramref name="surface"/> at
    /// (<paramref name="drawX"/>, <paramref name="top"/>) in <paramref name="style"/>, when it is one of the two
    /// dashes.</summary>
    public static void AfterGlyph(RgbaImage surface, GlyphRasterRequest request, GlyphMask mask, int drawX, int top,
                                  int nextPenX, AdvTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mask);
        if (request.Cp932Code is not { } code || !Applies(code)) return;
        Apply(surface, code, drawX, top, nextPenX, mask.Width,
              style.PrimaryFontSize > 0 ? style.PrimaryFontSize : request.PixelHeight,
              style.EffectOffsetX, style.EffectOffsetY);
    }

    /// <summary>Join the dash just drawn into <paramref name="surface"/>.</summary>
    /// <param name="drawX">The x the glyph was drawn at, before its own origin.</param>
    /// <param name="top">The string's y: the top of the glyph's cell.</param>
    /// <param name="nextPenX">The pen after the glyph's advance.</param>
    /// <param name="blackBoxWidth">The glyph's <c>gmBlackBoxX</c>.</param>
    /// <param name="fontSize">The primary font size (text manager +0x313d4).</param>
    public static void Apply(RgbaImage surface, ushort code, int drawX, int top, int nextPenX, int blackBoxWidth,
                             int fontSize, int effectOffsetX, int effectOffsetY)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!Applies(code)) return;
        unchecked
        {
            var source = new Rect
            {
                Left = drawX + effectOffsetX,
                Top = top,
                Right = drawX + blackBoxWidth - 2,
                Bottom = top + fontSize + effectOffsetY * 2,
            };
            var destination = new Rect
            {
                Left = drawX + 1 - (source.Right - source.Left),
                Top = top,
                Right = drawX + 1,
                Bottom = source.Bottom,
            };
            Copy(surface, ref source, ref destination);
            if (code != BothSides) return;
            destination.Right = nextPenX + effectOffsetX;
            destination.Left = source.Left + (destination.Right - source.Right);
            Copy(surface, ref source, ref destination);
        }
    }

    // gfx_copy_surface_rect's clip and copy for one surface whose bounds are (0, 0, width, height).
    private static void Copy(RgbaImage surface, ref Rect source, ref Rect destination)
    {
        unchecked
        {
            Clip(surface, ref destination, ref source);
            Clip(surface, ref source, ref destination);
        }
        if (source.Right <= source.Left || source.Bottom <= source.Top) return;
        RgbaSurfaceOps.CopyRect(surface, surface, source.Left, source.Top, source.Right - source.Left,
                                source.Bottom - source.Top, destination.Left, destination.Top);
    }

    private static void Clip(RgbaImage surface, ref Rect clipped, ref Rect other)
    {
        if (clipped.Left < 0)
        {
            other.Left += -clipped.Left;
            clipped.Left = 0;
        }
        if (clipped.Top < 0)
        {
            other.Top += -clipped.Top;
            clipped.Top = 0;
        }
        if (surface.Width < clipped.Right)
        {
            other.Right += surface.Width - clipped.Right;
            clipped.Right = surface.Width;
        }
        if (surface.Height < clipped.Bottom)
        {
            other.Bottom += surface.Height - clipped.Bottom;
            clipped.Bottom = surface.Height;
        }
    }
}
