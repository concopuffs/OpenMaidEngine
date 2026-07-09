using System.Collections.Generic;
namespace Age.Engine.Diagnostics;

/// <summary>Records the script-relative offset of every instruction executed in one target script, in
/// execution order — the VM side of the differential offset-path oracle (docs/engine-re.md). Offsets are
/// the instruction's <see cref="Model.Instruction.Offset"/> (the bytecode WORD index, same unit the engine
/// tracer emits as <c>(pc-codebase)/4</c>), so the two sequences are directly comparable.
///
/// Filters to the target frame: only Step events whose innermost running script is the target are recorded,
/// so call-script subroutines into OTHER scripts are excluded — matching the engine trace's per-codebase
/// filter, which isolates the scene from boot/system scripts. Observe-only (emits nothing, touches no VM
/// state) so trace parity is preserved.</summary>
public sealed class JsonOffsetTraceSink : TraceSinkBase
{
    private readonly string _target;
    private readonly List<int> _offsets = new();

    public JsonOffsetTraceSink(string targetScript) { _target = targetScript; }

    /// <summary>Executed offsets of the target script, in order.</summary>
    public IReadOnlyList<int> Offsets => _offsets;

    public override bool TracingSteps => true;

    protected override void OnEvent(in TraceEvent e)
    {
        if (e.Kind == TraceEventKind.Step && CurrentScript == _target && e.Ins is { } ins)
            _offsets.Add(ins.Offset);
    }
}
