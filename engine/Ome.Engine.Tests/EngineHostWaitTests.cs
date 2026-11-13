using System.Text.RegularExpressions;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Sys4;
using Xunit;

/// <summary>
/// Every wait that blocks the engine thread runs the engine tick, unless it is modal (engine layering spec §5.3, part
/// 1): the host blocks only through <c>EngineHost.Waits.cs</c>, a fade goes on while the host sleeps, and it stops under
/// a modal hold and catches up after, as native's does under the 終了 dialog (docs/engine-re.md).
/// </summary>
public class EngineHostWaitTests
{
    // A blocking call: a wait with a timeout other than zero (a zero timeout only polls), or a sleep.
    private static readonly Regex Blocking = new(
        @"\.WaitOne\((?!\s*0\s*\))|\.Wait\((?!\s*0\s*\))|WaitHandle\.Wait(Any|All)\(|Thread\.Sleep\(|Monitor\.Wait\(|SpinWait\.SpinUntil\(",
        RegexOptions.CultureInvariant);

    [Fact]
    public void TheHostBlocksOnlyThroughItsWaitHelper()
    {
        string hosting = Path.Combine(Paths.Repo, "engine", "Ome.Engine", "Hosting");
        string[] files = Directory.GetFiles(hosting, "EngineHost*.cs");
        Assert.Contains(files, file => Path.GetFileName(file) == "EngineHost.Waits.cs");
        var found = new List<string>();
        foreach (string file in files.Where(file => Path.GetFileName(file) != "EngineHost.Waits.cs"))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i];
                int comment = code.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) code = code[..comment];
                if (Blocking.IsMatch(code)) found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }
        Assert.True(found.Count == 0, "blocking calls outside EngineHost.Waits.cs:\n" + string.Join("\n", found));
    }

    [Fact]
    public void AFadeGoesOnWhileTheHostSleeps()
    {
        var rig = new AudioRig();
        using var locator = new PageLocatorState("SC0000", null);
        EngineHost host = EngineHostTests.Host(EngineHostTests.NothingAtAll(), locator, rig.Clock, audio: rig);
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(0, 1000);
        rig.Device.Commands.Clear();

        using (Frames(host, rig))
            host.Sleep(500);
        Assert.NotEmpty(rig.Device.CommandsStartingWith("volume 11"));   // the fade stepped while the sleep waited

        using (Frames(host, rig))
            host.Sleep(700);
        Assert.Contains("unload 11", rig.Device.Commands);   // and reached zero, which releases the source
        Assert.Equal(0, rig.Audio.CurrentBgmTrack);
    }

    [Fact]
    public void AModalHoldStopsTheFadeAndTheTickAfterCatchesUp()
    {
        var rig = new AudioRig();
        using var locator = new PageLocatorState("SC0000", null);
        EngineHost host = EngineHostTests.Host(EngineHostTests.NothingAtAll(), locator, rig.Clock, audio: rig);
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(0, 1000);
        rig.Device.Commands.Clear();

        host.SetFrontendModalHold(true);
        Task held = Task.Run(host.FrameYield);
        for (int frame = 0; frame < 100; frame++)   // two seconds of frames pass under the hold
        {
            rig.Clock.Advance(0.02);
            host.PulseFrame();
        }
        Thread.Sleep(100);
        Assert.False(held.IsCompleted);
        Assert.Empty(rig.Device.Commands);           // the modal wait ran no tick: the level held
        host.SetFrontendModalHold(false);
        Assert.True(held.Wait(TimeSpan.FromSeconds(5)));

        rig.Tick.Service();                          // the first tick after the hold makes up the time
        Assert.Contains("unload 11", rig.Device.Commands);
    }

    // Frames as the front end makes them: move the clock 16 ms and wake the engine thread, until disposed.
    private static IDisposable Frames(EngineHost host, AudioRig rig)
    {
        var stop = new CancellationTokenSource();
        Task pump = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                rig.Clock.Advance(0.016);
                host.PulseFrame();
                Thread.Sleep(1);
            }
        });
        return new Stopper(stop, pump);
    }

    private sealed class Stopper(CancellationTokenSource stop, Task pump) : IDisposable
    {
        public void Dispose()
        {
            stop.Cancel();
            pump.Wait();
            stop.Dispose();
        }
    }
}
