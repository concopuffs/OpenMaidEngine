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
}
