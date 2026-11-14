using System.Collections.Generic;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

public class TextureGeometryTests
{
    [Fact]
    public void GetTextureSizeWritesTheSurfaceSizeIntoOutputGlobals()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // 0x208 (global-int 50)(global-int 60)(global-int 61): slot=G[50], out_w=G[60], out_h=G[61]
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
        var vm = new VirtualMachine(script, table, new RecordingWaits());
        vm.Globals[50] = 9;
        vm.Graphics.CreateSurface(9, 0x140, 0xC8);
        vm.Run();
        Assert.Equal(0x140, vm.Globals[60]);
        Assert.Equal(0xC8, vm.Globals[61]);
    }
}
