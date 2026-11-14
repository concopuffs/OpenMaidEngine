using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // Ops 0x1b2 to 0x1b4: the text is the shell-services native's (ShellServices.PendingDiagnosticText), which a
    // session shares across its scene VMs as native's EngineCtx keeps it.

    // 0x1b2: generic operand text -> EngineCtx accumulator
    private StepResult OpAppendDiagnosticValue(Instruction instruction, int pc)
    {
        IReadOnlyList<Operand> a = instruction.Args;
        _shell.AppendDiagnostic(FormatDiagnosticOperand(a[0]));
        return StepResult.Next;
    }

    // 0x1b3: exact native CRLF bytes
    private StepResult OpAppendDiagnosticNewline(Instruction instruction, int pc)
    {
        _shell.AppendDiagnostic("\r\n");
        return StepResult.Next;
    }

    // 0x1b4: synchronous modal prompt (the waits' dialog wait), then erase
    private StepResult OpShowAndClearDiagnostic(Instruction instruction, int pc)
    {
        _waits.ShowDiagnosticMessage(BuildDiagnosticMessage(instruction));
        _shell.ClearDiagnostic();
        return StepResult.Next;
    }
}
