using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Profiles;
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
    [Trait("Category", "Workspace")]
    public void SC0000FiresTextureOpsWithAssignedFullScreenSlot()
    {
        // SC0000 hosted by SYSTEM4, as a Godot direct-scene run and the F4 launcher enter it.
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
        var provider = Sys4ScriptProvider.Load(table);
        GameProfileManifest profile = GameProfileRegistry.BuiltIn.Find("himegari")!;
        DebugSceneLaunchPolicy launch = profile.DebugSceneLaunch!;
        var scene = provider.RequireByName("SC0000.BIN");
        var writes = new Dictionary<int, long>();
        foreach (var seed in launch.CoordinatorWrites) writes[seed.Address] = seed.Value;
        writes[launch.PackedScriptIdAddress] = scene.PackedId;
        var host = new RecHost();
        var vm = new VirtualMachine(
            provider.RequireByName(launch.RootScript), table, host, new VmOptions(MaxSteps: 20_000_000), provider,
            compatibility: new VmCompatibilityContext(profile.Id, profile.EngineAbiId));
        vm.HostScene(new HostedSceneEntry(launch.CoordinatorScript, scene.PackedId, writes));
        vm.Run();   // headless, the scene ends polling INPUTNAME's name entry (SC0000@0xe632) until the step limit

        Assert.True(host.Creates > 0, "create-texture should fire");
        // SYSTEM4's boot and ADV setup fill the handle/slot tables, so the first bg uses its assigned slot.
        Assert.Contains(host.Sets, s => s.resId == 0x23 && s.slot == 5);
        Assert.Contains(host.Draws, d => d.slot == 5 && d.w == 0x320 && d.h == 0x258);
        // AE001H is the ritual/magic-circle sheet. At the post-effect transition SC0000 explicitly queries its
        // retained object, erases the object group, and releases the returned slot; it must not survive the scene.
        Assert.DoesNotContain(vm.Gfx.SnapshotVisibleObjects(), o => o.SurfaceResId == 0x37);
    }
}
