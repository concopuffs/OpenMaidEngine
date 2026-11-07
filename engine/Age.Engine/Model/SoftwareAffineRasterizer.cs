namespace Age.Engine.Model;

/// <summary>
/// Inverse-mapped RGBA8 affine compositor used by the Godot host and pure tests. It samples as native AGE's D3D9
/// sprite does (Himegari gfx_object_blit_d3d9@0x4774c0, SYS4433 gfx_draw_surface_through_sprite@0x47dac0; docs/
/// engine-re.md, "Object sampling"): a destination pixel is covered when its centre maps inside the source
/// rectangle, and takes the bilinear blend of the four texels around that point, with 8-bit weights and
/// coordinates clamped to the source image's edge (D3DTADDRESS_CLAMP), not to the rectangle. An integer
/// translation puts every pixel centre on a texel centre, so it copies texels exactly.
/// </summary>
public static class SoftwareAffineRasterizer
{
    /// <summary>The bilinear weights' resolution: 8 bits of subtexel position, as D3D9 hardware filters.</summary>
    private const int WeightOne = 256;

    public static void BlitRgba(byte[] dst, int dstW, int dstH, byte[] src, int srcW, int srcH,
                                int srcX, int srcY, int width, int height, Affine2D localToDest,
                                long tint, float tintStrength, float opacity, bool multiplyTint = false,
                                BlendKind blend = BlendKind.Alpha)
    {
        if (width <= 0 || height <= 0) return;
        int istr = (int)(System.Math.Clamp(tintStrength, 0f, 1f) * 255);
        int ia = (int)(System.Math.Clamp(opacity, 0f, 1f) * 255);
        if (ia == 0) return;
        int tr=(int)(tint>>16&255), tg=(int)(tint>>8&255), tb=(int)(tint&255);
        bool unmodulatedSourceOver = ia == 255 && istr == 0 && !multiplyTint && blend != BlendKind.Additive;
        if (TryIntegerTranslation(localToDest, out int tx, out int ty))
        {
            BlitTranslated(dst, dstW, dstH, src, srcW, srcX, srcY, width, height,
                           tx, ty, tr, tg, tb, istr, ia, multiplyTint, blend, unmodulatedSourceOver);
            return;
        }
        if (!localToDest.TryInverse(out var inv)) return;
        Bounds(localToDest, width, height, dstW, dstH, out int x0, out int y0, out int x1, out int y1);
        if (x1 <= x0 || y1 <= y0) return;
        if (localToDest.XY == 0 && localToDest.YX == 0 && x1 - x0 <= 4096)
        {
            BlitAxisAligned(dst, dstW, src, srcW, srcH, srcX, srcY, width, height, inv,
                            x0, y0, x1, y1, tr, tg, tb, istr, ia, multiplyTint, blend,
                            unmodulatedSourceOver);
            return;
        }
        for (int y=y0; y<y1; y++) for (int x=x0; x<x1; x++)
        {
            var p = inv.Apply(x + 0.5, y + 0.5);
            if (!(p.X >= 0 && p.X < width && p.Y >= 0 && p.Y < height)) continue;
            Tap(srcX + p.X, srcW, out int xa, out int xb, out int wx);
            Tap(srcY + p.Y, srcH, out int ya, out int yb, out int wy);
            Sample(src, srcW, xa, xb, wx, ya, yb, wy, out int r, out int g, out int b, out int a);
            BlendSample(dst, (y*dstW+x)*4, r, g, b, a, tr, tg, tb, istr, ia, multiplyTint, blend,
                        unmodulatedSourceOver);
        }
    }

    public static void FillRgba(byte[] dst, int dstW, int dstH, int width, int height,
                                Affine2D localToDest, long color, float opacity)
    {
        if (width <= 0 || height <= 0) return;
        int a=(int)(System.Math.Clamp(opacity,0f,1f)*255); if(a==0)return;
        int r=(int)(color>>16&255),g=(int)(color>>8&255),b=(int)(color&255);
        if (TryIntegerTranslation(localToDest, out int tx, out int ty))
        {
            FillTranslated(dst, dstW, dstH, width, height, tx, ty, r, g, b, a);
            return;
        }
        if (!localToDest.TryInverse(out var inv)) return;
        Bounds(localToDest,width,height,dstW,dstH,out int x0,out int y0,out int x1,out int y1);
        if (x1 <= x0 || y1 <= y0) return;
        if (localToDest.XY == 0 && localToDest.YX == 0 && x1 - x0 <= 4096)
        {
            FillAxisAligned(dst, dstW, width, height, inv, x0, y0, x1, y1, r, g, b, a);
            return;
        }
        for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){
            var p=inv.Apply(x+.5,y+.5);
            if(p.X>=0&&p.X<width&&p.Y>=0&&p.Y<height) Blend(dst,(y*dstW+x)*4,r,g,b,a);
        }
    }

    /// <summary>True when the transform is a pure integer translation, which maps every pixel centre onto a texel
    /// centre: native's bilinear filter then returns each texel unchanged, and a nearest lookup is exact.</summary>
    public static bool IsIntegerTranslation(Affine2D m) => TryIntegerTranslation(m, out _, out _);

    private static bool TryIntegerTranslation(Affine2D m, out int tx, out int ty)
    {
        tx = ty = 0;
        if (m.XX != 1 || m.XY != 0 || m.YX != 0 || m.YY != 1 ||
            m.TX != System.Math.Truncate(m.TX) || m.TY != System.Math.Truncate(m.TY) ||
            m.TX < int.MinValue || m.TX > int.MaxValue || m.TY < int.MinValue || m.TY > int.MaxValue)
            return false;
        tx = (int)m.TX;
        ty = (int)m.TY;
        return true;
    }

    /// <summary>
    /// One axis of a bilinear tap. <paramref name="position"/> is the sample point in source-image texel units,
    /// whose texel centres lie at k + 0.5. Returns the two texel indices (clamped to the image) and the second
    /// one's weight out of <see cref="WeightOne"/>, rounded to the nearest 1/256 as the hardware's 8-bit subtexel
    /// position is.
    /// </summary>
    private static void Tap(double position, int size, out int first, out int second, out int weight)
    {
        double centred = position - 0.5;
        double floor = System.Math.Floor(centred);
        int index = (int)floor;
        weight = (int)System.Math.Round((centred - floor) * WeightOne);
        int last = size - 1;
        first = index < 0 ? 0 : index > last ? last : index;
        int next = index + 1;
        second = next < 0 ? 0 : next > last ? last : next;
    }

    private static void Sample(byte[] src, int srcW, int xa, int xb, int wx, int ya, int yb, int wy,
                               out int r, out int g, out int b, out int a)
    {
        int i00 = (ya * srcW + xa) * 4, i10 = (ya * srcW + xb) * 4;
        int i01 = (yb * srcW + xa) * 4, i11 = (yb * srcW + xb) * 4;
        int ux = WeightOne - wx, uy = WeightOne - wy;
        int w00 = ux * uy, w10 = wx * uy, w01 = ux * wy, w11 = wx * wy;
        const int half = WeightOne * WeightOne / 2;
        r = (src[i00] * w00 + src[i10] * w10 + src[i01] * w01 + src[i11] * w11 + half) >> 16;
        g = (src[i00 + 1] * w00 + src[i10 + 1] * w10 + src[i01 + 1] * w01 + src[i11 + 1] * w11 + half) >> 16;
        b = (src[i00 + 2] * w00 + src[i10 + 2] * w10 + src[i01 + 2] * w01 + src[i11 + 2] * w11 + half) >> 16;
        a = (src[i00 + 3] * w00 + src[i10 + 3] * w10 + src[i01 + 3] * w01 + src[i11 + 3] * w11 + half) >> 16;
    }

    private static void BlendSample(byte[] dst, int di, int r, int g, int b, int a,
                                    int tr, int tg, int tb, int istr, int ia, bool multiplyTint,
                                    BlendKind blend, bool unmodulatedSourceOver)
    {
        if (unmodulatedSourceOver)
        {
            BlendUnmodulatedSourceOver(dst, di, r, g, b, a);
            return;
        }
        int sa = a * ia / 255;
        if (sa == 0) return;
        int sr = multiplyTint ? r * tr / 255 : (r * (255 - istr) + tr * istr) / 255;
        int sg = multiplyTint ? g * tg / 255 : (g * (255 - istr) + tg * istr) / 255;
        int sb = multiplyTint ? b * tb / 255 : (b * (255 - istr) + tb * istr) / 255;
        Blend(dst, di, sr, sg, sb, sa, blend);
    }

    private static void BlitTranslated(byte[] dst, int dstW, int dstH, byte[] src, int srcW,
                                       int srcX, int srcY, int width, int height, int tx, int ty,
                                       int tr, int tg, int tb, int istr, int ia, bool multiplyTint,
                                       BlendKind blend, bool unmodulatedSourceOver)
    {
        int x0 = System.Math.Max(0, tx), y0 = System.Math.Max(0, ty);
        int x1 = (int)System.Math.Min(dstW, (long)tx + width);
        int y1 = (int)System.Math.Min(dstH, (long)ty + height);
        if (unmodulatedSourceOver)
        {
            BlitTranslatedUnmodulated(dst, dstW, src, srcW, srcX, srcY, tx, ty, x0, y0, x1, y1);
            return;
        }
        for (int y = y0; y < y1; y++)
        {
            int v = y - ty;
            for (int x = x0; x < x1; x++)
            {
                int u = x - tx;
                int si = ((srcY + v) * srcW + (srcX + u)) * 4, di = (y * dstW + x) * 4;
                int sa = src[si + 3] * ia / 255;
                if (sa == 0) continue;
                int sr = multiplyTint ? src[si] * tr / 255 : (src[si] * (255 - istr) + tr * istr) / 255;
                int sg = multiplyTint ? src[si + 1] * tg / 255 : (src[si + 1] * (255 - istr) + tg * istr) / 255;
                int sb = multiplyTint ? src[si + 2] * tb / 255 : (src[si + 2] * (255 - istr) + tb * istr) / 255;
                Blend(dst, di, sr, sg, sb, sa, blend);
            }
        }
    }

    private static void BlitTranslatedUnmodulated(byte[] dst, int dstW, byte[] src, int srcW,
                                                   int srcX, int srcY, int tx, int ty,
                                                   int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        {
            int sourceRow = (srcY + y - ty) * srcW + srcX - tx;
            int destinationRow = y * dstW;
            for (int x = x0; x < x1; x++)
            {
                int si = (sourceRow + x) * 4;
                BlendUnmodulatedSourceOver(dst, (destinationRow + x) * 4,
                                           src[si], src[si + 1], src[si + 2], src[si + 3]);
            }
        }
    }

    private static void FillTranslated(byte[] dst, int dstW, int dstH, int width, int height,
                                       int tx, int ty, int r, int g, int b, int a)
    {
        int x0 = System.Math.Max(0, tx), y0 = System.Math.Max(0, ty);
        int x1 = (int)System.Math.Min(dstW, (long)tx + width);
        int y1 = (int)System.Math.Min(dstH, (long)ty + height);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                Blend(dst, (y * dstW + x) * 4, r, g, b, a);
    }

    // Axis-aligned scale is FIELD's dominant non-integer path. Its inverse source column is independent
    // of destination Y, and its source row is independent of destination X. Cache each column's two taps and
    // weight once per layer and compute the row's once per scanline, with the general path's exact arithmetic.
    // Pooled lookup storage avoids both per-layer garbage and repeated-stackalloc growth when the JIT inlines
    // this hot path; the 4096-column caller gate keeps the rented bucket small.
    private static void BlitAxisAligned(byte[] dst, int dstW, byte[] src, int srcW, int srcH,
                                        int srcX, int srcY, int width, int height, Affine2D inv,
                                        int x0, int y0, int x1, int y1,
                                        int tr, int tg, int tb, int istr, int ia,
                                        bool multiplyTint, BlendKind blend, bool unmodulatedSourceOver)
    {
        int count = x1 - x0;
        int[] columns = System.Buffers.ArrayPool<int>.Shared.Rent(count * 3);
        try
        {
            for (int x = x0; x < x1; x++)
            {
                double u = inv.Apply(x + 0.5, y0 + 0.5).X;
                int slot = (x - x0) * 3;
                if (!(u >= 0 && u < width))
                {
                    columns[slot] = -1;
                    continue;
                }
                Tap(srcX + u, srcW, out columns[slot], out columns[slot + 1], out columns[slot + 2]);
            }
            for (int y = y0; y < y1; y++)
            {
                double v = inv.Apply(x0 + 0.5, y + 0.5).Y;
                if (!(v >= 0 && v < height)) continue;
                Tap(srcY + v, srcH, out int ya, out int yb, out int wy);
                int destinationRow = y * dstW;
                for (int x = x0; x < x1; x++)
                {
                    int slot = (x - x0) * 3;
                    int xa = columns[slot];
                    if (xa < 0) continue;
                    Sample(src, srcW, xa, columns[slot + 1], columns[slot + 2], ya, yb, wy,
                           out int r, out int g, out int b, out int a);
                    BlendSample(dst, (destinationRow + x) * 4, r, g, b, a, tr, tg, tb, istr, ia,
                                multiplyTint, blend, unmodulatedSourceOver);
                }
            }
        }
        finally { System.Buffers.ArrayPool<int>.Shared.Return(columns); }
    }

    // Most FIELD layers have full object opacity and no tint. Preserve color-key transparency and partially
    // transparent edge texels, but make the overwhelmingly common alpha-255 case a four-byte copy instead of
    // performing tint and source-over multiply/divide work whose result is exactly the source texel.
    private static void BlendUnmodulatedSourceOver(byte[] dst, int di, int r, int g, int b, int a)
    {
        if (a == 0) return;
        if (a == 255)
        {
            dst[di] = (byte)r;
            dst[di + 1] = (byte)g;
            dst[di + 2] = (byte)b;
            dst[di + 3] = 255;
            return;
        }
        int inverse = 255 - a;
        dst[di] = (byte)((r * a + dst[di] * inverse) / 255);
        dst[di + 1] = (byte)((g * a + dst[di + 1] * inverse) / 255);
        dst[di + 2] = (byte)((b * a + dst[di + 2] * inverse) / 255);
        dst[di + 3] = (byte)System.Math.Min(255, dst[di + 3] + a);
    }

    private static void FillAxisAligned(byte[] dst, int dstW, int width, int height, Affine2D inv,
                                        int x0, int y0, int x1, int y1,
                                        int r, int g, int b, int a)
    {
        int count = x1 - x0;
        bool[] includedColumns = System.Buffers.ArrayPool<bool>.Shared.Rent(count);
        try
        {
            for (int x = x0; x < x1; x++)
            {
                double u = inv.Apply(x + 0.5, y0 + 0.5).X;
                includedColumns[x - x0] = u >= 0 && u < width;
            }
            for (int y = y0; y < y1; y++)
            {
                double v = inv.Apply(x0 + 0.5, y + 0.5).Y;
                if (v < 0 || v >= height) continue;
                int destinationRow = y * dstW;
                for (int x = x0; x < x1; x++)
                    if (includedColumns[x - x0]) Blend(dst, (destinationRow + x) * 4, r, g, b, a);
            }
        }
        finally { System.Buffers.ArrayPool<bool>.Shared.Return(includedColumns); }
    }

    private static void Bounds(Affine2D m,int w,int h,int dw,int dh,out int x0,out int y0,out int x1,out int y1)
    {
        var a=m.Apply(0,0);var b=m.Apply(w,0);var c=m.Apply(0,h);var d=m.Apply(w,h);
        x0=System.Math.Max(0,(int)System.Math.Floor(System.Math.Min(System.Math.Min(a.X,b.X),System.Math.Min(c.X,d.X))));
        y0=System.Math.Max(0,(int)System.Math.Floor(System.Math.Min(System.Math.Min(a.Y,b.Y),System.Math.Min(c.Y,d.Y))));
        x1=System.Math.Min(dw,(int)System.Math.Ceiling(System.Math.Max(System.Math.Max(a.X,b.X),System.Math.Max(c.X,d.X))));
        y1=System.Math.Min(dh,(int)System.Math.Ceiling(System.Math.Max(System.Math.Max(a.Y,b.Y),System.Math.Max(c.Y,d.Y))));
    }
    private static void Blend(byte[] d,int i,int r,int g,int b,int a,BlendKind blend=BlendKind.Alpha){
        if(blend==BlendKind.Additive){
            d[i]=(byte)System.Math.Min(255,d[i]+r*a/255);d[i+1]=(byte)System.Math.Min(255,d[i+1]+g*a/255);
            d[i+2]=(byte)System.Math.Min(255,d[i+2]+b*a/255);d[i+3]=(byte)System.Math.Min(255,d[i+3]+a);
            return;
        }
        d[i]=(byte)((r*a+d[i]*(255-a))/255);d[i+1]=(byte)((g*a+d[i+1]*(255-a))/255);
        d[i+2]=(byte)((b*a+d[i+2]*(255-a))/255);d[i+3]=(byte)System.Math.Min(255,d[i+3]+a);
    }
}
