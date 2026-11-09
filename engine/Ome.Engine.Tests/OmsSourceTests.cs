using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Script;
using Ome.Script.Abi;
using Ome.Script.Compile;
using Ome.Script.Ir;
using Ome.Script.Layout;
using Ome.Script.Syntax;
using Ome.Script.Text;
using Xunit;

/// <summary>Open Maid Script gate 2 (raw level): IR → source + sidecar → IR → identical bytes.</summary>
public class OmsSourceTests
{
    private const string Abi = "SYS4422";
    private const uint None = 0xFFFFFFFF;

    private static readonly OpcodeTable Table = new(new Dictionary<int, (string, int)>
    {
        [0x02] = ("exit", 0), [0x03] = ("call-script", 1), [0x05] = ("ret", 0), [0x55] = ("mov", 2),
        [0x64] = ("copy-inline-int-array", 2), [0x6e] = ("show-text", 2), [0x71] = ("reset-adv-text-layout", 1),
        [0x8c] = ("jmp", 1), [0x8f] = ("call", 1), [0xa0] = ("jcc", 3),
    }, Abi);

    private static readonly OpcodeContracts Contracts = OpcodeContracts.FromCodeTargets(new Dictionary<int, int[]>
    {
        [0x8c] = [1], [0x8f] = [1], [0xa0] = [2, 3],
    });

    private static IrInstruction Op(int opcode, params IrOperand[] operands) => new(opcode, operands);
    private static IrOperand Imm(uint value) => IrOperand.Literal(0, value);

    private static IrScript Canonical() => new()
    {
        Name = "SAMPLE.BIN",
        Magic = "SYS4422 "u8.ToArray(),
        LocalCounts = [5, 1, 1, 4, 1, 1],          // highest used + 2: L[3] and LP[2]
        Code =
        [
            Op(0x55, IrOperand.Literal(9, 3), Imm(1)),
            Op(0xa0, IrOperand.Literal(9, 3), Imm(None), IrOperand.Label(0, 4)),
            Op(0x6e, Imm(0), IrOperand.Data(2, 0)),
            Op(0x71, Imm(1)),
            Op(0x64, IrOperand.Literal(0xC, 2), IrOperand.Data(0, 1)),
            Op(0x55, IrOperand.Literal(1, BitConverter.SingleToUInt32Bits(-1.5f)), Imm(None)),
            Op(0x8f, IrOperand.Label(0, 8)),
            Op(0x8f, IrOperand.Literal(3, 0x6be)),
            Op(0x8c, IrOperand.Label(0, 0)),
            Op(0x05),
        ],
        Data = [IrDataItem.FromText("「テスト\"引用\"」\\"), IrDataItem.FromInts([4, -2, 5])],
    };

    private static (byte[] Original, byte[] Rebuilt, OmsDecompilation Decompiled) RoundTrip(IrScript script)
    {
        byte[] original = IrEncoder.Encode(script);
        byte[] rebuilt = OmsSourceRoundTrip.RoundTrip(original, script.Name, Table, Contracts, out OmsDecompilation decompiled);
        return (original, rebuilt, decompiled);
    }

    [Fact]
    public void CanonicalScriptRoundTripsWithAnEmptySidecar()
    {
        var (original, rebuilt, decompiled) = RoundTrip(Canonical());
        Assert.True(decompiled.Layout.IsEmpty, decompiled.Layout.ToJson());
        Assert.Equal(original, rebuilt);
        Assert.Contains("@op(0x6e, 0x0, \"「テスト\\\"引用\\\"」\\\\\")", decompiled.Source);
        Assert.Contains("@op(0x64, LP[0x2], [0x4, -2, 0x5])", decompiled.Source);
        Assert.Contains("@op(0x55, float -1.5, -1)", decompiled.Source);
        Assert.Contains("@op(0x8f, G[0x6be])", decompiled.Source);
        Assert.Contains("label_0:", decompiled.Source);
    }

    [Fact]
    public void NonCanonicalLayoutIsCarriedBySidecarFields()
    {
        IrScript script = Canonical();
        uint[] padded = script.Data[0].Dwords.ToArray();
        padded[^1] ^= 0x00AA0000;
        var reordered = new IrScript
        {
            Name = script.Name,
            Magic = "SYS4424 "u8.ToArray(),
            LocalCounts = [9, 1, 1, 4, 1, 1],
            Field6 = 0x1D,
            Code = script.Code.Select(i => i with
            {
                Operands = i.Operands.Select(o => o.Kind == IrOperandKind.DataRef
                    ? IrOperand.Data(o.Type, o.Value == 0 ? 2 : 0) : o).ToArray(),
            }).ToList(),
            Data = [script.Data[1], new IrDataItem(IrDataKind.Raw, [0xFFFFFFFF]),
                    script.Data[0] with { Dwords = padded }],
            Tables = new IrTables([0x1Eu], [], [0x1Bu, 0x1Eu]),
        };
        var (original, rebuilt, decompiled) = RoundTrip(reordered);
        OmsLayout layout = decompiled.Layout;

        Assert.Equal("SYS4424 ", layout.Magic);
        Assert.Equal(0x1Du, layout.Field6);
        Assert.Equal([9, 1, 1, 4, 1, 1], layout.LocalCounts!);
        Assert.Equal(3, layout.Data!.Count);
        Assert.Equal([1], layout.Data[0].Refs!);
        Assert.Equal([0xFFFFFFFFu], layout.Data[1].Raw!);
        Assert.Equal([0], layout.Data[2].Refs!);
        Assert.Equal(padded, layout.ItemDwords![0]);
        Assert.NotNull(layout.Tables);
        Assert.Equal(original, rebuilt);
    }

    [Fact]
    public void SharedDataItemsRoundTrip()
    {
        IrScript script = Canonical();
        script.Code.Insert(3, Op(0x6e, Imm(1), IrOperand.Data(2, 0)));     // a second reference to item 0
        for (int k = 0; k < script.Code.Count; k++)
            script.Code[k] = script.Code[k] with
            {
                Operands = script.Code[k].Operands.Select(o => o.Kind == IrOperandKind.CodeLabel && o.Value >= 3
                    ? IrOperand.Label(0, (int)o.Value + 1) : o).ToArray(),
            };
        var (original, rebuilt, decompiled) = RoundTrip(script);
        Assert.Equal([0, 1], decompiled.Layout.Data![0].Refs!);
        Assert.Equal(original, rebuilt);
    }

    [Theory]
    [InlineData("script \"X\"\n    @op(0x8c, nowhere)\n", "unknown label")]
    [InlineData("script \"X\"\na:\na:\n    @op(0x05)\n", "defined twice")]
    [InlineData("script \"X\"\n    @op(0x55, L[1])\n", "takes 2 operands")]
    [InlineData("script \"X\"\n    @op(0x6e, 0, \"éā\")\n", "not encodable")]
    [InlineData("script \"X\"\n    @op(0x999)\n", "not in ABI")]
    public void CompileErrorsNameTheProblem(string source, string expected)
    {
        var error = Assert.Throws<OmsCompileException>(() =>
            OmsCompiler.Compile(Parser.ParseScript(source, "t.oms"), Abi, Table));
        Assert.Contains(expected, error.Message);
        Assert.StartsWith("t.oms:", error.Message);
    }

    [Theory]
    [InlineData("script \"X\"\n    @op(0x55, L[1], \"open)\n", "unterminated string")]
    [InlineData("script \"X\"\n    @op(0x55 L[1])\n", "expected )")]
    [InlineData("oms 2\nscript \"X\"\n", "unsupported language version")]
    [InlineData("script \"X\"\n    @op(0x100000000)\n", "does not fit in 32 bits")]
    public void SyntaxErrorsCarryALocation(string source, string expected)
    {
        var error = Assert.Throws<OmsSyntaxException>(() => Parser.ParseScript(source, "t.oms"));
        Assert.Contains(expected, error.Message);
    }

    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [Fact]
    public void InstalledHimegariCorpusRoundTripsThroughRawSource()
        => AssertCorpus("Himegari_Game", "SYS4422");

    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [Fact]
    public void InstalledKamidoriCorpusRoundTripsThroughRawSource()
        => AssertCorpus("Kamidori", "SYS4433");

    private static void AssertCorpus(string directory, string abiId)
    {
        string gameRoot = Environment.GetEnvironmentVariable("OME_GAME_ROOT") ?? Path.Combine(Paths.Workspace, directory);
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"install not found at {gameRoot}");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var store = new Sys4AssetStore(catalog, gameRoot, gameRoot);
        OmsSourceRoundTripReport report = OmsSourceRoundTrip.Run(
            catalog.EnumerateScripts().Select(packed =>
                (packed.Asset.Name, packed.PackedId, (Func<byte[]>)(() => store.ReadAll(packed.Asset)))),
            OpcodeTableJson.Load(Paths.OpcodesJson, abiId), OpcodeContracts.Load(Paths.OpcodesJson));
        Assert.Empty(report.Failures);
        Assert.Equal(report.Scripts, report.Identical);
    }
}
