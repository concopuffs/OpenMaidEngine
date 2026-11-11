using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ome.Engine.Localization;

/// <summary>
/// Every string that reached a presentation site without a catalog entry, with the number of times it did
/// (localization spec §14, <c>--text-log</c>), and every presented message cut at the bottom of its window (§7.3).
/// Playing the game, or running the drive scenarios, finds the strings static extraction cannot. The presenter
/// records on the VM thread; the host writes the log from another, so the counts are locked.
/// </summary>
public sealed class TextMissLog
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _lock = new();
    private readonly Dictionary<(string Source, string Script, int Offset), int> _counts = [];
    private readonly Dictionary<(string Source, string Script, int Offset), (int Count, int Rows)> _cuts = [];
    private int _version;
    private int _writtenVersion;

    public void Record(string source, TextSite site)
    {
        var key = (source, TextCatalog.NormalizeScriptName(site.Script), site.Offset);
        lock (_lock)
        {
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
            _version++;
        }
    }

    /// <summary>Records that the presented text of the message <paramref name="source"/>, drawn at
    /// <paramref name="site"/>, was cut at its window's bottom with <paramref name="rows"/> rows undrawn. The log keeps
    /// the most rows any time cut.</summary>
    public void RecordCut(string source, TextSite site, int rows)
    {
        var key = (source, TextCatalog.NormalizeScriptName(site.Script), site.Offset);
        lock (_lock)
        {
            (int count, int most) = _cuts.GetValueOrDefault(key);
            _cuts[key] = (count + 1, System.Math.Max(most, rows));
            _version++;
        }
    }

    /// <summary>The strings missed.</summary>
    public int Count
    {
        get { lock (_lock) return _counts.Count; }
    }

    /// <summary>The messages cut.</summary>
    public int CutCount
    {
        get { lock (_lock) return _cuts.Count; }
    }

    /// <summary>One JSON object per line, ordered by script and offset: <c>source</c>, <c>script</c>, <c>at</c> (the
    /// instruction's offset, in the F3 locator's hex form) and <c>count</c>. A cut message's line also has
    /// <c>rows_cut</c>, and follows a miss at the same place.</summary>
    public void WriteJsonLines(TextWriter writer)
    {
        (string Source, string Script, int Offset, int Count, int? Rows)[] rows;
        lock (_lock)
        {
            rows = _counts.Select(row => (row.Key.Source, row.Key.Script, row.Key.Offset, row.Value, (int?)null))
                .Concat(_cuts.Select(row => (row.Key.Source, row.Key.Script, row.Key.Offset, row.Value.Count,
                                             (int?)row.Value.Rows)))
                .OrderBy(row => row.Script, StringComparer.Ordinal).ThenBy(row => row.Offset)
                .ThenBy(row => row.Source, StringComparer.Ordinal).ThenBy(row => row.Item5 != null).ToArray();
            _writtenVersion = _version;
        }
        foreach ((string source, string script, int offset, int count, int? cut) in rows)
            writer.WriteLine(cut is { } rowsCut
                ? JsonSerializer.Serialize(new { source, script, at = $"0x{offset:x}", count, rows_cut = rowsCut }, Json)
                : JsonSerializer.Serialize(new { source, script, at = $"0x{offset:x}", count }, Json));
    }

    /// <summary>Writes the log to <paramref name="path"/> if it changed since the last write; returns whether it
    /// wrote.</summary>
    public bool WriteIfChanged(string path)
    {
        lock (_lock)
            if (_version == _writtenVersion) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        using (var writer = new StreamWriter(temporary, append: false, new System.Text.UTF8Encoding(false)))
            WriteJsonLines(writer);
        File.Move(temporary, path, overwrite: true);
        return true;
    }
}
