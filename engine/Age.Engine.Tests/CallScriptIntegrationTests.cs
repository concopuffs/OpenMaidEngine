using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class CallScriptIntegrationTests
{
    private sealed class NullHost : IHost
    {
        public int CallScripts;
        public void ShowText(int o, string t) { }
        public void CallScript(long id) => CallScripts++;
        public void OnStub(int op) { }
        public void WaitForInput() { }
        public void CreateTexture(int s, int w, int h) { }
        public void SetTexture(long r, int s) { }
        public void DrawTexture(int s, int sx, int sy, int w, int h, int dx, int dy) { }
        public (int Width, int Height) GetTextureSize(int s) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    [Fact]
    public void RealScriptExecutesRealSubroutinesAndReturns()
    {
        // ADDILL.BIN unconditionally call-scripts ADDILLSUB then CALCREVISE at entry, then exits.
        // With execution on, both subroutines load, run, and return, so ADDILL reaches its own exit.
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(t);
        var script = Sys4Loader.Load(Paths.Scripts()["ADDILL.BIN"], t);
        var host = new NullHost();
        var vm = new VirtualMachine(script, t, host, null, provider);
        vm.Run();
        Assert.Equal(2, vm.CallScriptDispatches); // ADDILLSUB + CALCREVISE both dispatched
        Assert.Equal("exit", vm.HaltReason);      // subroutines returned; ADDILL reached its own exit
    }

    [Fact]
    public void BunkiTopLevelRetReturnsCleanlyAsSubroutine()
    {
        // BUNKI.BIN ends with a top-level `ret` (empty intra-call stack). Called as a subroutine it
        // must return to the caller, not underflow-halt. Drive it directly.
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(t);
        var bunki = provider.GetById(0x143);   // BUNKI.BIN
        Assert.NotNull(bunki);
        var vm = new VirtualMachine(bunki!, t, new NullHost(), null, provider);
        vm.Run();
        // Reaching a frame-return at the top = clean "exit"; never "ret-underflow".
        Assert.NotEqual("ret-underflow", vm.HaltReason);
    }
}
