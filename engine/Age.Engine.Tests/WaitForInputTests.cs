using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class WaitForInputTests
{
    private sealed class NestedWaitHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        public bool SawDormantParentCallback;
        public bool SawParentCallbackRestored;

        public override void WaitForInput(int layoutSlot, System.Func<bool> serviceInputCallback)
        {
            Waits++;
            SawDormantParentCallback =
                Vm.RawInputCallbackScriptName == "PARENT"
                && !Vm.IsRawInputCallbackActive;
        }

        public override void Sleep(long duration)
        {
            SawParentCallbackRestored =
                Vm.RawInputCallbackScriptName == "PARENT"
                && Vm.IsRawInputCallbackActive;
        }
    }

    // wait-for-input (0x72) fires per page. Synthesize a two-page scene and assert it fires exactly
    // twice — full handling, no dependency on a real scene's (stubbed) line count.
    [Fact]
    public void WaitForInputFiresOncePerPage()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        (int, Operand[]) ShowText(int s) => (0x6e, new[] { new Operand(2, s), new Operand(0, 0) });
        (int, Operand[]) Wait() => (0x72, new[] { new Operand(0, 0) });
        (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

        var scene = ScriptAssembler.Assemble(t, "TWOPAGE",
            new List<(int, Operand[])> { ShowText(0), Wait(), ShowText(1), Wait(), Exit() },
            new[] { "page one", "page two" });

        var host = new RecordingHost();
        var vm = new VirtualMachine(scene, t, host);
        vm.Run();

        Assert.Equal(2, host.Waits);
        Assert.Equal(new[] { "page one", "page two" }, vm.Emitted.Select(e => e.Text).ToArray());
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void DormantParentRawInputCallbackDoesNotOwnNestedDialogueWait()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        var child = ScriptAssembler.Assemble(t, "CHILD", new List<(int, Operand[])>
        {
            (0x6e, new[] { new Operand(2, 0), new Operand(0, 0) }),
            (0x72, new[] { new Operand(0, 0) }),
            (0x2, System.Array.Empty<Operand>()),
        }, new[] { "nested dialogue" });
        var parent = ScriptAssembler.Assemble(t, "PARENT", new List<(int, Operand[])>
        {
            // FIELD-style timed mouse callback remains registered while call-script enters an ADV scene.
            (0xcc, new[] { new Operand(0, 50), new Operand(0, 0xffff_ffff) }),
            (0x3, new[] { new Operand(0, 5) }),
            (0xc8, new[] { new Operand(0, 0) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new NestedWaitHost();
        var vm = new VirtualMachine(parent, t, host, provider: new MapProvider(new() { [5] = child }));
        host.Vm = vm;

        vm.Run();

        Assert.True(host.SawDormantParentCallback);
        Assert.True(host.SawParentCallbackRestored);
        Assert.Equal(1, host.Waits);
        Assert.False(vm.IsRawInputCallbackActive);
        Assert.Equal("exit", vm.HaltReason);
    }
}
