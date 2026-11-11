using System.Globalization;
using System.Text;
using Ome.Engine.Localization;

namespace Ome.Mods.Localization;

/// <summary>
/// Writes a catalog file (localization spec §9.3) that <see cref="TextCatalogFileReader"/> reads back to the same
/// entries. Keys come in a fixed order and strings are TOML basic strings, so the output is deterministic and
/// diffs well. An entry's <c>script</c> is written only where it differs from the file's default scope.
/// </summary>
public static class TextCatalogFileWriter
{
    public static string Write(TextCatalogFile file, IEnumerable<string>? headerComments = null)
    {
        var text = new StringBuilder();
        foreach (string comment in headerComments ?? [])
            text.Append("# ").Append(comment.ReplaceLineEndings(" ")).Append('\n');
        if (file.Script != null) text.Append("script = ").Append(Quote(file.Script)).Append('\n');
        if (file.Fingerprint != null) text.Append("fingerprint = ").Append(Quote(file.Fingerprint)).Append('\n');
        foreach (TextEntry entry in file.Entries)
        {
            text.Append("\n[[text]]\n");
            if (!string.Equals(entry.Script, file.Script, StringComparison.Ordinal))
                text.Append("script = ").Append(Quote(entry.Script ?? "")).Append('\n');
            text.Append("source = ").Append(Quote(entry.Source)).Append('\n');
            if (entry.At is { } at) text.Append("at = 0x").Append(at.ToString("x", CultureInfo.InvariantCulture)).Append('\n');
            text.Append("text = ").Append(Quote(entry.Text)).Append('\n');
            if (entry.Align != TextAlign.Start)
                text.Append("align = ").Append(Quote(entry.Align == TextAlign.Center ? "center" : "end")).Append('\n');
            if (entry.Fit == TextFit.Shrink) text.Append("fit = \"shrink\"\n");
            if (entry.Measure is { } measure)
                text.Append("measure = ").Append(Quote(measure == TextMeasure.Source ? "source" : "presented")).Append('\n');
            if (entry.Cells is { } cells) text.Append("cells = ").Append(cells.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (entry.Note != null) text.Append("note = ").Append(Quote(entry.Note)).Append('\n');
        }
        foreach (EngineTextEntry entry in file.EngineEntries)
        {
            text.Append("\n[[engine]]\n");
            text.Append("key = ").Append(Quote(entry.Key)).Append('\n');
            text.Append("text = ").Append(Quote(entry.Text)).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>A TOML basic string: backslash and quote escaped, control characters as escapes.</summary>
    public static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2).Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': text.Append("\\\\"); break;
                case '"': text.Append("\\\""); break;
                case '\n': text.Append("\\n"); break;
                case '\t': text.Append("\\t"); break;
                case '\r': text.Append("\\r"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                default:
                    if (c < 0x20 || c == 0x7f)
                        text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else text.Append(c);
                    break;
            }
        }
        return text.Append('"').ToString();
    }
}
