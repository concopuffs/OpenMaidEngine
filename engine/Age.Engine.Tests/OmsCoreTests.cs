using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Script;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Modules;
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
        [0x6e] = ("show-text", 2), [0x7b] = ("coroutine-save-yield-handlers", 2), [0x7c] = ("coroutine-resume", 0),
        [0x8c] = ("jmp", 1), [0x8f] = ("call", 1), [0xa0] = ("jcc", 3), [0xcc] = ("mouse-callback", 2),
        [0x12c] = ("lookup-array-2d", 5), [0x1a2] = ("store-shared-profile-int", 1),
        [0x1a3] = ("load-shared-profile-int", 1), [0x1a7] = ("comment", 1), [0x1f4] = ("stmt-begin", 0),
        [0x1f5] = ("stmt-end", 0), [0x249] = ("u00422EB0", 3),
    }, Abi);

    private static readonly OpcodeContracts Contracts = OpcodeContracts.FromParts(
        new Dictionary<int, int[]> { [0x7b] = [1, 2], [0x8c] = [1], [0x8f] = [1], [0xa0] = [2, 3], [0xcc] = [2] },
        new Dictionary<(int, int), string[]>
        {
            [(0x61, 1)] = ["bind"], [(0x61, 2)] = ["address"], [(0x61, 3)] = ["read"],
            [(0x6e, 1)] = ["read"], [(0x6e, 2)] = ["read-string"],
            [(0x1a2, 1)] = ["read", "cell"], [(0x1a3, 1)] = ["write", "cell"],
            [(0x7b, 1)] = ["read"], [(0x7b, 2)] = ["read"], [(0xcc, 1)] = ["read"], [(0xcc, 2)] = ["read"],
        },
        new Dictionary<int, string>
        {
            [0x61] = "lookup-array", [0x6e] = "show-text", [0x1a2] = "store-shared-profile-int",
            [0x1a3] = "load-shared-profile-int", [0x1a7] = "comment", [0x1f4] = "stmt-begin", [0x1f5] = "stmt-end",
            [0x249] = "u00422EB0", [0x7b] = "coroutine-save-yield-handlers", [0x7c] = "coroutine-resume",
            [0xcc] = "register-mouse-callback",
        },
        new Dictionary<int, string>
        {
            [0x7b] = "coroutine", [0x8c] = "transfer", [0x8f] = "call", [0xa0] = "transfer", [0xcc] = "callback",
        });

    private static readonly IntrinsicCatalog Intrinsics = new(Table, Contracts);

    /// <summary>A small generated-style module: a scalar, a 1-D array, a row table with named columns, an open
    /// row table and a string cell sharing address 0x0 with an integer one (separate banks).</summary>
    private static readonly GameModule Globals = new("test.globals",
    [
        new GlobalSymbol("flow_request", 0x3, 0x0, [], new Dictionary<string, int>()),
        new GlobalSymbol("chapter_titles", 0x5, 0x0, [210], new Dictionary<string, int>()),
        new GlobalSymbol("unit_level", 0x3, 0x6930, [445], new Dictionary<string, int>()),
        new GlobalSymbol("unit_stats", 0x3, 0x6f70, [100, 14], new Dictionary<string, int> { ["hp"] = 0, ["max_fs"] = 13 }),
        new GlobalSymbol("range_grid", 0x3, 0xb8d86, [0, 27], new Dictionary<string, int>()),
    ]);

    private static readonly GameModule Scripts = GameModule.Scripts("test",
        [("RECOVER.BIN", 0x329d), ("$1$SC2600.BIN", 0x01000005)]);

    /// <summary>A hand-written source module: constants, a pinned global, a dialogue template with optional
    /// constant parameters, a template with a label of its own, and library subs (one calling another, one unused).</summary>
    private static readonly GameModule Adv = GameModule.FromSource(Parser.ParseScript("""
        module test.adv
        use test.globals
        enum speaker {
            HERO = 7
        }
        global int voice_pending @ 0x2e47
        @dialogue inline line(text: text, speaker: speaker = none, voice: const = none) {
            static if speaker == none {
                prologue()
            } else {
                G[0x10] = speaker
                static if voice != none {
                    play(voice)
                }
            }
            show_text(0, text)
        }
        inline twice(value: expr, target: cell) {
            target = value
        again:
            target += value
            if target < 10 goto again
        }
        sub prologue {
            voice_pending = 0
        }
        sub play(v: G[0x20]) {
            G[0x21] = v
            helper()
        }
        sub helper {
            G[0x22] = flow_request
        }
        sub unused {
            G[0x23] = 1
        }
        """, "adv.oms"));

    private static GameModule? Module(string name)
        => name == Globals.Name ? Globals : name == Scripts.Name ? Scripts : name == Adv.Name ? Adv : null;

    private static readonly OmsIdioms Idioms = OmsIdioms.Build([Globals, Scripts, Adv], Abi, Intrinsics);

    private static (byte[] Second, OmsDecompilation Decompiled) IdiomaticRoundTrip(byte[] first)
    {
        byte[] second = OmsSourceRoundTrip.RoundTrip(first, "T.BIN", Table, Contracts, Intrinsics,
            OmsSourceRoundTrip.IdiomaticLevel, out OmsDecompilation decompiled, out _, [Globals, Scripts, Adv], Idioms);
        return (second, decompiled);
    }

    private static IrScript Compile(string body, OmsLayout? layout = null)
        => OmsCompiler.Compile(Parser.ParseScript("script \"T.BIN\"\n" + body, "t.oms"), Abi, Table, layout, Intrinsics,
            Module);

    private static string Shape(IrScript script)
        => string.Join(" ", script.Code.Select(i => i.Opcode.ToString("x") + "("
            + string.Join(",", i.Operands.Select(o => o.Kind switch
            {
                IrOperandKind.CodeLabel => $"@{o.Value}",
                IrOperandKind.DataRef => $"d{o.Value}",
                _ => o.Value == None ? "-1" : $"{o.Type:x}:{o.Value:x}",
            })) + ")"));

    private static (byte[] First, byte[] Second, OmsDecompilation Decompiled) RoundTrip(byte[] first, GameModule? globals = null)
    {
        byte[] second = OmsSourceRoundTrip.RoundTrip(first, "T.BIN", Table, Contracts, Intrinsics,
            OmsSourceRoundTrip.CoreLevel, out OmsDecompilation decompiled, out _, globals == null ? null : [globals]);
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

    [Fact]
    public void SubsArePlacedBlocksWithBoundParametersAndResults()
    {
        IrScript script = Compile("""
            G[9] = scale(2, L[1] + 1)
            register_mouse_callback(0x32, on_tick)
            coroutine_save_yield_handlers(handler, handler)
            L[1] = load_shared_profile_int()
            G[8] = &on_tick
            exit
            sub scale(factor: G[1], value: L[2]) -> G[3] {
                G[3] = factor * value
            }
            sub on_tick {
                if G[4] { return }
                G[5] += 1
            }
            coroutine handler {
                G[6] = 0
                resume
            }
            """);
        // Arguments bind to the parameter cells (the second straight into L[2]); the result cell is copied out.
        // Parameter names stand for their cells; each sub ends with an implicit ret; the coroutine at its resume.
        Assert.Equal("55(3:1,0:2) 50(9:2,9:1,0:1) 8f(@9) 55(3:9,3:3) cc(0:32,@11) 7b(@15,@15) 1a3(9:1) "
                     + "55(3:8,@11) 2() "
                     + "52(3:3,3:1,9:2) 5() "
                     + "a0(3:4,-1,@13) 5() 50(3:5,3:5,0:1) 5() "
                     + "55(3:6,0:0) 7c()", Shape(script));
    }

    [Fact]
    public void DecompiledSubsAndCoroutinesRecompileIdentically()
    {
        var (first, second, decompiled) = SourceRoundTrip("""
            register_mouse_callback(0x32, tick)
            coroutine_save_yield_handlers(on_yield, on_yield)
            helper()
            G[1] = load_shared_profile_int()
            exit
            sub helper {
                for L[0] = 0; L[0] < 3; L[0] += 1 {
                    if G[2][L[0]] == 0 { return }
                }
                return
            }
            sub tick {
                G[3] += 1
                return
            }
            coroutine on_yield {
                G[4] = 1
                resume
            }
            """);
        Assert.Equal(first, second);
        Assert.Contains("\nsub label_", decompiled);
        Assert.Contains("\ncoroutine label_", decompiled);
        Assert.Contains("    resume\n}", decompiled);
        Assert.Contains("G[0x1] = load_shared_profile_int()", decompiled);
        Assert.DoesNotContain("@op(", decompiled);
    }

    [Theory]
    [InlineData("G[1] = 1\nsub f {\n return\n}\nexit\n", "control falls into sub 'f'")]
    [InlineData("exit\ncoroutine c {\n G[1] = 1\n}\n", "coroutine 'c' must end with resume")]
    [InlineData("coroutine_save_yield_handlers(f, f)\nexit\nsub f {\n return\n}\n", "takes a coroutine; 'f' is a sub")]
    [InlineData("register_mouse_callback(1, c)\nexit\ncoroutine c {\n resume\n}\n", "'c' is a coroutine")]
    [InlineData("c()\nexit\ncoroutine c {\n resume\n}\n", "coroutine 'c' cannot be called")]
    [InlineData("f(1)\nexit\nsub f(a: G[1], b: G[2]) {\n return\n}\n", "f takes 2 arguments, not 1")]
    [InlineData("G[1] = f()\nexit\nsub f {\n return\n}\n", "has no result cell")]
    [InlineData("G[1] = show_text(1)\n", "does not write exactly operand 1")]
    public void SubErrorsNameTheProblem(string body, string expected)
    {
        var error = Assert.Throws<OmsCompileException>(() => Compile(body));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void DeclarationsNameCellsArraysAndRowTables()
    {
        IrScript script = Compile("""
            use test.globals
            const MAX = 99
            global int counter @ 0x500
            global str label @ 0x12
            global int grid[][8] @ 0x900
            local int pinned @ 0x3
            local int i
            local int buf[2]
            i = MAX
            unit_level[i] = counter
            unit_stats[i].max_fs += 1
            counter = unit_stats[i][2] + grid[i][pinned]
            buf[1] = flow_request
            label = chapter_titles[i]
            exit
            """);
        // pinned is L[3], so i takes L[4] and buf L[5..6]; the temporary pool starts at L[7].
        Assert.Equal("55(9:4,0:63) "
                     + "61(c:0,3:6930,9:4) 55(c:0,3:500) "
                     + "12c(c:0,3:6f70,9:4,0:e,0:d) 12c(c:1,3:6f70,9:4,0:e,0:d) 50(c:0,c:1,0:1) "
                     + "12c(c:0,3:6f70,9:4,0:e,0:2) 12c(c:1,3:900,9:4,0:8,9:3) 50(3:500,c:0,c:1) "
                     + "61(c:0,9:5,0:1) 55(c:0,3:0) "
                     + "61(e:0,5:0,9:4) 55(5:12,e:0) 2()", Shape(script));
    }

    [Fact]
    public void DecompiledRegistryCellsPrintByNameAndRecompileIdentically()
    {
        IrScript compiled = Compile("""
            use test.globals
            flow_request = unit_level[L[1]] + unit_stats[L[1]].max_fs + unit_stats[L[2]][0x3]
            GS[0x5] = chapter_titles[L[1]]
            G[0x6935] = range_grid[L[1]][L[2]] + G[0x6f70:0x7][L[1]][L[2]] + G[0x6930:0x2][L[1]][L[2]]
            exit
            """);
        var (first, second, decompiled) = RoundTrip(IrEncoder.Encode(compiled), Globals);
        Assert.Equal(first, second);
        string source = decompiled.Source;
        Assert.Contains("\nuse test.globals\n", source);
        Assert.Contains("flow_request = unit_level[L[0x1]] + unit_stats[L[0x1]].max_fs + unit_stats[L[0x2]][0x3]", source);
        Assert.Contains("GS[0x5] = chapter_titles[L[0x1]]", source);
        // Shapes the declaration cannot express stay raw: another row width, or a 2-D view of a 1-D array.
        Assert.Contains("G[0x6935] = range_grid[L[0x1]][L[0x2]] + G[0x6f70:0x7][L[0x1]][L[0x2]] + G[0x6930:0x2][L[0x1]][L[0x2]]", source);
    }

    [Fact]
    public void ScriptsRunByCatalogName()
    {
        IrScript compiled = Compile("""
            use test.scripts
            run RECOVER
            run "$1$SC2600"
            run 0x7
            exit
            """);
        Assert.Equal("3(0:329d) 3(0:1000005) 3(0:7) 2()", Shape(compiled));
        byte[] first = IrEncoder.Encode(compiled);
        byte[] second = OmsSourceRoundTrip.RoundTrip(first, "T.BIN", Table, Contracts, Intrinsics,
            OmsSourceRoundTrip.CoreLevel, out OmsDecompilation decompiled, out _, [Scripts]);
        Assert.Equal(first, second);
        Assert.Contains("use test.scripts\n", decompiled.Source);
        Assert.Contains("    run RECOVER\n    run \"$1$SC2600\"\n    run 0x7\n", decompiled.Source);
        var error = Assert.Throws<OmsCompileException>(() => Compile("use test.scripts\nrun \"NOPE\"\n"));
        Assert.Contains("unknown script \"NOPE\"", error.Message);
    }

    [Fact]
    public void WarningsFlagRecursionZeroDivisorsAndUnobservedOpcodes()
    {
        // Registry observations: only exit, mov, div and call were ever executed under this ABI.
        const string registry = """
            {"opcodes": [
              {"op": "0x2", "observed_revisions": ["SYS4422"]}, {"op": "0x55", "observed_revisions": ["SYS4422"]},
              {"op": "0x53", "observed_revisions": ["SYS4422"]}, {"op": "0x8f", "observed_revisions": ["SYS4422"]},
              {"op": "0x5", "observed_revisions": ["SYS4422"]},
              {"op": "0x249", "observed_revisions": ["SYS4433"], "semantics": {"name": "u00422EB0"}}
            ]}
            """;
        var contracts = OpcodeContracts.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(registry)));
        var warnings = new List<OmsWarning>();
        OmsCompiler.Compile(Parser.ParseScript("""
            script "T.BIN"
            G[1] = G[2] / 0
            G[3] %= 0
            op_0x249(1, 2, 3)
            @op(0x249, 1, 2, 3)
            walk(1)
            exit
            sub walk(steps: G[4]) {
                walk(steps - 1)
            }
            """, "t.oms"), Abi, Table, null, new IntrinsicCatalog(Table, contracts), warnings: warnings);
        string[] messages = warnings.Select(warning => warning.Message).ToArray();
        Assert.Equal(4, messages.Length);
        Assert.Contains("'/' by a constant zero (the VM does not guard it)", messages);
        Assert.Contains("'%=' by a constant zero (the VM does not guard it)", messages);
        Assert.Contains("opcode 0x249 is installed in SYS4422 but no game corpus executes it", messages);   // once
        Assert.Contains("the recursive call to 'walk' overwrites its own bound parameters", messages);
    }

    [Theory]
    [InlineData("use nowhere.globals\nexit\n", "unknown module 'nowhere.globals'")]
    [InlineData("L[1] = missing[2]\n", "'missing' is not a declared array")]
    [InlineData("use test.globals\nL[1] = unit_level[1][2]\n", "'unit_level' is declared with 1 dimensions, not 2")]
    [InlineData("use test.globals\nL[1] = unit_stats[1].mp\n", "'unit_stats' has no column 'mp'")]
    [InlineData("const A = 1\nconst A = 2\n", "'A' is declared twice")]
    public void DeclarationErrorsNameTheProblem(string body, string expected)
    {
        var error = Assert.Throws<OmsCompileException>(() => Compile(body));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void AGlobalNeedsItsCell()
    {
        var error = Assert.Throws<OmsSyntaxException>(() => Compile("global int x\n"));
        Assert.Contains("a global needs its cell", error.Message);
    }

    [Theory]
    [InlineData("lookup_array(LP[1], G[1], G[2])\n", "is written; mark it 'out'")]
    [InlineData("show_text(out L[1], \"x\")\n", "is not written; remove 'out'")]
    [InlineData("break\n", "outside a loop")]
    [InlineData("nothing_here(1)\n", "unknown intrinsic or label")]
    [InlineData("L[1] = GP[2][L[3]]\n", "element access needs an integer, float or string cell")]
    [InlineData("load_shared_profile_int(out L[1] + 1)\n", "only a storage cell, a parameter or an element")]
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

    // ------------------------------------------------------------------------------------------- gate 5

    [Fact]
    public void TemplatesDialogueAndLibrarySubsCompileToTheirHandExpandedForm()
    {
        IrScript expanded = Compile("""
            use test.adv
            "Narration."
            HERO "Hello." (voice: 0x34e)
            HERO "Quiet."
            twice(G[1] + 1, G[2])
            twice(2, L[0])
            exit
            """);
        // Each expansion gets its own label; referenced library subs link after the script in link order (play
        // pulls in helper during the same pass); the unused sub is left out.
        IrScript byHand = Compile("""
            prologue()
            show_text(0, "Narration.")
            G[0x10] = 7
            play(0x34e)
            show_text(0, "Hello.")
            G[0x10] = 7
            show_text(0, "Quiet.")
            G[2] = G[1] + 1
            again1:
            G[2] += G[1] + 1
            if G[2] < 10 goto again1
            L[0] = 2
            again2:
            L[0] += 2
            if L[0] < 10 goto again2
            exit
            sub prologue {
                G[0x2e47] = 0
            }
            sub play(v: G[0x20]) {
                G[0x21] = v
                helper()
            }
            sub helper {
                G[0x22] = G[0x0]
            }
            """);
        Assert.Equal(Shape(byHand), Shape(expanded));
        Assert.Equal(IrEncoder.Encode(byHand), IrEncoder.Encode(expanded));
    }

    [Fact]
    public void TheSidecarPlacesLibrarySubsWhereTheOriginalPutThem()
    {
        const string body = """
            use test.adv
            prologue()
            exit
            G[5] = 1
            exit
            """;
        Assert.Equal("8f(@4) 2() 55(3:5,0:1) 2() 55(3:2e47,0:0) 5()", Shape(Compile(body)));
        var layout = new OmsLayout { Library = new SortedDictionary<string, int> { ["prologue"] = 2 } };
        Assert.Equal("8f(@2) 2() 55(3:2e47,0:0) 5() 55(3:5,0:1) 2()", Shape(Compile(body, layout)));
        layout.Library["prologue"] = 99;
        Assert.Contains("not linked at that index", Assert.Throws<OmsCompileException>(() => Compile(body, layout)).Message);
    }

    [Fact]
    public void TemplatePatternsCoverEveryTraceableStaticIfVariant()
    {
        // line: narration, speaker, speaker with voice (a voice without a speaker leaves no trace); twice: one.
        Assert.Equal(["line:2", "line:2", "line:4", "twice:4"],
            Idioms.Templates.Select(pattern => $"{pattern.Template.Name}:{pattern.Code.Length}").OrderBy(text => text));
        Assert.Equal(["helper", "play", "prologue", "unused"], Idioms.Library.Select(pattern => pattern.Name).Order());
    }

    [Fact]
    public void LibrarySubsAndTemplateUsesDecompileBackToIdiomaticSource()
    {
        byte[] first = IrEncoder.Encode(Compile("""
            use test.adv
            "Narration."
            HERO "Hello." (voice: 0x34e)
            HERO "Quiet."
            twice(2, L[0])
            G[3] = 1
            exit
            """));
        var (second, decompiled) = IdiomaticRoundTrip(first);
        Assert.Equal(first, second);
        Assert.Null(decompiled.LiftFailure);
        Assert.Equal(3, decompiled.LibrarySubs);
        Assert.Equal(4, decompiled.TemplateLifts);
        Assert.Contains("\nuse test.adv\n", decompiled.Source);
        Assert.EndsWith("""

                "Narration."
                HERO "Hello." (voice: 0x34e)
                HERO "Quiet."
                twice(0x2, L[0x0])
                G[0x3] = 0x1
                exit

            """.Replace("\r\n", "\n"), decompiled.Source);
        // The library subs sit at the end in link order, so a canonical compile reproduces them: no sidecar at all.
        Assert.True(decompiled.Layout.IsEmpty, decompiled.Layout.ToJson());
    }

    [Fact]
    public void LibrarySubsOutOfLinkOrderKeepTheirPlaceInTheSidecar()
    {
        var layout = new OmsLayout { Library = new SortedDictionary<string, int> { ["prologue"] = 2 } };
        byte[] first = IrEncoder.Encode(Compile("use test.adv\nprologue()\nexit\nG[5] = 1\nexit\n", layout));
        var (second, decompiled) = IdiomaticRoundTrip(first);
        Assert.Equal(first, second);
        Assert.Equal(1, decompiled.LibrarySubs);
        Assert.Equal(new SortedDictionary<string, int> { ["prologue"] = 2 }, decompiled.Layout.Library);
        Assert.Contains("    prologue()\n    exit\n    G[0x5] = 0x1\n    exit\n", decompiled.Source);
        Assert.DoesNotContain("sub ", decompiled.Source);
    }

    [Fact]
    public void LibraryCopiesAreRecognizedInScriptsCompiledWithoutTheModule()
    {
        // The first sub is a byte-identical copy of test.adv's prologue; the second differs in one operand.
        byte[] first = IrEncoder.Encode(Compile("""
            copy()
            other()
            exit
            sub copy {
                G[0x2e47] = 0
            }
            sub other {
                G[0x2e47] = 5
            }
            """));
        var (second, decompiled) = IdiomaticRoundTrip(first);
        Assert.Equal(first, second);
        Assert.Equal(1, decompiled.LibrarySubs);
        Assert.Contains("    prologue()\n    label_", decompiled.Source);
        Assert.Contains("\nsub label_", decompiled.Source);
        Assert.Contains("    voice_pending = 0x5\n", decompiled.Source);   // the module also names its globals
        Assert.Contains("\nuse test.adv\n", decompiled.Source);
    }

    [Theory]
    [InlineData("\"Hi\"\n", "needs an @dialogue template")]
    [InlineData("use test.adv\nVILLAIN \"Hi\"\n", "unknown speaker 'VILLAIN'")]
    [InlineData("use test.adv\ntwice(1)\n", "'twice' needs argument 'target'")]
    [InlineData("use test.adv\ntwice(1, 2)\n", "argument 'target' of 'twice' must be a cell or element")]
    [InlineData("use test.adv\ntwice(1, G[1], 3)\n", "template 'twice' takes 2 arguments")]
    [InlineData("use test.adv\nline(\"a\", bogus: 1)\n", "template 'line' has no parameter 'bogus'")]
    [InlineData("use test.adv\nline(\"a\", text: \"b\")\n", "argument 'text' of 'line' is given twice")]
    [InlineData("use test.adv\nline(\"a\", voice: G[1])\n", "a const argument must be an integer, a constant or none")]
    [InlineData("use test.adv\nline(G[1])\n", "argument 'text' of 'line' must be a string literal")]
    [InlineData("use test.adv\nconst HERO = 1\n", "'HERO' is already declared by module test.adv")]
    [InlineData("use test.adv\nG[1] = twice(1, G[2])\n", "template 'twice' has no result")]
    [InlineData("use test.adv\nexit\nsub prologue {\n return\n}\n", "'prologue' is defined by the script and by module test.adv")]
    [InlineData("use test.adv\nprologue()\n", "control falls into sub 'prologue'")]
    [InlineData("static if 1 {\n exit\n}\n", "static if belongs in a template body")]
    [InlineData("inline again() {\n again()\n}\nagain()\n", "template 'again' expands itself")]
    [InlineData("inline bad(v: const = none) {\n G[1] = v\n}\nbad()\n", "'none' has no value here")]
    [InlineData("inline bad(v: const) {\n static if v == G[1] {\n exit\n }\n}\nbad(1)\n", "static if condition may use only")]
    [InlineData("loop {\n leave()\n}\ninline leave() {\n break\n}\n", "'break' outside a loop")]
    public void TemplateErrorsNameTheProblem(string body, string expected)
    {
        var error = Assert.Throws<OmsCompileException>(() => Compile(body));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void IndexAddressedLocalArraysAreDeclaredToKeepTheirFrame()
    {
        // L[3] is an array whose upper elements only a computed index reaches; the original pool started at L[8].
        IrScript compiled = Compile("""
            local int scratch[5] @ 3
            L[1] = scratch[L[2]] + 1
            G[1] = L[1] * 2 + G[2]
            exit
            """);
        Assert.Equal([10, 1, 1, 2, 1, 1], compiled.LocalCounts);   // L[8] (temporary) + 2
        Assert.Equal("61(c:0,9:3,9:2) 50(9:1,c:0,0:1) 52(9:8,9:1,0:2) 50(3:1,9:8,3:2) 2()", Shape(compiled));
        var (first, second, decompiled) = RoundTrip(IrEncoder.Encode(compiled));
        Assert.Equal(first, second);
        Assert.Contains("\nlocal int array_3[0x5] @ 0x3\n", decompiled.Source);
        Assert.Contains("L[0x1] = array_3[L[0x2]] + 0x1", decompiled.Source);
        Assert.Null(decompiled.Layout.TempBase);
        Assert.Null(decompiled.Layout.LocalCounts);
        // Without the sidecar the declaration alone keeps temporaries above the array.
        Assert.Equal(first, IrEncoder.Encode(OmsCompiler.Compile(Parser.ParseScript(decompiled.Source, "t.oms"), Abi, Table,
            null, Intrinsics)));
    }

    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [Fact]
    public void InstalledHimegariCorpusRoundTripsThroughCoreSource()
        => AssertCorpus("Himegari_Game", "SYS4422");

    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [Fact]
    public void InstalledHimegariCanonicalRebuildIsAFixedPoint()
        => AssertCorpus("Himegari_Game", "SYS4422", fixedPoint: true);

    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [Fact]
    public void InstalledHimegariCorpusRoundTripsThroughIdiomaticSource()
        => AssertCorpus("Himegari_Game", "SYS4422", level: OmsSourceRoundTrip.IdiomaticLevel);

    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [Fact]
    public void InstalledHimegariIdiomaticRebuildIsAFixedPoint()
        => AssertCorpus("Himegari_Game", "SYS4422", fixedPoint: true, level: OmsSourceRoundTrip.IdiomaticLevel);

    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [Fact]
    public void InstalledKamidoriCanonicalRebuildIsAFixedPoint()
        => AssertCorpus("Kamidori", "SYS4433", fixedPoint: true);

    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [Fact]
    public void InstalledKamidoriCorpusRoundTripsThroughCoreSource()
        => AssertCorpus("Kamidori", "SYS4433");

    private static void AssertCorpus(string directory, string abiId, bool fixedPoint = false,
                                     string level = OmsSourceRoundTrip.CoreLevel)
    {
        string gameRoot = Environment.GetEnvironmentVariable("AGE_GAME_ROOT") ?? Path.Combine(Paths.Workspace, directory);
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"install not found at {gameRoot}");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var store = new Sys4AssetStore(catalog, gameRoot, gameRoot);
        string profile = abiId == "SYS4422" ? "himegari" : "kamidori";
        Assert.True(File.Exists(Paths.GlobalsJson(profile)), $"run globals_build.py --build for {profile}");
        List<GameModule> modules =
        [
            GameModule.LoadGlobals(profile, Paths.GlobalsJson(profile)),
            GameModule.Scripts(profile, catalog.EnumerateScripts().Select(packed => (packed.Asset.Name, packed.PackedId))),
        ];
        if (level == OmsSourceRoundTrip.IdiomaticLevel) modules.AddRange(GameModule.LoadSources(Paths.OmsModules(profile), profile));
        OmsSourceRoundTripReport report = OmsSourceRoundTrip.Run(
            catalog.EnumerateScripts().Select(packed =>
                (packed.Asset.Name, packed.PackedId, (Func<byte[]>)(() => store.ReadAll(packed.Asset)))),
            OpcodeTableJson.Load(Paths.OpcodesJson, abiId), OpcodeContracts.Load(Paths.OpcodesJson), level, modules,
            fixedPoint);
        Assert.Empty(report.Failures);
        Assert.Equal(report.Scripts, report.Identical);
        Assert.Equal(0, report.RawStatements);
        if (level == OmsSourceRoundTrip.IdiomaticLevel)
        {
            // Gate 5: the scene library and the dialogue template lift everywhere they occur, and no lift is abandoned.
            Assert.Empty(report.LiftFailures ?? []);
            Assert.True(report.LibrarySubs > report.Scripts * 10, $"library subs {report.LibrarySubs}");
            Assert.True(report.TemplateLifts > 20000, $"template lifts {report.TemplateLifts}");
        }
        if (fixedPoint)
        {
            // Gate 4: compiler-generated code recompiles exactly, almost always without any sidecar.
            Assert.True(report.EmptySidecars >= report.Scripts * 95 / 100, $"empty sidecars {report.EmptySidecars}");
            return;
        }
        // Stage C2: most compiler temporaries fold back into expressions, and the canonical allocator reproduces
        // all but a small residue of statements (the rest carry sidecar slot overrides).
        Assert.True(report.FoldedInstructions > report.Instructions / 8, $"folded {report.FoldedInstructions}");
        Assert.True(report.TempOverrides < report.FoldedInstructions / 20, $"overrides {report.TempOverrides}");
    }
}
