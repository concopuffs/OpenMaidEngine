using System.Globalization;
using Tomlyn.Syntax;

namespace Ome.Mods.Data;

/// <summary>
/// The line a data file writes a path at (data patches plan §6.2), from the syntax tree of the reader's validating
/// parse: the model keeps no positions. A path's line is that of the first key-value or table header that writes it,
/// itself or something inside it (<c>stats.max_hp = 5</c> writes <c>stats</c>); for a path the file does not write,
/// that of the deepest one on the path (<c>[unit.761]</c> for <c>unit.761.statz</c>). Paths are lists of keys, an
/// array's element marked with <see cref="DataShapeCheck.IndexMark"/> and its index from 1, as the reader counts them.
/// </summary>
internal static class DataLines
{
    public static int?[] Find(DocumentSyntax syntax, IReadOnlyList<string[]> paths)
    {
        var search = new Search(paths);
        foreach (KeyValueSyntax keyValue in syntax.KeyValues) search.KeyValue([], keyValue);
        // The count of each array of tables so far, by its keys joined with '\0'.
        var arrays = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TableSyntaxBase table in syntax.Tables)
        {
            if (table.Name == null) continue;
            List<string> keys = Keys(table.Name);
            if (table is TableArraySyntax)
            {
                string joined = string.Join('\0', keys);
                arrays[joined] = arrays.GetValueOrDefault(joined) + 1;
                // An array inside this one starts again under its new element.
                foreach (string inner in arrays.Keys.Where(name => name.StartsWith(joined + '\0', StringComparison.Ordinal)).ToList())
                    arrays.Remove(inner);
            }
            var header = new List<string>();
            for (int k = 0; k < keys.Count; k++)
            {
                header.Add(keys[k]);
                if (arrays.TryGetValue(string.Join('\0', keys.Take(k + 1)), out int count))
                    header.Add(Index(count));
            }
            search.Node(header, table.Span.Start.Line + 1);
            if (table.Items != null)
                foreach (KeyValueSyntax keyValue in table.Items) search.KeyValue(header, keyValue);
        }
        return search.Lines;
    }

    private static string Index(int index) => DataShapeCheck.IndexMark + index.ToString(CultureInfo.InvariantCulture);

    private static List<string> Keys(KeySyntax key)
    {
        var keys = new List<string>();
        if (key.Key is { } first) keys.Add(KeyText(first));
        if (key.DotKeys != null)
            foreach (DottedKeyItemSyntax dotted in key.DotKeys)
                if (dotted.Key is { } part) keys.Add(KeyText(part));
        return keys;
    }

    private static string KeyText(BareKeyOrStringValueSyntax key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text ?? "",
        StringValueSyntax quoted => quoted.Value ?? "",
        _ => key.ToString()?.Trim() ?? "",
    };

    private sealed class Search(IReadOnlyList<string[]> paths)
    {
        private readonly bool[] _written = new bool[paths.Count];
        private readonly int[] _depth = new int[paths.Count];

        public int?[] Lines { get; } = new int?[paths.Count];

        public void KeyValue(IReadOnlyList<string> prefix, KeyValueSyntax keyValue)
        {
            if (keyValue.Key == null) return;
            var path = new List<string>(prefix);
            path.AddRange(Keys(keyValue.Key));
            Node(path, keyValue.Span.Start.Line + 1);
            Value(path, keyValue.Value);
        }

        private void Value(List<string> path, ValueSyntax? value)
        {
            switch (value)
            {
                case InlineTableSyntax inline when inline.Items != null:
                    foreach (InlineTableItemSyntax item in inline.Items)
                        if (item.KeyValue != null) KeyValue(path, item.KeyValue);
                    break;
                case ArraySyntax array when array.Items != null:
                    int index = 0;
                    foreach (ArrayItemSyntax item in array.Items)
                    {
                        var element = new List<string>(path) { Index(++index) };
                        if (item.Value == null) continue;
                        Node(element, item.Value.Span.Start.Line + 1);
                        Value(element, item.Value);
                    }
                    break;
            }
        }

        /// <summary>A key-value, a header or an array's element at <paramref name="line"/> writes <paramref name="node"/>.</summary>
        public void Node(IReadOnlyList<string> node, int line)
        {
            for (int k = 0; k < paths.Count; k++)
            {
                if (_written[k]) continue;
                string[] wanted = paths[k];
                if (StartsWith(node, wanted))
                {
                    _written[k] = true;
                    Lines[k] = line;
                }
                else if (node.Count > _depth[k] && StartsWith(wanted, node))
                {
                    _depth[k] = node.Count;
                    Lines[k] = line;
                }
            }
        }

        private static bool StartsWith(IReadOnlyList<string> path, IReadOnlyList<string> prefix)
        {
            if (prefix.Count > path.Count) return false;
            for (int k = 0; k < prefix.Count; k++)
                if (!string.Equals(path[k], prefix[k], StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
