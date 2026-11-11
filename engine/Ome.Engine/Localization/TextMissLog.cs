using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ome.Engine.Localization;

/// <summary>
/// Every string that reached a presentation site without a catalog entry, with the number of times it did
/// (localization spec §14, <c>--text-log</c>). Playing the game, or running the drive scenarios, finds the strings
/// static extraction cannot. The presenter records on the VM thread; the host writes the log from another, so the
/// counts are locked.
/// </summary>
public sealed class TextMissLog
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _lock = new();
    private readonly Dictionary<(string Source, string Script, int Offset), int> _counts = [];
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

    public int Count
    {
        get { lock (_lock) return _counts.Count; }
    }

    /// <summary>One JSON object per line, ordered by script and offset: <c>source</c>, <c>script</c>, <c>at</c> (the
    /// instruction's offset, in the F3 locator's hex form) and <c>count</c>.</summary>
    public void WriteJsonLines(TextWriter writer)
    {
        KeyValuePair<(string Source, string Script, int Offset), int>[] rows;
        lock (_lock)
        {
            rows = _counts.OrderBy(row => row.Key.Script, StringComparer.Ordinal).ThenBy(row => row.Key.Offset)
                .ThenBy(row => row.Key.Source, StringComparer.Ordinal).ToArray();
            _writtenVersion = _version;
        }
        foreach (var ((source, script, offset), count) in rows)
            writer.WriteLine(JsonSerializer.Serialize(new { source, script, at = $"0x{offset:x}", count }, Json));
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
