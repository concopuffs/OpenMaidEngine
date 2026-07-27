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

        static (int X, int Y, int Right, int Bottom) CursorAndBounds(AdvTextLayoutSnapshot layout)
            => (layout.CursorX, layout.CursorY, layout.Right, layout.Bottom);
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
