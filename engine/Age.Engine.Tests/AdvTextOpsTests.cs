using System;
using System.Collections.Generic;
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
