using System.Text;
using Ome.Engine.Modding;

namespace Ome.Mods.Data;

/// <summary>
/// The paths a document gives a value at (data patches spec §5.7): the keys from the root down to a value that is not a
/// table, joined by dots, a key that is not a bare TOML key written as a quoted one (plan §4.2). An array is one path.
/// Paths compare as text: the engine gives a key no meaning.
/// </summary>
public static class DataPaths
{
    /// <summary>Each path of <paramref name="root"/>, in the document's order.</summary>
    public static IReadOnlyList<string> Of(DataTable root)
    {
        var paths = new List<string>();
        Walk(root, new StringBuilder(), paths);
        return paths;
    }

    private static void Walk(DataTable table, StringBuilder prefix, List<string> paths)
    {
        int length = prefix.Length;
        foreach (var (key, value) in table.Entries)
        {
            if (length > 0) prefix.Append('.');
            prefix.Append(DataValue.KeyText(key));
            if (value is DataTable inner) Walk(inner, prefix, paths);
            else paths.Add(prefix.ToString());
            prefix.Length = length;
        }
    }
}
