using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Script;
using Ome.Script.Abi;
using Ome.Script.Compile;
using Ome.Script.Ir;
using Ome.Script.Layout;
using Ome.Script.Modules;
using Ome.Script.Syntax;
using Ome.Script.Text;
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
        @dialogue inline line(text: text, speaker: speaker = none, voice: const = none, form: const = 0x0) {
            static if speaker == none {
                prologue()
            } else {
                G[0x10] = speaker
                G[0x11] = form
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
        => name == Globals.Name ? Globals : name == Scripts.Name ? Scripts : name == Adv.Name ? Adv
           : name == Heroes.Name ? Heroes : name == Party.Name ? Party : null;

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
    public void BoundParametersAndResultsBindToDeclaredNames()
    {
        // Spec §6.3: a parameter binds to any declared cell, as `sub adv_load_layer(layer: adv_gfx_layer_index, …)`
        // does. Named cells lower exactly as the raw cells they stand for.
        const string Body = """
            G[9] = scale(2, L[1] + 1)
            exit
            sub scale(factor: {0}, value: {1}) -> {2} {
                {2} = factor * value
            }
            """;
        string Bind(string factor, string value, string result)
            => Body.Replace("{0}", factor).Replace("{1}", value).Replace("{2}", result);
        string raw = Shape(Compile(Bind("G[1]", "L[2]", "G[3]")));
        string named = Shape(Compile("global int factor_cell @ 0x1\nglobal int result_cell @ 0x3\nlocal int value_slot @ 0x2\n"
                                     + Bind("factor_cell", "value_slot", "result_cell")));
        Assert.Equal(raw, named);

        var array = Assert.Throws<OmsCompileException>(() => Compile("global int table_cells[4] @ 0x10\n"
                                                                      + Bind("table_cells", "L[2]", "G[3]")));
        Assert.Contains("'table_cells' is an array", array.Message);
        var unknown = Assert.Throws<OmsCompileException>(() => Compile(Bind("missing_cell", "L[2]", "G[3]")));
        Assert.Contains("'missing_cell' is not a declared cell", unknown.Message);
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
        // pinned is L[3], so i takes L[4] and buf L[5..6]; the temporary pool starts at L[7]. buf[1] has a constant
        // index, so it is the cell L[6] itself (spec §7.6).
        Assert.Equal("55(9:4,0:63) "
                     + "61(c:0,3:6930,9:4) 55(c:0,3:500) "
                     + "12c(c:0,3:6f70,9:4,0:e,0:d) 12c(c:1,3:6f70,9:4,0:e,0:d) 50(c:0,c:1,0:1) "
                     + "12c(c:0,3:6f70,9:4,0:e,0:2) 12c(c:1,3:900,9:4,0:8,9:3) 50(3:500,c:0,c:1) "
                     + "55(9:6,3:0) "
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
        // A plain cell inside a declared array prints as its element (spec §11.3).
        Assert.Contains("chapter_titles[0x5] = chapter_titles[L[0x1]]", source);
        // Shapes the declaration cannot express stay raw: another row width, or a 2-D view of a 1-D array.
        Assert.Contains("unit_level[0x5] = range_grid[L[0x1]][L[0x2]] + G[0x6f70:0x7][L[0x1]][L[0x2]] + G[0x6930:0x2][L[0x1]][L[0x2]]", source);
    }

    [Fact]
    public void ElementsWithConstantIndicesAreTheirCells()
    {
        IrScript script = Compile("""
            use test.globals
            const ROW = 2
            unit_level[3] = 1
            unit_stats[ROW].max_fs = 2
            unit_stats[1][2] = 3
            G[0x100][5] = 4
            G[0x200:0x4][2][3] = 5
            range_grid[5000][1] = 6
            L[1] = unit_level[2 + 1]
            note unit_level[4]
            exit
            """);
        // Constant elements are plain cells; a computed index keeps its lookup (spec §7.6).
        Assert.Equal("55(3:6933,0:1) 55(3:6f99,0:2) 55(3:6f80,0:3) 55(3:105,0:4) 55(3:20b,0:5) "
                     + "55(3:d9cdf,0:6) 50(9:3,0:2,0:1) 61(c:0,3:6930,9:3) 55(9:1,c:0) 1a7(3:6934) 2()", Shape(script));
    }

    [Theory]
    [InlineData("use test.globals\nunit_level[445] = 1\n", "index 445 is outside 'unit_level'")]
    [InlineData("use test.globals\nunit_stats[100].hp = 1\n", "index 100 is outside 'unit_stats'")]
    [InlineData("use test.globals\nrange_grid[1][27] = 1\n", "index 27 is outside 'range_grid'")]
    public void ConstantIndicesOutsideANamedArrayAreErrors(string body, string expected)
    {
        var error = Assert.Throws<OmsCompileException>(() => Compile(body));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void CellsInsideDeclaredArraysDecompileAsElementsAndRecompileIdentically()
    {
        IrScript compiled = Compile("""
            G[0x6933] = 1
            G[0x6f99] = G[0x6f80] + G[0x6930]
            GS[0x5] = GS[0xd2]
            G[0x6930:0x2][L[1]][L[2]] = 3
            exit
            """);
        var (first, second, decompiled) = RoundTrip(IrEncoder.Encode(compiled), Globals);
        Assert.Equal(first, second);
        string source = decompiled.Source;
        Assert.Contains("unit_level[0x3] = 0x1", source);
        // The cell at an array's own address keeps the bare name; a cell past the end of every array stays raw.
        Assert.Contains("unit_stats[0x2].max_fs = unit_stats[0x1][0x2] + unit_level", source);
        Assert.Contains("chapter_titles[0x5] = GS[0xd2]", source);
        Assert.Contains("G[0x6930:0x2][L[0x1]][L[0x2]] = 0x3", source);
    }

    [Fact]
    public void LookupsWithConstantIndicesStayExplicitInstructions()
    {
        // No original compiler output has one, but the VM accepts it; folded, it would read back as a plain cell.
        IrScript compiled = Compile("""
            lookup_array(out LP[0], G[0x100], 5)
            LP[0] = 7
            exit
            """);
        Assert.Equal("61(c:0,3:100,0:5) 55(c:0,0:7) 2()", Shape(compiled));
        var (first, second, decompiled) = RoundTrip(IrEncoder.Encode(compiled));
        Assert.Equal(first, second);
        Assert.DoesNotContain("G[0x100][0x5]", decompiled.Source);
    }

    /// <summary>A script's own arrays and a table over them (spec §5.3): fields in two banks, a scalar field and a
    /// row field with named columns.</summary>
    private const string HeroDeclarations = """
        global int levels[10] @ 0x100
        global int stats[10][4] @ 0x200 { hp: 0, mp: 1 }
        global str names[10] @ 0x50
        table hero[10] {
            level = levels
            stats = stats, name = names
        }

        """;

    [Fact]
    public void TableFieldsAreTheirArraysElements()
    {
        string table = Shape(Compile(HeroDeclarations + """
            hero[3].level = 1
            hero[2].stats.mp = 2
            hero[2].stats[3] = 3
            hero[L[1]].stats.hp = hero[L[2]].level
            exit
            """));
        string arrays = Shape(Compile(HeroDeclarations + """
            levels[3] = 1
            stats[2].mp = 2
            stats[2][3] = 3
            stats[L[1]].hp = levels[L[2]]
            exit
            """));
        Assert.Equal(arrays, table);
        // Constant records are plain cells (spec §7.6): 0x200 + 2 * 4 + 1 and 0x200 + 2 * 4 + 3.
        Assert.StartsWith("55(3:103,0:1) 55(3:209,0:2) 55(3:20b,0:3) ", table);
    }

    [Fact]
    public void WithBlocksEmitOnlyTheirStatements()
    {
        string with = Shape(Compile(HeroDeclarations + """
            with hero[L[1]] {
                .level = 1
                .stats.mp += .level
            again:
                with hero[2] { .stats[3] = .level }
                if .level < 5 goto again
            }
            exit
            """));
        string plain = Shape(Compile(HeroDeclarations + """
            hero[L[1]].level = 1
            hero[L[1]].stats.mp += hero[L[1]].level
            again:
            hero[2].stats[3] = hero[2].level
            if hero[L[1]].level < 5 goto again
            exit
            """));
        Assert.Equal(plain, with);
    }

    [Theory]
    [InlineData("hero[1] = 1", "a record of 'hero' is not a value")]
    [InlineData("hero[1].mana = 1", "table 'hero' has no field 'mana'")]
    [InlineData("hero[10].level = 1", "record 10 is outside table 'hero', which has 10 records")]
    [InlineData("hero[1].level[0] = 1", "holds one value per record")]
    [InlineData("hero[1].stats = 1", "is a row")]
    [InlineData("hero[1][2].level = 1", "takes one record index")]
    [InlineData("levels[1].x[2] = 1", "'levels' is not a table")]
    [InlineData("with levels[1] { L[1] = 1 }", "'levels' is not a table")]
    [InlineData("table short[5] { level = levels }", "needs an array of 5 entries or rows")]
    [InlineData("table other[10] { level = missing }", "names 'missing', which is not a declared array")]
    [InlineData("table levels[10] { level = levels }", "'levels' is declared twice")]
    public void TableMisuseIsACompileError(string statement, string expected)
    {
        var error = Assert.Throws<OmsCompileException>(() => Compile(HeroDeclarations + statement + "\n"));
        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData(".level = 1\n", "'.field' needs an enclosing with block")]
    [InlineData("with hero[1].level { }\n", "with takes one table record")]
    [InlineData("table t[2] { a = x, a = y }\n", "field 'a' is named twice")]
    public void RecordSyntaxErrors(string body, string expected)
    {
        var error = Assert.Throws<OmsSyntaxException>(() => Compile(body));
        Assert.Contains(expected, error.Message);
    }

    /// <summary>A generated-style module with a table over its arrays.</summary>
    private static readonly GameModule Heroes = new("test.heroes",
    [
        new GlobalSymbol("hero_levels", 0x3, 0x100, [10], new Dictionary<string, int>()),
        new GlobalSymbol("hero_stats", 0x3, 0x200, [10, 4], new Dictionary<string, int> { ["hp"] = 0, ["mp"] = 1 }),
        new GlobalSymbol("hero_names", 0x5, 0x50, [10], new Dictionary<string, int>()),
    ], tables: [new TableSymbol("hero", 10, [("level", "hero_levels"), ("stats", "hero_stats"), ("name", "hero_names")])]);

    [Fact]
    public void TableFieldsDecompileThroughTheirTableAndRecompileIdentically()
    {
        IrScript compiled = Compile("""
            G[0x103] = 1
            G[0x209] = G[0x100] + G[0x20b]
            G[0x200:0x4][L[1]][0x0] = G[0x100][L[2]]
            GS[0x51] = GS[0x50]
            exit
            """);
        var (first, second, decompiled) = RoundTrip(IrEncoder.Encode(compiled), Heroes);
        Assert.Equal(first, second);
        string source = decompiled.Source;
        Assert.Contains("\nuse test.heroes\n", source);
        Assert.Contains("hero[0x3].level = 0x1", source);
        // The cell at an array's own address keeps the array's name (spec §11.3).
        Assert.Contains("hero[0x2].stats.mp = hero_levels + hero[0x2].stats[0x3]", source);
        Assert.Contains("hero[L[0x1]].stats.hp = hero[L[0x2]].level", source);
        Assert.Contains("hero[0x1].name = hero_names", source);
    }

    [Fact]
    public void RecordFieldRunsDecompileAsWithBlocks()
    {
        IrScript compiled = Compile("""
            if L[1] goto middle
            G[0x103] = 1
            G[0x20d] = 2
            GS[0x53] = GS[0x50]
            G[0x104] = 3
            G[0x105] = 4
            G[0x214] = 5
            middle:
            G[0x215] = 6
            store_shared_profile_int(G[0x216])
            exit
            """);
        var (first, second, decompiled) = RoundTrip(IrEncoder.Encode(compiled), Heroes);
        Assert.Equal(first, second);
        string source = string.Join("\n", decompiled.Source.Split('\n').Select(line => line.Trim()));
        // Two or more consecutive targets in one record with a constant index form a block, as do calls whose first
        // argument is such a field; a label ends the run. (The decompiler names the label by its position.)
        Assert.Contains("\nwith hero[0x3] {\n.level = 0x1\n.stats.mp = 0x2\n.name = hero_names\n}\nhero[0x4].level = 0x3\n"
                        + "with hero[0x5] {\n.level = 0x4\n.stats.hp = 0x5\n}\nlabel_25:\nwith hero[0x5] {\n.stats.mp = 0x6\n"
                        + "store_shared_profile_int(.stats[0x2])\n}\n", source);
    }

    /// <summary>A source module declaring a table and a template that writes through it with a <c>with</c> block.</summary>
    private static readonly GameModule Party = GameModule.FromSource(Parser.ParseScript("""
        module test.party
        global int party_levels[4] @ 0x400
        table member[4] { level = party_levels }
        inline level_up(slot: expr) {
            with member[slot] { .level += 1 }
        }
        """, "party.oms"));

    [Fact]
    public void SourceModulesDeclareTablesForScriptsAndTemplates()
    {
        Assert.Equal("member", Assert.Single(Party.Tables).Name);
        string table = Shape(Compile("use test.party\nlevel_up(L[1])\nmember[2].level = 5\nexit\n"));
        string raw = Shape(Compile("G[0x400][L[1]] += 1\nG[0x402] = 5\nexit\n"));
        Assert.Equal(raw, table);
    }

    [Fact]
    public void RegistryTablesLoadWithTheirUsableFields()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oms-tables-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {"cells": {
                "G:0x100": {"name": "hero_levels", "type": "int[10]", "address": "0x100"},
                "G:0x200": {"name": "hero_stats", "type": "int[10][4]", "address": "0x200", "columns": {"0": "hp"}},
                "G:0x300": {"name": "short_array", "type": "int[5]", "address": "0x300"}
             },
             "tables": [
                {"name": "hero", "records": 10, "usage": "",
                 "fields": {"level": "hero_levels", "stats": "hero_stats", "short": "short_array", "gone": "missing"}},
                {"name": "empty", "records": 3, "usage": "", "fields": {"x": "short_array"}}
             ]}
            """);
        try
        {
            TableSymbol hero = Assert.Single(GameModule.LoadGlobals("test", path).Tables);
            Assert.Equal("hero", hero.Name);
            Assert.Equal<(string, string)>([("level", "hero_levels"), ("stats", "hero_stats")], hero.Fields);
        }
        finally
        {
            File.Delete(path);
        }
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
    [InlineData("G[1] = 99999999999999999999999\n", "does not fit in 32 bits")]
    [InlineData("G[1] = 0x10000000000000000\n", "does not fit in 32 bits")]
    [InlineData("G[1] = -4294967295\n", "negative integer does not fit in 32 bits")]
    [InlineData("const BIG = -2147483649\n", "negative integer does not fit in 32 bits")]
    [InlineData("local int big[4294967295]\n", "is larger than 2147483647")]
    public void ElementSyntaxErrorsNameTheProblem(string body, string expected)
    {
        var error = Assert.Throws<OmsSyntaxException>(() => Compile(body));
        Assert.Contains(expected, error.Message);
    }

    // The compiler and the decompiler walk the tree recursively; input deep enough to overflow the stack is refused.
    [Fact]
    public void DeepNestingIsASyntaxErrorNotAStackOverflow()
    {
        string parentheses = "G[1] = " + new string('(', 5000) + "1" + new string(')', 5000) + "\n";
        Assert.Contains("nested more than", Assert.Throws<OmsSyntaxException>(() => Compile(parentheses)).Message);
        string chain = "G[1] = 1" + string.Concat(Enumerable.Repeat(" + 1", 5000)) + "\n";
        Assert.Contains("nested more than", Assert.Throws<OmsSyntaxException>(() => Compile(chain)).Message);
        string blocks = string.Concat(Enumerable.Repeat("if G[1] {\n", 500)) + string.Concat(Enumerable.Repeat("}\n", 500));
        Assert.Contains("nested more than", Assert.Throws<OmsSyntaxException>(() => Compile(blocks)).Message);
        Assert.Equal(-2147483648, unchecked((int)Assert.Single(Compile("G[1] = -2147483648\n").Code).Operands[1].Value));
    }

    [Fact]
    public void ADeclarationInsideABlockIsDeclared()
    {
        IrScript inside = Compile("if G[1] {\n local int x\n x = 7\n}\nexit\n");
        IrScript outside = Compile("local int x\nif G[1] {\n x = 7\n}\nexit\n");
        Assert.Equal(Shape(outside), Shape(inside));
    }

    [Fact]
    public void AnIndexedCellParameterStandsForTheArgumentsArray()
    {
        IrScript viaTemplate = Compile("use test.globals\ninline clear(a: cell) {\n a[L[0]] = 0\n}\nclear(unit_level)\nexit\n");
        IrScript direct = Compile("use test.globals\nunit_level[L[0]] = 0\nexit\n");
        Assert.Equal(Shape(direct), Shape(viaTemplate));
        IrScript rawCell = Compile("inline clear(a: cell) {\n a[L[0]] = 0\n}\nclear(G[0x40])\nexit\n");
        IrScript rawDirect = Compile("G[0x40][L[0]] = 0\nexit\n");
        Assert.Equal(Shape(rawDirect), Shape(rawCell));
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
            G[0x11] = 0
            play(0x34e)
            show_text(0, "Hello.")
            G[0x10] = 7
            G[0x11] = 0
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
        // line: narration, speaker, speaker with voice (a voice without a speaker leaves no trace; form, which has a
        // default, may: narration never uses it); twice: one.
        Assert.Equal(["line:2", "line:3", "line:5", "twice:4"],
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
            HERO "Formal." (form: 2)
            twice(2, L[0])
            G[3] = 1
            exit
            """));
        var (second, decompiled) = IdiomaticRoundTrip(first);
        Assert.Equal(first, second);
        Assert.Null(decompiled.LiftFailure);
        Assert.Equal(3, decompiled.LibrarySubs);
        Assert.Equal(5, decompiled.TemplateLifts);
        Assert.Contains("\nuse test.adv\n", decompiled.Source);
        Assert.EndsWith("""

                "Narration."
                HERO "Hello." (voice: 0x34e)
                HERO "Quiet."
                HERO "Formal." (form: 0x2)
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
    [InlineData("inline bad() {\n local int x\n}\nbad()\n", "cannot appear in a template body")]
    [InlineData("use test.globals\nflow_request:\nexit\n", "names both a label or sub of this script and an imported cell")]
    [InlineData("use test.globals\nexit\nsub unit_level {\n return\n}\n", "names both a label or sub of this script and an imported cell")]
    [InlineData("G[1] = G[2] ^ G[3] ^ G[4] ^ G[5] ^ G[6] ^ G[7] ^ G[8] ^ G[9] ^ G[10] ^ G[11]\n", "'^' nested more than 8 deep")]
    [InlineData("inline f(a: cell) {\n a[1][2] = 0\n}\nf(G[4])\n", "cannot be indexed that way")]
    public void TemplateErrorsNameTheProblem(string body, string expected)
    {
        var error = Assert.Throws<OmsCompileException>(() => Compile(body));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void CodePositionsHeldAsNumbersDecodeAsLabelsAndRelocateWhenCodeMoves()
    {
        // op 0x140 returns a code position (registry value "code-address"); the scene loop compares it with the
        // loop head's offset, which the original compiler wrote as a plain number.
        var table = new OpcodeTable(new Dictionary<int, (string, int)>
        {
            [0x02] = ("exit", 0), [0x55] = ("mov", 2), [0x5a] = ("eq", 3), [0x8c] = ("jmp", 1), [0xa0] = ("jcc", 3),
            [0x140] = ("u0041F9C0", 4),
        }, Abi);
        var contracts = OpcodeContracts.FromParts(
            new Dictionary<int, int[]> { [0x8c] = [1], [0xa0] = [2, 3] },
            new Dictionary<(int, int), string[]>
            {
                [(0x140, 1)] = ["write"], [(0x140, 2)] = ["read-string"], [(0x140, 3)] = ["read-string"],
                [(0x140, 4)] = ["read"],
            },
            new Dictionary<int, string> { [0x140] = "coroutine-label-yield" },
            new Dictionary<int, string> { [0x8c] = "transfer", [0xa0] = "transfer" },
            [(0x140, 1)]);
        var intrinsics = new IntrinsicCatalog(table, contracts);
        IrScript Build(string body) => OmsCompiler.Compile(Parser.ParseScript("script \"T.BIN\"\n" + body, "t.oms"), Abi,
            table, null, intrinsics);
        // Writing the immediate as a number reproduces the original form; 0x9 is also an instruction start (the
        // first mov) but nothing compares it with a code value.
        byte[] original = IrEncoder.Encode(Build("""
            G[0x6be] = coroutine_label_yield("LABEL", "J", G[0x6be])
            G[0x6c3] = 0x22
            if G[0x6c3] == G[0x6be] goto done
            G[0x10] = 0x9
            done:
            exit
            G[0x11] = 0x1
            exit
            """));
        IrScript decoded = IrDecoder.Decode(original, table, contracts, "T.BIN", out int literal);
        Assert.Equal(0, literal);
        Assert.Equal(IrOperand.Label(0, 6), decoded.Code[1].Operands[1]);   // 0x22 is instruction 6, `G[0x11] = 1`
        Assert.Equal(IrOperand.Literal(0, 9), decoded.Code[4].Operands[1]);
        Assert.Equal(original, IrEncoder.Encode(decoded));

        OmsDecompilation source = OmsCoreDecompiler.Decompile(decoded, Abi, intrinsics);
        Assert.Contains("    G[0x6c3] = &label_22\n", source.Source);
        Assert.Contains("    G[0x10] = 0x9\n", source.Source);
        Assert.Contains("\nlabel_22:\n", source.Source);
        // An edit that inserts code before the loop head moves it; the compiled immediate follows.
        IrScript edited = OmsCompiler.Compile(Parser.ParseScript(source.Source.Replace(
            "\n\n", "\n\n    G[0x20] = 0x1\n"), "t.oms"), Abi, table, null, intrinsics);
        IrScript redecoded = IrDecoder.Decode(IrEncoder.Encode(edited), table, contracts, "T.BIN", out _);
        Assert.Equal(IrOperand.Label(0, 7), redecoded.Code[2].Operands[1]);
        Assert.Equal(0x27, redecoded.Code[7].OriginalOffset);
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
    public void InstalledKamidoriCorpusRoundTripsThroughIdiomaticSource()
        => AssertCorpus("Kamidori", "SYS4433", level: OmsSourceRoundTrip.IdiomaticLevel);

    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [Fact]
    public void InstalledKamidoriIdiomaticRebuildIsAFixedPoint()
        => AssertCorpus("Kamidori", "SYS4433", fixedPoint: true, level: OmsSourceRoundTrip.IdiomaticLevel);

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
        string gameRoot = Environment.GetEnvironmentVariable("OME_GAME_ROOT") ?? Path.Combine(Paths.Workspace, directory);
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
