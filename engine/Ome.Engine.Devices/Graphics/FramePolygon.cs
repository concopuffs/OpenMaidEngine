using Ome.Engine.Model;

namespace Ome.Engine.Devices;

/// <summary>How a polygon item lands on the frame (AGE 4.38D's four polygon blends, op 0x322's mode). The source
/// colour is the polygon's own after its textures; its alpha is scaled by the item's opacity.</summary>
public enum FramePolygonBlend
{
    /// <summary>Source alpha over the frame (Direct3D SRCALPHA, INVSRCALPHA).</summary>
    Alpha,
    /// <summary>The source, scaled by its alpha, added to the frame (SRCALPHA, ONE).</summary>
    Additive,
    /// <summary>The source replaces the frame's pixel, alpha included (ONE, ZERO).</summary>
    Replace,
    /// <summary>The source, scaled by its alpha, subtracted from the frame (reverse subtract, SRCALPHA, ONE).</summary>
    Subtractive,
}

/// <summary>One corner of a polygon item's triangle: its place on the canvas in pixels, its colour (0xAARRGGBB) and its
/// texture coordinates, where 0 and 1 are a texture's edges.</summary>
public readonly record struct FramePolygonVertex(float X, float Y, uint Argb, float U, float V);

/// <summary>
/// The geometry of a <see cref="FrameItemKind.Polygon"/> item
/// (docs/superpowers/specs/2026-11-19-polygon-objects-design.md §3): a triangle list, three vertices to a triangle,
/// and the blend. It never changes once it is in a frame: its producer replaces it when the polygon changes, so a
/// renderer may keep what it built from one by its identity.
/// </summary>
/// <remarks>
/// <para>A pixel belongs to a triangle when its centre is inside it, with the top-left rule on a shared edge, so two
/// triangles that share an edge never draw a pixel twice. Colour and texture coordinates are interpolated linearly
/// across the triangle. Both windings are drawn.</para>
/// <para>The source colour at a pixel: with no texture, the interpolated colour. With one texture, the texture
/// (sampled with clamped coordinates and bilinear filtering, texels matching its colour key transparent) times the
/// interpolated colour, channel by channel, alpha included. With two, the second texture is first blended over the
/// first by the second's alpha, colour and alpha alike, and the result is multiplied by the interpolated colour.</para>
/// <para>The reference renderer (<c>Ome.Engine.Hosting.ReferenceRenderer</c>) defines the item.</para>
/// </remarks>
public sealed class FramePolygonGeometry(FramePolygonVertex[] vertices, FramePolygonBlend blend)
{
    /// <summary>The triangle list: its length is a multiple of three.</summary>
    public FramePolygonVertex[] Vertices { get; } = vertices;
    public FramePolygonBlend Blend { get; } = blend;
}

/// <summary>A polygon item's second texture, identified as a texture item's is (<see cref="FrameItem"/>).</summary>
public sealed class FramePolygonTexture(RgbaImage pixels, int assetId, bool dynamic, long dynamicKey, long colorKey)
{
    public RgbaImage Pixels { get; } = pixels;
    public int AssetId { get; } = assetId;
    public bool Dynamic { get; } = dynamic;
    public long DynamicKey { get; } = dynamicKey;
    public long ColorKey { get; } = colorKey;
}
