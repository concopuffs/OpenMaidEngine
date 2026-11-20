using System.Collections.Generic;
using Ome.Engine.Devices;

namespace Ome.Engine.Model;

/// <summary>
/// The geometry of native's patterned surface reveals in the shipped draw mode
/// (<c>pattern_surface_transition_begin</c> and <c>_tick</c>: AGE 4.38D <c>0x43fce0</c> and <c>0x440510</c>, 4.33G
/// <c>0x43cdd0</c> and <c>0x43d600</c>): op 0x26's modes 0 to 4, op 0x27's 4 to 8 and op 0x28's 8 to 11. The tick
/// counts the interval timer's periods and draws the surface inside these rectangles over the frame that was there,
/// so the rest of the screen keeps the old frame (docs/engine-re.md, "The pattern reveals' geometry").
/// </summary>
/// <remarks>
/// <para>Modes 0 to 3 are one wipe from the left, right, top or bottom edge: the screen's width or height is cut into
/// <c>divisions</c> steps and one more step shows each period.</para>
/// <para>Modes 4 to 7 cut it into <c>divisions</c> strips, and every strip grows by one pixel a period from its own
/// left, right, top or bottom edge.</para>
/// <para>Modes 8 to 11 are the same strips started one period apart: the strip at the starting edge first.</para>
/// </remarks>
public static class LegacyPatternReveal
{
    /// <summary>The periods the reveal takes: after this many the tick reports it finished. 0 when
    /// <c>pattern_surface_transition_begin</c> does not begin (<see
    /// cref="LegacyScreenTransitionTiming.PatternBeginRefusal"/>).</summary>
    public static int Span(int width, int height, int divisions, SurfacePatternTransitionMode mode)
    {
        if (width <= 0 || height <= 0 || divisions <= 0) return 0;
        int number = (int)mode;
        if (number is < 0 or > 11) return 0;
        int extent = DividesWidth(number) ? width : height;
        if (extent % divisions != 0) return 0;
        int thickness = extent / divisions;
        return number <= 3 ? divisions : number <= 7 ? thickness : divisions - 1 + thickness;
    }

    /// <summary>The rectangles the surface shows in after <paramref name="periods"/> timer periods, into
    /// <paramref name="into"/> (cleared first). None before the first period or when the reveal does not
    /// begin.</summary>
    public static void Rects(int width, int height, int divisions, SurfacePatternTransitionMode mode, long periods,
                             List<FrameRect> into)
    {
        into.Clear();
        if (periods <= 0 || Span(width, height, divisions, mode) == 0) return;
        int number = (int)mode;
        bool alongX = DividesWidth(number);
        int extent = alongX ? width : height;
        int thickness = extent / divisions;
        bool fromFarEdge = (number & 1) != 0;   // the right or bottom edge
        if (number <= 3)
        {
            int shown = thickness * (int)System.Math.Min(periods, divisions);
            Add(into, alongX, fromFarEdge ? extent - shown : 0, fromFarEdge ? extent : shown, width, height);
            return;
        }
        for (int strip = 0; strip < divisions; strip++)
        {
            // Modes 8 to 11 keep a counter a strip, set to minus the strip's number when the reveal begins.
            long counter = number <= 7 ? periods : periods - strip;
            if (counter <= 0) continue;
            int shown = (int)System.Math.Min(counter, thickness);
            int edge = thickness * strip;
            if (fromFarEdge) Add(into, alongX, extent - edge - shown, extent - edge, width, height);
            else Add(into, alongX, edge, edge + shown, width, height);
        }
    }

    // Modes 0, 1, 4, 5, 8 and 9 divide the screen's width; the others its height.
    private static bool DividesWidth(int mode) => (mode & 2) == 0;

    private static void Add(List<FrameRect> into, bool alongX, int from, int to, int width, int height)
        => into.Add(alongX ? new FrameRect(from, 0, to, height) : new FrameRect(0, from, width, to));
}
