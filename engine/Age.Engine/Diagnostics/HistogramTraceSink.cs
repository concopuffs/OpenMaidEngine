using System.Collections.Generic;
using System.IO;
using System.Linq;
using Age.Engine.Model;
namespace Age.Engine.Diagnostics;

/// <summary>Aggregating diagnostic sink: counts opcode executions overall and per call-site
/// (script, pc), keeping a sample first operand. Answers "how many times did op X run, and from
/// where?" directly — the question that, this session, took a 2.5M-line text dump + grep/awk to
/// answer (sleep ran 493,175× from one call-site). Observe-only → trace parity preserved.
///
/// Thread-safe: Emit runs on the VM thread; WriteReport/OpCount may be read from another thread
/// (e.g. the Godot main thread at scene end), so both take a lock.</summary>
public sealed class HistogramTraceSink : TraceSinkBase
{
    public override bool TracingSteps => true;   // needs every Step

    private readonly object _lock = new();
    private readonly Dictionary<int, long> _opCounts = new();
    private readonly Dictionary<(string Script, int Pc, int Op), Site> _sites = new();
    private long _totalSteps;

    private sealed class Site { public long Count; public long SampleArg0; public bool HasArg; }

    protected override void OnEvent(in TraceEvent e)
    {
        if (e.Kind != TraceEventKind.Step) return;
        var args = e.Ins?.Args;
        bool hasArg = args is { Count: > 0 };
        long arg0 = hasArg ? args![0].Value : 0;
        var key = (CurrentScript, e.Pc, e.Opcode);
        lock (_lock)
        {
            _totalSteps++;
            _opCounts[e.Opcode] = _opCounts.GetValueOrDefault(e.Opcode) + 1;
            if (!_sites.TryGetValue(key, out var s)) { s = new Site(); _sites[key] = s; }
            s.Count++;
            if (hasArg) { s.SampleArg0 = arg0; s.HasArg = true; }
        }
    }

    /// <summary>How many times the given opcode executed.</summary>
    public long OpCount(int op) { lock (_lock) return _opCounts.GetValueOrDefault(op); }

    /// <summary>Total instructions executed (all frames).</summary>
    public long TotalSteps { get { lock (_lock) return _totalSteps; } }

    /// <summary>Write a sorted report: the opcode histogram, then the hottest call-sites (each tagged
    /// with the script it ran in and a sample first operand).</summary>
    public void WriteReport(TextWriter w, OpcodeTable? table = null, int topSites = 50)
    {
        string Mn(int op) => table?.Label(op) is { Length: > 0 } l ? l : $"0x{op:x}";
        lock (_lock)
        {
            w.WriteLine($"=== opcode execution histogram — {_totalSteps} steps, {_opCounts.Count} distinct ops ===");
            w.WriteLine($"{"count",12}  op     mnemonic");
            foreach (var kv in _opCounts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
                w.WriteLine($"{kv.Value,12}  0x{kv.Key:x3}  {Mn(kv.Key)}");

            w.WriteLine($"=== hottest call-sites (top {topSites} of {_sites.Count}) ===");
            w.WriteLine($"{"count",12}  script:pc  mnemonic  sample-arg0");
            foreach (var kv in _sites.OrderByDescending(k => k.Value.Count).ThenBy(k => k.Key.Script).Take(topSites))
            {
                var (script, pc, op) = kv.Key;
                var s = kv.Value;
                string arg = s.HasArg ? $"  arg0={s.SampleArg0}" : "";
                w.WriteLine($"{s.Count,12}  {script}:0x{pc:x}  {Mn(op)}{arg}");
            }
            w.Flush();
        }
    }
}
