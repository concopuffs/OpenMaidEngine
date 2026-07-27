using Age.Engine.Model;
using Xunit;

public class Transform2DMathTests
{
    [Fact]
    public void ValueMatrixBuild_MatchesFormerArrayCompositionExactly()
    {
        var random = new Random(0x240);
        for (int i = 0; i < 500; i++)
        {
            var transform = new TransformState(
                Next(random, -3, 3), Next(random, -3, 3), Next(random, -3, 3),
                Next(random, -500, 500), Next(random, -500, 500), Next(random, -500, 500),
                Next(random, -1000, 1000), Next(random, -1000, 1000), Next(random, -1000, 1000),
                Next(random, -1, 1), Next(random, -1, 1), Next(random, -1, 1),
                Next(random, -360, 360));
            var cycle = new RotationCycleState(
                random.Next(2) != 0, random.Next(1, 20000),
                Next(random, -1, 1), Next(random, -1, 1), Next(random, -1, 1),
                Next(random, -360, 360));

            var expected = ArrayBuild(transform, cycle);
            var actual = Transform2DMath.Build(transform, cycle);

            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.XX), BitConverter.DoubleToInt64Bits(actual.XX));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.XY), BitConverter.DoubleToInt64Bits(actual.XY));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.YX), BitConverter.DoubleToInt64Bits(actual.YX));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.YY), BitConverter.DoubleToInt64Bits(actual.YY));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.TX), BitConverter.DoubleToInt64Bits(actual.TX));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.TY), BitConverter.DoubleToInt64Bits(actual.TY));
        }
    }

    [Fact]
    public void ValueMatrixBuild_DoesNotAllocatePerObject()
    {
        var transform = new TransformState(1.25, 0.75, 1.1, 42, -17, 5, 400, 300, 9, 0, 0, 1, 37);
        var cycle = new RotationCycleState(true, 9000, 0, 0, -1, 123);
        _ = Transform2DMath.Build(transform, cycle); // JIT/warm-up outside the measured interval.

        long before = GC.GetAllocatedBytesForCurrentThread();
        Affine2D result = default;
        for (int i = 0; i < 10_000; i++) result = Transform2DMath.Build(transform, cycle);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(result);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CanvasAxisDecompositionPreservesBunkiPopupScale()
    {
        var affine = Transform2DMath.Build(new TransformState(
            0.05, 0.65, 1, 0, 0, 0, 400, 300, 0));

        var (rotation, scaleX, scaleY) = affine.DecomposeCanvasAxes();

        Assert.Equal(0, rotation, 12);
        Assert.Equal(0.05, scaleX, 12);
        Assert.Equal(0.65, scaleY, 12);
    }

    private static double Next(Random random, double minimum, double maximum)
        => minimum + random.NextDouble() * (maximum - minimum);

    // The former heap-array implementation, retained only as a differential oracle.
    private static Affine2D ArrayBuild(TransformState t, RotationCycleState cycle)
    {
        double[] m = Identity();
        m = Mul(m, Translation(-t.AnchorX, -t.AnchorY, -t.AnchorZ));
        m = Mul(m, Scale(t.ScaleX, t.ScaleY, t.ScaleZ));
        m = Mul(m, AxisAngle(t.RotationAxisX, t.RotationAxisY, t.RotationAxisZ, t.RotationAngleDegrees));
        m = Mul(m, Translation(t.TranslateX, t.TranslateY, t.TranslateZ));
        if (cycle.Enabled) m = Mul(m, AxisAngle(cycle.AxisX, cycle.AxisY, cycle.AxisZ, cycle.AngleDegrees));
        m = Mul(m, Translation(t.AnchorX, t.AnchorY, t.AnchorZ));
        return new(m[0], m[1], m[4], m[5], m[12], m[13]);
    }

    private static double[] Identity() => new double[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
    private static double[] Scale(double x, double y, double z)
        => new double[] { x,0,0,0, 0,y,0,0, 0,0,z,0, 0,0,0,1 };
    private static double[] Translation(double x, double y, double z)
        => new double[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, x,y,z,1 };

    private static double[] AxisAngle(double x, double y, double z, double degrees)
    {
        double len = Math.Sqrt(x*x + y*y + z*z);
        if (len < 1e-12 || Math.Abs(degrees) < 1e-12) return Identity();
        x /= len; y /= len; z /= len;
        double r = degrees * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r), q = 1-c;
        return new double[] {
            x*x*q+c,   x*y*q+z*s, x*z*q-y*s, 0,
            x*y*q-z*s, y*y*q+c,   y*z*q+x*s, 0,
            x*z*q+y*s, y*z*q-x*s, z*z*q+c,   0,
            0,0,0,1
        };
    }

    private static double[] Mul(double[] a, double[] b)
    {
        var output = new double[16];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                for (int k = 0; k < 4; k++) output[row * 4 + column] += a[row * 4 + k] * b[k * 4 + column];
        return output;
    }
}
