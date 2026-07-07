using Age.Engine.Model;
namespace Age.Engine.Diagnostics;

/// <summary>The one built-in formatter: writes each event as a deterministic text line to a
/// TextWriter (Console.Out or a file). Indents by frame depth. If an OpcodeTable is supplied, Step
/// lines show the mnemonic; otherwise the raw opcode. Step lines only appear when includeSteps is set.</summary>
public sealed class TextTraceSink : ITraceSink
{
    private readonly TextWriter _w;
    private readonly OpcodeTable? _table;
    private readonly bool _steps;

    public TextTraceSink(TextWriter writer, OpcodeTable? table = null, bool includeSteps = false)
    { _w = writer; _table = table; _steps = includeSteps; }

    public bool TracingSteps => _steps;

    public void Emit(in TraceEvent e)
    {
        string indent = new string(' ', Math.Max(0, e.Depth - 1) * 2);
        switch (e.Kind)
        {
            case TraceEventKind.FrameEnter:
                _w.WriteLine($"{indent}» {e.Name} (enter, {e.Cause})"); break;
            case TraceEventKind.FrameExit:
                _w.WriteLine($"{indent}« {e.Name} ({e.Text})"); break;
            case TraceEventKind.Step:
                _w.WriteLine($"{indent}  {e.Pc:x4} {Mnemonic(e.Opcode)} {Args(e.Ins)}"); break;
            case TraceEventKind.CallScript:
                _w.WriteLine($"{indent}  call-script 0x{e.Id:x} ={e.Name ?? "?"} " +
                             $"({(e.Name != null ? "resolved" : "stub/unresolved")})"); break;
            case TraceEventKind.Stub:
                _w.WriteLine($"{indent}  {e.Pc:x4} STUB op=0x{e.Opcode:x}"); break;
            case TraceEventKind.Halt:
                _w.WriteLine($"halt: {e.Text} @ {e.Steps} steps"); break;
        }
    }

    private string Mnemonic(int op) => _table?.Label(op) is { Length: > 0 } l ? l : $"0x{op:x}";
    private static string Args(Instruction? ins) =>
        ins == null ? "" : string.Join(" ", ins.Args.Select(o => $"{o.Type}:{o.Value}"));
}
