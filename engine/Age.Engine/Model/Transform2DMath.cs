namespace Age.Engine.Model;

/// <summary>The axis-aligned 2D reduction of AGE's row-vector object matrix. Native composition is
/// T(-anchor) * scale * middle(rotation) * translation * T(anchor). With rotation deferred, a point is
/// therefore anchor + (point-anchor)*scale + translation; Z remains a retained 3D channel, not opacity.</summary>
public static class Transform2DMath
{
    public static (double X, double Y) Apply(double x, double y, TransformState transform)
        => (transform.AnchorX + (x - transform.AnchorX) * transform.ScaleX + transform.TranslateX,
            transform.AnchorY + (y - transform.AnchorY) * transform.ScaleY + transform.TranslateY);
}
