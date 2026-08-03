using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepDiagnostic(string label, Instruction instruction, int pc)
    {
        IReadOnlyList<Operand> a = instruction.Args;
        switch (label)
        {
            case "u00425790": // upstream ABI label
            case "append-diagnostic-value": // 0x1b2: generic operand text -> EngineCtx accumulator
                _diagnosticOutput.Append(FormatDiagnosticOperand(a[0]));
                return pc + 1;
            case "u004257D0": // upstream ABI label
            case "append-diagnostic-newline": // 0x1b3: exact native CRLF bytes
                _diagnosticOutput.Append("\r\n");
                return pc + 1;
            case "u004237C0": // upstream ABI label
            case "show-and-clear-diagnostic": // 0x1b4: synchronous host prompt, then erase
                _host.ShowDiagnosticMessage(BuildDiagnosticMessage(instruction));
                _diagnosticOutput.Clear();
                return pc + 1;
            default:
                throw new InvalidOperationException($"Non-diagnostic opcode routed to diagnostic handler: {label}");
        }
    }
}
