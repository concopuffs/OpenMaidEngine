using Ome.Engine.Devices;

namespace Ome.Engine.Model;

// The triangles of a polygon item (FramePolygonGeometry): what the reference renderer draws for one, and so what the
// item means. Native AGE 4.38D draws a polygon as pre-transformed Direct3D 9 vertices (gfx_draw_polygon_primitive
// @0x4895b0; docs/superpowers/specs/2026-11-19-polygon-objects-design.md §2), whose rules these are: a pixel is
// covered when its centre is inside the triangle, with the top-left rule on an edge; colours and texture coordinates
// are interpolated linearly; a texture is sampled with clamped coordinates and bilinear filtering.
public static partial class SoftwareAffineRasterizer
{
    // Vertex positions are snapped to 1/256 of a pixel, so that the edge tests are exact integers.
    private const int SubpixelBits = 8;
    private const long SubpixelOne = 1L << SubpixelBits;

    /// <summary>
    /// Draw <paramref name="vertices"/>, a triangle list in destination pixels, into <paramref name="dst"/>.
    /// <paramref name="first"/> and <paramref name="second"/> are the polygon's textures as RGBA8 pixels with their
    /// colour keys already applied, or null; <paramref name="opacity"/> scales the source alpha.
    /// </summary>
    public static void DrawPolygonRgba(byte[] dst, int dstW, int dstH, FramePolygonVertex[] vertices,
                                       FramePolygonBlend blend, float opacity,
                                       byte[]? first, int firstW, int firstH,
                                       byte[]? second, int secondW, int secondH)
    {
        int ia = (int)(System.Math.Clamp(opacity, 0f, 1f) * 255);
        if (ia == 0 && blend != FramePolygonBlend.Replace) return;
        if (first == null || firstW <= 0 || firstH <= 0)
        {
            // Native binds the textures it finds to stages in order: a second texture without a first is the first.
            first = second; firstW = secondW; firstH = secondH;
            second = null;
        }
        if (second != null && (secondW <= 0 || secondH <= 0)) second = null;
        if (first != null && (firstW <= 0 || firstH <= 0)) first = null;
        for (int index = 0; index + 2 < vertices.Length; index += 3)
            DrawTriangle(dst, dstW, dstH, vertices[index], vertices[index + 1], vertices[index + 2], blend, ia,
                         first, firstW, firstH, second, secondW, secondH);
    }

    private static void DrawTriangle(byte[] dst, int dstW, int dstH,
                                     FramePolygonVertex a, FramePolygonVertex b, FramePolygonVertex c,
                                     FramePolygonBlend blend, int ia,
                                     byte[]? first, int firstW, int firstH,
                                     byte[]? second, int secondW, int secondH)
    {
        long ax = Snap(a.X), ay = Snap(a.Y), bx = Snap(b.X), by = Snap(b.Y), cx = Snap(c.X), cy = Snap(c.Y);
        long area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (area == 0) return;
        if (area < 0)
        {
            // Both windings are drawn: put the triangle the way the edge tests expect.
            (b, c) = (c, b);
            (bx, cx) = (cx, bx);
            (by, cy) = (cy, by);
            area = -area;
        }

        int x0 = (int)System.Math.Max(0, System.Math.Min(ax, System.Math.Min(bx, cx)) >> SubpixelBits);
        int y0 = (int)System.Math.Max(0, System.Math.Min(ay, System.Math.Min(by, cy)) >> SubpixelBits);
        int x1 = (int)System.Math.Min(dstW - 1L, System.Math.Max(ax, System.Math.Max(bx, cx)) >> SubpixelBits);
        int y1 = (int)System.Math.Min(dstH - 1L, System.Math.Max(ay, System.Math.Max(by, cy)) >> SubpixelBits);
        if (x1 < x0 || y1 < y0) return;

        // Edge functions, each zero on its edge and positive inside. A pixel centre on an edge belongs to the
        // triangle only when the edge is a top or a left one, so two triangles that share an edge never both draw it.
        long biasAb = TopLeft(bx - ax, by - ay) ? 0 : -1;
        long biasBc = TopLeft(cx - bx, cy - by) ? 0 : -1;
        long biasCa = TopLeft(ax - cx, ay - cy) ? 0 : -1;
        double inverseArea = 1.0 / area;

        uint ca = a.Argb, cb = b.Argb, cc = c.Argb;
        for (int y = y0; y <= y1; y++)
        {
            long py = ((long)y << SubpixelBits) + SubpixelOne / 2;
            for (int x = x0; x <= x1; x++)
            {
                long px = ((long)x << SubpixelBits) + SubpixelOne / 2;
                long ab = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
                long bc = (cx - bx) * (py - by) - (cy - by) * (px - bx);
                long ca2 = (ax - cx) * (py - cy) - (ay - cy) * (px - cx);
                if (ab + biasAb < 0 || bc + biasBc < 0 || ca2 + biasCa < 0) continue;

                // Each vertex's weight is the edge function of the edge opposite it.
                double wa = bc * inverseArea, wb = ca2 * inverseArea, wc = ab * inverseArea;
                int alpha = Channel(ca, cb, cc, 24, wa, wb, wc);
                int red = Channel(ca, cb, cc, 16, wa, wb, wc);
                int green = Channel(ca, cb, cc, 8, wa, wb, wc);
                int blue = Channel(ca, cb, cc, 0, wa, wb, wc);

                if (first != null)
                {
                    double u = a.U * wa + b.U * wb + c.U * wc, v = a.V * wa + b.V * wb + c.V * wc;
                    SampleClamped(first, firstW, firstH, u, v, out int tr, out int tg, out int tb, out int ta);
                    if (second != null)
                    {
                        // Stage 1 blends over stage 0 by its own alpha, colour and alpha alike.
                        SampleClamped(second, secondW, secondH, u, v, out int sr, out int sg, out int sb, out int sa);
                        tr = (sr * sa + tr * (255 - sa)) / 255;
                        tg = (sg * sa + tg * (255 - sa)) / 255;
                        tb = (sb * sa + tb * (255 - sa)) / 255;
                        ta = (sa * sa + ta * (255 - sa)) / 255;
                    }
                    red = tr * red / 255;
                    green = tg * green / 255;
                    blue = tb * blue / 255;
                    alpha = ta * alpha / 255;
                }

                BlendPolygon(dst, (y * dstW + x) * 4, red, green, blue, alpha * ia / 255, blend);
            }
        }
    }

    private static long Snap(float coordinate)
    {
        double scaled = System.Math.Round((double)coordinate * SubpixelOne);
        // Far outside any canvas: keep the products of two differences inside 64 bits.
        return (long)System.Math.Clamp(scaled, -(double)(1L << 30), 1L << 30);
    }

    // For a triangle whose inside is on the positive side of its edges, in pixel coordinates with y down: a top edge
    // runs right along a row, and a left edge runs up.
    private static bool TopLeft(long dx, long dy) => dy < 0 || (dy == 0 && dx > 0);

    private static int Channel(uint a, uint b, uint c, int shift, double wa, double wb, double wc)
    {
        double value = ((a >> shift) & 0xff) * wa + ((b >> shift) & 0xff) * wb + ((c >> shift) & 0xff) * wc;
        return value <= 0 ? 0 : value >= 255 ? 255 : (int)(value + 0.5);
    }

    // A texture at (u, v), where 0 and 1 are its edges: the four texels around the point, taps clamped to the image.
    private static void SampleClamped(byte[] texture, int width, int height, double u, double v,
                                      out int r, out int g, out int b, out int a)
    {
        Tap(u * width, width, out int xa, out int xb, out int wx);
        Tap(v * height, height, out int ya, out int yb, out int wy);
        Sample(texture, width, xa, xb, wx, ya, yb, wy, out r, out g, out b, out a);
    }

    private static void BlendPolygon(byte[] d, int i, int r, int g, int b, int a, FramePolygonBlend blend)
    {
        switch (blend)
        {
            case FramePolygonBlend.Replace:
                d[i] = (byte)r; d[i + 1] = (byte)g; d[i + 2] = (byte)b; d[i + 3] = (byte)a;
                return;
            case FramePolygonBlend.Additive:
                if (a != 0) Blend(d, i, r, g, b, a, BlendKind.Additive);
                return;
            case FramePolygonBlend.Subtractive:
                if (a == 0) return;
                d[i] = (byte)System.Math.Max(0, d[i] - r * a / 255);
                d[i + 1] = (byte)System.Math.Max(0, d[i + 1] - g * a / 255);
                d[i + 2] = (byte)System.Math.Max(0, d[i + 2] - b * a / 255);
                return;
            default:
                if (a != 0) Blend(d, i, r, g, b, a);
                return;
        }
    }
}
