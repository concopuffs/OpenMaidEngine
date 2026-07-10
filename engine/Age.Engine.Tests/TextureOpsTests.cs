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
        public void Sleep(long duration) { }
        public void FrameYield() { }
        public void CreateTexture(int slot, int w, int h) => Creates++;
        public void SetTexture(long resId, int slot) => Sets.Add((resId, slot));
        public void DrawTexture(int slot, int srcX, int srcY, int w, int h, int dstX, int dstY) => Draws.Add((slot, w, h));
        public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    [Fact]
    public void SC0000FiresTextureOpsWithAssignedFullScreenSlot()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(table);
        var session = new GameSession();
        foreach (var name in new[] { "INITCONFIG.BIN", "INIT2.BIN", "INIT.BIN" })
            session.RunScene(Sys4Loader.Load(Paths.Scripts()[name], table), table, new CaptureHost(), provider: provider);

        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var host = new RecHost();
        var vm = new VirtualMachine(script, table, host, new VmOptions(MaxSteps: 20_000_000), provider);
        foreach (var kv in session.Globals) vm.Globals[kv.Key] = kv.Value;
        foreach (var kv in session.GlobalStrings) vm.GlobalStrings[kv.Key] = kv.Value;
        vm.Run();

        Assert.True(host.Creates > 0, "create-texture should fire");
        // Boot + coroutine setup fill the handle/slot tables, so the first bg uses its assigned slot.
        Assert.Contains(host.Sets, s => s.resId == 0x23 && s.slot == 5);
        Assert.Contains(host.Draws, d => d.slot == 5 && d.w == 0x320 && d.h == 0x258);
        // AE001H is the ritual/magic-circle sheet. At the post-effect transition SC0000 explicitly queries its
        // retained object, erases the object group, and releases the returned slot; it must not survive the scene.
        Assert.DoesNotContain(vm.Gfx.SnapshotVisibleObjects(), o => o.SurfaceResId == 0x37);
    }
}
