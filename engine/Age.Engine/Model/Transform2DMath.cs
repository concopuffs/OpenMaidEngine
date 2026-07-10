namespace Age.Engine.Model;

/// <summary>A projected row-vector affine transform: x'=x*XX+y*YX+TX, y'=x*XY+y*YY+TY.</summary>
public readonly record struct Affine2D(double XX, double XY, double YX, double YY, double TX, double TY)
{
    public (double X, double Y) Apply(double x, double y)
        => (x * XX + y * YX + TX, x * XY + y * YY + TY);

    public Affine2D FromLocalOrigin(double worldX, double worldY)
    {
        var p = Apply(worldX, worldY);
        return new(XX, XY, YX, YY, p.X, p.Y);
    }

    public bool TryInverse(out Affine2D inverse)
    {
        double det = XX * YY - XY * YX;
        if (System.Math.Abs(det) < 1e-12) { inverse = default; return false; }
        double xx = YY / det, xy = -XY / det, yx = -YX / det, yy = XX / det;
        inverse = new(xx, xy, yx, yy, -(TX * xx + TY * yx), -(TX * xy + TY * yy));
        return true;
    }
}

/// <summary>Exact 2D projection of AGE's row-vector retained-object matrix. Native call order is anchored
/// scale, one-shot axis-angle rotation, translation, then separately anchored cyclic rotation. The adjacent
/// anchor translations cancel, yielding T(-a)*S*R1*T*Rcycle*T(+a). Z is projected away only afterward.</summary>
public static class Transform2DMath
{
    public static Affine2D Build(TransformState t, RotationCycleState cycle = default)
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

    public static (double X, double Y) Apply(double x, double y, TransformState transform,
                                             RotationCycleState cycle = default)
        => Build(transform, cycle).Apply(x, y);

    private static double[] Identity() => new double[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
    private static double[] Scale(double x, double y, double z)
        => new double[] { x,0,0,0, 0,y,0,0, 0,0,z,0, 0,0,0,1 };
    private static double[] Translation(double x, double y, double z)
        => new double[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, x,y,z,1 };

    private static double[] AxisAngle(double x, double y, double z, double degrees)
    {
        double len = System.Math.Sqrt(x*x + y*y + z*z);
        if (len < 1e-12 || System.Math.Abs(degrees) < 1e-12) return Identity();
        x /= len; y /= len; z /= len;
        double r = degrees * System.Math.PI / 180.0, c = System.Math.Cos(r), s = System.Math.Sin(r), q = 1-c;
        return new double[] {
            x*x*q+c,   x*y*q+z*s, x*z*q-y*s, 0,
            x*y*q-z*s, y*y*q+c,   y*z*q+x*s, 0,
            x*z*q+y*s, y*z*q-x*s, z*z*q+c,   0,
            0,0,0,1
        };
    }

    private static double[] Mul(double[] a, double[] b)
    {
        var o = new double[16];
        for (int row=0; row<4; row++)
            for (int col=0; col<4; col++)
                for (int k=0; k<4; k++) o[row*4+col] += a[row*4+k] * b[k*4+col];
        return o;
    }
}
