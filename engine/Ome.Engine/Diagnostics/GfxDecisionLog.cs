namespace Ome.Engine.Diagnostics;

/// <summary>
/// <c>--gfx-log</c> and the timeline's <c>objects</c> event: for each frame drawn, only the objects whose lowering
/// outcome changed since the last one (added, gone, drawn and skipped, a resource change). Quiet until something
/// changes, so the frame where a layer drops out, and why, stands out. The outcomes come from the frame lowering
/// (<c>Graphics.LowerFrame</c>'s decisions).
/// </summary>
public sealed class GfxDecisionLog
{
    private readonly string? _path;
    private readonly TimelineLog? _timeline;
    private System.IO.StreamWriter? _writer;
    private readonly Dictionary<long, string> _last = new();
    private readonly Dictionary<long, string> _current = new();
    private int _frame;

    public GfxDecisionLog(string? path, TimelineLog? timeline)
    {
        _path = path;
        _timeline = timeline;
    }

    /// <summary>A log or a timeline wants the outcomes.</summary>
    public bool Enabled => _path != null || _timeline != null;

    /// <summary>The dictionary to lower the next frame's outcomes into, emptied; null when nothing wants them.</summary>
    public Dictionary<long, string>? NextFrame()
    {
        if (!Enabled) return null;
        _current.Clear();
        return _current;
    }

    /// <summary>Report the outcomes <see cref="NextFrame"/> collected, for the frame drawn at <paramref name="nowMs"/>
    /// on page <paramref name="page"/>.</summary>
    public void Record(long nowMs, int page)
    {
        if (_path != null && _writer == null)
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            _writer = new System.IO.StreamWriter(_path) { AutoFlush = true };
        }
        _frame++;
        var lines = new List<string>();
        foreach (var kv in _current)
            if (!_last.TryGetValue(kv.Key, out var prev) || prev != kv.Value)
                lines.Add($"  0x{kv.Key:x}: {kv.Value}" + (_last.ContainsKey(kv.Key) ? "" : "  [NEW]"));
        foreach (var kv in _last)
            if (!_current.ContainsKey(kv.Key))
                lines.Add($"  0x{kv.Key:x}: GONE (was {kv.Value})");
        if (lines.Count > 0 && _writer != null)
        {
            _writer.WriteLine($"[frame {_frame} nowMs={nowMs} page={page}] {_current.Count} visible, {lines.Count} changes:");
            foreach (var line in lines) _writer.WriteLine(line);
        }
        if (lines.Count > 0)
            _timeline?.Event("objects", new() { ["visible_count"] = _current.Count, ["changes"] = lines.ToArray() });
        _last.Clear();
        foreach (var kv in _current) _last[kv.Key] = kv.Value;
    }
}
