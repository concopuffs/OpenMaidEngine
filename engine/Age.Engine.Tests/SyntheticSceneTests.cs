using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

/// <summary>Regression coverage built from a SYNTHESIZED scene rather than a real scene run in a
/// crippled mode. The scene exercises the known-good sequence (show-text, wait-for-input, a real
/// nested call-script, shared-global return) with FULL op handling on, and asserts its exact
/// deterministic output. This is the model for engine regression tests: synthesize the data, don't
/// disable features to keep a real scene matching a frozen number.</summary>
public class SyntheticSceneTests
{
    // Opcodes: show-text=0x6e(argc2), end-text-line=0x6f, wait-for-input=0x72(argc1),
    // call-script=0x3(argc1), mov=0x55(argc2), exit=0x2. Operand types: imm=0, string=2, global-int=3.
    private static (int, Operand[]) ShowText(int strIndex) => (0x6e, new[] { new Operand(2, strIndex), new Operand(0, 0) });
    private static (int, Operand[]) Wait() => (0x72, new[] { new Operand(0, 0) });
    private static (int, Operand[]) CallScript(long id) => (0x3, new[] { new Operand(0, id) });
    private static (int, Operand[]) MovGG(int dst, int src) => (0x55, new[] { new Operand(3, dst), new Operand(3, src) });
    private static (int, Operand[]) MovGI(int dst, long imm) => (0x55, new[] { new Operand(3, dst), new Operand(0, imm) });
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void SynthesizedSceneRunsWithFullHandling()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);

        // Callee (id 5): show "Sub", set g[0x31]=42, exit.
        var callee = ScriptAssembler.Assemble(t, "SUBSCENE",
            new List<(int, Operand[])> { ShowText(0), MovGI(0x31, 42), Exit() },
            new[] { "Sub" });

        // Caller: show "Hello", wait, show "World", call-script 5, g[0x30]=g[0x31], exit.
        var caller = ScriptAssembler.Assemble(t, "SCENE",
            new List<(int, Operand[])> { ShowText(0), Wait(), ShowText(1), CallScript(5), MovGG(0x30, 0x31), Exit() },
            new[] { "Hello", "World" });

        var host = new RecordingHost();
        var vm = new VirtualMachine(caller, t, host, null, new MapProvider(new() { [5] = callee }));
        vm.Run();

        // Emitted in execution order, with the callee's line interleaved after the call site.
        Assert.Equal(new[] { "Hello", "World", "Sub" }, vm.Emitted.Select(e => e.Text).ToArray());
        Assert.Equal("SCENE", vm.Emitted[0].Script);
        Assert.Equal("SUBSCENE", vm.Emitted[2].Script);   // proves the subroutine actually executed
        Assert.Equal(1, host.Waits);                       // wait-for-input fired once
        Assert.Equal(42, vm.Globals[0x31]);                // callee wrote a shared global
        Assert.Equal(42, vm.Globals[0x30]);                // caller read it AFTER the call returned
        Assert.Equal("exit", vm.HaltReason);               // clean top-level exit
    }
}
