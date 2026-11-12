using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0x1b2: generic operand text -> EngineCtx accumulator
    private StepResult OpAppendDiagnosticValue(Instruction instruction, int pc)
    {
        IReadOnlyList<Operand> a = instruction.Args;
        _diagnosticOutput.Append(FormatDiagnosticOperand(a[0]));
        return StepResult.Next;
    }

    // 0x1b3: exact native CRLF bytes
    private StepResult OpAppendDiagnosticNewline(Instruction instruction, int pc)
    {
        _diagnosticOutput.Append("\r\n");
        return StepResult.Next;
    }

    // 0x1b4: synchronous host prompt, then erase
    private StepResult OpShowAndClearDiagnostic(Instruction instruction, int pc)
    {
        _host.ShowDiagnosticMessage(BuildDiagnosticMessage(instruction));
        _diagnosticOutput.Clear();
        return StepResult.Next;
    }
}
