using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Script;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Modules;
using Age.Script.Patch;
using Age.Script.Syntax;
using Age.Script.Text;
using Xunit;

/// <summary>Open Maid Script fragments and patches (spec §10): fragment syntax, plan semantics, conflicts between plans,
/// stable tables, new storage and library reuse.</summary>
public class OmsPatchTests
{
    private const string Abi = "SYS4422";
    private const uint None = 0xFFFFFFFF;
    private const string Name = "T.BIN";

    private static readonly OpcodeTable Table = new(new Dictionary<int, (string, int)>
    {
        [0x02] = ("exit", 0), [0x03] = ("call-script", 1), [0x05] = ("ret", 0), [0x50] = ("add", 3),
        [0x52] = ("mul", 3), [0x55] = ("mov", 2), [0x5a] = ("eq", 3), [0x5c] = ("lt", 3), [0x6e] = ("show-text", 2),
        [0x71] = ("reset-adv-text-layout", 1), [0x8c] = ("jmp", 1), [0x8f] = ("call", 1), [0xa0] = ("jcc", 3),
        [0x1a3] = ("load-shared-profile-int", 1), [0x1a7] = ("comment", 1),
    }, Abi);

    private static readonly OpcodeContracts Contracts = OpcodeContracts.FromParts(
        new Dictionary<int, int[]> { [0x8c] = [1], [0x8f] = [1], [0xa0] = [2, 3] },
        new Dictionary<(int, int), string[]>
        {
            [(0x6e, 1)] = ["read"], [(0x6e, 2)] = ["read-string"], [(0x1a3, 1)] = ["write", "cell"],
        },
        new Dictionary<int, string>
        {
            [0x6e] = "show-text", [0x1a3] = "load-shared-profile-int", [0x1a7] = "comment",
        },
        new Dictionary<int, string> { [0x8c] = "transfer", [0x8f] = "call", [0xa0] = "transfer" });

    private static readonly IntrinsicCatalog Intrinsics = new(Table, Contracts);

    private static readonly GameModule Adv = GameModule.FromSource(Parser.ParseScript("""
        module test.adv
        enum speaker {
            HERO = 7
        }
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
        sub prologue {
            G[0x2e47] = 0
        }
        sub play(v: G[0x20]) {
            G[0x21] = v
            helper()
        }
        sub helper {
            G[0x22] = 1
        }
        """, "adv.oms"));

    private static readonly IReadOnlyList<GameModule> Modules = [Adv];
    private static readonly OmsIdioms Idioms = OmsIdioms.Build(Modules, Abi, Intrinsics);

    /// <summary>Two ADV lines (each led by a read-message boundary), a conditional jump back to the second, a local
    /// call and the called routine.</summary>
    private const string DialogueBase = """
        start:
            @op(0x71, 1)
            @op(0x6e, 0, "one")
        second:
            @op(0x71, 1)
            @op(0x6e, 0, "two")
            @op(0xa0, G[1], -1, second)
            @op(0x8f, routine)
            exit
        routine:
            G[2] = 1
            @op(0x05)
        """;

    private sealed record Base(IrScript Script, byte[] Bytes, int[] Offsets)
    {
        /// <summary>The base label of instruction <paramref name="index"/>.</summary>
        public string Label(int index) => $"label_{Offsets[index]:x}";
    }

    private static Base Compile(string body)
    {
        IrScript compiled = OmsCompiler.Compile(Parser.ParseScript($"script \"{Name}\"\n" + body, "base.oms"), Abi, Table,
            null, Intrinsics, name => Modules.FirstOrDefault(module => module.Name == name));
        byte[] bytes = IrEncoder.Encode(compiled);
        IrScript decoded = IrDecoder.Decode(bytes, Table, Contracts, Name);
        return new Base(decoded, bytes, OmsPatchBase.From(decoded).Offsets.ToArray());
    }

    private static string Header(byte[] bytes) => $"patch \"{Name}\" fingerprint \"{OmsPatchPlan.FingerprintOf(bytes)}\"\n";

    private static OmsPatchPlan Plan(Base base_, string body, bool idiomatic = false)
        => OmsPatchCompiler.Compile(Parser.ParseScript(Header(base_.Bytes) + body, "p.oms"), base_.Bytes, Table, Contracts,
            Intrinsics, Modules, idiomatic ? Idioms : null);

    private static OmsPatchResult Apply(Base base_, params OmsPatchPlan[] plans)
        => OmsPatchApplier.Apply(base_.Bytes, Name, plans, Table, Contracts);

    private static string Shape(IrScript script)
        => string.Join(" ", script.Code.Select(i => i.Opcode.ToString("x") + "("
            + string.Join(",", i.Operands.Select(o => o.Kind switch
            {
                IrOperandKind.CodeLabel => $"@{o.Value}",
                IrOperandKind.DataRef => $"d{o.Value}",
                _ => o.Value == None ? "-1" : $"{o.Type:x}:{o.Value:x}",
            })) + ")"));

    // ------------------------------------------------------------------------------------------- identity

    [Fact]
    public void AnEmptyPlanReproducesTheBase()
    {
        Base base_ = Compile(DialogueBase);
        Assert.Equal("71(0:1) 6e(0:0,d0) 71(0:1) 6e(0:0,d1) a0(3:1,-1,@2) 8f(@7) 2() 55(3:2,0:1) 5()", Shape(base_.Script));
        OmsPatchPlan empty = Plan(base_, "use test.adv\n");
        Assert.Empty(empty.Operations);
        Assert.Null(empty.Append);
        OmsPatchResult result = Apply(base_, empty);
        Assert.Equal(base_.Bytes, result.Bytes);
        Assert.True(result.Script.Tables.IsDerived);
    }

    [Fact]
    public void TheProbePlanAppliesAndAnEmptyPlanIsIdentical()
    {
        Base base_ = Compile(DialogueBase);
        OmsRelocation probed = OmsSourceRoundTrip.PatchProbe(base_.Bytes, Name, Table, Contracts, 4, out bool emptyIdentical);
        Assert.True(emptyIdentical);
        IrScript patched = IrDecoder.Decode(probed.Rebuilt, Table, Contracts, Name);
        // The notes are inert comments; without them the probed script is the base again, with its own targets.
        var kept = Enumerable.Range(0, patched.Code.Count).Where(j => !probed.RebuiltInserted![j]).ToList();
        Assert.Equal(base_.Script.Code.Count, kept.Count);
        Assert.Equal(base_.Script.Code.Select(i => i.Opcode), kept.Select(j => patched.Code[j].Opcode));
        Assert.All(patched.Code.Where((_, j) => probed.RebuiltInserted![j]), note => Assert.Equal(0x1a7, note.Opcode));
    }

    // ------------------------------------------------------------------------------------------- site operations

    [Fact]
    public void BaseTransfersLandOnABeforeBody()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchResult result = Apply(base_, Plan(base_, $"before site {base_.Label(2)} {{\n    G[9] = 1\n}}\n"));
        // The jump back to `second` (base index 2) now runs the inserted body first.
        Assert.Equal("71(0:1) 6e(0:0,d0) 55(3:9,0:1) 71(0:1) 6e(0:0,d1) a0(3:1,-1,@2) 8f(@8) 2() 55(3:2,0:1) 5()",
            Shape(result.Script));
        Assert.Equal([OmsPatchOrigin.Base, OmsPatchOrigin.Base, OmsPatchOrigin.Before, OmsPatchOrigin.Base],
            result.Provenance.Take(4).Select(origin => origin.Origin));
        Assert.True(result.Script.Tables.IsDerived);    // nothing inserted is a table site
    }

    [Fact]
    public void AnAfterBodyRunsOnlyWhenControlFallsThrough()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchResult result = Apply(base_, Plan(base_, $"after site {base_.Offsets[1]} {{\n    G[9] = 1\n}}\n"));
        // The jump to `second` skips the body inserted after the instruction in front of it.
        Assert.Equal("71(0:1) 6e(0:0,d0) 55(3:9,0:1) 71(0:1) 6e(0:0,d1) a0(3:1,-1,@3) 8f(@8) 2() 55(3:2,0:1) 5()",
            Shape(result.Script));
    }

    [Fact]
    public void AnAfterBodyNeedsAnInstructionThatFallsThrough()
    {
        Base base_ = Compile(DialogueBase);
        var error = Assert.Throws<OmsCompileException>(() => Plan(base_, $"after site {base_.Label(6)} {{\n    G[9] = 1\n}}\n"));
        Assert.Contains("never falls through", error.Message);
    }

    [Fact]
    public void ARegionIsReplacedAndItsOnlyDataReferenceDropsTheItem()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchResult result = Apply(base_, Plan(base_, $"replace site {base_.Label(1)} {{\n    @op(0x6e, 0, \"uno\")\n}}\n"));
        Assert.Equal("71(0:1) 6e(0:0,d1) 71(0:1) 6e(0:0,d0) a0(3:1,-1,@2) 8f(@7) 2() 55(3:2,0:1) 5()", Shape(result.Script));
        Assert.Equal(["two", "uno"], result.Script.Data.Select(item => item.Text));
        Assert.Equal(OmsPatchOrigin.Region, result.Provenance[1].Origin);
    }

    [Fact]
    public void ARegionAcceptsTransfersToItsStartButNotIntoItsInterior()
    {
        Base base_ = Compile(DialogueBase);
        // The conditional jump targets `second` (index 2): a region starting there is fine...
        OmsPatchResult result = Apply(base_, Plan(base_,
            $"replace site {base_.Label(2)} through {base_.Label(3)} {{\n    @op(0x71, 1)\n    @op(0x6e, 0, \"zwei\")\n}}\n"));
        Assert.Equal("71(0:1) 6e(0:0,d0) 71(0:1) 6e(0:0,d1) a0(3:1,-1,@2) 8f(@7) 2() 55(3:2,0:1) 5()", Shape(result.Script));
        // The removed line's T1 slot points at the replacement, which starts with a line of its own: no second slot.
        Assert.True(result.Script.Tables.IsDerived);
        // ...one that covers it from the instruction before is not.
        var error = Assert.Throws<OmsCompileException>(() => Plan(base_,
            $"replace site {base_.Label(1)} through {base_.Label(2)} {{\n    @op(0x6e, 0, \"x\")\n}}\n"));
        Assert.Contains($"transfers to 0x{base_.Offsets[2]:x} inside it", error.Message);
    }

    [Fact]
    public void OperandReplacementsKeepTheInstructionAndRetargetCode()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchResult result = Apply(base_, Plan(base_, $"""
            replace site {base_.Label(7)} operand 2 with 7
            replace site {base_.Label(4)} operand 3 with {base_.Label(0)}
            """ + "\n"));
        Assert.Equal("71(0:1) 6e(0:0,d0) 71(0:1) 6e(0:0,d1) a0(3:1,-1,@0) 8f(@7) 2() 55(3:2,0:7) 5()", Shape(result.Script));
    }

    [Theory]
    [InlineData(7, "operand 3 with 7", "has 2 operands, not an operand 3")]
    [InlineData(7, "operand 2 with label_0", "is not a code position and takes no label")]
    [InlineData(4, "operand 3 with 5", "is a code position and takes a label")]
    public void OperandReplacementsMustFitTheInstruction(int index, string replacement, string expected)
    {
        Base base_ = Compile(DialogueBase);
        var error = Assert.Throws<OmsCompileException>(() =>
            Plan(base_, $"replace site {base_.Label(index)} {replacement}\n"));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void AWrittenOperandTakesACell()
    {
        Base base_ = Compile("@op(0x1a3, G[4])\nexit\n");
        var error = Assert.Throws<OmsCompileException>(() => Plan(base_, "replace site 0 operand 1 with 5\n"));
        Assert.Contains("is written, so it takes a cell", error.Message);
        Assert.Equal("1a3(3:6) 2()", Shape(Apply(base_, Plan(base_, "replace site 0 operand 1 with G[6]\n")).Script));
    }

    // ------------------------------------------------------------------------------------------- plans together

    [Fact]
    public void PlansAtOneSiteRunInLoadOrderAndTheInteractionIsReported()
    {
        Base base_ = Compile(DialogueBase);
        string site = base_.Label(2);
        OmsPatchResult result = Apply(base_, Plan(base_, $"before site {site} {{\n    G[9] = 1\n}}\n"),
            Plan(base_, $"before site {site} {{\n    G[9] = 2\n}}\n"));
        Assert.StartsWith("71(0:1) 6e(0:0,d0) 55(3:9,0:1) 55(3:9,0:2) 71(0:1) 6e(0:0,d1) a0(3:1,-1,@2)", Shape(result.Script));
        Assert.Equal([$"plans 1, 2 insert before 0x{base_.Offsets[2]:x}, in that order"], result.Interactions);
        Assert.Equal([-1, -1, 0, 1, -1], result.Provenance.Take(5).Select(origin => origin.Plan));
    }

    [Theory]
    [InlineData("replace site {7} operand 2 with 7", "replace site {7} operand 2 with 8",
                "conflict: plans 1 and 2 both replace operand 2 at {o7}")]
    [InlineData("replace site {1} {{\n    G[9] = 1\n}}", "replace site {1} {{\n    G[9] = 2\n}}",
                "overlaps the region plan 1 replaces at {o1}")]
    [InlineData("replace site {1} {{\n    G[9] = 1\n}}", "before site {1} {{\n    G[9] = 2\n}}",
                "conflict: plan 2 inserts before {o1}, inside the region plan 1 replaces at {o1}")]
    [InlineData("replace site {2} through {3} {{\n    G[9] = 1\n}}", "before site {0} {{\n    goto label_{x3}\n}}",
                "conflict: plan 2: block 0 names {o3}, inside the region plan 1 replaces at {o2}")]
    public void PlansThatTouchTheSameCodeConflict(string first, string second, string expected)
    {
        Base base_ = Compile(DialogueBase);
        string Fill(string text)
        {
            for (int k = 0; k < base_.Offsets.Length; k++)
                text = text.Replace($"{{{k}}}", base_.Label(k)).Replace($"{{o{k}}}", $"0x{base_.Offsets[k]:x}")
                    .Replace($"{{x{k}}}", $"{base_.Offsets[k]:x}");
            return text.Replace("{{", "{").Replace("}}", "}");
        }
        var error = Assert.Throws<OmsPatchException>(() =>
            Apply(base_, Plan(base_, Fill(first) + "\n"), Plan(base_, Fill(second) + "\n")));
        Assert.Contains(Fill(expected), error.Message);
    }

    [Fact]
    public void APlanAppliesOnlyToTheBaseItWasCompiledAgainst()
    {
        Base base_ = Compile(DialogueBase);
        Base other = Compile(DialogueBase.Replace("\"two\"", "\"deux\""));
        OmsPatchPlan plan = Plan(base_, $"before site 0 {{\n    G[9] = 1\n}}\n");
        Assert.Contains("was compiled against", Assert.Throws<OmsPatchException>(() => Apply(other, plan)).Message);
        var error = Assert.Throws<OmsCompileException>(() => OmsPatchCompiler.Compile(
            Parser.ParseScript(Header(base_.Bytes) + "use test.adv\n", "p.oms"), other.Bytes, Table, Contracts, Intrinsics,
            Modules));
        Assert.Contains($"this patch targets {OmsPatchPlan.FingerprintOf(base_.Bytes)}", error.Message);
    }

    // ------------------------------------------------------------------------------------------- tables and storage

    [Fact]
    public void BaseTableEntriesKeepTheirIndexAndInsertedOnesAreAppended()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchResult result = Apply(base_, Plan(base_,
            $"before site {base_.Label(2)} {{\n    @op(0x71, 1)\n    @op(0x6e, 0, \"new\")\n}}\n"));
        int[] offsets = OmsPatchBase.From(result.Script).Offsets.ToArray();
        // Saves resume by T1 index: entry 1 still names the base's second line, which moved past the inserted one.
        Assert.Equal([(uint)offsets[0], (uint)offsets[4], (uint)offsets[2]], result.Script.Tables.ReadMessageSites!);
        Assert.Equal([(uint)offsets[7]], result.Script.Tables.LocalCallSites!);
        IrScript decoded = IrDecoder.Decode(result.Bytes, Table, Contracts, Name);
        Assert.Equal(result.Script.Tables.ReadMessageSites, decoded.Tables.ReadMessageSites);
    }

    [Fact]
    public void ARegionThatRemovesATableSitePointsItsSlotAtTheReplacement()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchResult result = Apply(base_, Plan(base_, $"replace site {base_.Label(2)} {{\n    G[9] = 1\n}}\n"));
        Assert.Equal([0u, (uint)base_.Offsets[2]], result.Script.Tables.ReadMessageSites!);
        Assert.Contains(result.Notes, note => note.Contains($"removes read-message site 0x{base_.Offsets[2]:x}; its T1 slot 1"));
    }

    [Fact]
    public void NewStorageGoesAboveEverySlotTheBaseUses()
    {
        Base base_ = Compile("L[3] = 1\nexit\n");
        Assert.Equal(5, base_.Script.LocalCounts[0]);
        OmsPatchPlan plan = Plan(base_, """
            local int x
            before site 0 {
                x = 2
                G[9] = G[1] * 2 + G[2] * 3
            }
            """ + "\n");
        var slots = plan.Blocks[0].SelectMany(instruction => instruction.Operands)
            .Where(operand => operand.Type == StorageBanks.LocalBankTypes[0]).Select(operand => operand.Value).ToList();
        Assert.Contains(4u, slots);                         // x
        Assert.All(slots, slot => Assert.True(slot >= 4, $"L[{slot}] is a base slot"));
        OmsPatchResult result = Apply(base_, plan);
        Assert.Equal((int)slots.Max() + 2, result.Script.LocalCounts[0]);
    }

    // ------------------------------------------------------------------------------------------- library subs

    [Fact]
    public void LibrarySubsTheBaseLinksAreCalledThereAndOthersAreAppended()
    {
        Base base_ = Compile("use test.adv\n\"Narration.\"\nexit\n");
        int prologue = base_.Offsets[3];
        OmsPatchPlan reuse = Plan(base_, "use test.adv\nbefore site 0 {\n    \"Inserted.\"\n}\n", idiomatic: true);
        Assert.Null(reuse.Append);
        Assert.Equal(OmsPatchOperand.Site(0, prologue), reuse.Blocks[0][0].Operands[0]);

        OmsPatchPlan linked = Plan(base_, "use test.adv\nbefore site 0 {\n    HERO \"Hi.\" (voice: 0x34e)\n}\n", idiomatic: true);
        Assert.NotNull(linked.Append);
        Assert.Equal(5, linked.Blocks[linked.Append.Value].Count);     // play and helper, not prologue again
        OmsPatchOperand call = linked.Blocks[0].Single(instruction => instruction.Opcode == 0x8f).Operands[0];
        Assert.Equal(OmsPatchOperandKind.Label, call.Kind);
        Assert.Equal(linked.Append.Value, call.Block);
        OmsPatchResult result = Apply(base_, linked);
        Assert.Equal(OmsPatchOrigin.Append, result.Provenance[^1].Origin);
    }

    [Fact]
    public void AppendedCodeNeedsABaseThatCannotReachItsEnd()
    {
        Base base_ = Compile("G[1] = 1\n");
        var error = Assert.Throws<OmsCompileException>(() => Plan(base_, """
            sub extra {
                G[2] = 1
            }
            before site 0 {
                extra()
            }
            """ + "\n"));
        Assert.Contains("can reach the end of its code", error.Message);
    }

    // ------------------------------------------------------------------------------------------- plan format

    [Fact]
    public void APlanRoundTripsThroughJson()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchPlan plan = Plan(base_, $$"""
            sub extra {
                G[2] = 1
            }
            before site {{base_.Label(2)}} {
                @op(0x71, 1)
                @op(0x6e, 0, "new")
                extra()
                goto {{base_.Label(0)}}
            }
            replace site {{base_.Label(7)}} operand 2 with G[3]
            """ + "\n");
        string json = plan.ToJson();
        OmsPatchPlan read = OmsPatchPlan.FromJson(json);
        Assert.Equal(json, read.ToJson());
        Assert.Contains("\"site\": 0", json);
        Assert.Contains("\"label\": [", json);
        Assert.Equal(Apply(base_, plan).Bytes, Apply(base_, read).Bytes);
        Assert.Contains("unsupported transform plan version", Assert.Throws<InvalidDataException>(() =>
            OmsPatchPlan.FromJson(json.Replace("\"oms_patch\": 1", "\"oms_patch\": 3"))).Message);
    }

    // ------------------------------------------------------------------------------------------- syntax

    [Theory]
    [InlineData("before site 1 {\n}\n", "0x1 is not an instruction start in T.BIN")]
    [InlineData("before site nowhere {\n}\n", "'nowhere' is not a label of T.BIN")]
    [InlineData("replace site 0 operand 0 with 1\n", "operands count from 1")]
    [InlineData("before site 0 {\n    before site 3 {\n    }\n}\n", "top level of a patch unit")]
    [InlineData("G[1] = 2\n", "a patch holds declarations, subs and site operations")]
    [InlineData("before site 0 {\nlabel_5:\n    G[1] = 1\n}\n", "'label_5' is reserved")]
    [InlineData("before site 0 {\n    goto label_99\n}\n", "unknown label 'label_99': neither the fragment nor its base defines it")]
    public void FragmentErrorsNameTheProblem(string body, string expected)
    {
        Base base_ = Compile(DialogueBase);
        Exception error = Assert.ThrowsAny<Exception>(() => Plan(base_, body));
        Assert.True(error is OmsSyntaxException or OmsCompileException, error.ToString());
        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("script \"T.BIN\"\nbefore site 0 {\n}\n", "site operations belong in a patch unit")]
    [InlineData("patch \"T.BIN\" fingerprint \"sha256:00\"\n", "a fingerprint is \"sha256:\" and 64 hex digits")]
    public void SiteOperationsNeedAFingerprintedPatchUnit(string source, string expected)
    {
        var error = Assert.Throws<OmsSyntaxException>(() => Parser.ParseScript(source, "p.oms"));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void APatchUnitNamesItsScriptAndFingerprint()
    {
        string fingerprint = "sha256:" + new string('a', 64);
        ScriptUnit unit = Parser.ParseScript($"patch \"SC0000.BIN\" fingerprint \"{fingerprint}\"\nbefore site 0x877 {{\n}}\n", "p.oms");
        Assert.True(unit.IsPatch);
        Assert.Equal("SC0000.BIN", unit.Name);
        Assert.Equal(fingerprint, unit.Fingerprint);
        SiteStatement site = Assert.IsType<SiteStatement>(Assert.Single(unit.Body));
        Assert.Equal(SiteOperation.Before, site.Operation);
        Assert.Equal(0x877u, site.Site.Offset);
    }

    // ------------------------------------------------------------------------------------------- named patch points

    private static OmsPatchPoints Points(Base base_, string script = Name, int index = 2)
        => new([new OmsPatchPoint("test.dialogue.second_line", script, "before the second line",
            [new OmsPatchPointSite("sha256:" + new string('0', 64), 0x10),
             new OmsPatchPointSite(OmsPatchPlan.FingerprintOf(base_.Bytes), base_.Offsets[index])])]);

    private static OmsPatchPlan PlanWithPoints(Base base_, string body, OmsPatchPoints? points)
        => OmsPatchCompiler.Compile(Parser.ParseScript(Header(base_.Bytes) + body, "p.oms"), base_.Bytes, Table, Contracts,
            Intrinsics, Modules, points: points);

    [Fact]
    public void ANamedPatchPointCompilesToTheSamePlanAsItsOffset()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchPlan byName = PlanWithPoints(base_,
            "before site test.dialogue.second_line {\n    G[9] = 1\n}\nreplace site test.dialogue.second_line operand 1 with 0\n",
            Points(base_));
        OmsPatchPlan byOffset = PlanWithPoints(base_,
            $"before site 0x{base_.Offsets[2]:x} {{\n    G[9] = 1\n}}\nreplace site 0x{base_.Offsets[2]:x} operand 1 with 0\n",
            null);

        Assert.Equal(2, byName.Operations.Count);
        Assert.All(byName.Operations, operation => Assert.Equal("test.dialogue.second_line", operation.Point));
        Assert.All(byOffset.Operations, operation => Assert.Null(operation.Point));
        Assert.Equal(byOffset.Operations.Select(operation => operation with { Source = null, Value = null }),
                     byName.Operations.Select(operation => operation with { Point = null, Source = null, Value = null }));
        Assert.Equal(byOffset.Operations.Select(operation => operation.Value),
                     byName.Operations.Select(operation => operation.Value));
        Assert.Equal(Apply(base_, byOffset).Bytes, Apply(base_, byName).Bytes);
        Assert.Equal("test.dialogue.second_line", OmsPatchPlan.FromJson(byName.ToJson()).Operations[0].Point);
    }

    [Fact]
    public void ANamedPatchPointFailsForARevisionItDoesNotList()
    {
        Base base_ = Compile(DialogueBase);
        Base other = Compile(DialogueBase.Replace("\"two\"", "\"zwei\""));
        var error = Assert.Throws<OmsCompileException>(() => PlanWithPoints(other,
            "before site test.dialogue.second_line {\n    G[9] = 1\n}\n", Points(base_)));
        Assert.Contains("has no site for T.BIN " + OmsPatchPlan.FingerprintOf(other.Bytes), error.Message);
        Assert.Contains("it supports sha256:" + new string('0', 64) + ", " + OmsPatchPlan.FingerprintOf(base_.Bytes),
                        error.Message);
    }

    [Theory]
    [InlineData("test.dialogue.second_line", "OTHER.BIN", "is in OTHER.BIN, not T.BIN")]
    [InlineData("test.dialogue.missing", "T.BIN", "is not a named patch point of this profile")]
    public void ANamedPatchPointMustExistForThisScript(string point, string script, string reason)
    {
        Base base_ = Compile(DialogueBase);
        var error = Assert.Throws<OmsCompileException>(() => PlanWithPoints(base_,
            $"before site {point} {{\n    G[9] = 1\n}}\n", Points(base_, script)));
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public void ANamedPatchPointNeedsARegistryAndAnInstructionStart()
    {
        Base base_ = Compile(DialogueBase);
        var missing = Assert.Throws<OmsCompileException>(() => PlanWithPoints(base_,
            "before site test.dialogue.second_line {\n    G[9] = 1\n}\n", null));
        Assert.Contains("no patch-point registry is loaded", missing.Message);

        var inside = new OmsPatchPoints([new OmsPatchPoint("test.dialogue.inside", Name, null,
            [new OmsPatchPointSite(OmsPatchPlan.FingerprintOf(base_.Bytes), base_.Offsets[1] + 1)])]);
        var error = Assert.Throws<OmsCompileException>(() => PlanWithPoints(base_,
            "before site test.dialogue.inside {\n    G[9] = 1\n}\n", inside));
        Assert.Contains("which is not an instruction start", error.Message);
    }

    // ------------------------------------------------------------------------------------------- resource operands

    private static OmsResourceScope Scope()
        => new OmsResourceScope("example.mod", ["HELLO", "OTHER"], ["ICON.AGF"])
            .WithDependency("base.lib", ["UTIL"], []);

    private static OmsPatchPlan ModPlan(Base base_, string body)
        => OmsPatchCompiler.Compile(Parser.ParseScript(Header(base_.Bytes) + body, "p.oms"), base_.Bytes, Table, Contracts,
            Intrinsics, Modules, resources: Scope());

    [Fact]
    public void ModNamesCompileToResourceOperandsThatTheLinkerBinds()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchPlan plan = ModPlan(base_,
            "before site 0x0 {\n    run HELLO\n    run \"base.lib:UTIL\"\n    G[5] = asset \"icon.agf\"\n    run HELLO\n}\n");

        Assert.Equal(OmsPatchPlan.ResourceVersion, plan.Version);
        Assert.Equal(["example.mod:HELLO", "base.lib:UTIL", "example.mod:ICON.AGF"], plan.Resources);
        string json = plan.ToJson();
        Assert.Contains("\"res\": 0", json);
        Assert.Contains("\"resources\"", json);
        OmsPatchPlan loaded = OmsPatchPlan.FromJson(json);
        Assert.Equal(plan.Resources, loaded.Resources);

        // An unbound plan does not apply; bound with two selector maps it encodes each map's ids.
        var unbound = Assert.Throws<OmsPatchException>(() => Apply(base_, loaded));
        Assert.Contains("unresolved resource reference 'example.mod:HELLO'", unbound.Message);
        var selectors = new Dictionary<string, uint> { ["example.mod"] = 2, ["base.lib"] = 5 };
        uint Bind(string reference, IReadOnlyDictionary<string, uint> map) => reference switch
        {
            "example.mod:HELLO" => map["example.mod"] << 24 | 0,
            "example.mod:ICON.AGF" => map["example.mod"] << 24 | 2,
            "base.lib:UTIL" => map["base.lib"] << 24 | 0,
            _ => throw new InvalidOperationException(reference),
        };
        string first = Shape(Apply(base_, loaded.ResolveResources(reference => Bind(reference, selectors))).Script);
        string second = Shape(Apply(base_, loaded.ResolveResources(reference =>
            Bind(reference, new Dictionary<string, uint> { ["example.mod"] = 9, ["base.lib"] = 5 }))).Script);
        Assert.StartsWith("3(0:2000000) 3(0:5000000) 55(3:5,0:2000002) 3(0:2000000) 71(0:1)", first);
        Assert.StartsWith("3(0:9000000) 3(0:5000000) 55(3:5,0:9000002) 3(0:9000000) 71(0:1)", second);
        Assert.DoesNotContain("resources", loaded.ResolveResources(reference => Bind(reference, selectors)).ToJson());
    }

    [Fact]
    public void PlansWithoutResourcesStayVersionOne()
    {
        Base base_ = Compile(DialogueBase);
        OmsPatchPlan plan = ModPlan(base_, "before site 0x0 {\n    G[9] = 1\n}\n");
        Assert.Equal(OmsPatchPlan.CurrentVersion, plan.Version);
        Assert.DoesNotContain("resources", plan.ToJson());

        string forged = plan.ToJson().Replace("\"lit\": 1", "\"res\": 0");
        Assert.Contains("\"res\": 0", forged);
        var error = Assert.Throws<InvalidDataException>(() => OmsPatchPlan.FromJson(forged));
        Assert.Contains("cannot hold resource references", error.Message);
    }

    [Theory]
    [InlineData("run \"other.mod:X\"", "'other.mod' is not example.mod or a mod it declares in [[requires]]")]
    [InlineData("run \"base.lib:MISSING\"", "base.lib has no script named 'MISSING'")]
    [InlineData("G[1] = asset \"SYSTEM.AGF\"", "'SYSTEM.AGF' is not a new asset of example.mod")]
    public void ModNamesOutsideTheScopeAreErrors(string statement, string reason)
    {
        Base base_ = Compile(DialogueBase);
        var error = Assert.Throws<OmsCompileException>(() => ModPlan(base_, $"before site 0x0 {{\n    {statement}\n}}\n"));
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public void ScriptsWithResourcesEncodeOnlyOnceResolved()
    {
        IrScript compiled = OmsCompiler.Compile(Parser.ParseScript("script \"HELLO.BIN\"\nrun OTHER\nG[1] = asset \"ICON.AGF\"\nexit\n",
            "s.oms"), Abi, Table, null, Intrinsics, name => Modules.FirstOrDefault(module => module.Name == name),
            resources: Scope());
        Assert.Equal(["example.mod:OTHER", "example.mod:ICON.AGF"], compiled.Resources);
        var error = Assert.Throws<InvalidDataException>(() => IrEncoder.Encode(compiled));
        Assert.Contains("resource reference 'example.mod:OTHER' is unresolved", error.Message);

        IrScript bound = compiled.ResolveResources(reference => reference.EndsWith("OTHER") ? 0x03000001u : 0x03000002u);
        IrScript decoded = IrDecoder.Decode(IrEncoder.Encode(bound), Table, Contracts, "HELLO.BIN");
        Assert.Equal("3(0:3000001) 55(3:1,0:3000002) 2()", Shape(decoded));

        var outside = Assert.Throws<OmsCompileException>(() => OmsCompiler.Compile(
            Parser.ParseScript("script \"X.BIN\"\nG[1] = asset \"ICON.AGF\"\n", "x.oms"), Abi, Table, null, Intrinsics));
        Assert.Contains("compile it as part of its mod", outside.Message);
    }
}
