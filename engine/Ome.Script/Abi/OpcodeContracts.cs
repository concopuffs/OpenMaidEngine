using System.Text.Json;

namespace Ome.Script.Abi;

/// <summary>
/// A table of native AGE's that an operand indexes, where native does not keep the operand inside the table (registry
/// <c>unchecked_index</c>; docs/engine-re.md, "Indices outside their tables"). <see cref="First"/> to
/// <see cref="Count"/> − 1 are inside. Without <see cref="Admits"/> native does not test the value; with it, native's
/// test lets through each value below it, compared unsigned unless <see cref="Signed"/> (so only a signed test lets a
/// negative value through).
/// </summary>
public sealed record NativeIndex(string Table, int Count, int First = 0, int? Admits = null, bool Signed = false)
{
    /// <summary>Whether native lets <paramref name="value"/> reach memory outside the table, where the port halts.</summary>
    public bool LetsPast(long value)
    {
        if (value >= First && value < Count) return false;
        if (Admits is not int admits) return true;
        return value < 0 ? Signed : value < admits;
    }
}

/// <summary>
/// Operand contracts from the generated opcode registry view (<c>build/opcodes.json</c>, generated from
/// <c>vm-map/opcodes.toml</c>): which operand positions are code targets and each operand's native access
/// kinds per engine ABI. Instruction framing (argument counts) stays with the engine's ABI-resolved
/// <see cref="Ome.Engine.Model.OpcodeTable"/>.
/// </summary>
public sealed class OpcodeContracts
{
    private readonly Dictionary<int, HashSet<int>> _codeTargets;
    private readonly Dictionary<(int Opcode, int Operand), OperandAccess> _access;
    private readonly Dictionary<int, string> _semanticNames;
    private readonly Dictionary<int, string> _codeTargetKinds;
    private readonly Dictionary<int, HashSet<string>> _observedRevisions = new();
    private readonly HashSet<(int Opcode, int Operand)> _codeValues = new();
    private readonly Dictionary<(int Opcode, int Operand), NativeIndex> _uncheckedIndices = new();

    private OpcodeContracts(
        Dictionary<int, HashSet<int>> codeTargets,
        Dictionary<(int, int), OperandAccess> access,
        Dictionary<int, string>? semanticNames = null,
        Dictionary<int, string>? codeTargetKinds = null)
    {
        _codeTargets = codeTargets;
        _access = access;
        _semanticNames = semanticNames ?? new Dictionary<int, string>();
        _codeTargetKinds = codeTargetKinds ?? new Dictionary<int, string>();
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

    /// <summary>The contracts of the opcode metadata embedded in <c>Ome.Engine</c>
    /// (<see cref="Ome.Engine.Sys4.OpcodeTableJson.OpenEmbedded"/>).</summary>
    public static OpcodeContracts LoadEmbedded()
    {
        using Stream stream = Ome.Engine.Sys4.OpcodeTableJson.OpenEmbedded();
        return Load(stream);
    }

    public static OpcodeContracts Load(Stream stream)
    {
        using JsonDocument doc = JsonDocument.Parse(stream);
        var codeTargets = new Dictionary<int, HashSet<int>>();
        var access = new Dictionary<(int, int), OperandAccess>();
        var names = new Dictionary<int, string>();
        var kinds = new Dictionary<int, string>();
        var observed = new Dictionary<int, HashSet<string>>();
        var codeValues = new HashSet<(int, int)>();
        var uncheckedIndices = new Dictionary<(int, int), NativeIndex>();
        foreach (JsonElement entry in doc.RootElement.GetProperty("opcodes").EnumerateArray())
        {
            int op = Convert.ToInt32(entry.GetProperty("op").GetString(), 16);
            if (entry.TryGetProperty("semantics", out JsonElement named) && named.ValueKind == JsonValueKind.Object
                && named.TryGetProperty("name", out JsonElement name) && name.GetString() is { Length: > 0 } text)
                names[op] = text;
            if (entry.TryGetProperty("code_target_args", out JsonElement targets))
                codeTargets[op] = targets.EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
            if (entry.TryGetProperty("code_target_kind", out JsonElement kind) && kind.GetString() is { Length: > 0 } k)
                kinds[op] = k;
            if (entry.TryGetProperty("observed_revisions", out JsonElement revisions))
                observed[op] = revisions.EnumerateArray().Select(value => value.GetString() ?? "").ToHashSet(StringComparer.Ordinal);
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
                if (arg.TryGetProperty("value", out JsonElement value) && value.GetString() == "code-address")
                    codeValues.Add((op, index.GetInt32()));
                if (arg.TryGetProperty("unchecked_index", out JsonElement table))
                    uncheckedIndices[(op, index.GetInt32())] = new NativeIndex(
                        table.GetProperty("table").GetString() ?? "",
                        table.GetProperty("count").GetInt32(),
                        table.TryGetProperty("first", out JsonElement first) ? first.GetInt32() : 0,
                        table.TryGetProperty("admits", out JsonElement admits) ? admits.GetInt32() : null,
                        table.TryGetProperty("signed", out JsonElement signed) && signed.GetBoolean());
            }
        }
        var contracts = new OpcodeContracts(codeTargets, access, names, kinds);
        foreach (var (op, revisions) in observed) contracts._observedRevisions[op] = revisions;
        contracts._codeValues.UnionWith(codeValues);
        foreach (var (key, table) in uncheckedIndices) contracts._uncheckedIndices[key] = table;
        return contracts;
    }

    /// <summary>Contracts with code targets, access lists and semantic names (for tests).</summary>
    public static OpcodeContracts FromParts(
        IReadOnlyDictionary<int, int[]> codeTargets,
        IReadOnlyDictionary<(int Opcode, int Operand), string[]> access,
        IReadOnlyDictionary<int, string> semanticNames,
        IReadOnlyDictionary<int, string>? codeTargetKinds = null,
        IEnumerable<(int Opcode, int Operand)>? codeValues = null,
        IReadOnlyDictionary<(int Opcode, int Operand), NativeIndex>? uncheckedIndices = null)
    {
        var contracts = new OpcodeContracts(codeTargets.ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet()),
            access.ToDictionary(pair => (pair.Key.Opcode, pair.Key.Operand), pair => new OperandAccess(pair.Value, null)),
            semanticNames.ToDictionary(pair => pair.Key, pair => pair.Value),
            codeTargetKinds?.ToDictionary(pair => pair.Key, pair => pair.Value));
        contracts._codeValues.UnionWith(codeValues ?? []);
        foreach (var (key, table) in uncheckedIndices ?? new Dictionary<(int, int), NativeIndex>())
            contracts._uncheckedIndices[key] = table;
        return contracts;
    }

    /// <summary>A deterministic digest of every contract held (code targets and their kinds, operand access per ABI,
    /// semantic names, observed revisions, code values, unchecked indices), for caches whose output depends on them.
    /// </summary>
    public string Fingerprint()
    {
        var text = new System.Text.StringBuilder();
        foreach (var (op, positions) in _codeTargets.OrderBy(pair => pair.Key))
            text.Append("t ").Append(op).Append(':').AppendJoin(',', positions.Order()).Append('\n');
        foreach (var (op, kind) in _codeTargetKinds.OrderBy(pair => pair.Key))
            text.Append("k ").Append(op).Append(':').Append(kind).Append('\n');
        foreach (var (key, access) in _access.OrderBy(pair => pair.Key.Opcode).ThenBy(pair => pair.Key.Operand))
        {
            text.Append("a ").Append(key.Opcode).Append('.').Append(key.Operand).Append(':');
            if (access.Common != null) text.AppendJoin(',', access.Common);
            if (access.ByAbi != null)
                foreach (var (abi, kinds) in access.ByAbi.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    text.Append(';').Append(abi).Append('=').AppendJoin(',', kinds);
            text.Append('\n');
        }
        foreach (var (op, name) in _semanticNames.OrderBy(pair => pair.Key))
            text.Append("n ").Append(op).Append(':').Append(name).Append('\n');
        foreach (var (op, revisions) in _observedRevisions.OrderBy(pair => pair.Key))
            text.Append("r ").Append(op).Append(':').AppendJoin(',', revisions.Order(StringComparer.Ordinal)).Append('\n');
        foreach (var (op, operand) in _codeValues.Order())
            text.Append("v ").Append(op).Append('.').Append(operand).Append('\n');
        foreach (var (key, table) in _uncheckedIndices.OrderBy(pair => pair.Key.Opcode).ThenBy(pair => pair.Key.Operand))
            text.Append("u ").Append(key.Opcode).Append('.').Append(key.Operand).Append(':').Append(table.Table)
                .Append(',').Append(table.First).Append(',').Append(table.Count).Append(',').Append(table.Admits)
                .Append(',').Append(table.Signed).Append('\n');
        return "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    /// <summary>Whether the one-based operand position holds a code offset.</summary>
    public bool IsCodeTarget(int opcode, int operand)
        => _codeTargets.TryGetValue(opcode, out HashSet<int>? positions) && positions.Contains(operand);

    /// <summary>What the opcode's code-target operands point at: <c>transfer</c>, <c>call</c>, <c>callback</c> or
    /// <c>coroutine</c> (registry <c>code_target_kind</c>).</summary>
    public string? CodeTargetKind(int opcode) => _codeTargetKinds.GetValueOrDefault(opcode);

    /// <summary>Whether a game corpus of this engine ABI ever executes the opcode (registry observations). Contracts
    /// built without observation data report every opcode as observed.</summary>
    public bool IsObserved(int opcode, string abiId)
        => _observedRevisions.Count == 0
           || _observedRevisions.TryGetValue(opcode, out HashSet<string>? revisions) && revisions.Contains(abiId);

    /// <summary>Whether the one-based operand's value is a code position held as data (registry
    /// <c>value = "code-address"</c>), such as the PC op 0x140 writes.</summary>
    public bool IsCodeValue(int opcode, int operand) => _codeValues.Contains((opcode, operand));

    /// <summary>The native table a one-based operand indexes without keeping it inside (registry
    /// <c>unchecked_index</c>), if any.</summary>
    public NativeIndex? UncheckedIndex(int opcode, int operand) => _uncheckedIndices.GetValueOrDefault((opcode, operand));

    /// <summary>Every operand that has an <see cref="UncheckedIndex"/>.</summary>
    public IReadOnlyDictionary<(int Opcode, int Operand), NativeIndex> UncheckedIndices => _uncheckedIndices;

    /// <summary>The native access kinds of a one-based operand under an engine ABI, if recorded.</summary>
    public IReadOnlyList<string>? Access(int opcode, int operand, string abiId)
        => _access.TryGetValue((opcode, operand), out OperandAccess? record) ? record.For(abiId) : null;

    private sealed record OperandAccess(string[]? Common, Dictionary<string, string[]>? ByAbi)
    {
        public IReadOnlyList<string>? For(string abiId)
            => ByAbi != null ? (ByAbi.TryGetValue(abiId, out string[]? kinds) ? kinds : null) : Common;
    }
}
