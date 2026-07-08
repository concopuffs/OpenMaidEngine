using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class TextureOpsTests
{
    private sealed class RecHost : IHost
    {
        public List<(long resId, int slot)> Sets = new();
        public List<(int slot, int w, int h)> Draws = new();
        public int Creates;
        public void ShowText(int o, string t) { }
        public void WaitForInput() { }
        public void CreateTexture(int slot, int w, int h) => Creates++;
        public void SetTexture(long resId, int slot) => Sets.Add((resId, slot));
        public void DrawTexture(int slot, int srcX, int srcY, int w, int h, int dstX, int dstY) => Draws.Add((slot, w, h));
        public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    [Fact]
    public void SC0000FiresTextureOpsWithSlot0FullScreenSlideshow()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var host = new RecHost();
        new VirtualMachine(script, table, host).Run();
        Assert.True(host.Creates > 0, "create-texture should fire");
        // The intro loads a sequence of full-screen images into slot 0; res 0x23 is the first bg.
        Assert.Contains(host.Sets, s => s.resId == 0x23 && s.slot == 0);
        Assert.Contains(host.Draws, d => d.slot == 0 && d.w == 0x320 && d.h == 0x258);
    }
}
