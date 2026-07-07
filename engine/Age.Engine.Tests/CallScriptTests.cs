using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class CallScriptTests
{
    // Opcodes (from build/opcodes.json): exit=0x2, call-script=0x3(argc1), mov=0x55(argc2).
    // Operand types: imm=0, global-int=3, local-int=9.
    private const uint OP_EXIT = 0x2, OP_CALLSCRIPT = 0x3, OP_MOV = 0x55;

    private sealed class NullHost : IHost
    {
        public List<long> Calls = new();
        public void ShowText(int o, string t) { }
        public void CallScript(long id) => Calls.Add(id);
        public void OnStub(int op) { }
        public void WaitForInput() { }
        public void CreateTexture(int s, int w, int h) { }
        public void SetTexture(long r, int s) { }
        public void DrawTexture(int s, int sx, int sy, int w, int h, int dx, int dy) { }
        public (int Width, int Height) GetTextureSize(int s) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    private sealed class MapProvider : IScriptProvider
    {
        private readonly Dictionary<long, Script> _m;
        public MapProvider(Dictionary<long, Script> m) => _m = m;
        public Script? GetById(long id) => _m.TryGetValue(id, out var s) ? s : null;
    }

    // Build a Script from raw dwords via the real loader (guarantees identical decode).
    private static Script Asm(OpcodeTable t, string name, params uint[] body)
    {
        var bytes = new byte[0x3C + body.Length * 4];
        System.Text.Encoding.ASCII.GetBytes("SYS4422 ").CopyTo(bytes, 0);
        // fields[8] (F8 = code end) at header offset 8 + 8*4 = 0x28; set to body length (all code).
        System.BitConverter.GetBytes(body.Length).CopyTo(bytes, 8 + 8 * 4);
        for (int i = 0; i < body.Length; i++) System.BitConverter.GetBytes(body[i]).CopyTo(bytes, 0x3C + i * 4);
        return Sys4Loader.Parse(bytes, t, name);
    }

    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);

    [Fact]
    public void CalleeRunsAndControlResumesAfterTheCall()
    {
        var t = Table();
        // Callee (id 5): mov g[0x10] = 7, then exit.
        var callee = Asm(t, "CALLEE", OP_MOV, 3, 0x10, 0, 7, OP_EXIT);
        // Caller: call-script 5 ; mov g[0x11] = g[0x10] ; exit.
        var caller = Asm(t, "CALLER",
            OP_CALLSCRIPT, 0, 5,
            OP_MOV, 3, 0x11, 3, 0x10,
            OP_EXIT);
        var host = new NullHost();
        var vm = new VirtualMachine(caller, t, host, null, new MapProvider(new() { [5] = callee }));
        vm.Run();
        Assert.Equal(7, vm.Globals[0x10]);           // callee wrote a shared global
        Assert.Equal(7, vm.Globals[0x11]);           // caller read it AFTER the call returned
        Assert.Equal("exit", vm.HaltReason);         // top-level exit
        Assert.Contains(5L, host.Calls);             // host notified
    }

    [Fact]
    public void MissingProviderFallsBackToStub()
    {
        var t = Table();
        var caller = Asm(t, "CALLER", OP_CALLSCRIPT, 0, 5, OP_EXIT);
        var host = new NullHost();
        var vm = new VirtualMachine(caller, t, host, null, null);   // no provider
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);        // did not halt on the call; stub + continue
        Assert.Contains(5L, host.Calls);
    }

    [Fact]
    public void UnresolvedIdHalts()
    {
        var t = Table();
        var caller = Asm(t, "CALLER", OP_CALLSCRIPT, 0, 99, OP_EXIT);
        var vm = new VirtualMachine(caller, t, new NullHost(), null, new MapProvider(new()));
        vm.Run();
        Assert.StartsWith("callscript-unresolved", vm.HaltReason);
    }

    [Fact]
    public void LocalsDoNotLeakBetweenCallerAndCallee()
    {
        var t = Table();
        // Callee writes LOCAL-int 0 = 42 (mov to local-int, type 9), then exit.
        var callee = Asm(t, "CALLEE", OP_MOV, 9, 0, 0, 42, OP_EXIT);
        // Caller sets local-int 0 = 1, calls, then copies its own local-int 0 to global 0x20.
        var caller = Asm(t, "CALLER",
            OP_MOV, 9, 0, 0, 1,                  // l[0] = 1
            OP_CALLSCRIPT, 0, 5,                 // call-script 5 (callee sets ITS local 0 = 42)
            OP_MOV, 3, 0x20, 9, 0,               // g[0x20] = l[0]
            OP_EXIT);
        var vm = new VirtualMachine(caller, t, new NullHost(), null, new MapProvider(new() { [5] = callee }));
        vm.Run();
        Assert.Equal(1, vm.Globals[0x20]);   // caller's local 0 unchanged by callee's local 0
    }
}
