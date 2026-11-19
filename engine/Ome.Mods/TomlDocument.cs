using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Serialization;
using Tomlyn.Syntax;
using Tomlyn.Text;

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
    /// <remarks>Tomlyn's serializer keeps the last of a repeated key or <c>[table]</c> without a word, and drops the
    /// earlier tables of an array of tables that another table interrupts; its validating syntax parser takes two
    /// different keys for one at random (<see cref="TomlStructure"/>). So the text goes through
    /// <see cref="TomlStructure"/> first, and a syntax error keeps the serializer's own message.</remarks>
    public static TomlTable? Parse(string text)
    {
        TomlStructureResult structure = Checked(text);
        TomlTable? table = Model(text);
        Followed(structure);
        return table;
    }

    /// <summary><see cref="Parse(string)"/>, giving the 1-based line of each top-level <c>[[name]]</c> header too, by
    /// the name's decoded text, in order, for a reader that names a table by its line. The lines come from the check's
    /// walk, so the text is parsed no more: on the largest converted text catalog (5.4 million characters) a syntax tree
    /// for them cost about 185 ms of a 475 ms read (data patches plan §12.6).</summary>
    public static TomlTable? Parse(string text, out IReadOnlyDictionary<string, IReadOnlyList<int>> arrayLines)
    {
        var lines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        TomlStructureResult structure = Checked(text, new TomlStructureOptions
        {
            ArrayHeader = (name, line) =>
            {
                if (!lines.TryGetValue(name, out List<int>? starts)) lines[name] = starts = [];
                starts.Add(line);
            },
        });
        TomlTable? table = Model(text);
        Followed(structure);
        arrayLines = lines.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value, StringComparer.Ordinal);
        return table;
    }

    /// <summary>Tomlyn's serializer's model, which takes a repeated key and loses an interrupted array of tables: the
    /// caller checks the text with <see cref="TomlStructure"/> (the data files' reader, <c>Data/DataDocumentReader.cs</c>,
    /// walks it first, to bound its size before the model is built).</summary>
    internal static TomlTable? Model(string text) => TomlSerializer.Deserialize(text, TomlDocumentContext.Default.TomlTable);

    /// <summary>The reason given where <see cref="TomlStructure"/> stops at a text the serializer took: none of
    /// toml-test's valid files stops it (data patches plan §12.6), so the walk's own gap is refused rather than passed
    /// unchecked.</summary>
    internal const string Unfollowed = "the text here is TOML that the engine's check of its tables does not follow";

    /// <summary>The walk of <paramref name="text"/>; throws, as Tomlyn's validating parser did, for each problem it
    /// found.</summary>
    private static TomlStructureResult Checked(string text, TomlStructureOptions? options = null)
    {
        TomlStructureResult structure = TomlStructure.Walk(text, options);
        if (structure.Problems.Count == 0) return structure;
        var diagnostics = new DiagnosticsBag();
        foreach (TomlStructureProblem problem in structure.Problems) diagnostics.Error(Span(problem.At), problem.Message);
        throw new TomlException(diagnostics);
    }

    /// <summary>Throws where the walk stopped at a text the parsers took.</summary>
    private static void Followed(TomlStructureResult structure)
    {
        if (structure.Unread is not { } unread) return;
        var diagnostics = new DiagnosticsBag();
        diagnostics.Error(Span(unread), Unfollowed);
        throw new TomlException(diagnostics);
    }

    private static SourceSpan Span(TomlTextPosition at)
    {
        var position = new TextPosition(at.Offset, at.Line, at.Column);
        return new SourceSpan("", position, position);
    }
}

[TomlSerializable(typeof(TomlTable))]
internal sealed partial class TomlDocumentContext : TomlSerializerContext;
