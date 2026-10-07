using System.Text.Json;

namespace Age.Script.Abi;

/// <summary>
/// Operand contracts from the generated opcode registry view (<c>build/opcodes.json</c>, generated from
/// <c>vm-map/opcodes.toml</c>): which operand positions are code targets and each operand's native access
/// kinds per engine ABI. Instruction framing (argument counts) stays with the engine's ABI-resolved
/// <see cref="Age.Engine.Model.OpcodeTable"/>.
/// </summary>
public sealed class OpcodeContracts
{
    private readonly Dictionary<int, HashSet<int>> _codeTargets;
    private readonly Dictionary<(int Opcode, int Operand), OperandAccess> _access;
    private readonly Dictionary<int, string> _semanticNames;

    private OpcodeContracts(
        Dictionary<int, HashSet<int>> codeTargets,
        Dictionary<(int, int), OperandAccess> access,
        Dictionary<int, string>? semanticNames = null)
    {
        _codeTargets = codeTargets;
        _access = access;
        _semanticNames = semanticNames ?? new Dictionary<int, string>();
    }

    /// <summary>Registry semantic names (e.g. "show-text"), keyed by opcode.</summary>
    public IReadOnlyDictionary<int, string> SemanticNames => _semanticNames;

    /// <summary>Contracts with the given code-target positions and no access data (for tests).</summary>
    public static OpcodeContracts FromCodeTargets(IReadOnlyDictionary<int, int[]> codeTargets)
        => new(codeTargets.ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet()),
               new Dictionary<(int, int), OperandAccess>());

    public static OpcodeContracts Load(string opcodesJsonPath)
    {
        using FileStream stream = File.OpenRead(opcodesJsonPath);
        return Load(stream);
    }

    public static OpcodeContracts Load(Stream stream)
    {
        using JsonDocument doc = JsonDocument.Parse(stream);
        var codeTargets = new Dictionary<int, HashSet<int>>();
        var access = new Dictionary<(int, int), OperandAccess>();
        var names = new Dictionary<int, string>();
        foreach (JsonElement entry in doc.RootElement.GetProperty("opcodes").EnumerateArray())
        {
            int op = Convert.ToInt32(entry.GetProperty("op").GetString(), 16);
            if (entry.TryGetProperty("semantics", out JsonElement named) && named.ValueKind == JsonValueKind.Object
                && named.TryGetProperty("name", out JsonElement name) && name.GetString() is { Length: > 0 } text)
                names[op] = text;
            if (entry.TryGetProperty("code_target_args", out JsonElement targets))
                codeTargets[op] = targets.EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
            if (!entry.TryGetProperty("semantics", out JsonElement semantics)
                || semantics.ValueKind != JsonValueKind.Object
                || !semantics.TryGetProperty("args", out JsonElement args))
                continue;
            foreach (JsonElement arg in args.EnumerateArray())
            {
                if (!arg.TryGetProperty("i", out JsonElement index)) continue;
                string[]? common = arg.TryGetProperty("access", out JsonElement list)
                    ? list.EnumerateArray().Select(kind => kind.GetString() ?? "").ToArray()
                    : null;
                Dictionary<string, string[]>? byAbi = null;
                if (arg.TryGetProperty("access_by_abi", out JsonElement map))
                {
                    byAbi = new Dictionary<string, string[]>(StringComparer.Ordinal);
                    foreach (JsonProperty abi in map.EnumerateObject())
                        byAbi[abi.Name] = abi.Value.EnumerateArray().Select(kind => kind.GetString() ?? "").ToArray();
                }
                if (common != null || byAbi != null)
                    access[(op, index.GetInt32())] = new OperandAccess(common, byAbi);
            }
        }
        return new OpcodeContracts(codeTargets, access, names);
    }

    /// <summary>Contracts with code targets, access lists and semantic names (for tests).</summary>
    public static OpcodeContracts FromParts(
        IReadOnlyDictionary<int, int[]> codeTargets,
        IReadOnlyDictionary<(int Opcode, int Operand), string[]> access,
        IReadOnlyDictionary<int, string> semanticNames)
        => new(codeTargets.ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet()),
               access.ToDictionary(pair => (pair.Key.Opcode, pair.Key.Operand), pair => new OperandAccess(pair.Value, null)),
               semanticNames.ToDictionary(pair => pair.Key, pair => pair.Value));

    /// <summary>Whether the one-based operand position holds a code offset.</summary>
    public bool IsCodeTarget(int opcode, int operand)
        => _codeTargets.TryGetValue(opcode, out HashSet<int>? positions) && positions.Contains(operand);

    /// <summary>The native access kinds of a one-based operand under an engine ABI, if recorded.</summary>
    public IReadOnlyList<string>? Access(int opcode, int operand, string abiId)
        => _access.TryGetValue((opcode, operand), out OperandAccess? record) ? record.For(abiId) : null;

    private sealed record OperandAccess(string[]? Common, Dictionary<string, string[]>? ByAbi)
    {
        public IReadOnlyList<string>? For(string abiId)
            => ByAbi != null ? (ByAbi.TryGetValue(abiId, out string[]? kinds) ? kinds : null) : Common;
    }
}
