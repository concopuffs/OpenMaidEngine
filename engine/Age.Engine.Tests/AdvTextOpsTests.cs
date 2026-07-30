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

        Assert.Equal(new Age.Engine.Hosting.AdvWaitIndicatorConfig(1, 385, 140, 12, 0, 0, 30, 27, 12, 48),
                     Assert.Single(host.WaitIndicators));
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
