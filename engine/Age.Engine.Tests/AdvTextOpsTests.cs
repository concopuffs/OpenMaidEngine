using System;
using System.Collections.Generic;
using System.Linq;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class AdvTextOpsTests
{
    [Fact]
    public void SetFontFaceFlowsIntoLiveAndRetainedTextStyles()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand I(long value) => new(0, value);
        static Operand S(int index) => new(2, index);
        var script = ScriptAssembler.Assemble(table, "ADV_FONT_FACE",
            new List<(int, Operand[])>
            {
                (0x1a5, new[] { S(0) }),
                (0x6e, new[] { I(0), S(2) }),
                (0x1a5, new[] { S(1) }),
                (0x6e, new[] { I(0), S(3) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "ＭＳ 明朝", "ＭＳ ゴシック", "mincho", "gothic" });
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(new[] { "ＭＳ 明朝", "ＭＳ ゴシック" },
            host.LiveTextRuns.Select(run => run.Run.Style.FontFace));
        Assert.Equal(new[] { "ＭＳ 明朝", "ＭＳ ゴシック" },
            vm.TextHistory.Records.Select(record => record.Style.FontFace));
    }

    [Fact]
    public void TextEffectModeColorAndOffsetsFlowIntoLiveAndRetainedStyles()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand I(long value) => new(0, value);
        static Operand S(int index) => new(2, index);
        var script = ScriptAssembler.Assemble(table, "ADV_TEXT_EFFECT",
            new List<(int, Operand[])>
            {
                (0x77, new[] { I(0x123456) }),
                (0x78, new[] { I(1) }),
                (0x1a4, new[] { I(2), I(-1) }),
                (0x6e, new[] { I(0), S(0) }),
                (0x78, new[] { I(3) }),
                (0x1a4, new[] { I(1), I(1) }),
                (0x6e, new[] { I(0), S(1) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "shadow", "outline" });
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Collection(host.LiveTextRuns,
            shadow => Assert.Equal((0x123456L, 1, 2, -1),
                (shadow.Run.Style.EffectColor, shadow.Run.Style.RenderMode,
                 shadow.Run.Style.EffectOffsetX, shadow.Run.Style.EffectOffsetY)),
            outline => Assert.Equal((0x123456L, 3, 1, 1),
                (outline.Run.Style.EffectColor, outline.Run.Style.RenderMode,
                 outline.Run.Style.EffectOffsetX, outline.Run.Style.EffectOffsetY)));
        Assert.Equal(
            host.LiveTextRuns.Select(run => run.Run.Style),
            vm.TextHistory.Records.Select(record => record.Style));
    }

    [Fact]
    public void TextCursorAndDrawStringReachHostWithLocalStringPointer()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int lookup = table.ByLabel("lookup-array")!.Value;
        var script = ScriptAssembler.Assemble(table, "ADVTEXT",
            new List<(int, Operand[])>
            {
                (lookup, new[] { new Operand(14, 0), new Operand(5, 0x315), new Operand(0, 2) }),
                (0x204, new[] { new Operand(0, 13), new Operand(0, 1), new Operand(0, 1), new Operand(14, 0) }),
                (0x7a, new[] { new Operand(0, 1), new Operand(0, 75), new Operand(0, 47) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.GlobalStrings[0x317] = "speaker";

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((1, 75, 47), Assert.Single(host.TextCursors));
        Assert.Equal((13, 1, 1, "speaker"), Assert.Single(host.SurfaceStrings));
    }

    [Fact]
    public void ShowTextPublishesDynamicStringOperandsAsAdjacentLiveRuns()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "DYNAMIC_ADV_TEXT",
            new List<(int, Operand[])>
            {
                (0x70, new[] { new Operand(0, 1), new Operand(0, 640), new Operand(0, 160),
                               new Operand(0, 0), new Operand(0, 430) }),
                (0x79, new[] { new Operand(0, 1), new Operand(0, 100), new Operand(0, 47) }),
                (0x71, new[] { new Operand(0, 1) }),
                (0x1b5, new[] { new Operand(0, 0) }),
                (0x6e, new[] { new Operand(0, 0), new Operand(2, 0) }),
                (0x6e, new[] { new Operand(0, 0), new Operand(5, 0x279) }),
                (0x6e, new[] { new Operand(0, 0), new Operand(2, 1) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "「", "！」" });
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.GlobalStrings[0x279] = "リリィ";

        vm.Run();

        Assert.Equal(new[] { "「", "リリィ", "！」" },
                     host.LiveTextRuns.Select(emitted => emitted.Run.Text));
        Assert.All(host.LiveTextRuns,
            emitted => Assert.Equal((0, 430, 100, 47),
                (emitted.Run.Layout.OriginX, emitted.Run.Layout.OriginY,
                 emitted.Run.Layout.CursorX, emitted.Run.Layout.CursorY)));
    }

    [Fact]
    public void ReusableDynamicTextHelperMayEmitPastTheLiteralLineLoopCap()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int call = table.ByLabel("call")!.Value;
        int ret = table.ByLabel("ret")!.Value;
        static Operand I(long value) => new(0, value);
        const int helperOffset = 10;
        var script = ScriptAssembler.Assemble(table, "DYNAMIC_TEXT_HELPER",
            new List<(int, Operand[])>
            {
                (call, new[] { I(helperOffset) }),
                (call, new[] { I(helperOffset) }),
                (call, new[] { I(helperOffset) }),
                (0x2, Array.Empty<Operand>()),
                (0x6e, new[] { I(9), new Operand(5, 0x322) }),
                (ret, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.GlobalStrings[0x322] = "Melodiana";

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(3, host.LiveTextRuns.Count);
        Assert.All(host.LiveTextRuns, run => Assert.Equal("Melodiana", run.Run.Text));
        Assert.All(vm.Emitted, line => Assert.Equal(helperOffset, line.Offset));
    }

    [Fact]
    public void RepeatedInlineLineStillTripsTheLiteralLineLoopCap()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int call = table.ByLabel("call")!.Value;
        int ret = table.ByLabel("ret")!.Value;
        static Operand I(long value) => new(0, value);
        const int helperOffset = 10;
        const int inlineStringOffset = 16;
        var script = ScriptAssembler.Assemble(table, "LITERAL_TEXT_LOOP",
            new List<(int, Operand[])>
            {
                (call, new[] { I(helperOffset) }),
                (call, new[] { I(helperOffset) }),
                (call, new[] { I(helperOffset) }),
                (0x2, Array.Empty<Operand>()),
                (0x6e, new[] { I(1), new Operand(2, 0) }),
                (ret, Array.Empty<Operand>()),
            }, new[] { "same authored line" });
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal($"LOOP:line@0x{inlineStringOffset:x}×3", vm.HaltReason);
        Assert.Equal(2, host.LiveTextRuns.Count);
    }

    [Fact]
    public void LayoutResetRestoresConfiguredCursorAndPreservesConfiguredBounds()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "ADV_LAYOUT_CONFIG",
            new List<(int, Operand[])>
            {
                (0x70, new[] { new Operand(0, 1), new Operand(0, 800), new Operand(0, 160),
                               new Operand(0, 0), new Operand(0, 430) }),
                (0x71, new[] { new Operand(0, 1) }),
                (0x79, new[] { new Operand(0, 1), new Operand(0, 100), new Operand(0, 47) }),
                (0x1c1, new[] { new Operand(0, 1), new Operand(0, 720), new Operand(0, 147) }),
                (0x7a, new[] { new Operand(0, 1), new Operand(0, 12), new Operand(0, 34) }),
                (0x71, new[] { new Operand(0, 1) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(new[] { (1, 0, 0), (1, 12, 34), (1, 100, 47) }, host.TextCursors);
        Assert.Equal(new AdvTextLayoutSnapshot(1, 800, 160, 0, 430, 100, 47, 720, 147),
                     vm.TextHistory.GetLayoutSnapshot(1));
    }

    [Fact]
    public void SuppressedUiTextPublishesRetainedRunsImmediatelyAndAdvancesLines()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand I(long value) => new(0, value);
        static Operand L(int index) => new(9, index);
        static Operand S(int index) => new(2, index);
        var script = ScriptAssembler.Assemble(table, "UI_RETAINED_TEXT",
            new List<(int, Operand[])>
            {
                (0x1bb, new[] { I(0) }),
                (0x70, new[] { I(7), I(500), I(180), I(0), I(0) }),
                (0x79, new[] { I(7), I(53), I(10) }),
                (0x71, new[] { I(7) }),
                (0x75, new[] { I(16) }),
                (0x8b, new[] { I(9) }),
                (0x7f, new[] { L(0) }),
                (0x1b5, new[] { I(0) }),
                (0x198, new[] { I(7), I(275), I(90) }),
                (0x6e, new[] { I(0), S(0) }),
                (0x6f, new[] { I(0) }),
                (0x6e, new[] { I(0), S(1) }),
                (0x6f, new[] { I(0) }),
                (0x198, new[] { I(7), I(275), I(135) }),
                (0x6e, new[] { I(0), S(2) }),
                (0x6f, new[] { I(0) }),
                (0x6e, new[] { I(0), S(3) }),
                (0x6f, new[] { I(0) }),
                (0x198, new[] { I(7), I(275), I(180) }),
                (0x6e, new[] { I(0), S(4) }),
                (0x6f, new[] { I(0) }),
                (0x6e, new[] { I(0), S(5) }),
                (0x6f, new[] { I(0) }),
                (0x1b5, new[] { L(0) }),
                (0x1bb, new[] { I(1) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "row1a", "row1b", "row2a", "row2b", "row3a", "row3b" });
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(50, host.MessageGlyphDelayMilliseconds);
        Assert.Empty(vm.TextHistory.Entries);
        Assert.Empty(vm.TextHistory.Records);
        Assert.All(host.LiveTextRuns, emitted => Assert.Equal(0, emitted.GlyphDelayMilliseconds));
        Assert.Equal(
            new[]
            {
                (275, 90, 53, 10),
                (275, 90, 53, 35),
                (275, 135, 53, 60),
                (275, 135, 53, 85),
                (275, 180, 53, 110),
                (275, 180, 53, 135),
            },
            host.LiveTextRuns.Select(emitted =>
                (emitted.Run.Layout.OriginX, emitted.Run.Layout.OriginY,
                 emitted.Run.Layout.CursorX, emitted.Run.Layout.CursorY)));
        Assert.Equal(new[] { "row1a", "row1b", "row2a", "row2b", "row3a", "row3b" },
                     host.LiveTextRuns.Select(emitted => emitted.Run.Text));
    }

    [Fact]
    public void RetainedGlyphMetricsAdvanceCanonicalCursorBetweenRuns()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand I(long value) => new(0, value);
        static Operand S(int index) => new(2, index);
        var script = ScriptAssembler.Assemble(table, "RETAINED_CURSOR",
            new List<(int, Operand[])>
            {
                (0x70, new[] { I(1), I(100), I(50), I(10), I(20) }),
                (0x79, new[] { I(1), I(2), I(3) }),
                (0x71, new[] { I(1) }),
                (0x213, new[] { I(1), I(100), I(10) }),
                (0x6e, new[] { I(0), S(0) }),
                (0x6e, new[] { I(0), S(1) }),
                (0x2, Array.Empty<Operand>()),
            },
            new[] { "A", "BC" });
        var host = new RecordingHost
        {
            OnRetainedText = run => new AdvRetainedTextRunResult(
                run.Layout.Slot,
                FirstGlyphIndex: 0,
                GlyphCount: run.Text.Length,
                CursorX: run.Layout.CursorX + run.Text.Length * 4,
                CursorY: run.Layout.CursorY),
        };
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(new[] { (2, 3), (6, 3) },
            host.LiveTextRuns.Select(run =>
                (run.Run.Layout.CursorX, run.Run.Layout.CursorY)));
        Assert.Equal(new[] { (2, 3), (6, 3) },
            vm.TextHistory.Records.Select(record =>
                (record.Layout.CursorX, record.Layout.CursorY)));
        Assert.Equal((14, 3),
            (vm.TextHistory.GetLayoutSnapshot(1).CursorX,
             vm.TextHistory.GetLayoutSnapshot(1).CursorY));
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void RealMamesResearchDescriptionUsesImmediateRetainedTextPath()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var scripts = Sys4ScriptProvider.Load(table);
        var mames = scripts.RequireByName("MAMES.BIN");
        static Operand I(long value) => new(0, value);
        var caller = ScriptAssembler.Assemble(table, "MAMES_RESEARCH_CALLER",
            new List<(int, Operand[])>
            {
                (0x1bb, new[] { I(0) }),
                (0x70, new[] { I(7), I(500), I(180), I(0), I(0) }),
                (0x79, new[] { I(7), I(53), I(10) }),
                (0x71, new[] { I(7) }),
                (0x75, new[] { I(16) }),
                (0x8b, new[] { I(9) }),
                (0x198, new[] { I(7), I(275), I(90) }),
                (0x3, new[] { I(mames.PackedId) }),
                (0x1bb, new[] { I(1) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(caller, table, host, provider: scripts);
        vm.Globals[0x1560e7] = 7;

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(50, host.MessageGlyphDelayMilliseconds);
        Assert.Empty(vm.TextHistory.Records);
        Assert.Collection(host.LiveTextRuns,
            first =>
            {
                Assert.Equal("錬成の研究をして知識を高める", first.Run.Text);
                Assert.Equal(0, first.GlyphDelayMilliseconds);
                Assert.True(first.Run.BelongsToScript("MAMES_RESEARCH_CALLER"));
                Assert.True(first.Run.BelongsToScript("MAMES.BIN"));
                Assert.Equal((275, 90, 53, 10),
                    (first.Run.Layout.OriginX, first.Run.Layout.OriginY,
                     first.Run.Layout.CursorX, first.Run.Layout.CursorY));
            },
            second =>
            {
                Assert.Equal("錬成LVの熟練度が上昇", second.Run.Text);
                Assert.Equal(0, second.GlyphDelayMilliseconds);
                Assert.Equal((275, 90, 53, 35),
                    (second.Run.Layout.OriginX, second.Run.Layout.OriginY,
                     second.Run.Layout.CursorX, second.Run.Layout.CursorY));
            });
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void System4BootstrapReplaysAllResetCursorAndBoundsConfigurations()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var scripts = Sys4ScriptProvider.Load(table);
        var history = new AdvTextHistory();

        Assert.Equal(9, AdvTextLayoutBootstrap.ApplyLeadingDefinitionsAndResets(
            scripts.RequireByName("SYSTEM4.BIN"), table, history));

        // SYSTEM4's configuration follows its initial reset run, so the configured cursor is deferred.
        Assert.Equal((0, 0, 720, 147), CursorAndBounds(history.GetLayoutSnapshot(1)));
        history.ResetLayout(1);
        Assert.Equal((100, 47, 720, 147), CursorAndBounds(history.GetLayoutSnapshot(1)));
        history.ResetLayout(4);
        Assert.Equal((45, 42, 645, 135), CursorAndBounds(history.GetLayoutSnapshot(4)));
        history.ResetLayout(8);
        Assert.Equal((53, 10, 495, 60), CursorAndBounds(history.GetLayoutSnapshot(8)));
        history.ResetLayout(9);
        Assert.Equal((10, 10, 250, 368), CursorAndBounds(history.GetLayoutSnapshot(9)));
        Assert.Equal(new[] { 1 }, history.LayoutsCoveredByTextObjectErase(0xd6d8, 0x1f4));
        Assert.Equal(new[] { 7 }, history.LayoutsCoveredByTextObjectErase(0x7d0, 0x1f4));
        Assert.All(Enumerable.Range(1, 9), slot =>
        {
            AdvTextLayoutPresentationBinding binding = history.GetPresentationBinding(slot);
            Assert.Equal(slot + 0x14, binding.SourceSurfaceSlot);
            Assert.Equal(0x1f4, binding.ObjectCapacity);
        });

        static (int X, int Y, int Right, int Bottom) CursorAndBounds(AdvTextLayoutSnapshot layout)
            => (layout.CursorX, layout.CursorY, layout.Right, layout.Bottom);
    }

    [Fact]
    public void FullNativeGlyphRangeEraseClearsDetachedLayoutPresentation()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand I(long value) => new(0, value);
        var script = ScriptAssembler.Assemble(table, "ADV_GLYPH_ERASE",
            new List<(int, Operand[])>
            {
                (0x70, new[] { I(1), I(800), I(160), I(0), I(430) }),
                (0x213, new[] { I(1), I(0xd6d8), I(0x1f4) }),
                (0x1f7, new[] { I(0xd6d8), I(1) }),       // partial erase cannot clear collapsed text
                (0x1f7, new[] { I(0xd6d8), I(0x1f4) }),  // SC0000 transition teardown
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(new[] { 1 }, host.ClearedTextLayouts);
        Assert.Null(vm.Gfx.TryGet(1)); // 0x213 configures the ADV layout, not retained gfx handle 1
    }

    [Fact]
    public void WaitIndicatorConfigurationReachesHost()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "WAITMARK",
            new List<(int, Operand[])>
            {
                (0x73, new[]
                {
                    new Operand(0, 1), new Operand(0, 385), new Operand(0, 140), new Operand(0, 12),
                    new Operand(0, 0), new Operand(0, 0), new Operand(0, 30), new Operand(0, 27),
                    new Operand(0, 12), new Operand(0, 48),
                }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal(new AdvWaitIndicatorConfig(1, 385, 140, 12, 0, 0, 30, 27, 12, 48),
                     Assert.Single(host.WaitIndicators));
    }

    [Fact]
    public void GridWaitIndicatorKeepsSeparateAtlasColumnsAndTerminalFrame()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        static Operand I(long value) => new(0, value);
        var script = ScriptAssembler.Assemble(table, "KAMIDORI-WAITMARK",
            new List<(int, Operand[])>
            {
                (0x2bc, new[]
                {
                    I(1), I(2), new Operand(9, 0), I(12), I(0), I(0),
                    I(30), I(30), I(32), I(20), I(64),
                }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host,
            compatibility: new("kamidori", "SYS4433")).Run();

        Assert.Equal(
            new AdvWaitIndicatorConfig(1, 2, 0, 12, 0, 0, 30, 30, 32, 20, 64),
            Assert.Single(host.WaitIndicators));
    }

    [Fact]
    public void WaitIndicatorLastGlyphAnchorModeReachesHost()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var script = ScriptAssembler.Assemble(table, "KAMIDORI-WAITMARK-ANCHOR",
            new List<(int, Operand[])>
            {
                (0x1b1, new[] { new Operand(0, 1) }),
                (0x1b1, new[] { new Operand(0, 0) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host,
            compatibility: new("kamidori", "SYS4433")).Run();

        Assert.Equal(new[] { true, false }, host.WaitIndicatorFollowLastGlyphChanges);
    }

    [Fact]
    public void Sys4433TextAspectModeIsCapturedByFollowingTextRuns()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var script = ScriptAssembler.Assemble(table, "KAMIDORI-TEXT-ASPECT",
            new List<(int, Operand[])>
            {
                (0x2db, new[] { new Operand(0, 1) }),
                (0x6e, new[] { new Operand(0, 0), new Operand(2, 0) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "aspect-aware" });
        var host = new RecordingHost();

        new VirtualMachine(script, table, host,
            compatibility: new("kamidori", "SYS4433")).Run();

        Assert.Equal(1, Assert.Single(host.LiveTextRuns).Run.Style.AspectMode);
    }

    [Theory]
    [InlineData("ＭＳ 明朝", 7)]
    [InlineData("@ＭＳ 明朝", 7)]
    [InlineData("missing", -1)]
    public void Sys4433FontFamilyLookupWritesHostCacheIndex(string face, int expected)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var script = ScriptAssembler.Assemble(table, "KAMIDORI-FONT-LOOKUP",
            new List<(int, Operand[])>
            {
                (0x2de, new[] { new Operand(3, 0x100), new Operand(2, 0) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { face });
        var host = new RecordingHost();
        host.FontFamilyIndices["ＭＳ 明朝"] = 7;
        var vm = new VirtualMachine(script, table, host,
            compatibility: new("kamidori", "SYS4433"));

        vm.Run();

        Assert.Equal(expected, vm.Globals[0x100]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(37)]
    public void Sys4433FontFamilyCountUsesTheHostCacheSentinel(int familyCount)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var script = ScriptAssembler.Assemble(table, "KAMIDORI-FONT-COUNT",
            new List<(int, Operand[])>
            {
                (0x2dc, new[] { new Operand(3, 0x100) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost { FontFamilyCount = familyCount };
        var vm = new VirtualMachine(script, table, host,
            compatibility: new("kamidori", "SYS4433"));

        vm.Run();

        Assert.Equal(familyCount, vm.Globals[0x100]);
    }

    [Theory]
    [InlineData(1, "Yu Mincho")]
    [InlineData(-1, "")]
    [InlineData(2, "")]
    public void Sys4433FontFamilyNameReturnsIndexedFaceOrEmptyString(int index, string expected)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var script = ScriptAssembler.Assemble(table, "KAMIDORI-FONT-NAME",
            new List<(int, Operand[])>
            {
                (0x2dd, new[] { new Operand(5, 0x100), new Operand(0, index) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost { FontFamilyCount = 2 };
        host.FontFamilyNames[0] = "MS Gothic";
        host.FontFamilyNames[1] = "Yu Mincho";
        var vm = new VirtualMachine(script, table, host,
            compatibility: new("kamidori", "SYS4433"));

        vm.Run();

        Assert.Equal(expected, vm.GlobalStrings[0x100]);
    }

    [Fact]
    public void WaitIndicatorHandlePublishesItsResolvedLayoutBindingToHost()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand I(long value) => new(0, value);
        var script = ScriptAssembler.Assemble(table, "WAITMARK_BINDING",
            new List<(int, Operand[])>
            {
                (0x70, new[] { I(1), I(800), I(160), I(0), I(430) }),
                (0x79, new[] { I(1), I(100), I(47) }),
                (0x213, new[] { I(1), I(0xd6d8), I(500) }),
                (0x212, new[] { I(1), I(0xd674) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        var retained = Assert.Single(host.WaitIndicatorBindings);
        Assert.Equal(
            new AdvTextLayoutPresentationBinding(
                1, 21, 0xd6d8, 500, 0xd674, 100, 47),
            retained.Binding);
        Assert.Equal(
            new AdvTextLayoutSnapshot(1, 800, 160, 0, 430, 0, 0, 800, 160),
            retained.Layout);
    }

    [Fact]
    public void WaitIndicatorTerminalFrameIsExclusive()
    {
        var config = new AdvWaitIndicatorConfig(
            1, 385, 140, 12, 0, 0, 30, 27, 12, 48);

        int[] frames = Enumerable.Range(0, 24)
            .Select(tick => config.FrameAt(tick * 48L))
            .ToArray();

        Assert.Equal(
            Enumerable.Range(0, 12).Concat(Enumerable.Range(0, 12)),
            frames);
        Assert.DoesNotContain(12, frames);
    }

    [Fact]
    public void WaitIndicatorToggleAndTextLayoutPublicationReachHost()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "WAITMARK_SERVICE",
            new List<(int, Operand[])>
            {
                (0x1ce, new[] { new Operand(0, 1) }),
                (0x20a, new[] { new Operand(0, 4) }),
                (0x1ce, new[] { new Operand(0, 0) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal(new[] { true, false }, host.WaitIndicatorEnabledChanges);
        Assert.Equal(4, Assert.Single(host.PublishedAdvTextLayouts));
    }
}
