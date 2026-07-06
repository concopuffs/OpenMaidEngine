namespace Age.Engine.Model;
public sealed class Script
{
    public required ScriptHeader Header { get; init; }
    public required IReadOnlyList<Instruction> Instructions { get; init; }
    public required IReadOnlyDictionary<int, int> IndexByOffset { get; init; }
    public required IReadOnlyDictionary<int, string> Strings { get; init; }
    public string GetString(int offset) => Strings.TryGetValue(offset, out var s) ? s : "";
}
