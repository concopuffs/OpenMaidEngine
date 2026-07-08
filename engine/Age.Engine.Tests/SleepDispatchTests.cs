using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class SleepDispatchTests
{
    [Fact]
    public void SleepOp_ForwardsRawOperand_ToHost_AndAdvancesLikeNoop()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        (int, Operand[]) Sleep(long ms) => (0xc8, new[] { new Operand(0, ms) });
        (int, Operand[]) Show(int s) => (0x6e, new[] { new Operand(2, s), new Operand(0, 0) });
        (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

        var script = ScriptAssembler.Assemble(table, "SLEEPTEST",
            new List<(int, Operand[])> { Sleep(200), Show(0), Sleep(1000), Exit() }, new[] { "hi" });

        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.Run();

        Assert.Equal(new List<long> { 200, 1000 }, host.SleptDurations);
        Assert.Single(host.Lines);                        // the show-text between the sleeps still ran
    }
}
