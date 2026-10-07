using Age.Engine.Model;
using Age.Engine.Profiles;
using Age.Engine.Sys4;
using Age.Script.Abi;
using Age.Script.Ir;
using Xunit;

/// <summary>Open Maid Script gate 1: the intermediate form decodes and re-encodes SYS4 scripts byte for byte.</summary>
public class OmsIrCodecTests
{
    private const uint None = 0xFFFFFFFF;

    private static readonly OpcodeTable Table = new(new Dictionary<int, (string, int)>
    {
        [0x02] = ("exit", 0), [0x03] = ("call-script", 1), [0x05] = ("ret", 0), [0x55] = ("mov", 2),
        [0x64] = ("copy-inline-int-array", 2), [0x6e] = ("show-text", 2), [0x71] = ("reset-adv-text-layout", 1),
        [0x8c] = ("jmp", 1), [0x8f] = ("call", 1), [0xa0] = ("jcc", 3),
    }, "TEST");

    private static readonly OpcodeContracts Contracts = OpcodeContracts.FromCodeTargets(new Dictionary<int, int[]>
    {
        [0x8c] = [1], [0x8f] = [1], [0xa0] = [2, 3],
    });

    private static IrInstruction Op(int opcode, params IrOperand[] operands) => new(opcode, operands);
    private static IrOperand Imm(uint value) => IrOperand.Literal(0, value);

    private static IrScript Sample(IrTables? tables = null) => new()
    {
        Name = "SAMPLE.BIN",
        Magic = "SYS4422 "u8.ToArray(),
        LocalCounts = [4, 1, 1, 3, 1, 1],
        Code =
        [
            Op(0x55, IrOperand.Literal(9, 0), Imm(1)),                           // 0: mov L0 1
            Op(0xa0, IrOperand.Literal(9, 0), Imm(None), IrOperand.Label(0, 4)),  // 1: jcc L0 -1 -> 4
            Op(0x6e, Imm(0), IrOperand.Data(2, 0)),                              // 2: show-text 0 "..."
            Op(0x71, Imm(1)),                                                    // 3: T1 site
            Op(0x64, IrOperand.Literal(9, 2), IrOperand.Data(0, 1)),             // 4: copy array
            Op(0x8f, IrOperand.Label(0, 8)),                                     // 5: call sub at 8
            Op(0x8f, IrOperand.Literal(3, 0x6be)),                              // 6: indirect call G[0x6be]
            Op(0x03, Imm(0x329d)),                                               // 7: call-script
            Op(0x8c, IrOperand.Label(0, 0)),                                     // 8: jmp back to 0
            Op(0x05),                                                            // 9: ret
            Op(0x02),                                                            // 10: exit
        ],
        Data =
        [
            IrDataItem.FromText("「テスト」"),
            IrDataItem.FromInts([4, 0, 5, 0, 538]),
            new IrDataItem(IrDataKind.Raw, [0xFFFFFFFF, 0x12345678]),             // unreferenced data
        ],
        Tables = tables ?? IrTables.Derived,
    };

    [Fact]
    public void EncodedIrDecodesToTheSameStructureAndBytes()
    {
        byte[] bytes = IrEncoder.Encode(Sample());
        IrScript decoded = IrDecoder.Decode(bytes, Table, Contracts, "SAMPLE.BIN");

        Assert.Equal(11, decoded.Code.Count);
        Assert.Equal(IrOperand.Label(0, 4), decoded.Code[1].Operands[2]);
        Assert.Equal(IrOperand.Literal(0, None), decoded.Code[1].Operands[1]);
        Assert.Equal(IrOperand.Data(2, 0), decoded.Code[2].Operands[1]);
        Assert.Equal(IrOperand.Data(0, 1), decoded.Code[4].Operands[1]);
        Assert.Equal(IrOperand.Literal(3, 0x6be), decoded.Code[6].Operands[0]);   // a variable is never a label
        Assert.Equal(IrOperand.Label(0, 0), decoded.Code[8].Operands[0]);
        Assert.Equal([IrDataKind.String, IrDataKind.IntArray, IrDataKind.Raw], decoded.Data.Select(d => d.Kind));
        Assert.Equal("「テスト」", decoded.Data[0].Text);
        Assert.True(decoded.Tables.IsDerived);
        Assert.Equal(bytes, IrEncoder.Encode(decoded));
    }

    [Fact]
    public void HeaderAndTablesFollowTheNativeLayout()
    {
        byte[] bytes = IrEncoder.Encode(Sample());
        int Field(int k) => BitConverter.ToInt32(bytes, 8 + k * 4);
        int codeLength = Sample().Code.Sum(i => i.SizeInDwords);
        int dataLength = Sample().Data.Sum(d => d.SizeInDwords);

        Assert.Equal("SYS4422 ", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.Equal([4, 1, 1, 3, 1, 1], Enumerable.Range(0, 6).Select(Field));
        Assert.Equal(0x1C, Field(6));
        Assert.Equal(codeLength + dataLength, Field(8));                 // F8: strings precede the tables
        Assert.Equal((1, 1, 2), (Field(7), Field(9), Field(11)));        // one 0x71, one 0x03, two 0x8f sites
        Assert.Equal(Field(8) + Field(7), Field(10));
        Assert.Equal(Field(10) + Field(9), Field(12));
        Assert.Equal(IrScript.HeaderSize + 4 * (Field(12) + Field(11)), bytes.Length);
    }

    [Fact]
    public void ExplicitTablesAndNonCanonicalStringPaddingSurviveTheRoundTrip()
    {
        IrScript sample = Sample(new IrTables([0x5u], [], [0x3u]));
        uint[] padded = IrDataItem.FromText("あ").Dwords.ToArray();
        padded[^1] ^= 0x00AA0000;                         // non-zero padding byte after the NUL
        sample.Data[0] = new IrDataItem(IrDataKind.String, padded, "あ");
        byte[] bytes = IrEncoder.Encode(sample);
        IrScript decoded = IrDecoder.Decode(bytes, Table, Contracts);

        Assert.False(decoded.Tables.IsDerived);
        Assert.Equal([0x5u], decoded.Tables.ReadMessageSites!);
        Assert.Equal(padded, decoded.Data[0].Dwords);
        Assert.Equal(bytes, IrEncoder.Encode(decoded));
    }

    [Fact]
    public void LabelsRelocateWhenCodeIsInserted()
    {
        IrScript sample = Sample();
        sample.Code.Insert(0, Op(0x55, IrOperand.Literal(9, 1), Imm(7)));
        for (int k = 0; k < sample.Code.Count; k++)          // shift label indices past the insertion
            sample.Code[k] = sample.Code[k] with
            {
                Operands = sample.Code[k].Operands.Select(o =>
                    o.Kind == IrOperandKind.CodeLabel ? IrOperand.Label(o.Type, (int)o.Value + 1) : o).ToArray(),
            };
        byte[] bytes = IrEncoder.Encode(sample);
        IrScript decoded = IrDecoder.Decode(bytes, Table, Contracts);

        Assert.Equal(IrOperand.Label(0, 5), decoded.Code[2].Operands[2]);
        Assert.Equal("「テスト」", decoded.Data[decoded.Code[3].Operands[1].Value is var item ? (int)item : 0].Text);
        Assert.Equal(bytes, IrEncoder.Encode(decoded));
    }

    [Fact]
    public void BrokenTableGeometryIsRejected()
    {
        byte[] bytes = IrEncoder.Encode(Sample());
        BitConverter.GetBytes(BitConverter.ToInt32(bytes, 8 + 10 * 4) + 1).CopyTo(bytes, 8 + 10 * 4);
        Assert.Throws<InvalidDataException>(() => IrDecoder.Decode(bytes, Table, Contracts));
    }

    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [Fact]
    public void InstalledHimegariCorpusRoundTripsByteIdentically()
        => AssertCorpusRoundTrips("Himegari_Game", "SYS4422");

    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [Fact]
    public void InstalledKamidoriCorpusRoundTripsByteIdentically()
        => AssertCorpusRoundTrips("Kamidori", "SYS4433");

    private static void AssertCorpusRoundTrips(string directory, string abiId)
    {
        string gameRoot = Environment.GetEnvironmentVariable("AGE_GAME_ROOT")
            ?? Path.Combine(Paths.Workspace, directory);
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"install not found at {gameRoot}");
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, abiId);
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var store = new Sys4AssetStore(catalog, gameRoot, gameRoot);

        IrRoundTripReport report = IrRoundTrip.Run(
            catalog.EnumerateScripts().Select(packed =>
                (packed.Asset.Name, packed.PackedId, (Func<byte[]>)(() => store.ReadAll(packed.Asset)))),
            table, OpcodeContracts.Load(Paths.OpcodesJson));

        Assert.Empty(report.Failures);
        Assert.Equal(report.Scripts, report.Identical);
        Assert.True(report.Scripts > 0);
        Assert.Equal(0, report.ExplicitTableScripts);
    }
}
