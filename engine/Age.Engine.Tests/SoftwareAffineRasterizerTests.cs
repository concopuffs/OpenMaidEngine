using Age.Engine.Model;
using Xunit;

public class SoftwareAffineRasterizerTests
{
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
}
