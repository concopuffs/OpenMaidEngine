using Age.Engine.Model;

namespace Age.Engine.Vm;

/// <summary>
/// Replays the system script's literal op-0x64 copies (copy-inline-int-array: a direct global destination and an
/// immediate blob offset) for a direct-scene diagnostic harness which starts after the persistent system root.
/// Natural boot executes the same opcode normally. Himegari's SYSTEM4 has one, at 0x1ca: the SFX channel table
/// [0,4,5,6,9] at G[0x6242c..0x62430], which the ADV scenes pass to the SFX ops as channels.
/// </summary>
public static class InlineArrayBootstrap
{
    private const int CopyInlineIntArray = 0x64, ImmediateType = 0, GlobalIntType = 3;

    public static int Apply(Script systemScript, IDictionary<int, long> globals)
    {
        ArgumentNullException.ThrowIfNull(systemScript);
        ArgumentNullException.ThrowIfNull(globals);
        IReadOnlyList<uint> body = systemScript.BodyDwords;
        int applied = 0;
        foreach (Instruction instruction in systemScript.Instructions)
        {
            if (instruction.Opcode != CopyInlineIntArray || instruction.Args.Count != 2
                || instruction.Args[0].Type != GlobalIntType || instruction.Args[1].Type != ImmediateType)
                continue;
            long offset = instruction.Args[1].Value;
            if (offset < 0 || offset >= body.Count) continue;
            uint count = body[(int)offset];
            if (count > body.Count - offset - 1) continue;
            int destination = checked((int)instruction.Args[0].Value);
            for (int i = 0; i < count; i++)
                globals[destination + i] = unchecked((int)body[(int)offset + 1 + i]);
            applied++;
        }
        return applied;
    }
}
