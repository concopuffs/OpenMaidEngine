using Ome.Engine.Model;

namespace Ome.Engine.Devices;

/// <summary>What a frame item draws: a textured quad from a source rectangle, or a solid fill.</summary>
public enum FrameItemKind { Texture, Fill }

/// <summary>
/// One draw of a frame description (docs/superpowers/specs/2026-11-12-engine-layering-design.md §6.3): a quad mapped
/// to the screen by one affine matrix. Everything a renderer needs is here, sampled for the frame; nothing needs the
/// engine's object model or a clock.
/// </summary>
/// <remarks>
/// <para>A <see cref="FrameItemKind.Texture"/> item draws the rectangle <see cref="SourceX"/>, <see cref="SourceY"/>,
/// <see cref="Width"/> × <see cref="Height"/> of <see cref="Pixels"/>, clipped to the image, with texels that match
/// <see cref="ColorKey"/> made transparent. Its colour: with <see cref="MultiplyTint"/>, each texel's RGB is multiplied
/// by <see cref="Tint"/>; otherwise it is moved toward <see cref="Tint"/> by <see cref="TintStrength"/>/255 (the LERP
/// tint). <see cref="Opacity"/> scales its alpha, and <see cref="Blend"/> says how it lands on the frame.</para>
/// <para>A <see cref="FrameItemKind.Fill"/> item covers <see cref="Width"/> × <see cref="Height"/> with
/// <see cref="Tint"/> at <see cref="Opacity"/>, source-over.</para>
/// <para>A texture's identity says what a renderer may cache: a static image is one <see cref="AssetId"/>; a dynamic
/// surface (<see cref="Dynamic"/>, a script-drawn surface or a movie) is <see cref="DynamicKey"/>, and its pixels change
/// by replacement, so a new <see cref="RgbaImage.Pixels"/> array means new contents.</para>
/// <para>The reference renderer (<c>Ome.Engine.Hosting.ReferenceRenderer</c>) defines each field's meaning.</para>
/// </remarks>
public readonly record struct FrameItem(
    FrameItemKind Kind,
    RgbaImage? Pixels,
    int AssetId,
    bool Dynamic,
    long DynamicKey,
    long ColorKey,
    int SourceX,
    int SourceY,
    int Width,
    int Height,
    Affine2D Matrix,
    float Opacity,
    long Tint,
    int TintStrength,
    bool MultiplyTint,
    BlendKind Blend)
{
    /// <summary>A solid fill of <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static FrameItem Fill(int width, int height, Affine2D matrix, long tint, float opacity)
        => new(FrameItemKind.Fill, null, 0, false, 0, -1, 0, 0, width, height, matrix, opacity, tint, 0, true,
               BlendKind.Alpha);
}

/// <summary>A whole-screen transition (native screen_transition_tick mode 4): the source frame is drawn opaque, and the
/// target, flattened over a clear of <see cref="OverlayRgb"/>, is blended over it at <see cref="Alpha"/> (0..256,
/// 256 = the target alone). At <see cref="Alpha"/> 0 the target is not drawn and <see cref="Target"/> is empty.</summary>
public sealed class FrameScreenTransition
{
    public List<FrameItem> Source { get; } = new(256);
    public List<FrameItem> Target { get; } = new(256);
    public long OverlayRgb { get; set; }
    public int Alpha { get; set; }
}

/// <summary>
/// One frame for a renderer (spec §6.3, part 1): the background, whether the previous frame's pixels stay, and the
/// ordered draw items. Effects are already lowered to items (a surface transition draws its target range at the
/// sampled progress; a blur draws its samples), except the whole-screen transition, which needs the flattened target.
/// </summary>
/// <remarks>Its producer reuses one description: it is valid until the producer lowers the next frame, and a renderer
/// draws it before asking for the next. The pixels it refers to never change: surfaces are replaced, not written
/// in place.</remarks>
public sealed class FrameDescription
{
    /// <summary>The colour under everything when the frame starts clean (0xRRGGBB; 0 = black).</summary>
    public long BackgroundRgb { get; set; }

    /// <summary>The frame draws over the previous frame's pixels instead of a clean background.</summary>
    public bool PreserveExistingPixels { get; set; }

    /// <summary>For a retained renderer: the items add to the previous frame's layers rather than replacing them
    /// (a partial publication, or an empty full one).</summary>
    public bool AppendLayers { get; set; }

    /// <summary>The draws, bottom first.</summary>
    public List<FrameItem> Items { get; } = new(1024);

    /// <summary>A whole-screen transition; when set, <see cref="Items"/> is empty and the frame starts clean.</summary>
    public FrameScreenTransition? ScreenTransition { get; set; }

    /// <summary>The canvas size.</summary>
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>A graphics device that draws frame descriptions with the GPU: the renderer for play (spec §6.3, part 3).
/// It must match the reference renderer within the stated tolerance.</summary>
public interface IFrameRenderer
{
    /// <summary>Draw <paramref name="frame"/>, or return false without drawing when it holds something this renderer
    /// cannot draw, so the frame goes to the reference renderer instead.</summary>
    bool TryDraw(FrameDescription frame);
}

/// <summary>A graphics device that shows a finished frame (spec §6.3, part 3): the reference renderer's output.</summary>
public interface IFramePresenter
{
    /// <summary>Show <paramref name="rgba"/>, <paramref name="width"/> × <paramref name="height"/> RGBA8 pixels, rows
    /// top down. The buffer is the caller's; copy what is kept.</summary>
    void Present(byte[] rgba, int width, int height);
}
