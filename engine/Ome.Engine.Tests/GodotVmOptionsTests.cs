public class GodotVmOptionsTests
{
    [Fact]
    public void PersistentInteractiveRunIsUnboundedButSelftestRetainsDiagnosticCap()
    {
        var interactive = GodotVmOptions.Create(selftest: false, ignoreExitRequests: true);
        var selftest = GodotVmOptions.Create(selftest: true, ignoreExitRequests: false);

        Assert.Equal(long.MaxValue, interactive.MaxSteps);
        Assert.True(interactive.IgnoreExitRequests);
        Assert.False(interactive.NoSaveDat);

        Assert.Equal(GodotVmOptions.DiagnosticMaxSteps, selftest.MaxSteps);
        Assert.False(selftest.IgnoreExitRequests);
        Assert.True(selftest.NoSaveDat);
    }
}
