namespace Age.Engine.Model;

/// <summary>Nearest-neighbour inverse-mapped RGBA8 affine compositor used by the Godot host and pure tests.</summary>
public static class SoftwareAffineRasterizer
{
    public static void BlitRgba(byte[] dst, int dstW, int dstH, byte[] src, int srcW, int srcH,
                                int srcX, int srcY, int width, int height, Affine2D localToDest,
                                long tint, float tintStrength, float opacity, bool multiplyTint = false)
    {
        if (width <= 0 || height <= 0 || !localToDest.TryInverse(out var inv)) return;
        Bounds(localToDest, width, height, dstW, dstH, out int x0, out int y0, out int x1, out int y1);
        int istr = (int)(System.Math.Clamp(tintStrength, 0f, 1f) * 255);
        int ia = (int)(System.Math.Clamp(opacity, 0f, 1f) * 255);
        int tr=(int)(tint>>16&255), tg=(int)(tint>>8&255), tb=(int)(tint&255);
        for (int y=y0; y<y1; y++) for (int x=x0; x<x1; x++)
        {
            var p = inv.Apply(x + 0.5, y + 0.5);
            int u=(int)System.Math.Floor(p.X), v=(int)System.Math.Floor(p.Y);
            if ((uint)u >= (uint)width || (uint)v >= (uint)height) continue;
            int si=((srcY+v)*srcW+(srcX+u))*4, di=(y*dstW+x)*4;
            int sa=src[si+3]*ia/255; if(sa==0) continue;
            int sr=multiplyTint ? src[si]*tr/255 : (src[si]*(255-istr)+tr*istr)/255;
            int sg=multiplyTint ? src[si+1]*tg/255 : (src[si+1]*(255-istr)+tg*istr)/255;
            int sb=multiplyTint ? src[si+2]*tb/255 : (src[si+2]*(255-istr)+tb*istr)/255;
            Blend(dst,di,sr,sg,sb,sa);
        }
    }

    public static void FillRgba(byte[] dst, int dstW, int dstH, int width, int height,
                                Affine2D localToDest, long color, float opacity)
    {
        if (width <= 0 || height <= 0 || !localToDest.TryInverse(out var inv)) return;
        Bounds(localToDest,width,height,dstW,dstH,out int x0,out int y0,out int x1,out int y1);
        int a=(int)(System.Math.Clamp(opacity,0f,1f)*255); if(a==0)return;
        int r=(int)(color>>16&255),g=(int)(color>>8&255),b=(int)(color&255);
        for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){
            var p=inv.Apply(x+.5,y+.5);
            if(p.X>=0&&p.X<width&&p.Y>=0&&p.Y<height) Blend(dst,(y*dstW+x)*4,r,g,b,a);
        }
    }

    private static void Bounds(Affine2D m,int w,int h,int dw,int dh,out int x0,out int y0,out int x1,out int y1)
    {
        var a=m.Apply(0,0);var b=m.Apply(w,0);var c=m.Apply(0,h);var d=m.Apply(w,h);
        x0=System.Math.Max(0,(int)System.Math.Floor(System.Math.Min(System.Math.Min(a.X,b.X),System.Math.Min(c.X,d.X))));
        y0=System.Math.Max(0,(int)System.Math.Floor(System.Math.Min(System.Math.Min(a.Y,b.Y),System.Math.Min(c.Y,d.Y))));
        x1=System.Math.Min(dw,(int)System.Math.Ceiling(System.Math.Max(System.Math.Max(a.X,b.X),System.Math.Max(c.X,d.X))));
        y1=System.Math.Min(dh,(int)System.Math.Ceiling(System.Math.Max(System.Math.Max(a.Y,b.Y),System.Math.Max(c.Y,d.Y))));
    }
    private static void Blend(byte[] d,int i,int r,int g,int b,int a){
        d[i]=(byte)((r*a+d[i]*(255-a))/255);d[i+1]=(byte)((g*a+d[i+1]*(255-a))/255);
        d[i+2]=(byte)((b*a+d[i+2]*(255-a))/255);d[i+3]=(byte)System.Math.Min(255,d[i+3]+a);
    }
}
