using System.Text.RegularExpressions;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Xunit;

/// <summary>
/// The input and waits native (engine layering milestone 11). Every wait that blocks the engine thread runs the engine
/// tick, unless it is modal (spec §5.3, part 1): the engine thread blocks only through <c>InputAndWaits.Blocking.cs</c>, a fade goes on while a sleep waits, and it stops under a modal hold and catches up
/// after, as native's does under the 終了 dialog (docs/engine-re.md). The front end's thread reaches the engine thread
/// only through queued input, frame turns and requests (spec §5.3, part 2; <c>InputAndWaits.Turns.cs</c>).
/// </summary>
public class InputAndWaitsTests
{
    // A blocking call: a wait with a timeout other than zero (a zero timeout only polls), or a sleep.
    private static readonly Regex Blocking = new(
        @"\.WaitOne\((?!\s*0\s*\))|\.Wait\((?!\s*0\s*\))|WaitHandle\.Wait(Any|All)\(|Thread\.Sleep\(|Monitor\.Wait\(|SpinWait\.SpinUntil\(",
        RegexOptions.CultureInvariant);

    // The engine thread blocks in InputAndWaits.Blocking.cs; InputAndWaits.Turns.cs blocks the front end's thread while
    // it waits for the engine thread's answer.
    private static readonly string[] BlockingFiles = ["InputAndWaits.Blocking.cs", "InputAndWaits.Turns.cs"];

    [Fact]
    public void TheWaitsBlockOnlyThroughTheirHelper()
    {
        string engine = Path.Combine(Paths.Repo, "engine", "Ome.Engine");
        string[] waits = Directory.GetFiles(Path.Combine(engine, "Natives"), "InputAndWaits*.cs");
        foreach (string name in BlockingFiles) Assert.Contains(waits, file => Path.GetFileName(file) == name);
        var found = new List<string>();
        foreach (string file in waits.Where(file => !BlockingFiles.Contains(Path.GetFileName(file))))
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
        Assert.True(found.Count == 0, "blocking calls outside the waits' helpers:\n" + string.Join("\n", found));
    }

    [Fact]
    public void AFadeGoesOnWhileASleepWaits()
    {
        var rig = new AudioRig();
        using var locator = new PageLocatorState("SC0000", null);
        InputAndWaits waits = HeadlessNativesTests.Waits(locator, rig.Clock, audio: rig);
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(0, 1000);
        rig.Device.Commands.Clear();

        using (Frames(waits, rig))
            waits.Sleep(500);
        Assert.NotEmpty(rig.Device.CommandsStartingWith("volume 11"));   // the fade stepped while the sleep waited

        using (Frames(waits, rig))
            waits.Sleep(700);
        Assert.Contains("unload 11", rig.Device.Commands);   // and reached zero, which releases the source
        Assert.Equal(0, rig.Audio.CurrentBgmTrack);
    }

    [Fact]
    public void AModalHoldStopsTheFadeAndTheTickAfterCatchesUp()
    {
        var rig = new AudioRig();
        using var locator = new PageLocatorState("SC0000", null);
        InputAndWaits waits = HeadlessNativesTests.Waits(locator, rig.Clock, audio: rig);
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(0, 1000);
        rig.Device.Commands.Clear();

        waits.SetFrontendModalHold(true);
        Task held = Task.Run(waits.FrameYield);
        for (int frame = 0; frame < 100; frame++)   // two seconds of frames pass under the hold
        {
            rig.Clock.Advance(0.02);
            waits.PulseFrame();
        }
        Thread.Sleep(100);
        Assert.False(held.IsCompleted);
        Assert.Empty(rig.Device.Commands);           // the modal wait ran no tick: the level held
        waits.SetFrontendModalHold(false);
        Assert.True(held.Wait(TimeSpan.FromSeconds(5)));

        rig.Tick.Service();                          // the first tick after the hold makes up the time
        Assert.Contains("unload 11", rig.Device.Commands);
    }

    // ---- the tick under a statement block (docs/engine-re.md, "Statement blocks") ----

    [Fact]
    public void AnEffectWaitLetsTheDelayedStartsThroughAnOpenBlock()
    {
        // Native's tick holds the delayed sound effect and voice starts while a block is open, unless op 0x21c's effect
        // wait parks the interpreter (run-state 0x400, which only that op sets).
        using BlockRig rig = new();
        rig.OnEngineThread(() => rig.Waits.Sleep(300));
        Assert.Empty(rig.Audio.Device.CommandsStartingWith("play"));   // a sleep in the block holds them

        GfxState gfx = rig.Natives.Graphics.State;
        gfx.QueueSurfaceAlphaTransition(102, 6, 101, 1, 100, 1, 0, 300);
        rig.OnEngineThread(() => rig.Waits.WaitForForegroundTransition(gfx));
        Assert.Equal(["play 2 sfx:40 once 0.0dB"], rig.Audio.Device.CommandsStartingWith("play 2"));
        Assert.Equal([(10L, false)], rig.Audio.Device.Voices);   // its delay counted from the park's first tick
        Assert.True(rig.Audio.Tick.StatementBlockOpen);
    }

    [Fact]
    public void AScreenTransitionHoldsTheDelayedStartsUnderAnOpenBlock()
    {
        // Ops 0x21, 0x22, 0x24 and 0x25 run under other run-state bits, which do not open the tick's check.
        using BlockRig rig = new();
        rig.OnEngineThread(() => rig.Waits.FadeSurfaceToWhite(rig.Natives.Graphics.State, 0, 20));   // 320 ms
        Assert.Empty(rig.Audio.Device.CommandsStartingWith("play"));

        rig.Audio.Tick.CloseStatementBlock();
        rig.Audio.Advance(0);
        Assert.Equal(["play 2 sfx:40 once 0.0dB"], rig.Audio.Device.CommandsStartingWith("play 2"));
    }

    // A sound effect and a voice due 100 ms after they are scheduled, inside an open statement block.
    private sealed class BlockRig : IDisposable
    {
        public BlockRig()
        {
            Natives = HeadlessNativesTests.Natives(_locator, Audio.Clock, audio: Audio);
            Audio.Audio.LoadSoundEffect(40, 2);
            Audio.Audio.ScheduleSoundEffectStart(2, 0, 100);
            Audio.Audio.ScheduleVoicePlayback(10, 0, 100);
            Audio.Tick.OpenStatementBlock();
        }

        private readonly PageLocatorState _locator = new("SC0000", null);
        public AudioRig Audio { get; } = new();
        public HeadlessNativesTests.NativeSet Natives { get; }
        public InputAndWaits Waits => Natives.Waits;

        // Run a wait on an engine thread while this thread takes the front end's frame turns, 16 ms apart.
        public void OnEngineThread(Action wait)
        {
            Task engine = Task.Run(() => Waits.RunEngine(wait));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (!engine.IsCompleted && DateTime.UtcNow < deadline)
            {
                Audio.Clock.Advance(0.016);
                Waits.RunFrameTurn(null, null, null, 4, out _);
                Thread.Sleep(1);
            }
            Assert.True(engine.Wait(TimeSpan.FromSeconds(10)), "the wait did not end");
        }

        public void Dispose() => _locator.Dispose();
    }

    // ---- the input routing (InputAndWaits.Events.cs) ----

    [Fact]
    public void UnderClickOnUpAPressOnlyUpdatesTheButtonState()
    {
        var interpreter = new FakeInterpreter { MouseAction = InputBindings.AdvanceAction };
        InputAndWaits waits = RoutedWaits(interpreter, clickOnUp: true);

        waits.MouseButton(0, pressed: true, 10, 20);
        Assert.Equal(["pointer 10,20", "button 1 down", "physical 0 down"], interpreter.Calls);
    }

    [Fact]
    public void AClickWhileNothingPollsStaysInTheLatch()
    {
        var interpreter = new FakeInterpreter { MouseAction = InputBindings.AdvanceAction, Polling = false };
        InputAndWaits waits = RoutedWaits(interpreter, clickOnUp: true);

        waits.MouseButton(0, pressed: false, 10, 20);
        Assert.Equal("hold 0x10", interpreter.Calls[^1]);
    }

    [Fact]
    public void AClickAtAPollingWaitActivatesTheHotspotUnderThePointer()
    {
        var interpreter = new FakeInterpreter
        {
            MouseAction = InputBindings.AdvanceAction, Polling = true, ActivatesPointer = true,
        };
        InputAndWaits waits = RoutedWaits(interpreter, clickOnUp: false);

        waits.MouseButton(0, pressed: true, 10, 20);
        Assert.Equal(["actions 0x10", "activate 10,20"], interpreter.Calls.TakeLast(2));
    }

    [Fact]
    public void AnAdvanceKeyAtAPollingWaitEndsTheWait()
    {
        var interpreter = new FakeInterpreter { KeyAction = InputBindings.AdvanceAction, Polling = true };
        InputAndWaits waits = RoutedWaits(interpreter, clickOnUp: false);
        Task wait = Task.Run(() => waits.RunEngine(
            () => waits.WaitAtAdvInputPoll(static () => false, static () => default, textPage: true)));
        Assert.True(SpinWait.SpinUntil(() => waits.IsWaiting, TimeSpan.FromSeconds(10)));

        waits.Key(0x0d, pressed: false);   // a release raises nothing
        Assert.False(wait.Wait(50));
        waits.Key(0x0d, pressed: true);    // the advance reaches the wait on the engine thread
        Assert.True(wait.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, waits.Pages);
    }

    // ---- the engine thread and the front end's (InputAndWaits.Turns.cs) ----

    [Fact]
    public void InputFromTheFrontEndIsRoutedOnTheEngineThread()
    {
        var interpreter = new FakeInterpreter { MouseAction = InputBindings.AdvanceAction, Polling = false };
        InputAndWaits waits = RoutedWaits(interpreter, clickOnUp: false);
        using var engine = EngineThread.Start(waits);

        waits.MouseButton(0, pressed: true, 10, 20);
        waits.Invoke(static () => { });   // input queued before a request is routed before it runs
        Assert.Equal("hold 0x10", interpreter.Calls[^1]);
        Assert.All(interpreter.Threads, thread => Assert.Equal(engine.ThreadId, thread));
    }

    [Fact]
    public void PointerMovesNotYetTakenCoalesceAndKeepTheirPlaceAmongTheOtherInput()
    {
        var interpreter = new FakeInterpreter { MouseAction = InputBindings.AdvanceAction, Polling = false };
        InputAndWaits waits = RoutedWaits(interpreter, clickOnUp: false);
        using var busy = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        Task engine = Task.Run(() => waits.RunEngine(() =>
        {
            started.Set();
            busy.Wait();          // inside an instruction: nothing pumps
            waits.FrameYield();   // the instruction boundary takes the front end's messages
        }));
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));

        waits.PointerMoved(1, 1);
        waits.PointerMoved(2, 2);
        waits.MouseButton(0, pressed: true, 2, 2);
        waits.PointerMoved(3, 3);
        waits.PointerMoved(4, 4);
        busy.Set();
        Assert.True(engine.Wait(TimeSpan.FromSeconds(10)));

        Assert.Equal(["pointer 2,2", "pointer 2,2", "button 1 down", "physical 0 down", "hold 0x10", "pointer 4,4"],
                     interpreter.Calls);
    }

    [Fact]
    public void InvokeRunsOnTheEngineThreadAndThrowsWhatItThrew()
    {
        InputAndWaits waits = RoutedWaits(new FakeInterpreter(), clickOnUp: false);
        Assert.Equal(Environment.CurrentManagedThreadId, waits.Invoke(static () => Environment.CurrentManagedThreadId));

        using var engine = EngineThread.Start(waits);
        Assert.Equal(engine.ThreadId, waits.Invoke(static () => Environment.CurrentManagedThreadId));
        var error = Assert.Throws<InvalidOperationException>(
            () => waits.Invoke<int>(static () => throw new InvalidOperationException("from the engine thread")));
        Assert.Equal("from the engine thread", error.Message);
    }

    [Fact]
    public void TryInvokeGivesUpOnAnEngineThreadThatDoesNotAnswer()
    {
        InputAndWaits waits = RoutedWaits(new FakeInterpreter(), clickOnUp: false);
        using var stalled = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        Task engine = Task.Run(() => waits.RunEngine(() =>
        {
            started.Set();
            stalled.Wait();   // an engine thread stuck outside its waits
        }));
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        bool ran = false;
        Assert.False(waits.TryInvoke(() => ran = true, 50, out bool _));
        stalled.Set();
        Assert.True(engine.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(ran);   // given up, it never runs: not even when the engine thread drains its requests
    }

    [Fact]
    public void AFrameTurnInsideABurstPresentsNothingAndAtAWaitPresentsTheBurst()
    {
        InputAndWaits waits = RoutedWaits(new FakeInterpreter(), clickOnUp: false, out Graphics graphics);
        bool burstRuns = true, waitEnds = false;
        using var inBurst = new ManualResetEventSlim(false);
        using var waiting = new ManualResetEventSlim(false);
        Task engine = Task.Run(() => waits.RunEngine(() =>
        {
            waits.EnterScriptContext("SC0000.BIN");
            graphics.RequestPresentation();
            inBurst.Set();
            while (Volatile.Read(ref burstRuns)) waits.FrameYield();   // the burst's instructions
            waits.WaitForServiceTrigger(() =>
            {
                waiting.Set();
                return Volatile.Read(ref waitEnds);
            });
            waits.ExitScriptContext("SC0000.BIN");
        }));

        // At an instruction boundary the turn is answered at once, and the frame due is not drawn: the burst's state
        // is not the screen's yet.
        Assert.True(inBurst.Wait(TimeSpan.FromSeconds(10)));
        Assert.Null(waits.RunFrameTurn(null, null, null, 10_000, out bool late));
        Assert.False(late);

        Volatile.Write(ref burstRuns, false);
        Assert.True(waiting.Wait(TimeSpan.FromSeconds(10)));
        var lowered = new List<int>();
        Assert.NotNull(waits.RunFrameTurn(null, null, () => lowered.Add(Environment.CurrentManagedThreadId), 10_000,
                                          out late));
        Assert.False(late);
        Assert.Single(lowered);
        Assert.NotEqual(Environment.CurrentManagedThreadId, lowered[0]);   // lowered on the engine thread

        Volatile.Write(ref waitEnds, true);
        Assert.True(engine.Wait(TimeSpan.FromSeconds(10)));
        // With no engine thread the front end's turn runs where it is.
        graphics.RequestPresentation();
        Assert.NotNull(waits.RunFrameTurn(null, null, null, 0, out late));
    }

    [Fact]
    public void AStopFromTheFrontEndEndsTheEnginesWait()
    {
        InputAndWaits waits = RoutedWaits(new FakeInterpreter(), clickOnUp: false);
        Task engine = Task.Run(() => waits.RunEngine(() => waits.WaitForServiceTrigger(static () => false)));
        Assert.False(engine.Wait(100));
        waits.Stop();
        Assert.True(engine.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(waits.StopAndReload.StopRequested);
    }

    // The native over headless natives, routing to the interpreter given.
    private static InputAndWaits RoutedWaits(IInterpreterInput interpreter, bool clickOnUp)
        => RoutedWaits(interpreter, clickOnUp, out _);

    private static InputAndWaits RoutedWaits(IInterpreterInput interpreter, bool clickOnUp, out Graphics graphics)
    {
        var clock = new FrameClock();
        SkipAndAuto skip = SkipAndAuto.WithSystemClock();
        Audio audio = Audio.Silent(skip);
        graphics = Graphics.Headless();
        var waits = new InputAndWaits(skip, audio, new EngineTick(audio), graphics, Movies.Headless(graphics),
                                      ShellServices.Headless(), TextManager.Headless(graphics, skip), clock,
                                      new PageLocatorState("SC0000", null), synchronizeExplicitPresentation: false)
        {
            ClickOnUp = clickOnUp,
        };
        waits.RouteInputTo(interpreter);
        return waits;
    }

    /// <summary>An engine thread parked in a wait that pumps the front end's messages, until disposed.</summary>
    private sealed class EngineThread : IDisposable
    {
        private readonly InputAndWaits _waits;
        private readonly Task _task;
        private volatile bool _stop;
        private int _threadId;

        private EngineThread(InputAndWaits waits)
        {
            _waits = waits;
            using var started = new ManualResetEventSlim(false);
            _task = Task.Run(() => waits.RunEngine(() =>
            {
                _threadId = Environment.CurrentManagedThreadId;
                started.Set();
                waits.WaitForServiceTrigger(() => _stop);
            }));
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        }

        public int ThreadId => _threadId;

        public static EngineThread Start(InputAndWaits waits) => new(waits);

        public void Dispose()
        {
            _stop = true;
            _waits.PulseFrame();
            Assert.True(_task.Wait(TimeSpan.FromSeconds(10)));
        }
    }

    /// <summary>Keeps what the routing asks, in order, and the thread that asked; each binding raises the action it is
    /// given.</summary>
    private sealed class FakeInterpreter : IInterpreterInput
    {
        private readonly List<string> _calls = [];
        private readonly List<int> _threads = [];

        public List<string> Calls { get { lock (_calls) return [.. _calls]; } }
        public List<int> Threads { get { lock (_calls) return [.. _threads]; } }

        private void Record(string call)
        {
            lock (_calls)
            {
                _calls.Add(call);
                _threads.Add(Environment.CurrentManagedThreadId);
            }
        }

        public int MouseAction { get; init; } = -1;
        public int KeyAction { get; init; } = -1;
        public bool Polling { get; init; }
        public bool ActivatesPointer { get; init; }

        public void UpdatePointer(int x, int y) => Record($"pointer {x},{y}");
        public void UpdateMouseButtonState(int bit, bool pressed) => Record($"button {bit} {(pressed ? "down" : "up")}");
        public int UpdatePhysicalMouseButtonState(int physicalButton, bool pressed)
        {
            Record($"physical {physicalButton} {(pressed ? "down" : "up")}");
            return MouseAction;
        }
        public int UpdateKeyboardVirtualKeyState(int virtualKey, bool pressed) => KeyAction;
        public int UpdateJoystickButtonState(int physicalButton, bool pressed) => 0;
        public void UpdateJoystickAxisState(int axis, double value) { }
        public MouseWheelOutcome ApplyMouseWheel(int delta, bool advanceMessageOnWheel, bool textRevealing)
            => MouseWheelOutcome.Dropped;
        public MouseWheelOutcome ApplyMouseHorizontalWheel(int delta)
        {
            Record($"hwheel {delta}");
            return MouseWheelOutcome.Accumulated;
        }
        public bool IsRawInputCallbackActive => false;
        public bool IsAdvTextWaitPolling => Polling;
        public bool TryLatchAdvCoroutineYieldTrigger(int actionMask) => false;
        public bool TryLatchMessageSkipCancelClick() => false;
        public bool TryActivateInputActions(int actionMask)
        {
            Record($"actions 0x{actionMask:x}");
            return false;
        }
        public bool TryActivatePointer(int x, int y)
        {
            Record($"activate {x},{y}");
            return ActivatesPointer;
        }
        public bool TryRequestAdvWaitCoroutineYield() => false;
        public void HoldTriggers(int actionMask) => Record($"hold 0x{actionMask:x}");
    }

    // Frames as the front end makes them: move the clock 16 ms and wake the engine thread, until disposed.
    private static IDisposable Frames(InputAndWaits waits, AudioRig rig)
    {
        var stop = new CancellationTokenSource();
        Task pump = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                rig.Clock.Advance(0.016);
                waits.PulseFrame();
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
