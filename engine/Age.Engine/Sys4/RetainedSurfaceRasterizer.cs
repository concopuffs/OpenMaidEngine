using Age.Engine.Model;

namespace Age.Engine.Sys4;

/// <summary>Platform-neutral software publication of retained gfx objects into an AGE surface.
/// Native scripts use the same object list for the backbuffer and selected offscreen render targets.</summary>
public static class RetainedSurfaceRasterizer
{
    public static int CompositeRange(
        RgbaImage destination,
        IReadOnlyList<RenderObject> visible,
        long firstHandle,
        long count,
        Func<RenderObject, RgbaImage?> resolveSource)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(resolveSource);
        if (destination.Width <= 0 || destination.Height <= 0
            || destination.Pixels.Length != checked(destination.Width * destination.Height * 4)
            || count <= 0)
            return 0;

        int rendered = 0;
        foreach (RenderObject item in visible)
        {
            if (item.Handle < firstHandle || item.Handle - firstHandle >= count) continue;

            TransformState transform = item.Transform;
            Affine2D localToDestination =
                Transform2DMath.Build(transform, item.Rotation, item.ScaleCycle)
                    .FromLocalOrigin(item.DstX, item.DstY);
            if (item.RangeTransform is { } rangeTransform)
                localToDestination = localToDestination.Then(rangeTransform);

            RgbaImage? source = resolveSource(item);
            float opacity = item.Alpha / 255f;
            float tintStrength = item.TintStrength / 255f;
            if (source == null)
            {
                if (item.SurfaceResId != 0 || item.Blend == BlendKind.Opaque) continue;
                int width = item.W > 0 ? item.W : destination.Width;
                int height = item.H > 0 ? item.H : destination.Height;
                float fillOpacity = item.MultiplyTint ? opacity : opacity * tintStrength;
                SoftwareAffineRasterizer.FillRgba(
                    destination.Pixels, destination.Width, destination.Height,
                    width, height, localToDestination, item.Tint, fillOpacity);
                rendered++;
                continue;
            }

            if (item.W <= 0 || item.H <= 0 || item.SrcX < 0 || item.SrcY < 0) continue;
            int widthToDraw = System.Math.Min(item.W, source.Width - item.SrcX);
            int heightToDraw = System.Math.Min(item.H, source.Height - item.SrcY);
            if (widthToDraw <= 0 || heightToDraw <= 0) continue;
            SoftwareAffineRasterizer.BlitRgba(
                destination.Pixels, destination.Width, destination.Height,
                source.Pixels, source.Width, source.Height,
                item.SrcX, item.SrcY, widthToDraw, heightToDraw,
                localToDestination, item.Tint, tintStrength, opacity,
                item.MultiplyTint, item.Blend);
            rendered++;
        }
        return rendered;
    }
}
