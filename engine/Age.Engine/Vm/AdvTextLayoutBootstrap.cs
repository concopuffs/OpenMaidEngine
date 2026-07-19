using Age.Engine.Model;

namespace Age.Engine.Vm;

/// <summary>
/// Replays the data-only leading ADV layout block from an engine system script. This lets a bounded
/// single-scene runner inherit script-owned layout state without hard-coding one game's coordinates or
/// entering the system script's later menu/session control flow.
/// </summary>
public static class AdvTextLayoutBootstrap
{
    public static int ApplyLeadingDefinitionsAndResets(
        Script systemScript, OpcodeTable table, AdvTextHistory history)
    {
        int? defineOpcode = table.ByLabel("define-adv-text-layout");
        int? resetOpcode = table.ByLabel("reset-adv-text-layout");
        if (defineOpcode == null || resetOpcode == null) return 0;

        bool foundDefinition = false;
        bool resetPhase = false;
        int definitions = 0;
        foreach (var instruction in systemScript.Instructions)
        {
            if (!foundDefinition && instruction.Opcode != defineOpcode.Value) continue;
            if (instruction.Opcode == defineOpcode.Value && !resetPhase)
            {
                if (instruction.Args.Count != 5 || instruction.Args.Any(arg => arg.Type != 0))
                    throw new InvalidDataException(
                        $"{systemScript.Name}@0x{instruction.Offset:x}: leading ADV layout must use five immediates");
                history.DefineLayout(
                    checked((int)instruction.Args[0].Value), checked((int)instruction.Args[1].Value),
                    checked((int)instruction.Args[2].Value), checked((int)instruction.Args[3].Value),
                    checked((int)instruction.Args[4].Value));
                foundDefinition = true;
                definitions++;
                continue;
            }
            if (foundDefinition && instruction.Opcode == resetOpcode.Value)
            {
                if (instruction.Args.Count != 1 || instruction.Args[0].Type != 0)
                    throw new InvalidDataException(
                        $"{systemScript.Name}@0x{instruction.Offset:x}: leading ADV reset must use one immediate");
                resetPhase = true;
                history.ResetLayout(checked((int)instruction.Args[0].Value));
                continue;
            }
            break;
        }
        return definitions;
    }
}
