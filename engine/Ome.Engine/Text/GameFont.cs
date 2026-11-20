using Ome.Engine.Sys4;

namespace Ome.Engine.Text;

/// <summary>
/// The font a game ships in its catalog and AGE registers for its own process at start-up (docs/sys4-format-notes.md,
/// "The font list"). AGE 4.38D's engine_initialize@0x416c00 takes the first record of the list: when its file id is
/// above 0 it reads that catalog file and gives the bytes to AddFontMemResourceEx. It registers no later record, and
/// it does not use the record's face name: its text code names the face itself
/// (<see cref="TextRules.ExtendedGlyphFace"/>). 創刻のアテリアル's is file 37, <c>AGE-EXTEND.TTF</c>, the face
/// <c>AGE Extend</c>.
/// </summary>
/// <param name="FaceName">The face the catalog names for the file.</param>
/// <param name="FileName">The catalog file's name, for messages.</param>
/// <param name="Data">The font file.</param>
public sealed record GameFont(string FaceName, string FileName, byte[] Data)
{
    /// <summary>
    /// The font AGE would register for <paramref name="catalog"/>, read through <paramref name="store"/>; null when
    /// the catalog lists none. Native reports a file it cannot open in a message box and does not start; the port
    /// says so through <paramref name="warn"/> and goes on without the font.
    /// </summary>
    public static GameFont? Load(Sys4AssetCatalog catalog, IAssetStore store, Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(store);
        if (catalog.Fonts.Count == 0 || catalog.Fonts[0].FileId <= 0) return null;
        Sys4CatalogFont font = catalog.Fonts[0];
        if (catalog.ResolvePacked(font.FileId) is not { } entry)
        {
            warn?.Invoke($"the catalog's font '{font.FaceName}' names file {font.FileId}, which the catalog lacks");
            return null;
        }
        try
        {
            return new GameFont(font.FaceName, entry.Name, store.ReadAll(entry));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            warn?.Invoke($"the catalog's font '{font.FaceName}' ({entry.Name}) could not be read: {error.Message}");
            return null;
        }
    }

    /// <summary>Gives the font to <paramref name="rasterizer"/> when it can take one
    /// (<see cref="IGameFontRasterizer"/>); false when it cannot.</summary>
    public bool AddTo(IGlyphMaskRasterizer rasterizer)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        if (rasterizer is not IGameFontRasterizer fonts) return false;
        fonts.AddGameFont(FaceName, Data);
        return true;
    }
}
