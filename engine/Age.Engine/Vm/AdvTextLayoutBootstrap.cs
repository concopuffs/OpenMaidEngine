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
        int? resetCursorOpcode = table.ByLabel("set-adv-text-reset-cursor");
        int? boundsOpcode = table.ByLabel("set-adv-text-bounds");
        int? waitIndicatorHandleOpcode = table.ByLabel("set-adv-wait-indicator-handle");
        int? textObjectRangeOpcode = table.ByLabel("set-adv-text-object-range");
        int? addOpcode = table.ByLabel("add");
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

        // SYSTEM4 configures the cursor restored by future resets and the right/bottom overflow bounds
        // after the initial define/reset run. Most operands are immediate; slot 1 computes its bounds with
        // two constant add instructions. Interpret only that small, data-only expression vocabulary rather
        // than entering SYSTEM4's later menu/session flow.
        if (resetCursorOpcode != null && boundsOpcode != null)
        {
            var localInts = new Dictionary<int, long>();
            bool inLayoutBlock = false;
            int cursorConfigurations = 0;
            int boundConfigurations = 0;
            int objectRangeConfigurations = 0;
            foreach (var instruction in systemScript.Instructions)
            {
                if (!inLayoutBlock)
                {
                    if (instruction.Opcode != defineOpcode.Value) continue;
                    inLayoutBlock = true;
                }

                if (addOpcode != null && instruction.Opcode == addOpcode.Value)
                {
                    if (instruction.Args.Count == 3 && instruction.Args[0].Type == 9
                        && TryResolveConstant(instruction.Args[1], localInts, out long left)
                        && TryResolveConstant(instruction.Args[2], localInts, out long right))
                        localInts[checked((int)instruction.Args[0].Value)] = left + right;
                    continue;
                }

                if (instruction.Opcode == resetCursorOpcode.Value)
                {
                    var values = ResolveConfiguration(instruction, localInts, systemScript.Name);
                    history.SetResetCursor(values[0], values[1], values[2]);
                    cursorConfigurations++;
                }
                else if (instruction.Opcode == boundsOpcode.Value)
                {
                    var values = ResolveConfiguration(instruction, localInts, systemScript.Name);
                    history.SetBounds(values[0], values[1], values[2]);
                    boundConfigurations++;
                }
                else if (waitIndicatorHandleOpcode != null
                         && instruction.Opcode == waitIndicatorHandleOpcode.Value)
                {
                    var values = ResolveConfiguration(
                        instruction, localInts, systemScript.Name, expectedCount: 2);
                    history.SetWaitIndicatorObjectHandle(values[0], values[1]);
                }
                else if (textObjectRangeOpcode != null
                         && instruction.Opcode == textObjectRangeOpcode.Value)
                {
                    var values = ResolveConfiguration(instruction, localInts, systemScript.Name);
                    history.SetTextObjectRange(values[0], values[1], values[2]);
                    objectRangeConfigurations++;
                }

                if (cursorConfigurations >= definitions && boundConfigurations >= definitions
                    && (textObjectRangeOpcode == null || objectRangeConfigurations >= definitions))
                    break;
            }
        }
        return definitions;
    }

    private static int[] ResolveConfiguration(
        Instruction instruction, IReadOnlyDictionary<int, long> localInts, string scriptName,
        int expectedCount = 3)
    {
        if (instruction.Args.Count != expectedCount)
            throw new InvalidDataException(
                $"{scriptName}@0x{instruction.Offset:x}: ADV layout configuration must use " +
                $"{expectedCount} operands");
        var values = new int[expectedCount];
        for (int i = 0; i < values.Length; i++)
        {
            if (!TryResolveConstant(instruction.Args[i], localInts, out long value))
                throw new InvalidDataException(
                    $"{scriptName}@0x{instruction.Offset:x}: ADV layout configuration operand {i + 1} " +
                    "is not an immediate or constant local integer");
            values[i] = checked((int)value);
        }
        return values;
    }

    private static bool TryResolveConstant(
        Operand operand, IReadOnlyDictionary<int, long> localInts, out long value)
    {
        if (operand.Type == 0)
        {
            value = operand.Value;
            return true;
        }
        if (operand.Type == 9 && localInts.TryGetValue(checked((int)operand.Value), out value)) return true;
        value = 0;
        return false;
    }
}
