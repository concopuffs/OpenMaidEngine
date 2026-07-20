using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class SfxOpsTests
{
    [Fact]
    public void Sc0000AudioLifecycleDelayAndDuckControlReachHost()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "SFX",
            new List<(int, Operand[])>
            {
                (0xb4, new[] { new Operand(0, 0x28), new Operand(0, 0) }),
                (0xb5, new[] { new Operand(0, 0) }),
                (0x1cf, new[] { new Operand(0, 1) }),
                (0x2bf, new[] { new Operand(0, 4), new Operand(0, 0), new Operand(0, 100) }),
                (0xb6, new[] { new Operand(0, 0) }),
                (0xc2, new[] { new Operand(0, 25), new Operand(0, 3000) }),
                (0xd9, Array.Empty<Operand>()),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((0x28L, 0), Assert.Single(host.SfxLoads));
        Assert.Equal(0, Assert.Single(host.SfxStarts));
        Assert.Equal(1, Assert.Single(host.VoiceBgmDuckControls));
        Assert.Equal((4, 0, 100L), Assert.Single(host.ScheduledSfxStarts));
        Assert.Equal(0, Assert.Single(host.SfxReleases));
        Assert.Equal((25, 3000L), Assert.Single(host.BgmFades));
    }
}
