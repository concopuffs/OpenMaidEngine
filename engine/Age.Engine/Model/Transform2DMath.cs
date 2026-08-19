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

    /// <summary>Compose this row-vector transform followed by <paramref name="next"/>. Native uses this
    /// order when it post-multiplies an object's matrix by the selected retained-gfx range transform.</summary>
    public Affine2D Then(Affine2D next)
        => new(
            XX * next.XX + XY * next.YX,
            XX * next.XY + XY * next.YY,
            YX * next.XX + YY * next.YX,
            YX * next.XY + YY * next.YY,
            TX * next.XX + TY * next.YX + next.TX,
            TX * next.XY + TY * next.YY + next.TY);

    public bool TryInverse(out Affine2D inverse)
    {
        double det = XX * YY - XY * YX;
        if (System.Math.Abs(det) < 1e-12) { inverse = default; return false; }
        double xx = YY / det, xy = -XY / det, yx = -YX / det, yy = XX / det;
        inverse = new(xx, xy, yx, yy, -(TX * xx + TY * yx), -(TX * xy + TY * yy));
        return true;
    }

    /// <summary>Decompose the projected axes for a Godot-style canvas item. AGE's ordinary retained
    /// transforms contain scale/rotation but no shear; the determinant preserves reflected Y axes.</summary>
    public (double RotationRadians, double ScaleX, double ScaleY) DecomposeCanvasAxes()
    {
        double scaleX = System.Math.Sqrt(XX * XX + XY * XY);
        double scaleY = System.Math.Sqrt(YX * YX + YY * YY);
        double determinant = XX * YY - XY * YX;
        if (determinant < 0) scaleY = -scaleY;
        double rotation = scaleX > 1e-12
            ? System.Math.Atan2(XY, XX)
            : System.Math.Atan2(-YX, YY);
        return (rotation, scaleX, scaleY);
    }
}

/// <summary>Exact 2D projection of AGE's row-vector retained-object matrix. Native call order is anchored
/// one-shot scale/axis-angle rotation/translation, then separately anchored cyclic scale, cyclic rotation,
/// and cyclic translation.
/// The adjacent anchor translations cancel, yielding T(-a)*S*R1*T*Scycle*Rcycle*Tcycle*T(+a).
/// Z is projected away only afterward.</summary>
public static class Transform2DMath
{
    public static Affine2D Build(TransformState t, RotationCycleState cycle = default,
                                 ScaleCycleState scaleCycle = default,
                                 TranslationCycleState translationCycle = default)
    {
        Matrix3D m = Identity();
        m = Mul(m, Translation(-t.AnchorX, -t.AnchorY, -t.AnchorZ));
        m = Mul(m, Scale(t.ScaleX, t.ScaleY, t.ScaleZ));
        m = Mul(m, AxisAngle(t.RotationAxisX, t.RotationAxisY, t.RotationAxisZ, t.RotationAngleDegrees));
        m = Mul(m, Translation(t.TranslateX, t.TranslateY, t.TranslateZ));
        if (scaleCycle.Enabled) m = Mul(m, Scale(scaleCycle.ScaleX, scaleCycle.ScaleY, scaleCycle.ScaleZ));
        if (cycle.Enabled) m = Mul(m, AxisAngle(cycle.AxisX, cycle.AxisY, cycle.AxisZ, cycle.AngleDegrees));
        if (translationCycle.Enabled)
            m = Mul(m, Translation(translationCycle.TranslateX, translationCycle.TranslateY,
                                   translationCycle.TranslateZ));
        m = Mul(m, Translation(t.AnchorX, t.AnchorY, t.AnchorZ));
        return new(m.M11, m.M12, m.M21, m.M22, m.TX, m.TY);
    }

    public static (double X, double Y) Apply(double x, double y, TransformState transform,
                                             RotationCycleState cycle = default,
                                             ScaleCycleState scaleCycle = default,
                                             TranslationCycleState translationCycle = default)
        => Build(transform, cycle, scaleCycle, translationCycle).Apply(x, y);

    // AGE composes affine 4x4 row-vector matrices, whose last column is always (0,0,0,1). Carry only
    // the 3x3 linear part and translation row as a value type: the old double[16] implementation allocated
    // about eleven arrays per rendered object, or roughly 2.2 MB on every DEBUGMAP composition.
    private readonly record struct Matrix3D(
        double M11, double M12, double M13,
        double M21, double M22, double M23,
        double M31, double M32, double M33,
        double TX, double TY, double TZ);

    private static Matrix3D Identity() => new(
        1,0,0, 0,1,0, 0,0,1, 0,0,0);

    private static Matrix3D Scale(double x, double y, double z) => new(
        x,0,0, 0,y,0, 0,0,z, 0,0,0);

    private static Matrix3D Translation(double x, double y, double z) => new(
        1,0,0, 0,1,0, 0,0,1, x,y,z);

    private static Matrix3D AxisAngle(double x, double y, double z, double degrees)
    {
        double len = System.Math.Sqrt(x*x + y*y + z*z);
        if (len < 1e-12 || System.Math.Abs(degrees) < 1e-12) return Identity();
        x /= len; y /= len; z /= len;
        double r = degrees * System.Math.PI / 180.0, c = System.Math.Cos(r), s = System.Math.Sin(r), q = 1-c;
        return new(
            x*x*q+c,   x*y*q+z*s, x*z*q-y*s,
            x*y*q-z*s, y*y*q+c,   y*z*q+x*s,
            x*z*q+y*s, y*z*q-x*s, z*z*q+c,
            0,0,0);
    }

    private static Matrix3D Mul(Matrix3D a, Matrix3D b) => new(
        Sum4(a.M11*b.M11, a.M12*b.M21, a.M13*b.M31, 0),
        Sum4(a.M11*b.M12, a.M12*b.M22, a.M13*b.M32, 0),
        Sum4(a.M11*b.M13, a.M12*b.M23, a.M13*b.M33, 0),
        Sum4(a.M21*b.M11, a.M22*b.M21, a.M23*b.M31, 0),
        Sum4(a.M21*b.M12, a.M22*b.M22, a.M23*b.M32, 0),
        Sum4(a.M21*b.M13, a.M22*b.M23, a.M23*b.M33, 0),
        Sum4(a.M31*b.M11, a.M32*b.M21, a.M33*b.M31, 0),
        Sum4(a.M31*b.M12, a.M32*b.M22, a.M33*b.M32, 0),
        Sum4(a.M31*b.M13, a.M32*b.M23, a.M33*b.M33, 0),
        Sum4(a.TX*b.M11, a.TY*b.M21, a.TZ*b.M31, b.TX),
        Sum4(a.TX*b.M12, a.TY*b.M22, a.TZ*b.M32, b.TY),
        Sum4(a.TX*b.M13, a.TY*b.M23, a.TZ*b.M33, b.TZ));

    // Accumulate in the same order as the former 4x4 loop so boundary-sensitive nearest-neighbour
    // projection retains its floating-point behavior while avoiding an intermediate array.
    private static double Sum4(double a, double b, double c, double d)
    {
        double result = 0;
        result += a;
        result += b;
        result += c;
        result += d;
        return result;
    }
}
