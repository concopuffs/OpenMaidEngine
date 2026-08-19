using Age.Engine.Model;
namespace Age.Engine.Diagnostics;

public enum TraceEventKind { Step, FrameEnter, FrameExit, CallScript, Stub, UnsupportedOpcode, Halt }
public enum FrameCause { TopScene, CallScript, RootReload, SaveRestore }

/// <summary>An engine diagnostic fact. A <c>readonly struct</c> with a Kind discriminator and a shared
/// field set — no per-event heap allocation. Only the fields relevant to a Kind are populated; the
/// static factories are the only intended constructors.</summary>
public readonly struct TraceEvent
{
    public TraceEventKind Kind { get; private init; }
    public int    Pc     { get; private init; }
    public int    Opcode { get; private init; }
    public int    Depth  { get; private init; }
    public long   Id     { get; private init; }   // call-script id
    public long   Steps  { get; private init; }   // total steps at Halt
    public FrameCause Cause { get; private init; }
    public string? Name  { get; private init; }    // script/scene name; resolved call-script name (null => unresolved/stub)
    public string? Text  { get; private init; }    // halt reason; frame outcome
    public Instruction? Ins { get; private init; } // Step: the instruction (args) by ref, never copied
    public Vm.UnsupportedOpcodeDiagnostic? CompatibilityDiagnostic { get; private init; }

    public static TraceEvent Step(int pc, Instruction ins, int depth) => new()
        { Kind = TraceEventKind.Step, Pc = pc, Opcode = ins.Opcode, Ins = ins, Depth = depth };
    public static TraceEvent FrameEnter(string name, int depth, FrameCause cause, long id = 0) => new()
        { Kind = TraceEventKind.FrameEnter, Name = name, Depth = depth, Cause = cause, Id = id };
    public static TraceEvent FrameExit(string name, int depth, string outcome) => new()
        { Kind = TraceEventKind.FrameExit, Name = name, Depth = depth, Text = outcome };
    public static TraceEvent CallScript(long id, string? name) => new()
        { Kind = TraceEventKind.CallScript, Id = id, Name = name };
    public static TraceEvent Stub(int opcode, int pc) => new()
        { Kind = TraceEventKind.Stub, Opcode = opcode, Pc = pc };
    public static TraceEvent UnsupportedOpcode(Vm.UnsupportedOpcodeDiagnostic diagnostic, int pc) => new()
        { Kind = TraceEventKind.UnsupportedOpcode, Opcode = diagnostic.Opcode, Pc = pc,
          CompatibilityDiagnostic = diagnostic };
    public static TraceEvent Halt(string reason, long steps) => new()
        { Kind = TraceEventKind.Halt, Text = reason, Steps = steps };
}
