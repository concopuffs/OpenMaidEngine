using Ome.Engine.Model;

namespace Ome.Engine.Hosting;

/// <summary>
/// A numbered surface's pixels as an image, for the save thumbnails (ops 0x1ae and 0x1af) and the front end's
/// self-test. It is not part of <see cref="IHost"/>: its members carry <see cref="RgbaImage"/>, a device type that
/// handler code cannot name (docs/superpowers/specs/2026-11-12-engine-layering-design.md §3.3). Natives reach it
/// through <see cref="Persistence.NumberedSaveThumbnails"/>; a host without it has no surface pixels to give.
/// </summary>
public interface ISurfacePixels
{
    /// <summary>Return a stable RGBA snapshot of one numbered surface, or null when unavailable.</summary>
    RgbaImage? CaptureSurfacePixels(int slot);

    /// <summary>Replace one numbered surface from decoded RGBA pixels. False means unsupported.</summary>
    bool ReplaceSurfacePixels(int slot, RgbaImage image);
}
