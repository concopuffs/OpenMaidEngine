using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Script;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Syntax;
using Age.Script.Text;
using Xunit;

/// <summary>Open Maid Script core level, stage C1: structured three-address statements lower to exactly the
/// canonical instruction shapes and decompile back to source that recompiles byte-identically.</summary>
public class OmsCoreTests
{
    private const string Abi = "SYS4422";
    private const uint None = 0xFFFFFFFF;

    private static readonly OpcodeTable Table = new(new Dictionary<int, (string, int)>
    {
        [0x02] = ("exit", 0), [0x03] = ("call-script", 1), [0x05] = ("ret", 0), [0x50] = ("add", 3),
        [0x51] = ("sub", 3), [0x55] = ("mov", 2), [0x5a] = ("eq", 3), [0x5c] = ("lt", 3), [0x61] = ("lookup-array", 3),
        [0x6e] = ("show-text", 2), [0x8c] = ("jmp", 1), [0x8f] = ("call", 1), [0xa0] = ("jcc", 3),
        [0x1a7] = ("comment", 1), [0x1f4] = ("stmt-begin", 0), [0x1f5] = ("stmt-end", 0), [0x249] = ("u00422EB0", 3),
    }, Abi);

    private static readonly OpcodeContracts Contracts = OpcodeContracts.FromParts(
        new Dictionary<int, int[]> { [0x8c] = [1], [0x8f] = [1], [0xa0] = [2, 3] },
        new Dictionary<(int, int), string[]>
        {
            [(0x61, 1)] = ["bind"], [(0x61, 2)] = ["address"], [(0x61, 3)] = ["read"],
            [(0x6e, 1)] = ["read"], [(0x6e, 2)] = ["read-string"],
        },
        new Dictionary<int, string>
        {
            [0x61] = "lookup-array", [0x6e] = "show-text", [0x1a7] = "comment", [0x1f4] = "stmt-begin",
            [0x1f5] = "stmt-end", [0x249] = "u00422EB0",
        });

    private static readonly IntrinsicCatalog Intrinsics = new(Table, Contracts);

    private static IrScript Compile(string body)
        => OmsCompiler.Compile(Parser.ParseScript("script \"T.BIN\"\n" + body, "t.oms"), Abi, Table, null, Intrinsics);

    private static string Shape(IrScript script)
        => string.Join(" ", script.Code.Select(i => i.Opcode.ToString("x") + "("
            + string.Join(",", i.Operands.Select(o => o.Kind switch
            {
                IrOperandKind.CodeLabel => $"@{o.Value}",
                IrOperandKind.DataRef => $"d{o.Value}",
                _ => o.Value == None ? "-1" : $"{o.Type:x}:{o.Value:x}",
            })) + ")"));

    private static (byte[] First, byte[] Second, string Decompiled) SourceRoundTrip(string body)
    {
        byte[] first = IrEncoder.Encode(Compile(body));
        byte[] second = OmsSourceRoundTrip.RoundTrip(first, "T.BIN", Table, Contracts, Intrinsics,
            OmsSourceRoundTrip.CoreLevel, out OmsDecompilation decompiled, out _);
        return (first, second, decompiled.Source);
    }

    [Fact]
    public void IfElseChainsLowerToTheCanonicalShapeAndShareOneEnd()
    {
        IrScript script = Compile("""
            if L[1] {
                L[2] = 1
            } else if L[3] {
                L[2] = 2
            } else {
                L[2] = 3
            }
            exit
            """);
        Assert.Equal("a0(9:1,-1,@3) 55(9:2,0:1) 8c(@7) a0(9:3,-1,@6) 55(9:2,0:2) 8c(@7) 55(9:2,0:3) 2()",
            Shape(script));
        Assert.Equal(script.Code[2].Operands[0], script.Code[5].Operands[0]);   // both arms jump to the same end
    }

    [Fact]
    public void LoopsAndJumpsLowerToBackEdgesAndBranchSenses()
    {
        IrScript script = Compile("""
            L[0] = 0
            loop {
                L[5] = L[0] < 10
                if !L[5] break
                L[0] = L[0] + 1
                if L[0] continue
            }
            do {
                L[1] = L[1] - 1
            } while L[1]
            if L[2] goto done else goto other
            other:
            done:
            exit
            """);
        Assert.Equal("55(9:0,0:0) 5c(9:5,9:0,0:a) a0(9:5,-1,@6) 50(9:0,9:0,0:1) a0(9:0,@1,-1) 8c(@1) "
                     + "51(9:1,9:1,0:1) a0(9:1,@6,-1) a0(9:2,@9,@9) 2()", Shape(script));
    }

    [Fact]
    public void StatementsCoverCallsNotesAtomicAndRawForms()
    {
        IrScript script = Compile("""
            atomic {
                show_text(0, "「テスト」")
                lookup_array(out LP[1], G[0x6930], G[0x66715])
            }
            note "LABEL"
            note G[0x6be]
            call *G[0x6be]
            helper()
            run 0x329d
            exit
            helper:
            op_0x249(0x2ae2, L[4], L[0x397])
            @op(0x55, L[1], float 1.5)
            return
            """);
        Assert.Equal("1f4() 6e(0:0,d0) 61(c:1,3:6930,3:66715) 1f5() 1a7(d1) 1a7(3:6be) 8f(3:6be) 8f(@10) 3(0:329d) 2() "
                     + "249(0:2ae2,9:4,9:397) 55(9:1,1:3fc00000) 5()", Shape(script));
    }

    [Fact]
    public void CompiledSourceDecompilesToSourceThatRecompilesIdentically()
    {
        var (first, second, decompiled) = SourceRoundTrip("""
            atomic {
                L[1] = 1
            }
            if L[1] {
                show_text(0, "a")
            } else if L[2] {
                show_text(0, "b")
            } else {
                show_text(0, "c")
            }
            loop {
                L[5] = L[0] < 10
                if !L[5] break
                L[0] = L[0] + 1
            }
            do {
                L[1] = L[1] - 1
            } while L[1]
            call *G[0x6be]
            exit
            """);
        Assert.Equal(first, second);
        Assert.True(decompiled.Contains("} else if L[0x2] {"), decompiled);
        Assert.Contains("if !L[0x5] break", decompiled);
        Assert.Contains("} while L[0x1]", decompiled);
        Assert.Contains("atomic {", decompiled);
        Assert.Contains("call *G[0x6be]", decompiled);
        Assert.DoesNotContain("@op(", decompiled);
    }

    [Fact]
    public void SharedJoinLabelsStayFlatGotosInsteadOfNestedElses()
    {
        var (first, second, decompiled) = SourceRoundTrip("""
            L[1] = G[1] == 1
            if L[1] {
                G[0] = 1
                goto finish
            }
            L[1] = G[1] == 2
            if L[1] {
                G[0] = 2
                goto finish
            }
            L[1] = G[1] == 3
            if L[1] {
                G[0] = 3
                goto finish
            }
            finish:
            exit
            """);
        Assert.Equal(first, second);
        Assert.DoesNotContain("else", decompiled);
        Assert.Equal(3, decompiled.Split("goto label_").Length - 1);
    }

    [Theory]
    [InlineData("lookup_array(LP[1], G[1], G[2])\n", "is written; mark it 'out'")]
    [InlineData("show_text(out L[1], \"x\")\n", "is not written; remove 'out'")]
    [InlineData("break\n", "outside a loop")]
    [InlineData("nothing_here(1)\n", "unknown intrinsic or label")]
    [InlineData("L[1] = L[2] + L[3] * L[4]\n", "single-operation assignments")]
    [InlineData("if L[1] + 1 { exit }\n", "single operand")]
    public void CoreCompileErrorsNameTheProblem(string body, string expected)
    {
        var error = Assert.Throws<OmsCompileException>(() => Compile(body));
        Assert.Contains(expected, error.Message);
    }

    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [Fact]
    public void InstalledHimegariCorpusRoundTripsThroughCoreSource()
        => AssertCorpus("Himegari_Game", "SYS4422");

    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [Fact]
    public void InstalledKamidoriCorpusRoundTripsThroughCoreSource()
        => AssertCorpus("Kamidori", "SYS4433");

    private static void AssertCorpus(string directory, string abiId)
    {
        string gameRoot = Environment.GetEnvironmentVariable("AGE_GAME_ROOT") ?? Path.Combine(Paths.Workspace, directory);
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"install not found at {gameRoot}");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var store = new Sys4AssetStore(catalog, gameRoot, gameRoot);
        OmsSourceRoundTripReport report = OmsSourceRoundTrip.Run(
            catalog.EnumerateScripts().Select(packed =>
                (packed.Asset.Name, packed.PackedId, (Func<byte[]>)(() => store.ReadAll(packed.Asset)))),
            OpcodeTableJson.Load(Paths.OpcodesJson, abiId), OpcodeContracts.Load(Paths.OpcodesJson),
            OmsSourceRoundTrip.CoreLevel);
        Assert.Empty(report.Failures);
        Assert.Equal(report.Scripts, report.Identical);
        Assert.Equal(0, report.RawStatements);
    }
}
