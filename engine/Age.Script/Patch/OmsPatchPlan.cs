using System.Text.Json;
using System.Text.Json.Serialization;
using Age.Script.Ir;

namespace Age.Script.Patch;

/// <summary>What a plan operand refers to (spec §10.3).</summary>
public enum OmsPatchOperandKind
{
    /// <summary>The value is stored as-is.</summary>
    Literal,
    /// <summary>The value is an index into the plan's data items.</summary>
    Data,
    /// <summary>A position inside one of the plan's blocks: <see cref="OmsPatchOperand.Block"/> and the instruction
    /// index <see cref="OmsPatchOperand.Value"/>, where the block's length means "after this block".</summary>
    Label,
    /// <summary>A base instruction, by its original dword offset in <see cref="OmsPatchOperand.Value"/>.</summary>
    Site,
    /// <summary>A packed resource id bound at link time (mod loader spec §6.2): <see cref="OmsPatchOperand.Value"/>
    /// indexes <see cref="OmsPatchPlan.Resources"/>. <see cref="OmsPatchPlan.ResolveResources"/> makes it a literal.</summary>
    Resource,
}

/// <summary>One operand of plan code: its SYS4 type tag and a value whose meaning depends on <see cref="Kind"/>.</summary>
[JsonConverter(typeof(OmsPatchOperandConverter))]
public readonly record struct OmsPatchOperand(int Type, OmsPatchOperandKind Kind, uint Value, int Block = 0)
{
    public static OmsPatchOperand Literal(int type, uint value) => new(type, OmsPatchOperandKind.Literal, value);
    public static OmsPatchOperand Data(int type, int item) => new(type, OmsPatchOperandKind.Data, (uint)item);
    public static OmsPatchOperand Label(int type, int block, int index) => new(type, OmsPatchOperandKind.Label, (uint)index, block);
    public static OmsPatchOperand Site(int type, int originalOffset) => new(type, OmsPatchOperandKind.Site, (uint)originalOffset);
    public static OmsPatchOperand Resource(int type, int reference) => new(type, OmsPatchOperandKind.Resource, (uint)reference);
}

/// <summary>One instruction of plan code.</summary>
public sealed record OmsPatchInstruction(
    [property: JsonPropertyName("op")] int Opcode,
    [property: JsonPropertyName("args")] IReadOnlyList<OmsPatchOperand> Operands);

public enum OmsPatchOperationKind
{
    /// <summary><c>before site S { … }</c></summary>
    Before,
    /// <summary><c>after site S { … }</c></summary>
    After,
    /// <summary><c>replace site S [through E] { … }</c></summary>
    Replace,
    /// <summary><c>replace site S operand N with V</c></summary>
    Operand,
}

/// <summary>
/// One site operation. Sites are original dword offsets of base instructions. <see cref="Through"/> is the last
/// instruction of a replaced region; <see cref="Block"/> is the operation's code; <see cref="Operand"/> counts from 1.
/// </summary>
public sealed record OmsPatchOperation
{
    [JsonPropertyName("kind")] public required OmsPatchOperationKind Kind { get; init; }
    [JsonPropertyName("site")] public required int Site { get; init; }
    [JsonPropertyName("through")] public int? Through { get; init; }
    [JsonPropertyName("block")] public int? Block { get; init; }
    [JsonPropertyName("operand")] public int? Operand { get; init; }
    [JsonPropertyName("value")] public OmsPatchOperand? Value { get; init; }
    /// <summary>The named patch point <see cref="Site"/> was resolved from (mod loader spec §5), for attribution and
    /// diagnostics; the applier uses <see cref="Site"/> only.</summary>
    [JsonPropertyName("point")] public string? Point { get; init; }
    /// <summary>The named patch point <see cref="Through"/> was resolved from.</summary>
    [JsonPropertyName("through_point")] public string? ThroughPoint { get; init; }
    /// <summary>Where the operation was written, for diagnostics.</summary>
    [JsonPropertyName("source")] public string? Source { get; init; }
}

/// <summary>A data item of a plan: the exact dwords, as in <see cref="IrDataItem"/>.</summary>
public sealed record OmsPatchDataItem(
    [property: JsonPropertyName("kind")] IrDataKind Kind,
    [property: JsonPropertyName("dwords")] uint[] Dwords,
    [property: JsonPropertyName("text")] string? Text = null)
{
    public IrDataItem ToIr() => new(Kind, Dwords, Text);
    public static OmsPatchDataItem From(IrDataItem item) => new(item.Kind, item.Dwords.ToArray(), item.Text);
}

/// <summary>
/// A transform plan (spec §10.3): a fragment compiled against one fingerprinted base script. It holds no offset of
/// the output, so it applies to the pristine base wherever other plans insert code.
/// </summary>
public sealed class OmsPatchPlan
{
    /// <summary>The format version of a plan without resource references; older readers load it.</summary>
    public const int CurrentVersion = 1;
    /// <summary>The format version of a plan that holds resource references (<c>resources</c>, <c>res</c> operands).</summary>
    public const int ResourceVersion = 2;

    [JsonPropertyName("oms_patch")] public int Version { get; init; } = CurrentVersion;
    [JsonPropertyName("script")] public required string Script { get; init; }
    /// <summary><c>sha256:</c> and the lowercase hex SHA-256 of the base script's bytes.</summary>
    [JsonPropertyName("fingerprint")] public required string Fingerprint { get; init; }
    [JsonPropertyName("abi")] public required string Abi { get; init; }
    /// <summary>Minimum header F0..F5 the plan's code needs.</summary>
    [JsonPropertyName("local_counts")] public int[] LocalCounts { get; init; } = [1, 1, 1, 1, 1, 1];
    [JsonPropertyName("operations")] public List<OmsPatchOperation> Operations { get; init; } = new();
    [JsonPropertyName("blocks")] public List<List<OmsPatchInstruction>> Blocks { get; init; } = new();
    /// <summary>The block placed after the base code: the fragment's subs and the library subs it links.</summary>
    [JsonPropertyName("append")] public int? Append { get; init; }
    [JsonPropertyName("data")] public List<OmsPatchDataItem> Data { get; init; } = new();
    /// <summary>The symbolic references <see cref="OmsPatchOperandKind.Resource"/> operands index, as
    /// <c>mod.id:NAME</c> (version 2).</summary>
    [JsonIgnore] public List<string> Resources { get; init; } = new();

    /// <summary>The JSON form of <see cref="Resources"/>, left out when empty so version 1 plans keep their text.</summary>
    [JsonPropertyName("resources")]
    public List<string>? ResourcesJson
    {
        get => Resources.Count > 0 ? Resources : null;
        init => Resources = value ?? new();
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter<IrDataKind>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<OmsPatchOperationKind>(JsonNamingPolicy.SnakeCaseLower),
        },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static OmsPatchPlan FromJson(string json)
    {
        OmsPatchPlan plan = JsonSerializer.Deserialize<OmsPatchPlan>(json, Options)
            ?? throw new InvalidDataException("empty transform plan");
        if (plan.Version is not (CurrentVersion or ResourceVersion))
            throw new InvalidDataException($"unsupported transform plan version {plan.Version}");
        bool usesResources = plan.Blocks.SelectMany(block => block).SelectMany(instruction => instruction.Operands)
            .Concat(plan.Operations.Where(operation => operation.Value != null).Select(operation => operation.Value!.Value))
            .Any(operand => operand.Kind == OmsPatchOperandKind.Resource);
        if (usesResources && plan.Version < ResourceVersion)
            throw new InvalidDataException($"a version {plan.Version} plan cannot hold resource references");
        return plan;
    }

    /// <summary>A copy whose resource operands are the packed ids <paramref name="resolve"/> gives their references.</summary>
    public OmsPatchPlan ResolveResources(Func<string, uint> resolve)
    {
        if (Resources.Count == 0) return this;
        uint[] ids = Resources.Select(resolve).ToArray();
        OmsPatchOperand Bind(OmsPatchOperand operand) => operand.Kind == OmsPatchOperandKind.Resource
            ? OmsPatchOperand.Literal(operand.Type, ids[operand.Value]) : operand;
        return new OmsPatchPlan
        {
            Version = CurrentVersion, Script = Script, Fingerprint = Fingerprint, Abi = Abi, LocalCounts = LocalCounts,
            Append = Append, Data = Data,
            Blocks = Blocks.Select(block => block.Select(instruction =>
                instruction with { Operands = instruction.Operands.Select(Bind).ToArray() }).ToList()).ToList(),
            Operations = Operations.Select(operation =>
                operation.Value is { } value ? operation with { Value = Bind(value) } : operation).ToList(),
        };
    }

    /// <summary>The fingerprint of a script's bytes: <c>sha256:</c> and the lowercase hex digest.</summary>
    public static string FingerprintOf(byte[] bytes)
        => "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>Writes an operand as <c>{"t": type, "lit"|"data"|"site"|"res": value}</c> or <c>{"t": type, "label": [block,
/// index]}</c>.</summary>
internal sealed class OmsPatchOperandConverter : JsonConverter<OmsPatchOperand>
{
    public override OmsPatchOperand Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        int type = root.GetProperty("t").GetInt32();
        if (root.TryGetProperty("lit", out JsonElement literal)) return OmsPatchOperand.Literal(type, literal.GetUInt32());
        if (root.TryGetProperty("data", out JsonElement data)) return OmsPatchOperand.Data(type, data.GetInt32());
        if (root.TryGetProperty("site", out JsonElement site)) return OmsPatchOperand.Site(type, site.GetInt32());
        if (root.TryGetProperty("res", out JsonElement resource)) return OmsPatchOperand.Resource(type, resource.GetInt32());
        if (root.TryGetProperty("label", out JsonElement label) && label.GetArrayLength() == 2)
            return OmsPatchOperand.Label(type, label[0].GetInt32(), label[1].GetInt32());
        throw new JsonException("a plan operand needs one of lit, data, site, res or label");
    }

    public override void Write(Utf8JsonWriter writer, OmsPatchOperand value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("t", value.Type);
        switch (value.Kind)
        {
            case OmsPatchOperandKind.Literal: writer.WriteNumber("lit", value.Value); break;
            case OmsPatchOperandKind.Data: writer.WriteNumber("data", value.Value); break;
            case OmsPatchOperandKind.Site: writer.WriteNumber("site", value.Value); break;
            case OmsPatchOperandKind.Resource: writer.WriteNumber("res", value.Value); break;
            case OmsPatchOperandKind.Label:
                writer.WriteStartArray("label");
                writer.WriteNumberValue(value.Block);
                writer.WriteNumberValue(value.Value);
                writer.WriteEndArray();
                break;
        }
        writer.WriteEndObject();
    }
}
