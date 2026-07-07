using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class WaitForInputTests
{
    private sealed class CountHost : IHost
    {
        public int Waits;
        public List<int> Emitted = new();
        public void ShowText(int offset, string text) => Emitted.Add(offset);
        public void CallScript(long id) { }
        public void OnStub(int opcode) { }
        public void WaitForInput() => Waits++;
        public void CreateTexture(int slot, int w, int h) { }
        public void SetTexture(long resId, int slot) { }
        public void DrawTexture(int slot, int srcX, int srcY, int w, int h, int dstX, int dstY) { }
    }

    [Fact]
    public void WaitForInputFiresAndEmittedIsStable()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var host = new CountHost();
        var vm = new VirtualMachine(script, table, host);
        vm.Run();
        Assert.True(host.Waits > 0, "wait-for-input (0x72) should fire at least once");
        Assert.Equal(186, host.Emitted.Count);   // unchanged vs the A1 SC0000 trace
        Assert.Equal("exit", vm.HaltReason);
    }
}
