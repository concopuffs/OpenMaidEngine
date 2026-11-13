namespace Ome.Engine.Model;

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
