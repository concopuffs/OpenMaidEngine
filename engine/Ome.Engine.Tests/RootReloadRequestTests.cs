using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>The 終了 dialog's タイトルに戻る (FUN_00405740 result 2): native calls op_0x9_reset_scene_and_reload_root
/// from the window procedure. The port ends every frame at its next opcode boundary, interrupting a blocking host
/// wait, and runs op 0x9's reset, including the global banks and the initial-root-run flag SYSTEM4 reads to skip
/// LOGO/OP.</summary>
public class RootReloadRequestTests
{
    private const int InitialFlag = 0x7400, SceneFlag = 0x7401, AfterWait = 0x7402;

    private sealed class InterruptibleSleepHost : RecordingWaits
    {
        private readonly ManualResetEventSlim _interrupted = new(false);
        public ManualResetEventSlim FirstSleepEntered { get; } = new(false);
        public VirtualMachine? Vm;
        public long AfterWaitSeenBySecondRun = -1;

        public override void Sleep(long duration)
        {
            SleptDurations.Add(duration);
            if (SleptDurations.Count == 1)
            {
                FirstSleepEntered.Set();
                if (!_interrupted.Wait(VmRunThread.HangGuard)) throw new TimeoutException("sleep was not interrupted");
                return;
            }
            AfterWaitSeenBySecondRun = Vm!.Globals.GetValueOrDefault(AfterWait);
        }

        public override void InterruptForRootReload()
        {
            base.InterruptForRootReload();
            _interrupted.Set();
        }
    }

    [Fact]
    public void RequestRootReloadInterruptsAWaitAndReloadsRootWithoutTheInitialFlag()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var root = ScriptAssembler.Assemble(table, "SYSTEM4.BIN", new List<(int, Operand[])>
        {
            (0x130, new[] { G(InitialFlag) }),        // get-initial-root-run
            (0xc8, new[] { I(1000) }),                 // the wait the dialog interrupts
            (0x55, new[] { G(AfterWait), I(1) }),     // must not run in the interrupted pass
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new InterruptibleSleepHost();
        var vm = new VirtualMachine(root, table, host,
            provider: new MapProvider(new Dictionary<long, Script> { [0] = root }));
        host.Vm = vm;
        vm.Globals[SceneFlag] = 5;

        var run = new VmRunThread(vm);
        VmRunThread.AwaitSignal(host.FirstSleepEntered, "entered the first sleep");
        Assert.Equal(1, vm.Globals.GetValueOrDefault(InitialFlag));
        vm.RequestRootReload();
        run.Join();

        Assert.Equal(1, host.RootReloadInterrupts);
        Assert.Equal(1, host.SceneContextResets);
        Assert.Equal(2, host.SleptDurations.Count);
        Assert.Equal(0, host.AfterWaitSeenBySecondRun);         // the interrupted pass stopped at the boundary
        Assert.Equal(0, vm.Globals.GetValueOrDefault(InitialFlag)); // the reloaded root is not the initial run
        Assert.Equal(1, vm.Globals.GetValueOrDefault(AfterWait));
        Assert.False(vm.Globals.ContainsKey(SceneFlag));          // the reset zeroed the global banks
        Assert.Equal("exit", vm.HaltReason);
    }

    /// <summary>The first callback script asked for by name is held at its lookup until the test lets it go; ids
    /// resolve at once.</summary>
    private sealed class HeldCallbackProvider(Dictionary<long, Script> scripts, Script callback) : IScriptProvider
    {
        public ManualResetEventSlim CallbackRequested { get; } = new(false);
        public ManualResetEventSlim ReleaseCallback { get; } = new(false);

        public Script? GetById(long id) => scripts.TryGetValue(id, out Script? script) ? script : null;

        public Script? GetByName(string name)
        {
            if (!CallbackRequested.IsSet)
            {
                CallbackRequested.Set();
                VmRunThread.AwaitSignal(ReleaseCallback, "was let go of the callback");
            }
            return callback;
        }
    }

    [Fact]
    public void ARequestTheReloadTookBeforeItsWakeRanLeavesTheNewRootsWaitBlocking()
    {
        // The request arrives while the instruction boundary looks up a menu's callback script, so its wake is posted.
        // The callback's first instruction takes the reload, and the root's loop returns straight to op 0x9's reset
        // without pumping; the new root's op 0xfa poll then pumps the late wake, which must not end it.
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        Script first = ScriptAssembler.Assemble(table, "FIRST", [(0x2, [])], []);
        Script callback = ScriptAssembler.Assemble(table, "CALLBACK", [(0x2, [])], []);
        Script root = ScriptAssembler.Assemble(table, "SYSTEM4.BIN", [
            (0xfa, []),                               // the new root's first input wait
            (0x55, [G(AfterWait), I(1)]),             // reached only if the wait ended
            (0x2, []),
        ], []);
        var provider = new HeldCallbackProvider(new Dictionary<long, Script> { [0] = root }, callback);
        using var locator = new PageLocatorState("FIRST", null);
        HeadlessNativesTests.NativeSet natives = HeadlessNativesTests.Natives(locator, new FrameClock());
        VirtualMachine vm = natives.Vm(first, table, provider: provider);
        natives.Shell.RequestSettingCallback();   // the 設定 menu's callback, taken at the first instruction boundary

        Task run = Task.Run(() => natives.Waits.RunEngine(() => vm.Run()));
        VmRunThread.AwaitSignal(provider.CallbackRequested, "looked up the callback");
        vm.RequestRootReload();
        provider.ReleaseCallback.Set();

        var guard = System.Diagnostics.Stopwatch.StartNew();
        while (!natives.Waits.IsWaiting && !run.IsCompleted)
        {
            if (guard.Elapsed > VmRunThread.HangGuard) throw new TimeoutException("the new root never waited");
            Thread.Sleep(1);
        }
        bool waiting = natives.Waits.IsWaiting;
        natives.Waits.Stop();
        if (!run.Wait(VmRunThread.HangGuard)) throw new TimeoutException("the VM run did not end");
        run.GetAwaiter().GetResult();

        Assert.True(waiting, $"the new root's wait ended at once ({vm.HaltReason})");
        Assert.False(vm.Globals.ContainsKey(AfterWait));
    }

    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
}
