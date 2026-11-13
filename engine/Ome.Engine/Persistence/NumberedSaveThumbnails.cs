using Ome.Engine.Hosting;

namespace Ome.Engine.Persistence;

/// <summary>
/// The thumbnail of a numbered save (`SAVEnn.STH`): op 0x1ae writes a surface as one, op 0x1af loads one into a
/// surface. The image stays here, so a handler passes only numbers
/// (docs/superpowers/specs/2026-11-12-engine-layering-design.md §3.3). Both return the script's result: 0 done,
/// 1 when there is no save store, no thumbnail to load or the file fails, 2 when the slot, the surface or the image
/// is unusable.
/// </summary>
public static class NumberedSaveThumbnails
{
    /// <summary>Op 0x1ae: encode surface <paramref name="surface"/> and store it as save <paramref name="slot"/>'s
    /// thumbnail.</summary>
    public static int Write(ISurfacePixels? graphics, INativeDatStore? store, int slot, int surface)
    {
        if (store == null) return 1;
        try
        {
            var image = graphics?.CaptureSurfacePixels(surface);
            if (image == null) return 2;
            store.SaveNumberedThumbnail(slot, NumberedThumbnailCodec.Encode(image));
            return 0;
        }
        catch (Exception error) when (ResultOf(error) is int result) { return result; }
    }

    /// <summary>Op 0x1af: decode save <paramref name="slot"/>'s thumbnail into surface
    /// <paramref name="surface"/>.</summary>
    public static int Load(ISurfacePixels? graphics, INativeDatStore? store, int slot, int surface)
    {
        if (store == null) return 1;
        try
        {
            byte[]? encoded = store.LoadNumberedThumbnail(slot);
            if (encoded == null) return 1;
            var image = NumberedThumbnailCodec.Decode(encoded);
            return graphics?.ReplaceSurfacePixels(surface, image) == true ? 0 : 2;
        }
        catch (Exception error) when (ResultOf(error) is int result) { return result; }
    }

    private static int? ResultOf(Exception error) => error switch
    {
        ArgumentOutOfRangeException or InvalidDataException or OverflowException => 2,
        IOException or UnauthorizedAccessException => 1,
        _ => null,
    };
}
