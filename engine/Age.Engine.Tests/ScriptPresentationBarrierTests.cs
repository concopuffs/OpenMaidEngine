using System.Threading;
using System.Threading.Tasks;
using Age.Engine.Hosting;
using Xunit;

public class ScriptPresentationBarrierTests
{
    [Fact]
    public void ScriptBurstWithholdsPresentationUntilExit()
    {
        var barrier = new ScriptPresentationBarrier();

        barrier.EnterScript();
        Assert.False(barrier.TryEnterPresentation());

        barrier.ExitScript();
        Assert.True(barrier.TryEnterPresentation());
        barrier.ExitPresentation();
    }

    [Fact]
    public void NestedScriptsRemainOneAtomicBurst()
    {
        var barrier = new ScriptPresentationBarrier();

        barrier.EnterScript();
        barrier.EnterScript();
        barrier.ExitScript();
        Assert.False(barrier.TryEnterPresentation());

        barrier.ExitScript();
        Assert.True(barrier.TryEnterPresentation());
        barrier.ExitPresentation();
    }

    [Fact]
    public void ServiceBoundaryTemporarilyAllowsPresentation()
    {
        var barrier = new ScriptPresentationBarrier();

        barrier.EnterScript();
        bool suspended = barrier.SuspendScript();
        Assert.True(suspended);
        Assert.True(barrier.TryEnterPresentation());
        barrier.ExitPresentation();

        barrier.ResumeScript(suspended);
        Assert.False(barrier.TryEnterPresentation());
        barrier.ExitScript();
    }

    [Fact]
    public async Task RenderThreadCannotEnterWhileVmThreadIsRebuilding()
    {
        var barrier = new ScriptPresentationBarrier();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task vmBurst = Task.Run(() =>
        {
            barrier.EnterScript();
            entered.Set();
            release.Wait();
            barrier.ExitScript();
        });

        await Task.Run(entered.Wait);
        Assert.False(barrier.TryEnterPresentation());

        release.Set();
        await vmBurst;
        Assert.True(barrier.TryEnterPresentation());
        barrier.ExitPresentation();
    }
}
