using Age.Engine.Model;
using Xunit;

public class SoftwareAffineRasterizerTests
{
    [Theory]
    [InlineData(-1, 2, false)]
    [InlineData(4, -1, false)]
    [InlineData(1, 1, true)]
    public void BlitRgba_IntegerTranslationFastPath_MatchesGeneralAffine(int tx, int ty, bool multiplyTint)
    {
        byte[] src = new byte[5 * 4 * 4];
        for (int i = 0; i < src.Length; i += 4)
        {
            src[i] = (byte)(20 + i % 211); src[i + 1] = (byte)(70 + i % 173);
            src[i + 2] = (byte)(130 + i % 107); src[i + 3] = (byte)(40 + i % 216);
        }
        byte[] expected = new byte[6 * 5 * 4];
        for (int i = 0; i < expected.Length; i++) expected[i] = (byte)(i * 17 + 3);
        byte[] actual = (byte[])expected.Clone();
        var translation = new Affine2D(1, 0, 0, 1, tx, ty);

        ReferenceBlit(expected, 6, 5, src, 5, 1, 1, 3, 2, translation,
                      0x4080c0, 0.35f, 0.6f, multiplyTint);
        SoftwareAffineRasterizer.BlitRgba(actual, 6, 5, src, 5, 4, 1, 1, 3, 2, translation,
                                          0x4080c0, 0.35f, 0.6f, multiplyTint);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(-2, 1)]
    [InlineData(4, -1)]
    [InlineData(1, 2)]
    public void FillRgba_IntegerTranslationFastPath_MatchesGeneralAffine(int tx, int ty)
    {
        byte[] expected = new byte[6 * 5 * 4];
        for (int i = 0; i < expected.Length; i++) expected[i] = (byte)(i * 11 + 5);
        byte[] actual = (byte[])expected.Clone();
        var translation = new Affine2D(1, 0, 0, 1, tx, ty);

        ReferenceFill(expected, 6, 5, 4, 3, translation, 0x9a4f21, 0.42f);
        SoftwareAffineRasterizer.FillRgba(actual, 6, 5, 4, 3, translation, 0x9a4f21, 0.42f);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BlitRgba_RotatesTwoPixelsClockwiseWithNearestSampling()
    {
        byte[] src = { 255,0,0,255, 0,255,0,255 };
        byte[] dst = new byte[4*4*4];
        var world = Transform2DMath.Build(
            new TransformState(1,1,1,0,0,0,1,1,0, 0,0,1,90));
        SoftwareAffineRasterizer.BlitRgba(dst,4,4,src,2,1,0,0,2,1,
            world.FromLocalOrigin(1,1),0xffffff,0,1);
        Assert.Equal(new byte[] {255,0,0,255}, dst[16..20]);
        Assert.Equal(new byte[] {0,255,0,255}, dst[32..36]);
    }

    [Fact]
    public void FillRgba_UsesAffineShapeRatherThanBoundingBox()
    {
        byte[] dst = new byte[5*5*4];
        var m = new Affine2D(1,0.5,-0.5,1,2,1);
        SoftwareAffineRasterizer.FillRgba(dst,5,5,2,2,m,0xff0000,1);
        int colored = 0;
        for(int i=3;i<dst.Length;i+=4) if(dst[i]!=0) colored++;
        Assert.InRange(colored, 3, 5);
        Assert.Equal(0, dst[(1*5+1)*4+3]);
    }

    [Fact]
    public void BlitRgba_MultiplyTintUsesD3dStyleRgbModulationAndOpacity()
    {
        byte[] src = { 200, 100, 50, 255 };
        byte[] dst = new byte[4];
        SoftwareAffineRasterizer.BlitRgba(dst, 1, 1, src, 1, 1, 0, 0, 1, 1,
            new Affine2D(1, 0, 0, 1, 0, 0), 0x80ff40, 0, 0.5f, multiplyTint: true);
        Assert.InRange(dst[0], 49, 50);
        Assert.InRange(dst[1], 49, 50);
        Assert.InRange(dst[2], 5, 7);
        Assert.InRange(dst[3], 126, 127);
    }

    [Fact]
    public void BlitRgba_WhiteModulationPreservesYellowAndZeroOpacityHidesIt()
    {
        byte[] src = { 240, 192, 16, 255 };
        byte[] visible = new byte[4];
        var identity = new Affine2D(1, 0, 0, 1, 0, 0);
        SoftwareAffineRasterizer.BlitRgba(visible, 1, 1, src, 1, 1, 0, 0, 1, 1,
            identity, 0xffffff, 0, 1, multiplyTint: true);
        Assert.Equal(new byte[] { 240, 192, 16, 255 }, visible);

        byte[] hidden = new byte[4];
        SoftwareAffineRasterizer.BlitRgba(hidden, 1, 1, src, 1, 1, 0, 0, 1, 1,
            identity, 0xffffff, 0, 0, multiplyTint: true);
        Assert.Equal(new byte[4], hidden);
    }

    [Fact]
    public void BlitRgba_AdditiveMakesBlackTransparentAndAddsScaledColor()
    {
        var identity = new Affine2D(1, 0, 0, 1, 0, 0);
        byte[] background = { 40, 50, 60, 255 };
        byte[] black = { 0, 0, 0, 255 };
        SoftwareAffineRasterizer.BlitRgba(background, 1, 1, black, 1, 1, 0, 0, 1, 1,
            identity, 0xffffff, 0, 1, multiplyTint: true, blend: BlendKind.Additive);
        Assert.Equal(new byte[] { 40, 50, 60, 255 }, background);

        byte[] blueGlow = { 16, 32, 200, 128 };
        SoftwareAffineRasterizer.BlitRgba(background, 1, 1, blueGlow, 1, 1, 0, 0, 1, 1,
            identity, 0xffffff, 0, 0.5f, multiplyTint: true, blend: BlendKind.Additive);
        Assert.Equal(new byte[] { 43, 57, 109, 255 }, background);
    }

    // Pre-fast-path affine algorithm retained here as an independent differential oracle.
    private static void ReferenceBlit(byte[] dst, int dstW, int dstH, byte[] src, int srcW,
                                      int srcX, int srcY, int width, int height, Affine2D transform,
                                      long tint, float tintStrength, float opacity, bool multiplyTint)
    {
        if (!transform.TryInverse(out var inv)) return;
        ReferenceBounds(transform, width, height, dstW, dstH, out int x0, out int y0, out int x1, out int y1);
        int istr = (int)(System.Math.Clamp(tintStrength, 0f, 1f) * 255);
        int ia = (int)(System.Math.Clamp(opacity, 0f, 1f) * 255);
        int tr = (int)(tint >> 16 & 255), tg = (int)(tint >> 8 & 255), tb = (int)(tint & 255);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
        {
            var p = inv.Apply(x + 0.5, y + 0.5);
            int u = (int)System.Math.Floor(p.X), v = (int)System.Math.Floor(p.Y);
            if ((uint)u >= (uint)width || (uint)v >= (uint)height) continue;
            int si = ((srcY + v) * srcW + (srcX + u)) * 4, di = (y * dstW + x) * 4;
            int sa = src[si + 3] * ia / 255;
            if (sa == 0) continue;
            int sr = multiplyTint ? src[si] * tr / 255 : (src[si] * (255 - istr) + tr * istr) / 255;
            int sg = multiplyTint ? src[si + 1] * tg / 255 : (src[si + 1] * (255 - istr) + tg * istr) / 255;
            int sb = multiplyTint ? src[si + 2] * tb / 255 : (src[si + 2] * (255 - istr) + tb * istr) / 255;
            ReferenceBlend(dst, di, sr, sg, sb, sa);
        }
    }

    private static void ReferenceFill(byte[] dst, int dstW, int dstH, int width, int height,
                                      Affine2D transform, long color, float opacity)
    {
        if (!transform.TryInverse(out var inv)) return;
        ReferenceBounds(transform, width, height, dstW, dstH, out int x0, out int y0, out int x1, out int y1);
        int a = (int)(System.Math.Clamp(opacity, 0f, 1f) * 255);
        int r = (int)(color >> 16 & 255), g = (int)(color >> 8 & 255), b = (int)(color & 255);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
        {
            var p = inv.Apply(x + 0.5, y + 0.5);
            if (p.X >= 0 && p.X < width && p.Y >= 0 && p.Y < height)
                ReferenceBlend(dst, (y * dstW + x) * 4, r, g, b, a);
        }
    }

    private static void ReferenceBounds(Affine2D m, int w, int h, int dw, int dh,
                                        out int x0, out int y0, out int x1, out int y1)
    {
        var a = m.Apply(0, 0); var b = m.Apply(w, 0); var c = m.Apply(0, h); var d = m.Apply(w, h);
        x0 = System.Math.Max(0, (int)System.Math.Floor(System.Math.Min(System.Math.Min(a.X, b.X), System.Math.Min(c.X, d.X))));
        y0 = System.Math.Max(0, (int)System.Math.Floor(System.Math.Min(System.Math.Min(a.Y, b.Y), System.Math.Min(c.Y, d.Y))));
        x1 = System.Math.Min(dw, (int)System.Math.Ceiling(System.Math.Max(System.Math.Max(a.X, b.X), System.Math.Max(c.X, d.X))));
        y1 = System.Math.Min(dh, (int)System.Math.Ceiling(System.Math.Max(System.Math.Max(a.Y, b.Y), System.Math.Max(c.Y, d.Y))));
    }

    private static void ReferenceBlend(byte[] dst, int i, int r, int g, int b, int a)
    {
        dst[i] = (byte)((r * a + dst[i] * (255 - a)) / 255);
        dst[i + 1] = (byte)((g * a + dst[i + 1] * (255 - a)) / 255);
        dst[i + 2] = (byte)((b * a + dst[i + 2] * (255 - a)) / 255);
        dst[i + 3] = (byte)System.Math.Min(255, dst[i + 3] + a);
    }
}
