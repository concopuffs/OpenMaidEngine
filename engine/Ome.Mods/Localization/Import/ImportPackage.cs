using System.Globalization;
using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Sys4;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

namespace Ome.Mods.Localization.Import;

/// <summary>
/// Makes the output of <c>omp text import-scripts --package</c> a package that loads as it is (localization spec §12,
/// §18.1): the images the translated install replaces become the language's assets beside the catalogs, and a
/// manifest declares the language.
/// </summary>
public static class ImportPackage
{
    /// <summary>The image files the import writes to <c>assets/&lt;language&gt;/</c>, and so the ones it replaces
    /// there on the next run.</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".AGF", ".BMP", ".PNG", ".JPG", ".JPEG",
    };

    /// <summary>
    /// Writes each image to <paramref name="folder"/>: an AGF as <c>NAME.png</c>, decoded as the port decodes the
    /// translated install (a BMP under an AGF name included), any other image as it is. The folder keeps only this
    /// run's images: images an earlier run wrote are deleted first. An image that cannot be read or decoded is added
    /// to <see cref="AssetCounts.NotWritten"/> with the reason.
    /// </summary>
    public static void WriteAssets(IEnumerable<string> names, Func<string, byte[]> read, string folder,
                                   AssetCounts counts)
    {
        Directory.CreateDirectory(folder);
        foreach (string stale in Directory.EnumerateFiles(folder)
                     .Where(path => ImageExtensions.Contains(Path.GetExtension(path))))
            File.Delete(stale);
        counts.Written = 0;
        foreach (string name in names)
        {
            try
            {
                byte[] bytes = read(name);
                string file = name;
                if (Path.GetExtension(name).Equals(ModLayout.ImageExtension, StringComparison.OrdinalIgnoreCase))
                {
                    bytes = PngWriter.Write(AgfDecoder.Decode(bytes, name, allowBmpAsAgf: true));
                    file = Path.GetFileNameWithoutExtension(name) + ModLayout.PngExtension;
                }
                File.WriteAllBytes(Path.Combine(folder, file), bytes);
                counts.Written++;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                counts.NotWritten.Add($"{name}: {error.Message}");
            }
        }
    }

    /// <summary>The factor that keeps the reveal's pace for the translation (a language's
    /// <c>glyph_delay_scale</c>): the source's characters over the translation's, across the entries that translate
    /// their source, to two decimals and within the setting's range. 1 when no entry translates.</summary>
    public static double GlyphDelayScale(IEnumerable<TextEntry> entries)
    {
        long source = 0, text = 0;
        foreach (TextEntry entry in entries)
        {
            if (entry.Text.Length == 0 || entry.Text == entry.Source) continue;
            source += entry.Source.Length;
            text += entry.Text.Length;
        }
        if (source == 0 || text == 0) return 1;
        return Math.Clamp(Math.Round((double)source / text, 2), 0.01, 4);
    }

    /// <summary>The language's name in its own language, as the shell lists it, or the tag when the system does not
    /// list a culture of that name (it would otherwise make one up for any well-formed tag).</summary>
    public static string LanguageName(string tag)
    {
        CultureInfo? culture = CultureInfo.GetCultures(CultureTypes.AllCultures)
            .FirstOrDefault(known => known.Name.Equals(tag, StringComparison.OrdinalIgnoreCase));
        return culture is { NativeName.Length: > 0 } ? culture.NativeName : tag;
    }

    /// <summary>A manifest for the imported package: format version 2, the profile's game, and one
    /// <c>[[language]]</c> table. <paramref name="header"/> lines become comments at the top.</summary>
    public static string Manifest(string profile, string language, string languageName, string from,
                                  double glyphDelayScale, IEnumerable<string> header)
    {
        var text = new StringBuilder();
        foreach (string line in header) text.Append("# ").Append(line).Append('\n');
        text.Append('\n');
        text.Append($"omp = {ModManifest.LanguageOmpVersion}\n");
        text.Append($"id = {TextCatalogFileWriter.Quote($"local.{profile}-{language}")}\n");
        text.Append($"name = {TextCatalogFileWriter.Quote($"{languageName} ({from}, imported)")}\n");
        text.Append("version = \"0.1.0\"\n\n");
        text.Append("[game]\n");
        text.Append($"profile = {TextCatalogFileWriter.Quote(profile)}\n");
        text.Append("target = \"native\"\n\n");
        text.Append("[[language]]\n");
        text.Append($"tag = {TextCatalogFileWriter.Quote(language)}\n");
        text.Append($"name = {TextCatalogFileWriter.Quote(languageName)}\n");
        text.Append($"glyph_delay_scale = {glyphDelayScale.ToString("0.0#", CultureInfo.InvariantCulture)}\n");
        return text.ToString();
    }
}
