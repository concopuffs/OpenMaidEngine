using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ome.Engine.Localization;

/// <summary>
/// Every string that reached a presentation site without a catalog entry, with the number of times it did
/// (localization spec §14, <c>--text-log</c>), every presented message narrowed so that it fitted its window, and
/// every one cut at the bottom of its window (§7.3). Playing the game, or running the drive scenarios, finds the
/// strings static extraction cannot. The presenter records on the VM thread; the host writes the log from another, so
/// the counts are locked.
/// </summary>
public sealed class TextMissLog
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _lock = new();
    private readonly Dictionary<(string Source, string Script, int Offset), int> _counts = [];
    private readonly Dictionary<(string Source, string Script, int Offset), (int Count, int Percent)> _narrowed = [];
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
    /// <paramref name="site"/>, was narrowed to <paramref name="widthPercent"/> of its glyphs' width so that it was not
    /// cut. The log keeps the narrowest.</summary>
    public void RecordNarrowed(string source, TextSite site, int widthPercent)
    {
        var key = (source, TextCatalog.NormalizeScriptName(site.Script), site.Offset);
        lock (_lock)
        {
            (int count, int narrowest) = _narrowed.GetValueOrDefault(key, (0, 100));
            _narrowed[key] = (count + 1, System.Math.Min(narrowest, widthPercent));
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

    /// <summary>The messages narrowed to fit.</summary>
    public int NarrowedCount
    {
        get { lock (_lock) return _narrowed.Count; }
    }

    /// <summary>The messages cut.</summary>
    public int CutCount
    {
        get { lock (_lock) return _cuts.Count; }
    }

    /// <summary>One JSON object per line, ordered by script and offset: <c>source</c>, <c>script</c>, <c>at</c> (the
    /// instruction's offset, in the F3 locator's hex form) and <c>count</c>. A narrowed message's line also has
    /// <c>width_percent</c>, and a cut message's <c>rows_cut</c>; at one place a miss comes first, then a narrowing,
    /// then a cut.</summary>
    public void WriteJsonLines(TextWriter writer)
    {
        (string Source, string Script, int Offset, int Kind, int Count, int Value)[] rows;
        lock (_lock)
        {
            rows = _counts.Select(row => (row.Key.Source, row.Key.Script, row.Key.Offset, 0, row.Value, 0))
                .Concat(_narrowed.Select(row => (row.Key.Source, row.Key.Script, row.Key.Offset, 1, row.Value.Count,
                                                 row.Value.Percent)))
                .Concat(_cuts.Select(row => (row.Key.Source, row.Key.Script, row.Key.Offset, 2, row.Value.Count,
                                             row.Value.Rows)))
                .OrderBy(row => row.Script, StringComparer.Ordinal).ThenBy(row => row.Offset)
                .ThenBy(row => row.Source, StringComparer.Ordinal).ThenBy(row => row.Item4).ToArray();
            _writtenVersion = _version;
        }
        foreach ((string source, string script, int offset, int kind, int count, int value) in rows)
        {
            string at = $"0x{offset:x}";
            writer.WriteLine(kind switch
            {
                1 => JsonSerializer.Serialize(new { source, script, at, count, width_percent = value }, Json),
                2 => JsonSerializer.Serialize(new { source, script, at, count, rows_cut = value }, Json),
                _ => JsonSerializer.Serialize(new { source, script, at, count }, Json),
            });
        }
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
