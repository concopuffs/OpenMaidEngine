using System.Collections.Concurrent;
using System.Collections.Generic;
using Age.Engine.Diagnostics;

// Frontend-side trace consumer. Runs on the VM background thread, so it just queues the dispatched
// call-script ids; the main thread drains them (Godot drops GD.Print from background threads). This
// replaces the old IHost.CallScript -> GodotAdvHost.Dispatched hack: subroutine visibility is now an
// engine fact delivered over the trace seam.
public sealed class GodotTraceSink : ITraceSink
{
    private const int RecentStepCapacity = 128;
    private readonly GodotTimelineLog? _timeline;
    private readonly PageLocatorState _locator;
    private readonly object _snapshotLock = new();
    private readonly Stack<string> _scripts = new();
    private readonly Queue<GodotTraceStepSnapshot> _recentSteps = new();
    private GodotTraceStepSnapshot? _latestStep;
    private GodotTraceSnapshot? _haltSnapshot;
    public GodotTraceSink(PageLocatorState locator, GodotTimelineLog? timeline = null)
    { _locator = locator; _timeline = timeline; }
    // The page locator needs the exact script/offset even when the heavier timeline log is disabled.
    public bool TracingSteps => true;
    public readonly ConcurrentQueue<long> CallScripts = new();
    public void Emit(in TraceEvent e)
    {
        if (e.Kind == TraceEventKind.CallScript)
        {
            CallScripts.Enqueue(e.Id);
            _timeline?.Event("call-script", new()
            {
                ["id"] = $"0x{e.Id:x}", ["resolved_name"] = e.Name,
            });
        }
        if (e.Kind == TraceEventKind.FrameEnter && e.Name != null)
        {
            string[] callStack;
            lock (_snapshotLock)
            {
                _scripts.Push(e.Name);
                callStack = CurrentCallStackLocked();
            }
            _locator.CallStack(callStack);
            _timeline?.Event("frame-enter", new()
            {
                ["name"] = e.Name, ["depth"] = e.Depth,
                ["cause"] = e.Cause.ToString(), ["call_id"] = $"0x{e.Id:x}",
            });
        }
        else if (e.Kind == TraceEventKind.FrameExit && _scripts.Count > 0)
        {
            _timeline?.Event("frame-exit", new()
            {
                ["name"] = e.Name, ["depth"] = e.Depth, ["outcome"] = e.Text,
            });
            string[] callStack;
            lock (_snapshotLock)
            {
                if (e.Text == "Halted" && _haltSnapshot == null)
                    _haltSnapshot = SnapshotLocked();
                if (_scripts.Count > 0) _scripts.Pop();
                callStack = CurrentCallStackLocked();
            }
            _locator.CallStack(callStack);
        }
        else if (e.Kind == TraceEventKind.Step && e.Ins != null)
        {
            string script;
            lock (_snapshotLock)
            {
                script = _scripts.Count > 0 ? _scripts.Peek() : "<unknown>";
                if (_recentSteps.Count == RecentStepCapacity) _recentSteps.Dequeue();
                var step = new GodotTraceStepSnapshot(script, e.Ins.Offset, e.Opcode, e.Depth);
                _latestStep = step;
                _recentSteps.Enqueue(step);
            }
            _locator.Step(script, e.Ins.Offset);
            _timeline?.Step(script, e.Ins.Offset, e.Opcode, e.Depth);
        }
        else if (e.Kind == TraceEventKind.Stub)
            _timeline?.Event("stub", new()
            {
                ["stub_opcode"] = $"0x{e.Opcode:x}", ["pc_index"] = e.Pc,
            });
        else if (e.Kind == TraceEventKind.UnsupportedOpcode
                 && e.CompatibilityDiagnostic is { } diagnostic)
            _timeline?.Event("unsupported-opcode", new()
            {
                ["profile_id"] = diagnostic.ProfileId,
                ["engine_abi_id"] = diagnostic.EngineAbiId,
                ["script_revision"] = diagnostic.ScriptRevision,
                ["script_name"] = diagnostic.ScriptName,
                ["packed_script_id"] = $"0x{diagnostic.PackedScriptId:x}",
                ["offset"] = $"0x{diagnostic.Offset:x}",
                ["opcode"] = $"0x{diagnostic.Opcode:x}",
                ["canonical_label"] = diagnostic.CanonicalLabel,
                ["operands"] = diagnostic.Operands.Select(value => $"{value.Type}:{value.Value}").ToArray(),
            });
        else if (e.Kind == TraceEventKind.Halt)
            _timeline?.State("halted", new() { ["reason"] = e.Text, ["steps"] = e.Steps });
    }

    public GodotTraceSnapshot Snapshot()
    {
        lock (_snapshotLock) return SnapshotLocked();
    }

    /// <summary>The deepest still-active script stack captured before a halted frame unwinds.</summary>
    public GodotTraceSnapshot? HaltSnapshot
    {
        get { lock (_snapshotLock) return _haltSnapshot; }
    }

    /// <summary>Allocation-free current coordinate for once-per-frame diagnostics.</summary>
    public GodotTraceStepSnapshot? LatestStep
    {
        get { lock (_snapshotLock) return _latestStep; }
    }

    private string[] CurrentCallStackLocked()
    {
        var stack = _scripts.ToArray();
        System.Array.Reverse(stack);
        return stack;
    }

    private GodotTraceSnapshot SnapshotLocked()
    {
        GodotTraceStepSnapshot? current = _latestStep;
        return new GodotTraceSnapshot(
            current?.Script ?? (_scripts.Count > 0 ? _scripts.Peek() : "<unknown>"),
            current?.Offset ?? -1,
            current?.Opcode ?? -1,
            current?.Depth ?? System.Math.Max(0, _scripts.Count - 1),
            CurrentCallStackLocked(),
            _recentSteps.ToArray());
    }
}

public sealed record GodotTraceStepSnapshot(string Script, int Offset, int Opcode, int Depth);

public sealed record GodotTraceSnapshot(string CurrentScript, int CurrentOffset, int CurrentOpcode,
                                        int CurrentDepth, IReadOnlyList<string> CallStack,
                                        IReadOnlyList<GodotTraceStepSnapshot> RecentSteps);
