using Age.Engine.Model;

namespace Age.Engine.Vm;

/// <summary>
/// Replays SYSTEM4's data-only input configuration for a direct-scene diagnostic harness which starts
/// after the persistent system root. Natural boot executes the same opcode handlers normally.
/// </summary>
public static class InputBindingBootstrap
{
    public static int Apply(Script systemScript, InputBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(systemScript);
        ArgumentNullException.ThrowIfNull(bindings);
        int applied = 0;
        foreach (Instruction instruction in systemScript.Instructions)
        {
            if (instruction.Args.Any(arg => arg.Type != 0)) continue;
            long A(int index) => instruction.Args[index].Value;
            switch (instruction.Opcode)
            {
                case 0xfe when instruction.Args.Count == 1:
                    if (bindings.SetActionCount((int)A(0))) applied++;
                    break;
                case 0x107 when instruction.Args.Count == 2:
                    bindings.MapJoystickButton((int)A(0), (int)A(1)); applied++;
                    break;
                case 0x10b when instruction.Args.Count == 2:
                    bindings.MapMouseButton((int)A(0), (int)A(1)); applied++;
                    break;
                case 0x10c when instruction.Args.Count == 2:
                    if (bindings.MapKeyboardScanCode((int)A(0), (int)A(1))) applied++;
                    break;
            }
        }
        return applied;
    }
}
