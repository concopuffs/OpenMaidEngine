using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class TextureGeometryTests
{
    private sealed class FakeSizeHost : IHost
    {
        public void ShowText(int o, string t) { }
        public void CallScript(long id) { }
        public void OnStub(int op) { }
        public void WaitForInput() { }
        public void CreateTexture(int slot, int w, int h) { }
        public void SetTexture(long resId, int slot) { }
        public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy) { }
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
        public (int Width, int Height) GetTextureSize(int slot) => (0x140, 0xC8);
    }

    [Fact]
    public void GetTextureSizeWritesHostDimsIntoOutputGlobals()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // 0x208 (global-int 50)(global-int 60)(global-int 61): slot=50, out_w=G[60], out_h=G[61]
        const int T_GINT = 3;
        var ins = new Instruction(0, 0x208, new[]
        {
            new Operand(T_GINT, 50), new Operand(T_GINT, 60), new Operand(T_GINT, 61),
        });
        var script = new Script
        {
            Header = new ScriptHeader(0, 0, 0, 0, 0, 0),
            Instructions = new[] { ins },
            IndexByOffset = new Dictionary<int, int> { { 0, 0 } },
            Strings = new Dictionary<int, string>(),
        };
        var vm = new VirtualMachine(script, table, new FakeSizeHost());
        vm.Run();
        Assert.Equal(0x140, vm.Globals[60]);
        Assert.Equal(0xC8, vm.Globals[61]);
    }
}
