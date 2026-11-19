namespace Ome.Mods.Data;

/// <summary>
/// The line a data file writes a path at (data patches plan §6.2), from a second <see cref="TomlStructure"/> walk of
/// the text, made only for a document with problems: the model keeps no positions. A path's line is that of the first
/// key-value, table header or array element that writes it, itself or something inside it (<c>by_id.16.name = "x"</c>
/// writes <c>by_id</c>); for a path the file does not write, that of the deepest one on the path (<c>[by_id.16]</c> for
/// <c>by_id.16.namez</c>). Paths are lists of keys, an array's element marked with <see cref="TomlStructure.IndexMark"/>
/// and its index from 1, as the reader counts them. The walk keeps each array of tables' count in its tree, so the cost
/// grows with the text, not with the headers squared (plan §12.6).
/// </summary>
internal static class DataLines
{
    public static int?[] Find(string text, IReadOnlyList<string[]> paths)
    {
        var search = new Search(paths);
        TomlStructure.Walk(text, new TomlStructureOptions { Written = search.Node });
        return search.Lines;
    }

    private sealed class Search(IReadOnlyList<string[]> paths)
    {
        private readonly bool[] _written = new bool[paths.Count];
        private readonly int[] _depth = new int[paths.Count];

        public int?[] Lines { get; } = new int?[paths.Count];

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
