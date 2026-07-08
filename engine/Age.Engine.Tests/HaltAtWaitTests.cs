using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class HaltAtWaitTests
{
    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);

    // Two-page scene: show A ; wait-for-input ; show B ; exit.
    private static Age.Engine.Model.Script TwoPage(OpcodeTable t) => ScriptAssembler.Assemble(t, "TWOPAGE",
        new List<(int, Operand[])>
        {
            (0x6e, new[] { new Operand(2, 0), new Operand(0, 0) }),   // show-text A
            (0x72, new[] { new Operand(0, 0) }),                       // wait-for-input
            (0x6e, new[] { new Operand(2, 1), new Operand(0, 0) }),   // show-text B
            (0x2, Array.Empty<Operand>()),                            // exit
        }, new[] { "A", "B" });

    [Fact]
    public void Default_PlowsPastWait_EmitsBothPages()
    {
        var t = Table();
        var host = new RecordingHost();
        var vm = new VirtualMachine(TwoPage(t), t, host);   // default VmOptions => HaltAtWaitForInput=false
        vm.Run();
        Assert.Equal(2, host.Lines.Count);          // both pages emitted (plow)
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void HaltAtWait_StopsAtFirstWait_EmitsOnlyFirstPage()
    {
        var t = Table();
        var host = new RecordingHost();
        var vm = new VirtualMachine(TwoPage(t), t, host, new VmOptions(HaltAtWaitForInput: true));
        vm.Run();
        Assert.Single(host.Lines);                  // only page A ran; halted at the wait
        Assert.Equal("wait-for-input", vm.HaltReason);
    }
}
