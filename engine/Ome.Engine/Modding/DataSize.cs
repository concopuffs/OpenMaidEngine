using System.Globalization;

namespace Ome.Engine.Modding;

/// <summary>
/// The size of a data file's document (data patches plan §12.6): its <paramref name="Values"/> (each string, boolean and
/// number, each element of an array too), its <paramref name="Tables"/> (a header's, a dotted key's, an inline one, each
/// table of an array of tables, and each a header or a dotted key makes on the way) and its <paramref name="Arrays"/>
/// (an inline array, an array of tables); an empty table or array counts, the document's own table does not. The
/// <see cref="Total"/> is the one size: the document's bound, a delivery's budget and a shape's step limit. The report
/// shows the parts (<see cref="Describe"/>).
/// </summary>
public readonly record struct DataSize(int Values, int Tables, int Arrays)
{
    public int Total => Values + Tables + Arrays;

    /// <summary>As the report prints it: <c>1 value in 3 tables</c>, <c>3 values in 1 table and 1 array</c>,
    /// <c>2 values</c> at the top of the file, <c>no value in 8 tables</c>, <c>no value</c>.</summary>
    public string Describe()
    {
        string values = Values == 0 ? "no value" : Count(Values, "value");
        var holders = new List<string>(2);
        if (Tables > 0) holders.Add(Count(Tables, "table"));
        if (Arrays > 0) holders.Add(Count(Arrays, "array"));
        return holders.Count == 0 ? values : $"{values} in {string.Join(" and ", holders)}";
    }

    private static string Count(int count, string noun)
        => count.ToString("N0", CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? "" : "s");
}
