using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class FrameYieldTests
{
    private sealed class CountingHost : IHost
    {
        public long Yields;
        public void FrameYield() => Yields++;
        public void ShowText(int offset, string text) { }
        public void WaitForInput() { }
        public void Sleep(long duration) { }
        public void CreateTexture(int slot, int width, int height) { }
        public void SetTexture(long resourceId, int slot) { }
        public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy) { }
        public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void FrameYield_CalledOncePerStep()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["INITCONFIG.BIN"], table);
        var host = new CountingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.Run();
        Assert.True(vm.Steps > 0);
        Assert.Equal(vm.Steps, host.Yields);   // exactly one FrameYield per executed opcode
    }
}
