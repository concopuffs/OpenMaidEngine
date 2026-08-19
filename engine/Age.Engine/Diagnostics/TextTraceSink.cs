using System.Collections.Generic;
using System.Linq;
using Age.Engine.Model;
namespace Age.Engine.Diagnostics;

/// <summary>The built-in text formatter: writes each event as a deterministic line to a TextWriter
/// (Console.Out or a file), indented by frame depth. Step lines are tagged with the current script
/// (the "which script is this op in" answer) and, when <paramref name="opFilter"/> is set, only ops in
/// the filter are printed — so you can watch just sleep/draw-texture/wait-for-input in execution order
/// instead of a multi-million-line flood. If an OpcodeTable is supplied, Step lines show the mnemonic.</summary>
public sealed class TextTraceSink : TraceSinkBase
{
    private readonly TextWriter _w;
    private readonly OpcodeTable? _table;
    private readonly bool _steps;
    private readonly HashSet<int>? _opFilter;   // null = all ops; else only these ops' Step/Stub lines

    public TextTraceSink(TextWriter writer, OpcodeTable? table = null, bool includeSteps = false,
                         HashSet<int>? opFilter = null)
    { _w = writer; _table = table; _opFilter = opFilter; _steps = includeSteps || opFilter != null; }

    public override bool TracingSteps => _steps;

    protected override void OnEvent(in TraceEvent e)
    {
        string indent = new string(' ', System.Math.Max(0, e.Depth - 1) * 2);
        switch (e.Kind)
        {
            case TraceEventKind.FrameEnter:
                _w.WriteLine($"{indent}» {e.Name} (enter, {e.Cause})"); break;
            case TraceEventKind.FrameExit:
                _w.WriteLine($"{indent}« {e.Name} ({e.Text})"); break;
            case TraceEventKind.Step:
                if (_opFilter != null && !_opFilter.Contains(e.Opcode)) break;
                _w.WriteLine($"{indent}  {CurrentScript}:{e.Pc:x4} {Mnemonic(e.Opcode)} {Args(e.Ins)}"); break;
            case TraceEventKind.CallScript:
                _w.WriteLine($"{indent}  call-script 0x{e.Id:x} ={e.Name ?? "?"} " +
                             $"({(e.Name != null ? "resolved" : "stub/unresolved")})"); break;
            case TraceEventKind.Stub:
                if (_opFilter != null && !_opFilter.Contains(e.Opcode)) break;
                _w.WriteLine($"{indent}  {CurrentScript}:{e.Pc:x4} STUB op=0x{e.Opcode:x}"); break;
            case TraceEventKind.UnsupportedOpcode:
                _w.WriteLine($"{indent}  UNSUPPORTED {e.CompatibilityDiagnostic}"); break;
            case TraceEventKind.Halt:
                _w.WriteLine($"halt: {e.Text} @ {e.Steps} steps"); break;
        }
    }

    private string Mnemonic(int op) => _table?.Label(op) is { Length: > 0 } l ? l : $"0x{op:x}";
    private static string Args(Instruction? ins) =>
        ins == null ? "" : string.Join(" ", ins.Args.Select(o => $"{o.Type}:{o.Value}"));
}
