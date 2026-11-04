using System.Text;

namespace Age.Script.Ir;

/// <summary>How an operand's 32-bit value is represented in the intermediate form.</summary>
public enum IrOperandKind
{
    /// <summary>The value is stored as-is (immediates, variable indices, sentinels, unresolvable offsets).</summary>
    Literal,
    /// <summary>The value is a code offset; <see cref="IrOperand.Value"/> is the target instruction index
    /// (<c>Code.Count</c> denotes the end of the code).</summary>
    CodeLabel,
    /// <summary>The value is a data offset; <see cref="IrOperand.Value"/> is the data item index.</summary>
    DataRef,
    /// <summary>A packed resource id not known until link time (mod loader spec §6.2): <see cref="IrOperand.Value"/>
    /// indexes <see cref="IrScript.Resources"/>. <see cref="IrScript.ResolveResources"/> turns it into a literal; the
    /// encoder refuses one left unresolved.</summary>
    Resource,
}

/// <summary>One operand: its SYS4 type tag and a value whose meaning depends on <see cref="Kind"/>.</summary>
public readonly record struct IrOperand(int Type, IrOperandKind Kind, uint Value)
{
    public static IrOperand Literal(int type, uint value) => new(type, IrOperandKind.Literal, value);
    public static IrOperand Label(int type, int instructionIndex) => new(type, IrOperandKind.CodeLabel, (uint)instructionIndex);
    public static IrOperand Data(int type, int itemIndex) => new(type, IrOperandKind.DataRef, (uint)itemIndex);
    public static IrOperand Resource(int type, int referenceIndex) => new(type, IrOperandKind.Resource, (uint)referenceIndex);
}

/// <summary>One instruction. <see cref="OriginalOffset"/> is the decoded dword offset (the original-site
/// identity of roadmap §3.4); the encoder ignores it.</summary>
public sealed record IrInstruction(int Opcode, IReadOnlyList<IrOperand> Operands, int? OriginalOffset = null)
{
    public int SizeInDwords => 1 + 2 * Operands.Count;
}

public enum IrDataKind
{
    /// <summary>An XOR-0xFF CP932 string with its NUL and padding.</summary>
    String,
    /// <summary>An op-0x64 inline integer array: count followed by the values.</summary>
    IntArray,
    /// <summary>Data dwords with no recognized shape (unreferenced gaps or unparsable referenced data).</summary>
    Raw,
}

/// <summary>
/// One item of the data area between the code and the tables. <see cref="Dwords"/> holds the exact stored dwords,
/// so preserve-mode encoding is byte-identical; <see cref="Text"/> is the decoded view of a string item.
/// </summary>
public sealed record IrDataItem(IrDataKind Kind, IReadOnlyList<uint> Dwords, string? Text = null, int? OriginalOffset = null)
{
    private static readonly Encoding Cp932 = CreateCp932();

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>A string item in canonical layout: CP932 bytes, NUL, zero padding to a dword, each dword XOR 0xFFFFFFFF.</summary>
    public static IrDataItem FromText(string text)
    {
        var bytes = new List<byte>(Cp932.GetBytes(text)) { 0 };
        while (bytes.Count % 4 != 0) bytes.Add(0);
        byte[] packed = bytes.ToArray();
        var dwords = new uint[packed.Length / 4];
        for (int i = 0; i < dwords.Length; i++)
            dwords[i] = BitConverter.ToUInt32(packed, i * 4) ^ 0xFFFFFFFFu;
        return new IrDataItem(IrDataKind.String, dwords, text);
    }

    /// <summary>An inline integer array item: the count followed by the values, stored plainly.</summary>
    public static IrDataItem FromInts(IReadOnlyList<int> values)
        => new(IrDataKind.IntArray, values.Select(v => (uint)v).Prepend((uint)values.Count).ToArray());

    public int SizeInDwords => Dwords.Count;
}

/// <summary>
/// The three header pointer tables. A null list means "derive from the code" (every 0x71, 0x03 and 0x8f
/// instruction in code order, the only layout observed in the corpora); an explicit list holds raw code offsets.
/// </summary>
public sealed record IrTables(
    IReadOnlyList<uint>? ReadMessageSites = null,
    IReadOnlyList<uint>? ScriptCallSites = null,
    IReadOnlyList<uint>? LocalCallSites = null)
{
    public static IrTables Derived { get; } = new();
    public bool IsDerived => ReadMessageSites == null && ScriptCallSites == null && LocalCallSites == null;
}

/// <summary>
/// The intermediate form of one SYS4 script: symbolic code, typed data items, header local counts and tables.
/// It is the contract between the decoder, the encoder, the compiler and the decompiler.
/// </summary>
public sealed class IrScript
{
    public const int HeaderSize = 0x3C;
    public const int LocalBankCount = 6;

    public string Name { get; init; } = "";
    /// <summary>The eight-byte magic, e.g. "SYS4422 ".</summary>
    public required byte[] Magic { get; init; }
    /// <summary>Header F0..F5: integer, float, string, integer-reference, float-reference and string-reference local counts.</summary>
    public required int[] LocalCounts { get; init; }
    /// <summary>Header F6, the sub-header length marker (0x1c in every observed script).</summary>
    public uint Field6 { get; init; } = 0x1C;
    public required List<IrInstruction> Code { get; init; }
    public required List<IrDataItem> Data { get; init; }
    public IrTables Tables { get; init; } = IrTables.Derived;
    /// <summary>The symbolic references <see cref="IrOperandKind.Resource"/> operands index, as
    /// <c>mod.id:NAME</c>.</summary>
    public List<string> Resources { get; init; } = new();

    /// <summary>A copy whose resource operands are the packed ids <paramref name="resolve"/> gives their references.</summary>
    public IrScript ResolveResources(Func<string, uint> resolve)
    {
        if (Resources.Count == 0) return this;
        uint[] ids = Resources.Select(resolve).ToArray();
        return new IrScript
        {
            Name = Name, Magic = Magic, LocalCounts = LocalCounts, Field6 = Field6, Data = Data, Tables = Tables,
            Code = Code.Select(instruction => instruction.Operands.Any(operand => operand.Kind == IrOperandKind.Resource)
                ? instruction with
                {
                    Operands = instruction.Operands.Select(operand => operand.Kind == IrOperandKind.Resource
                        ? IrOperand.Literal(operand.Type, ids[operand.Value]) : operand).ToArray(),
                }
                : instruction).ToList(),
        };
    }
}
