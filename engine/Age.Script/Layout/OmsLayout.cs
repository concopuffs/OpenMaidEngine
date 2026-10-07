using System.Text.Json;
using System.Text.Json.Serialization;

namespace Age.Script.Layout;

/// <summary>
/// One physical slot of the data area. A referenced slot names the data-operand occurrences (numbered in code order)
/// that point at it; an unreferenced slot carries its exact dwords.
/// </summary>
public sealed record OmsDataSlot
{
    /// <summary>Data-operand occurrences that reference this slot; the first one defines its content.</summary>
    public int[]? Refs { get; init; }
    /// <summary>Exact dwords of an unreferenced slot.</summary>
    public uint[]? Raw { get; init; }
}

/// <summary>
/// The temporaries of one statement when they differ from the canonical plan (spec §7.2): absolute slots in emission
/// order. <see cref="Ints"/> lists integer temporaries (bank L); <see cref="Refs"/> lists reference temporaries of
/// every reference bank. A list is present only when it differs.
/// </summary>
public sealed record OmsTempOverride
{
    [JsonPropertyName("int")] public uint[]? Ints { get; init; }
    [JsonPropertyName("ref")] public uint[]? Refs { get; init; }
}

/// <summary>
/// The layout sidecar (spec §11 step 8): only facts a canonical compile would not reproduce. Every member is
/// optional; an empty sidecar means the source compiles to the original bytes on its own.
/// </summary>
public sealed class OmsLayout
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("oms_layout")] public int Version { get; init; } = CurrentVersion;
    /// <summary>Eight-byte magic when it differs from the target ABI's ("SYS4422 " for SYS4422).</summary>
    [JsonPropertyName("magic")] public string? Magic { get; set; }
    /// <summary>Header F6 when it is not 0x1c.</summary>
    [JsonPropertyName("field6")] public uint? Field6 { get; set; }
    /// <summary>Header F0..F5 when they differ from "highest slot used + 2, or 1 when unused".</summary>
    [JsonPropertyName("local_counts")] public int[]? LocalCounts { get; set; }
    /// <summary>Physical data order when it is not "one slot per occurrence, in code order".</summary>
    [JsonPropertyName("data")] public List<OmsDataSlot>? Data { get; set; }
    /// <summary>Exact dwords of a referenced item, keyed by its first occurrence, when canonical encoding differs.</summary>
    [JsonPropertyName("item_dwords")] public SortedDictionary<int, uint[]>? ItemDwords { get; set; }
    /// <summary>Explicit T1, T2, T3 raw code offsets when they are not the derived site lists.</summary>
    [JsonPropertyName("tables")] public uint[][]? Tables { get; set; }
    /// <summary>First slot of the integer temporary pool when it is not "highest local integer named in the source
    /// plus one".</summary>
    [JsonPropertyName("temp_base")] public uint? TempBase { get; set; }
    /// <summary>The script's integer-reference registers start in the order LP1, LP0 rather than LP0, LP1.</summary>
    [JsonPropertyName("ref_swap")] public bool? RefSwap { get; set; }
    /// <summary>Temporary slots of statements whose original allocation differs from the canonical plan, keyed by the
    /// index of the statement's first instruction.</summary>
    [JsonPropertyName("temps")] public SortedDictionary<int, OmsTempOverride>? Temps { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Magic == null && Field6 == null && LocalCounts == null && Data == null
                           && ItemDwords == null && Tables == null && TempBase == null && RefSwap == null
                           && Temps == null;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static OmsLayout FromJson(string json)
    {
        OmsLayout layout = JsonSerializer.Deserialize<OmsLayout>(json, Options)
            ?? throw new InvalidDataException("empty layout sidecar");
        if (layout.Version != CurrentVersion)
            throw new InvalidDataException($"unsupported layout sidecar version {layout.Version}");
        return layout;
    }
}
