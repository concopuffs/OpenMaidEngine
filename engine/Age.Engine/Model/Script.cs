namespace Age.Engine.Model;
public sealed class Script
{
    public string Name { get; init; } = "";
    /// <summary>Raw packed SYS4/AAI resource id used as this script's native ReadTextDB key.</summary>
    public uint PackedId { get; init; }
    public required ScriptHeader Header { get; init; }
    public required IReadOnlyList<Instruction> Instructions { get; init; }
    public required IReadOnlyDictionary<int, int> IndexByOffset { get; init; }
    public required IReadOnlyDictionary<int, string> Strings { get; init; }
    /// <summary>Unmodified SYS4 body dwords, retained for inline data operands such as opcode 0x64.</summary>
    public IReadOnlyList<uint> BodyDwords { get; init; } = Array.Empty<uint>();
    /// <summary>T1 entries: code DWORD offsets of op-0x71 message boundaries.</summary>
    public IReadOnlyList<int> ReadMessageOffsets { get; init; } = Array.Empty<int>();
    public string GetString(int offset) => Strings.TryGetValue(offset, out var s) ? s : "";
}
