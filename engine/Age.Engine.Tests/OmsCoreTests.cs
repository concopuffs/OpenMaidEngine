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

/// <summary>Open Maid Script core level: structured statements (stage C1) and expressions (stage C2) lower to exactly
/// the canonical instruction shapes, and decompiled source recompiles byte-identically, with the sidecar carrying
/// any non-canonical temporary slots.</summary>
public class OmsCoreTests
{
    private const string Abi = "SYS4422";
    private const uint None = 0xFFFFFFFF;

    private static readonly OpcodeTable Table = new(new Dictionary<int, (string, int)>
    {
        [0x02] = ("exit", 0), [0x03] = ("call-script", 1), [0x05] = ("ret", 0), [0x50] = ("add", 3),
        [0x51] = ("sub", 3), [0x52] = ("mul", 3), [0x55] = ("mov", 2), [0x56] = ("and", 3), [0x57] = ("or", 3),
        [0x5a] = ("eq", 3), [0x5b] = ("ne", 3), [0x5c] = ("lt", 3), [0x5e] = ("gt", 3), [0x61] = ("lookup-array", 3),
        [0x6e] = ("show-text", 2), [0x8c] = ("jmp", 1), [0x8f] = ("call", 1), [0xa0] = ("jcc", 3),
        [0x12c] = ("lookup-array-2d", 5), [0x1a2] = ("store-shared-profile-int", 1),
        [0x1a3] = ("load-shared-profile-int", 1), [0x1a7] = ("comment", 1), [0x1f4] = ("stmt-begin", 0),
        [0x1f5] = ("stmt-end", 0), [0x249] = ("u00422EB0", 3),
    }, Abi);

    private static readonly OpcodeContracts Contracts = OpcodeContracts.FromParts(
        new Dictionary<int, int[]> { [0x8c] = [1], [0x8f] = [1], [0xa0] = [2, 3] },
        new Dictionary<(int, int), string[]>
        {
            [(0x61, 1)] = ["bind"], [(0x61, 2)] = ["address"], [(0x61, 3)] = ["read"],
            [(0x6e, 1)] = ["read"], [(0x6e, 2)] = ["read-string"],
            [(0x1a2, 1)] = ["read", "cell"], [(0x1a3, 1)] = ["write", "cell"],
        },
        new Dictionary<int, string>
        {
            [0x61] = "lookup-array", [0x6e] = "show-text", [0x1a2] = "store-shared-profile-int",
            [0x1a3] = "load-shared-profile-int", [0x1a7] = "comment", [0x1f4] = "stmt-begin", [0x1f5] = "stmt-end",
            [0x249] = "u00422EB0",
        });

    private static readonly IntrinsicCatalog Intrinsics = new(Table, Contracts);

    private static IrScript Compile(string body, OmsLayout? layout = null)
        => OmsCompiler.Compile(Parser.ParseScript("script \"T.BIN\"\n" + body, "t.oms"), Abi, Table, layout, Intrinsics);

    private static string Shape(IrScript script)
        => string.Join(" ", script.Code.Select(i => i.Opcode.ToString("x") + "("
            + string.Join(",", i.Operands.Select(o => o.Kind switch
            {
                IrOperandKind.CodeLabel => $"@{o.Value}",
                IrOperandKind.DataRef => $"d{o.Value}",
                _ => o.Value == None ? "-1" : $"{o.Type:x}:{o.Value:x}",
            })) + ")"));

    private static (byte[] First, byte[] Second, OmsDecompilation Decompiled) RoundTrip(byte[] first)
    {
        byte[] second = OmsSourceRoundTrip.RoundTrip(first, "T.BIN", Table, Contracts, Intrinsics,
            OmsSourceRoundTrip.CoreLevel, out OmsDecompilation decompiled, out _);
        return (first, second, decompiled);
    }

    private static (byte[] First, byte[] Second, string Decompiled) SourceRoundTrip(string body)
    {
        var (first, second, decompiled) = RoundTrip(IrEncoder.Encode(Compile(body)));
        return (first, second, decompiled.Source);
    }

    /// <summary>Rewrites one operand in the code (or in instructions [first, last]), as an original compiler with other
    /// slot choices would.</summary>
    private static IrScript Retarget(IrScript script, IrOperand from, IrOperand to, int first = 0, int last = int.MaxValue)
        => new()
        {
            Name = script.Name, Magic = script.Magic, LocalCounts = script.LocalCounts, Field6 = script.Field6,
            Data = script.Data, Tables = script.Tables,
            Code = script.Code.Select((i, k) => k < first || k > last ? i
                : i with { Operands = i.Operands.Select(o => o == from ? to : o).ToArray() }).ToList(),
        };

    // ------------------------------------------------------------------------------------------- stage C1

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

    // ------------------------------------------------------------------------------------------- stage C2

    [Fact]
    public void ExpressionsLowerThroughThePoolAboveTheHighestNamedLocal()
    {
        // L[4] is the highest local the source names, so the integer pool starts at L[5].
        IrScript script = Compile("""
            L[1] = L[2] + L[3] * L[4]
            if L[1] < 3 && L[2] { exit }
            L[1] = -L[2]
            L[1] = -(1)
            L[1] = -1
            exit
            """);
        Assert.Equal("52(9:5,9:3,9:4) 50(9:1,9:2,9:5) 5c(9:5,9:1,0:3) 5b(9:6,9:2,0:0) 56(9:7,9:5,9:6) a0(9:7,-1,@7) 2() "
                     + "51(9:1,0:0,9:2) 51(9:1,0:0,0:1) 55(9:1,-1) 2()", Shape(script));
    }

    [Fact]
    public void TheCanonicalPlanNumbersTemporariesInTheOriginalCompilersLabellingOrder()
    {
        // One counter per statement: an operator before its element operands, an element before its indices.
        IrScript script = Compile("""
            if G[1][G[2]] == 0 { exit }
            if G[1][G[2][G[3]]] { exit }
            store_shared_profile_int(G[1][G[2] + 1])
            exit
            """);
        Assert.Equal("61(c:1,3:1,3:2) 5a(9:0,c:1,0:0) a0(9:0,-1,@4) 2() "
                     + "61(c:1,3:2,3:3) 61(c:0,3:1,c:1) a0(c:0,-1,@8) 2() "
                     + "50(9:1,3:2,0:1) 61(c:0,3:1,9:1) 1a2(c:0) 2()", Shape(script));
    }

    [Fact]
    public void ElementTargetsOrderTheirLookupsByAssignmentForm()
    {
        IrScript script = Compile("""
            G[1][G[2]] += G[3]
            G[1][G[2]] = G[1][G[2]] + 1
            G[1][G[2]] = G[4]
            L[1] = G[0x341ab:0x35][L[2]][L[3]] + 1
            load_shared_profile_int(out G[1][L[2]])
            exit
            """);
        Assert.Equal("61(c:0,3:1,3:2) 61(c:1,3:1,3:2) 50(c:0,c:1,3:3) "
                     + "61(c:0,3:1,3:2) 61(c:1,3:1,3:2) 50(c:1,c:0,0:1) "
                     + "61(c:0,3:1,3:2) 55(c:0,3:4) "
                     + "12c(c:0,3:341ab,9:2,0:35,9:3) 50(9:1,c:0,0:1) "
                     + "61(c:0,3:1,9:2) 1a3(c:0) 2()", Shape(script));
    }

    [Fact]
    public void ForAndWhileLowerToTheOriginalLoopLayouts()
    {
        IrScript script = Compile("""
            for L[0] = 1; L[0] < 5; L[0] += 1 {
                if L[1] continue
                G[1] = L[0]
            }
            while G[2] {
                G[2] -= 1
            }
            exit
            """);
        // init; jmp cond; inc: step; cond: test; body; jmp inc; end:  then  head: test; body; jmp head; end:
        Assert.Equal("55(9:0,0:1) 8c(@3) 50(9:0,9:0,0:1) 5c(9:2,9:0,0:5) a0(9:2,-1,@8) a0(9:1,@2,-1) 55(3:1,9:0) "
                     + "8c(@2) a0(3:2,-1,@11) 51(3:2,3:2,0:1) 8c(@8) 2()", Shape(script));
    }

    [Fact]
    public void FoldedSourceDecompilesToExpressionsThatRecompileIdentically()
    {
        var (first, second, decompiled) = SourceRoundTrip("""
            atomic {
                L[1] = 1
            }
            if L[1] {
                show_text(0, "a")
            } else if G[1][G[2]] == 0x3 {
                show_text(0, "b")
            } else {
                show_text(0, "c")
            }
            for L[0] = 0; L[0] < 10; L[0] += 1 {
                if G[0x341ab:0x35][L[0]][L[1]] == 0 break
                G[3][L[0]] += L[1] * 2 - 1
            }
            while G[4] != 0 {
                G[4] -= 1
            }
            loop {
                if !(L[0] < 20 && G[5]) break
                L[0] += 2
            }
            do {
                L[1] = L[1] - 1
            } while L[1] > G[2]
            call *G[0x6be]
            exit
            """);
        Assert.Equal(first, second);
        string source = decompiled;
        Assert.Contains("} else if G[0x1][G[0x2]] == 0x3 {", source);
        Assert.Contains("for L[0x0] = 0x0; L[0x0] < 0xa; L[0x0] += 0x1 {", source);
        Assert.Contains("if G[0x341ab:0x35][L[0x0]][L[0x1]] == 0x0 break", source);
        Assert.Contains("G[0x3][L[0x0]] += L[0x1] * 0x2 - 0x1", source);
        Assert.Contains("while G[0x4] != 0x0 {", source);
        Assert.Contains("while L[0x0] < 0x14 && G[0x5] != 0x0 {", source);
        Assert.Contains("} while L[0x1] > G[0x2]", source);
        Assert.Contains("call *G[0x6be]", source);
        Assert.DoesNotContain("@op(", source);
        Assert.DoesNotContain("LP[", source);
    }

    [Fact]
    public void NonCanonicalTemporarySlotsTravelInTheSidecar()
    {
        IrScript compiled = Compile("""
            if G[1][G[2]] == 0 { exit }
            L[1] = L[2] + L[3] * L[4]
            exit
            """);
        // The original compiler bound the first statement's element to LP[2] and gave the second statement's
        // temporary L[7]; the pool still starts at L[5], the slot above the highest named local.
        IrScript original = Retarget(Retarget(compiled, IrOperand.Literal(0xC, 1), IrOperand.Literal(0xC, 2)),
            IrOperand.Literal(0x9, 5), IrOperand.Literal(0x9, 7), first: 4);
        var (first, second, decompiled) = RoundTrip(IrEncoder.Encode(original));
        Assert.Equal(first, second);
        Assert.Contains("if G[0x1][G[0x2]] == 0x0 {", decompiled.Source);
        Assert.Contains("L[0x1] = L[0x2] + L[0x3] * L[0x4]", decompiled.Source);
        OmsLayout layout = decompiled.Layout;
        Assert.Null(layout.TempBase);
        Assert.NotNull(layout.Temps);
        Assert.Equal([2u], layout.Temps![0].Refs);
        Assert.Null(layout.Temps[0].Ints);
        Assert.Equal([7u], layout.Temps[4].Ints);
    }

    [Fact]
    public void ScriptsWhoseFirstTwoReferenceRegistersAreSwappedRecordOneFlag()
    {
        IrScript compiled = Compile("""
            G[1][G[2]] = 1
            store_shared_profile_int(G[1][G[2]])
            if G[1][G[2]] == 0 { exit }
            exit
            """);
        IrScript swapped = Retarget(Retarget(Retarget(compiled, IrOperand.Literal(0xC, 0), IrOperand.Literal(0xC, 9)),
            IrOperand.Literal(0xC, 1), IrOperand.Literal(0xC, 0)), IrOperand.Literal(0xC, 9), IrOperand.Literal(0xC, 1));
        var (first, second, decompiled) = RoundTrip(IrEncoder.Encode(swapped));
        Assert.Equal(first, second);
        Assert.True(decompiled.Layout.RefSwap);
        Assert.Null(decompiled.Layout.Temps);
    }

    [Theory]
    [InlineData("lookup_array(LP[1], G[1], G[2])\n", "is written; mark it 'out'")]
    [InlineData("show_text(out L[1], \"x\")\n", "is not written; remove 'out'")]
    [InlineData("break\n", "outside a loop")]
    [InlineData("nothing_here(1)\n", "unknown intrinsic or label")]
    [InlineData("L[1] = GP[2][L[3]]\n", "element access needs an integer, float or string cell")]
    [InlineData("load_shared_profile_int(out L[1] + 1)\n", "only a storage cell or an element can be assigned")]
    public void CoreCompileErrorsNameTheProblem(string body, string expected)
    {
        var error = Assert.Throws<OmsCompileException>(() => Compile(body));
        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("L[1] = G[1][2][3]\n", "needs a row stride")]
    [InlineData("L[1] = G[1:3][2]\n", "takes exactly [row][col]")]
    public void ElementSyntaxErrorsNameTheProblem(string body, string expected)
    {
        var error = Assert.Throws<OmsSyntaxException>(() => Compile(body));
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
        // Stage C2: most compiler temporaries fold back into expressions, and the canonical allocator reproduces
        // all but a small residue of statements (the rest carry sidecar slot overrides).
        Assert.True(report.FoldedInstructions > report.Instructions / 8, $"folded {report.FoldedInstructions}");
        Assert.True(report.TempOverrides < report.FoldedInstructions / 20, $"overrides {report.TempOverrides}");
    }
}
