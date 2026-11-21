using System;
using System.Collections.Generic;
using System.Threading;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

/// <summary>
/// Native AGE's waits and the input that ends them (engine layering spec §5.2, "Input and waits"; milestone 11). A wait
/// blocks the engine thread while native's main loop would run its services with the interpreter parked: a text wait
/// polling input (run-state 0x80000000), a reveal (0x20000000), a sleep (1), a timed-callback deadline (0x40), an
/// effect or screen transition (0x400), a synchronized presentation, the hidden-window wait (0x4000000), the modal movie
/// (0x2000), and the modal waits under a dialog or a front-end modal hold, which run nothing.
/// </summary>
/// <remarks>
/// <para>The engine thread runs native's main loop here (spec §5.3, part 2; <c>InputAndWaits.Turns.cs</c>): every wait
/// that is not modal runs the engine tick each time it wakes (<c>InputAndWaits.Blocking.cs</c> is the one place that
/// blocks), each wait serves the front end's frame turn before it blocks and presents where native would render, and
/// the front end's input (<c>InputAndWaits.Events.cs</c>) and requests reach the engine thread where native pumps its
/// window messages.</para>
/// </remarks>
public sealed partial class InputAndWaits : IFastForwardListener
{
    private readonly SkipAndAuto _skip;
    private readonly Audio _audio;          // ADV Auto waits for its voice
    private readonly EngineTick _tick;      // every wait that is not modal runs it; presentation waits for its block
    private readonly Graphics _graphics;    // the presentation waits block on its publication and transitions
    private readonly Movies _movies;        // op 0x20f's modal movie, and the movies that hold an effect wait
    private readonly ShellServices _shell;  // the dialogs a modal wait waits on
    private readonly TextManager _text;     // the reveal wait, the page's wait indicator
    private readonly FrameClock _clock;
    private readonly PageLocatorState _locator;
    private readonly TimelineLog? _timeline;

    private readonly SemaphoreSlim _gate = new(0, 1);                       // a click or Auto ends a text wait
    private readonly AutoResetEvent _inputCallbackSignal = new(false);      // input arrived for a wait's callbacks
    private readonly AutoResetEvent _frameSignal = new(false);   // the engine thread's wake: a frame turn, input, a request
    private readonly WaitHandle[] _blockHandles = new WaitHandle[2];
    // A stop, or a return to title from outside the script (the 終了 dialog's タイトルに戻る): every wait ends, until the
    // reload's scene reset takes it. The interpreter reads the same requests.
    private readonly StopAndReload _stopAndReload = new();
    private bool Stopping => _stopAndReload.StopRequested || _stopAndReload.ReloadRequested;
    private volatile int _pages;   // engine-thread page counter, raised before the wait shows as waiting
    private volatile int _pageNotices;

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5).
    private sealed class Scene
    {
        public volatile bool Waiting;
        public volatile bool TransitionWaiting;
        public volatile bool Sleeping;
        public volatile bool PresentingFrame;   // waiting for the front end to take a presented frame
        public volatile bool ModalMovieWaiting;
        public bool ModalMovieCancelled;
        public bool ModalMovieCancelPending;   // CancelNextModalMovie: a held action 4 stops the next movie
        public GfxState? ForegroundGfx;
        public long TransitionStartedAtMs = -1;
    }

    // Volatile: the front end reads the wait flags from its own thread (spec §5.3's table).
    private volatile Scene _scene = new();

    /// <param name="skipAndAuto">The skip and auto native: fast-forward ends a text wait and a sleep, and starting it
    /// finishes the reveal and wakes every wait.</param>
    /// <param name="audio">The audio native: ADV Auto waits for its voice.</param>
    /// <param name="tick">The engine tick: every wait that is not modal runs it each time it wakes, and presentation
    /// is withheld while its statement block is open.</param>
    /// <param name="graphics">The graphics native: the presentation waits block on its publication.</param>
    /// <param name="movies">The movie native: op 0x20f's modal wait plays over it, and an effect wait holds while one
    /// of its movies does.</param>
    /// <param name="shell">The shell-services native: the modal waits wait on its dialogs, a stop cancels them, and its
    /// menu requests wake a text wait.</param>
    /// <param name="text">The text native: the reveal wait services its reveal, and a click, a stop or fast-forward
    /// finishes it.</param>
    /// <param name="clock">The engine clock the waits time against.</param>
    /// <param name="locator">The page locator: each text wait records its page.</param>
    /// <param name="timeline">The --timeline-log, which records each wait's start and end.</param>
    /// <param name="synchronizeExplicitPresentation">False lets a presentation request return before a frame takes
    /// it (the self-test and the tests, which have no frames).</param>
    /// <param name="settings">SYS4REG.INI, which the input routing reads on each trigger (message:AdvanceMesOnWheel,
    /// system:EffectSkipOnClick).</param>
    /// <param name="cursors">The resources the AGE cursor's CUR records come from (ops 0x86 and 0x87); without them a
    /// script's cursor is not shown.</param>
    /// <param name="cursor">The front end's cursor device, which shows and moves the AGE cursor.</param>
    public InputAndWaits(SkipAndAuto skipAndAuto, Audio audio, EngineTick tick, Graphics graphics, Movies movies,
                         ShellServices shell, TextManager text, FrameClock clock, PageLocatorState locator,
                         TimelineLog? timeline = null, bool synchronizeExplicitPresentation = true,
                         Persistence.Sys4RegIniStore? settings = null, Sys4.ResourceMap? cursors = null,
                         Devices.ICursorDevice? cursor = null)
    {
        _settings = settings;
        _cursors = cursors;
        _cursor = cursor;
        _skip = skipAndAuto ?? throw new ArgumentNullException(nameof(skipAndAuto));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _movies = movies ?? throw new ArgumentNullException(nameof(movies));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _timeline = timeline;
        _synchronizeExplicitPresentation = synchronizeExplicitPresentation;
        _blockHandles[1] = _frameSignal;
        _stopAndReload.WakeWith(() => Post(WakeAllWaits));   // the front end asks; the engine thread ends its waits
        _skip.Listen(this);
        _movies.WakeWaitsWith(QueueWork);   // its frame service's frames and completions wait for the engine thread
        _shell.WakeWaitsWith(WakeInputCallbackService);   // a menu request reaches a text wait at once
        _text.ReadInputWaitFrom(() => _scene.Waiting);           // the wait indicator shows only in an input wait
    }

    /// <summary>A text wait or op 0xfa's poll waits for input (native run-state 0x80000000).</summary>
    public bool IsWaiting => _scene.Waiting;
    /// <summary>An effect or a screen transition parks the engine thread (run-state 0x400).</summary>
    public bool IsTransitionWaiting => _scene.TransitionWaiting;
    /// <summary>A script sleep (op 0xc8) parks the engine thread.</summary>
    public bool IsSleeping => _scene.Sleeping;
    /// <summary>The run's text pages so far, raised as each text wait starts; the driver and the front end read it (a
    /// new page clears the front end's status line).</summary>
    public int Pages => _pages;
    /// <summary>Raised when a text wait starts its page and when it ends it, and at a scene reset: the front end clears
    /// its status line when it changes.</summary>
    public int PageNotices => _pageNotices;

    // Engine thread: a page ended or the scene was reset.
    private void PostPageNotice() => _pageNotices++;

    /// <summary>What the waits and the natives they block on are doing, for the driver's state and the front end's
    /// stall diagnostic. Read on the engine thread.</summary>
    public WaitsDiagnosticSnapshot CaptureDiagnosticSnapshot()
        => new(
            _scene.Waiting,
            _scene.TransitionWaiting,
            _scene.Sleeping,
            _scene.PresentingFrame,
            _text.IsRevealing,
            _scene.ModalMovieWaiting,
            _text.IsAdvPagePresentationSuspended,
            _skip.FastForwardActive,
            _graphics.ScreenTransitionPending,
            TransitionStartedAtMs,
            _movies.SurfaceDiagnostics(),
            _movies.CompletedPlaybackIds());

    /// <summary>The front end, once per rendered frame when it asks for no frame turn (the self-test): wakes a wait
    /// parked on a frame, which then runs the engine tick.</summary>
    public void PulseFrame() => _frameSignal.Set();

    /// <summary>The stop and return-to-title requests, one copy for the waits and the interpreter (the VM reads them
    /// when it is built with this native).</summary>
    public StopAndReload StopAndReload => _stopAndReload;

    /// <summary>End every wait now and keep them ended: the engine is stopping.</summary>
    public void Stop() => _stopAndReload.RequestStop();

    /// <summary>A return to title from outside the script, for an interpreter that does not share
    /// <see cref="StopAndReload"/>: end every wait now, so it reaches an opcode boundary. Waits stay ended until the
    /// reload's scene reset. An interpreter built with this native asks through the shared copy instead.</summary>
    public void InterruptForRootReload() => _stopAndReload.RequestReload();

    // Posted by a request from the front end's thread, so it runs at the engine thread's next pump. The interpreter
    // reads the flags at each instruction boundary before it pumps, so a reload's scene reset can come first: then the
    // request was taken and the new root's waits must not end.
    private void WakeAllWaits()
    {
        if (!_stopAndReload.StopRequested && !_stopAndReload.ReloadRequested) return;
        _text.CompleteReveal();
        if (_gate.CurrentCount == 0) _gate.Release();
        _inputCallbackSignal.Set();
        _frameSignal.Set();
        _shell.CancelDialogs();   // an answer that comes later is dropped, so it cannot answer the next dialog
    }

    /// <summary>Op 0x9's scene reset (scene_context_init_reset): no wait is left, a reload's interrupt ends, and the
    /// backbuffer publishes everything again over a clean frame.</summary>
    public void ResetScene()
    {
        _stopAndReload.ReloadTaken();   // the interpreter took it at the reset's start; a host without one takes it here
        // scene_context_init_reset deletes every surface's movie object (the VM has had the movie native do it) but
        // keeps the surfaces, decoded asset caches and process-owned audio/configuration for the reloaded SYSTEM4 root.
        // The VM releases the surfaces afterwards when set:AutoFreeTex asks (docs/engine-re.md, "AutoFreeTex"). A
        // pending screen transition goes, and a render target's snapshot stays with its surface.
        _graphics.ResetScene();   // the VM has had the text native reset its layouts (TextManager.ResetScene)
        while (_gate.Wait(0)) { }
        _inputCallbackSignal.WaitOne(0);
        _scene = new();
        _graphics.RequestPresentation();
        PostPageNotice();
        _timeline?.State("scene-context-reset", new() { ["surfaces"] = _graphics.LiveSurfaceSlots() });
    }

    /// <summary>What a change of run-state 0x08000000 does to the waits (the skip and auto native tells each time a
    /// channel is set): starting it finishes the reveal and wakes the waits. (The audio native hears the end itself and
    /// plays the voice fast-forward held back.)</summary>
    void IFastForwardListener.FastForwardChannelSet(FastForwardChannel channel, bool value, bool active, bool changed)
    {
        if (!changed) return;
        _timeline?.State("message-skip", new()
        {
            ["enabled"] = active,
            ["source"] = channel == FastForwardChannel.Physical ? "logical-action-6" : "script",
        });
        if (active)
        {
            // Not the effect wait in progress: native's main tick services run-state 0x400 (and 8) ahead of
            // fast-forward, and those services poll only the EffectSkipOnClick click. Fast-forward reaches the next
            // 0x21c wait (WaitForForegroundTransition).
            _text.CompleteReveal();
            _frameSignal.Set();
            _inputCallbackSignal.Set();
        }
    }
}

/// <summary>The waits' state for a diagnostic (<see cref="InputAndWaits.CaptureDiagnosticSnapshot"/>).
/// <see cref="IsPresentingFrame"/> is true while the engine thread waits for the front end to take a frame it presented
/// (op 0x20c, or a sleep's synchronized present).</summary>
public sealed record WaitsDiagnosticSnapshot(
    bool IsInputWaiting, bool IsTransitionWaiting, bool IsSleeping, bool IsPresentingFrame,
    bool IsTextRevealing, bool IsModalMovieWaiting, bool IsAdvPagePresentationSuspended,
    bool IsMessageSkipActive, bool IsScreenTransitionActive, long TransitionStartedAtMs,
    IReadOnlyList<MovieSurfaceDiagnostic> MovieSurfaces, IReadOnlyList<long> CompletedMoviePlaybackIds);
