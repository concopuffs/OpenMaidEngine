using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

/// <summary>A direct-scene run hosted by the root, as the F4 developer launcher enters a scene
/// (<see cref="VirtualMachine.HostScene"/>).</summary>
public class HostedSceneEntryTests
{
    private const long CoordinatorId = 0x10, SceneId = 0x22;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);

    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);

    private static Script Assemble(string name, params (int, Operand[])[] instructions)
        => ScriptAssembler.Assemble(Table, name, instructions, Array.Empty<string>());

    /// <summary>A SYSTEM4-shaped root: it reads the initial-run flag (LOGO/OP), calls the coordinator, calls the
    /// script the coordinator chose, then marks that it ran on.</summary>
    private static (Script Root, MapProvider Provider) HostingRoot()
    {
        var root = Assemble("ROOT.BIN",
            (0x130, new[] { G(0x100) }),          // get-initial-root-run
            (0x3, new[] { I(CoordinatorId) }),    // call-script TITLE
            (0x3, new[] { G(0x699) }),            // call-script G[0x699]
            (0x55, new[] { G(0x104), I(1) }),     // the root after the scene: must not run
            (0x2, Array.Empty<Operand>()));
        var coordinator = Assemble("TITLE.BIN",
            (0x55, new[] { G(0x102), I(1) }),     // the coordinator's body: must not run
            (0x2, Array.Empty<Operand>()));
        var scene = Assemble("SCENE.BIN",
            (0x55, new[] { G(0x103), G(0x55) }),  // reads a handoff write
            (0x2, Array.Empty<Operand>()));
        return (root, new MapProvider(new Dictionary<long, Script>
        {
            [CoordinatorId] = coordinator,
            [SceneId] = scene,
        }));
    }

    [Fact]
    public void HostedRun_SkipsTheInitialRunReturnsTheCoordinatorAtEntryAndEndsAtTheScenesReturn()
    {
        var (root, provider) = HostingRoot();
        var vm = new VirtualMachine(root, Table, new RecordingHost(), provider: provider);
        vm.Globals[0x55] = 3;   // boot state the handoff overrides
        vm.HostScene(new HostedSceneEntry("TITLE.BIN", SceneId,
            new Dictionary<int, long> { [0x699] = SceneId, [0x55] = 7 }));

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0, vm.Globals[0x100]);            // as after a return to title: SYSTEM4 skips LOGO and OP
        Assert.False(vm.Globals.ContainsKey(0x102));   // TITLE returned before its first instruction
        Assert.Equal(7, vm.Globals[0x103]);            // the scene ran after the writes
        Assert.False(vm.Globals.ContainsKey(0x104));   // and its return ended the run
    }

    [Fact]
    public void UnhostedRun_RunsTheCoordinatorAndTheRootOnAfterTheScene()
    {
        var (root, provider) = HostingRoot();
        var vm = new VirtualMachine(root, Table, new RecordingHost(), provider: provider);
        vm.Globals[0x699] = SceneId;

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Equal(1, vm.Globals[0x102]);
        Assert.Equal(1, vm.Globals[0x104]);
    }

    private sealed class OrphanArmHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        public bool ClickConsumed = true;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Vm.UpdatePointer(20, 30);
            while (serviceInputCallback()) { }
            ClickConsumed = Vm.TryActivatePointer(20, 30);
            while (serviceInputCallback()) { }
        }
    }

    [Fact]
    public void CancelHotspotWait_EmptiesTheTable_SoAnArmWithoutARegistrationArmsNothing()
    {
        // Native op 0x93 (input_hotspot_reset) empties the frame's table. A scene's developer-loop advend (0x622)
        // is such an orphan 0x93; retail registers again before every later wait.
        const int activateTarget = 21;   // 0x90 (15) + 0x93 (1) + 0x94 (1) + 0x72 (3) + exit (1)
        var script = Assemble("ORPHAN",
            (0x90, new[] { I(10), I(20), I(20), I(20), I(-1), I(-1), I(activateTarget) }),
            (0x93, Array.Empty<Operand>()),
            (0x94, Array.Empty<Operand>()),
            (0x72, new[] { I(1) }),
            (0x2, Array.Empty<Operand>()),
            (0x55, new[] { G(0x130), I(1) }),
            (0x5, Array.Empty<Operand>()));
        var host = new OrphanArmHost();
        var vm = new VirtualMachine(script, Table, host);
        host.Vm = vm;

        vm.Run();

        Assert.False(host.ClickConsumed);   // the click advances the page instead
        Assert.False(vm.Globals.ContainsKey(0x130));
        Assert.Equal("exit", vm.HaltReason);
    }
}
