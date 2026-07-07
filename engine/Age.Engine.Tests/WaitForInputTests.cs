using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class WaitForInputTests
{
    // wait-for-input (0x72) fires per page. Synthesize a two-page scene and assert it fires exactly
    // twice — full handling, no dependency on a real scene's (stubbed) line count.
    [Fact]
    public void WaitForInputFiresOncePerPage()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        (int, Operand[]) ShowText(int s) => (0x6e, new[] { new Operand(2, s), new Operand(0, 0) });
        (int, Operand[]) Wait() => (0x72, new[] { new Operand(0, 0) });
        (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

        var scene = ScriptAssembler.Assemble(t, "TWOPAGE",
            new List<(int, Operand[])> { ShowText(0), Wait(), ShowText(1), Wait(), Exit() },
            new[] { "page one", "page two" });

        var host = new RecordingHost();
        var vm = new VirtualMachine(scene, t, host);
        vm.Run();

        Assert.Equal(2, host.Waits);
        Assert.Equal(new[] { "page one", "page two" }, vm.Emitted.Select(e => e.Text).ToArray());
        Assert.Equal("exit", vm.HaltReason);
    }
}
