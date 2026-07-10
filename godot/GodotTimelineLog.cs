using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>Diagnostic-only synchronized JSONL stream for correlating VM execution, host waits/audio,
/// and compositor object changes. All producers share one lock, so event order is unambiguous even though
/// the VM and compositor run on different threads.</summary>
public sealed class GodotTimelineLog : System.IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter _writer;
    private long _sequence;
    private int _frame;
    private long _nowMs;
    private string _script = "<startup>";
    private int _offset = -1;
    private int _opcode = -1;
    private string _state = "starting";
    private bool _disposed;

    public GodotTimelineLog(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _writer = new StreamWriter(path) { AutoFlush = true };
        Record("start", new() { ["transition"] = "unmodeled" });
    }

    public void SetFrame(int frame, long nowMs)
    {
        lock (_lock) { _frame = frame; _nowMs = nowMs; }
    }

    public void Step(string script, int offset, int opcode, int depth)
    {
        lock (_lock)
        {
            _script = script; _offset = offset; _opcode = opcode; _state = "running";
            WriteLocked("step", new() { ["depth"] = depth });
        }
    }

    public void State(string state, Dictionary<string, object?>? detail = null)
    {
        lock (_lock) { _state = state; WriteLocked(state, detail); }
    }

    public void Event(string kind, Dictionary<string, object?>? detail = null)
    {
        lock (_lock) WriteLocked(kind, detail);
    }

    private void Record(string kind, Dictionary<string, object?>? detail)
    {
        lock (_lock) WriteLocked(kind, detail);
    }

    private void WriteLocked(string kind, Dictionary<string, object?>? detail)
    {
        if (_disposed) return;
        var row = new Dictionary<string, object?>
        {
            ["seq"] = ++_sequence, ["kind"] = kind, ["frame"] = _frame, ["now_ms"] = _nowMs,
            ["script"] = _script, ["offset"] = _offset < 0 ? null : $"0x{_offset:x}",
            ["opcode"] = _opcode < 0 ? null : $"0x{_opcode:x}", ["vm_state"] = _state,
        };
        if (detail != null) foreach (var kv in detail) row[kv.Key] = kv.Value;
        _writer.WriteLine(JsonSerializer.Serialize(row));
    }

    public void Dispose() { lock (_lock) { if (_disposed) return; _disposed = true; _writer.Dispose(); } }
}
