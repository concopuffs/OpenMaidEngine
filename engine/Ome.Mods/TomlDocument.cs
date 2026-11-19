using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Serialization;
using Tomlyn.Syntax;

namespace Ome.Mods;

/// <summary>
/// Parses TOML text into Tomlyn's document model, the one form every TOML reader of the engine and the launcher takes.
/// The engine ships compiled with NativeAOT, where Tomlyn's reflection serializer is off, so the parse goes through a
/// source-generated context (<see cref="TomlDocumentContext"/>).
/// </summary>
public static class TomlDocument
{
    /// <summary>The document's root table, or null where Tomlyn's serializer gives none. Throws
    /// <see cref="TomlException"/>, with the line, for a syntax error and for a key or a table that the text gives
    /// twice, which TOML forbids.</summary>
    /// <remarks>Tomlyn's serializer keeps the last of a repeated key or <c>[table]</c> without a word; only its
    /// validating syntax parser refuses one. So a text the serializer takes is validated too, and a syntax error keeps
    /// the serializer's own message.</remarks>
    public static TomlTable? Parse(string text) => Parse(text, out _);

    /// <summary><see cref="Parse(string)"/>, giving the validated syntax tree too, whose spans give each table's line,
    /// so that a reader that needs lines does not parse the text again: on a converted text catalog of 5 MB a syntax
    /// parse takes about a quarter of a second (data patches plan §11).</summary>
    public static TomlTable? Parse(string text, out DocumentSyntax syntax)
    {
        TomlTable? table = Model(text);
        syntax = SyntaxParser.Parse(text, null, validate: true);
        if (syntax.HasErrors) throw new TomlException(syntax.Diagnostics);
        return table;
    }

    /// <summary>The model of a text that <see cref="SyntaxParser"/> has already validated, so that a large file is
    /// not validated twice (the data files' reader, <c>Data/DataDocumentReader.cs</c>).</summary>
    internal static TomlTable? Model(string text) => TomlSerializer.Deserialize(text, TomlDocumentContext.Default.TomlTable);
}

[TomlSerializable(typeof(TomlTable))]
internal sealed partial class TomlDocumentContext : TomlSerializerContext;
