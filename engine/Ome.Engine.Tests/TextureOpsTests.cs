using System.Collections.Generic;
using Ome.Engine.Hosting;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

public class TextureOpsTests
{
    private sealed class RecHost : IHost
    {
        public void ShowText(int o, string t) { }
        public void WaitForInput() { }
        public void Sleep(long duration) { }
        public void FrameYield() { }
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
            build: EngineBuilds.ForProfile(profile));
        vm.HostScene(new HostedSceneEntry(launch.CoordinatorScript, scene.PackedId, writes));
        List<string> surfaceOps = vm.TraceSurfaceOps();
        vm.Run();   // headless, the scene ends polling INPUTNAME's name entry (SC0000@0xe632) until the step limit

        Assert.Contains(surfaceOps, op => op.StartsWith("create-texture "));
        // SYSTEM4's boot and ADV setup fill the handle/slot tables, so the first bg uses its assigned slot.
        Assert.Contains(surfaceOps, op => op.StartsWith("set-texture slot=5 res=0x23 "));
        Assert.Contains(surfaceOps, op => op.StartsWith("draw-texture slot=5 ") && op.Contains(" 800x600) "));
        // AE001H is the ritual/magic-circle sheet. At the post-effect transition SC0000 explicitly queries its
        // retained object, erases the object group, and releases the returned slot; it must not survive the scene.
        Assert.DoesNotContain(vm.Gfx.SnapshotVisibleObjects(), o => o.SurfaceResId == 0x37);
    }
}
