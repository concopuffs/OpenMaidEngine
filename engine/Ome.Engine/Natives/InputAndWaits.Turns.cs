using System;
using System.Collections.Generic;
using System.Threading;
using Ome.Engine.Devices;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;

namespace Ome.Engine.Natives;

// The engine thread and the front end's thread (engine layering spec §5.3, part 2). The engine thread runs the
// interpreter, its waits and every native, as native AGE's main thread does. The front end's thread queues the input
// its devices deliver (InputAndWaits.Events.cs), asks for one frame turn each display frame and draws the frame the
// turn published (RunFrameTurn), and sends what else it needs of the engine as requests the engine thread runs
// (Invoke). The engine thread takes them where native's main loop pumps its messages: at each instruction boundary
// (FrameYield) and each time a wait wakes (InputAndWaits.Blocking.cs). It answers a frame turn at the same places, and
// presents in it only where native would render, outside a script's burst (InputAndWaits.Presentation.cs); at an
// instruction boundary inside a burst the turn ends with nothing to draw, as the burst's state is not yet the screen's.
//
// One lock joins the two threads, _turnLock. It guards which thread is the engine's (_engineThreadId), the turn's
// handshake (_turn and the diagnostics it carries), the published frame (_published) and the request queue
// (_requests). Nothing is run under it but a turn or a request that the front end runs itself while no engine thread
// runs (before it starts and after it returns), so the engine thread cannot start in the middle of one.
public sealed partial class InputAndWaits
{
    private enum TurnState { None, Requested, Started }

    private readonly object _turnLock = new();
    private int _engineThreadId;                   // the engine thread's managed id, 0 while none runs
    private TurnState _turn;
    private volatile bool _turnRequested;          // the engine's fast check: _turn is Requested
    private bool _turnAbandoned;                   // the front end stopped waiting: the turn's frame comes late
    private PerformanceFrameLog? _turnPerf;        // the asking frame's record, while the front end waits for the turn
    private Dictionary<long, string>? _turnDecisions;
    private Action? _turnLowering;                 // the front end's note at the lowering (its perf coordinate)
    private FrameDescription? _published;          // lowered by a turn and not yet taken
    private bool _publishedLate;                   // lowered by a turn the front end had stopped waiting for
    private readonly Queue<Request> _requests = new();
    private int _workQueued;                       // 1: a request, input or a movie's frame waits for the engine thread

    /// <summary>
    /// Run <paramref name="engine"/>, the interpreter's run, with this thread as the engine thread: until it returns, the
    /// front end's frame turns and requests wait for this thread. The requests still queued when it returns run here.
    /// </summary>
    public void RunEngine(Action engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        lock (_turnLock)
        {
            if (_engineThreadId != 0) throw new InvalidOperationException("An engine thread already runs.");
            _engineThreadId = Environment.CurrentManagedThreadId;
        }
        try
        {
            engine();
        }
        finally
        {
            lock (_turnLock)
            {
                RunRequests();
                _engineThreadId = 0;
                // A turn asked for and not started: the front end's next turn presents itself.
                if (_turn == TurnState.Requested) EndTurnLocked();
                Monitor.PulseAll(_turnLock);
            }
        }
    }

    /// <summary>
    /// The front end's thread, once a display frame: give the engine thread its frame turn, waiting for it at most
    /// <paramref name="timeoutMs"/>. The engine thread answers at its next instruction boundary or wait; in the turn it
    /// takes the front end's messages and, where native would render and a frame is due, presents. The frame it
    /// published is returned to draw, or null. A turn the engine thread has not started when the time is up stays asked
    /// for, without this frame's diagnostics, and its frame comes with a later turn (<paramref name="late"/>); one that
    /// has started is waited for, since it records into <paramref name="perf"/>. While no engine thread runs (before
    /// the interpreter starts and after it ends) the turn runs here.
    /// </summary>
    /// <param name="perf">This frame's <c>--perf-log</c> record: the presentation reasons and the lowering.</param>
    /// <param name="decisions">This frame's <c>--gfx-log</c> decisions, or null.</param>
    /// <param name="lowering">Run on the engine thread just before a lowering (the front end notes its coordinate).</param>
    /// <param name="late">The frame was lowered in an earlier turn, without this frame's diagnostics.</param>
    public FrameDescription? RunFrameTurn(PerformanceFrameLog? perf, Dictionary<long, string>? decisions,
                                          Action? lowering, int timeoutMs, out bool late)
    {
        lock (_turnLock)
        {
            // A turn the front end stopped waiting for may be lowering: its frame is the one to take.
            while (_turn == TurnState.Started) Monitor.Wait(_turnLock);
            if (_engineThreadId == 0)
            {
                PumpMessages();
                PresentTurn(perf, decisions, lowering, late: false);
                return TakePublishedLocked(out late);
            }
            _turn = TurnState.Requested;
            _turnAbandoned = false;
            _turnPerf = perf;
            _turnDecisions = decisions;
            _turnLowering = lowering;
            _turnRequested = true;
        }
        _frameSignal.Set();
        long deadline = Environment.TickCount64 + Math.Max(0, timeoutMs);
        lock (_turnLock)
        {
            while (_turn != TurnState.None)
            {
                if (_turn == TurnState.Started)
                {
                    Monitor.Wait(_turnLock);
                    continue;
                }
                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                {
                    _turnAbandoned = true;
                    _turnPerf = null;
                    _turnDecisions = null;
                    _turnLowering = null;
                    break;
                }
                Monitor.Wait(_turnLock, (int)remaining);
            }
            return TakePublishedLocked(out late);
        }
    }

    private FrameDescription? TakePublishedLocked(out bool late)
    {
        FrameDescription? frame = _published;
        late = frame != null && _publishedLate;
        _published = null;
        _publishedLate = false;
        return frame;
    }

    private void EndTurnLocked()
    {
        _turn = TurnState.None;
        _turnRequested = false;
        _turnAbandoned = false;
        _turnPerf = null;
        _turnDecisions = null;
        _turnLowering = null;
    }

    /// <summary>The engine thread, at an instruction boundary or a wait: answer the frame turn the front end asked for.
    /// True when one was answered, so a wait looks at its state again before it blocks.</summary>
    private bool ServeFrameTurn()
    {
        if (!_turnRequested) return false;
        PerformanceFrameLog? perf;
        Dictionary<long, string>? decisions;
        Action? lowering;
        bool late;
        lock (_turnLock)
        {
            if (_turn != TurnState.Requested) return false;
            _turn = TurnState.Started;
            _turnRequested = false;
            perf = _turnPerf;
            decisions = _turnDecisions;
            lowering = _turnLowering;
            late = _turnAbandoned;
        }
        try
        {
            PumpMessages();
            PresentTurn(perf, decisions, lowering, late);
        }
        finally
        {
            lock (_turnLock)
            {
                EndTurnLocked();
                Monitor.PulseAll(_turnLock);
            }
        }
        return true;
    }

    // The turn's presentation: if this is where native would render, the reasons a frame is due, and its lowering. A
    // frame published and not yet taken holds the next one back, with its reasons pending, so no publication is lost
    // (a partial one adds layers to the frame before it).
    private void PresentTurn(PerformanceFrameLog? perf, Dictionary<long, string>? decisions, Action? lowering,
                             bool late)
    {
        long phase = perf != null ? PerformanceFrameLog.Timestamp() : 0;
        bool held;
        lock (_turnLock) held = _published != null;
        if (held || !Presentable)
        {
            perf?.RecordPresentationReasons(0);
            perf?.RecordShouldRecomposite(PerformanceFrameLog.Timestamp() - phase);
            return;
        }
        long allocated = perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        HostPresentationReason reasons = ConsumePresentationReasons();
        perf?.RecordPresentationReasons((int)reasons);
        perf?.RecordShouldRecomposite(PerformanceFrameLog.Timestamp() - phase);
        if (reasons != HostPresentationReason.None)
        {
            lowering?.Invoke();
            FrameDescription frame = _graphics.LowerFrame(_clock.NowMs, perf, decisions);
            lock (_turnLock)
            {
                _published = frame;
                _publishedLate = late;
            }
        }
        perf?.RecordRecomposeAllocation(PerformanceFrameLog.AllocatedBytes() - allocated);
    }

    /// <summary>
    /// Run <paramref name="work"/> on the engine thread, at its next instruction boundary or wait, and return its
    /// result: how the front end reads and changes engine state. It runs at once when called on the engine thread, or
    /// while no engine thread runs. The calling thread blocks until it has run; an exception it throws is thrown here.
    /// </summary>
    public T Invoke<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!TryInvoke(work, Timeout.Infinite, out T result)) throw new InvalidOperationException("unreachable");
        return result;
    }

    /// <summary><see cref="Invoke{T}(Func{T})"/> for work with no result.</summary>
    public void Invoke(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Invoke<bool>(() =>
        {
            work();
            return true;
        });
    }

    /// <summary>
    /// <see cref="Invoke{T}(Func{T})"/>, giving up after <paramref name="timeoutMs"/> if the engine thread has not
    /// started the work by then (a diagnostic of an engine thread that has stopped answering). False when it gave up;
    /// the work then never runs.
    /// </summary>
    public bool TryInvoke<T>(Func<T> work, int timeoutMs, out T result)
    {
        ArgumentNullException.ThrowIfNull(work);
        Request? request = null;
        lock (_turnLock)
        {
            if (_engineThreadId == 0)
            {
                result = work();
                return true;
            }
            if (_engineThreadId != Environment.CurrentManagedThreadId)
            {
                request = new Request(() => work());
                _requests.Enqueue(request);
            }
        }
        if (request == null)
        {
            result = work();
            return true;
        }
        QueueWork();
        if (!request.Done.Wait(timeoutMs))
        {
            lock (_turnLock)
            {
                if (!request.Started)
                {
                    request.Cancelled = true;
                    result = default!;
                    return false;
                }
            }
            request.Done.Wait();
        }
        request.Done.Dispose();
        if (request.Error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(request.Error).Throw();
        result = (T)request.Result!;
        return true;
    }

    /// <summary>Run <paramref name="work"/> on the engine thread at its next instruction boundary or wait, without
    /// waiting for it: at once when called on the engine thread or while none runs.</summary>
    private void Post(Action work)
    {
        Request? request = null;
        lock (_turnLock)
        {
            if (_engineThreadId == 0)
            {
                work();
                return;
            }
            if (_engineThreadId != Environment.CurrentManagedThreadId)
            {
                request = new Request(() =>
                {
                    work();
                    return null;
                });
                _requests.Enqueue(request);
            }
        }
        if (request == null)
        {
            work();
            return;
        }
        QueueWork();
    }

    /// <summary>The engine thread, where native's main loop pumps its messages: the front end's queued input, then its
    /// requests (so a request sees the input sent before it), then the frames and completions its movie service took.
    /// </summary>
    private void PumpMessages()
    {
        // The exchange is a full fence: whatever was queued before the flag was raised is seen below, and what is queued
        // after raises it again.
        if (Volatile.Read(ref _workQueued) == 0 || Interlocked.Exchange(ref _workQueued, 0) == 0) return;
        RouteQueuedInput();
        RunRequests();
        _movies.PublishServiced();
    }

    private void RunRequests()
    {
        while (true)
        {
            Request? request;
            lock (_turnLock)
            {
                if (!_requests.TryDequeue(out request)) return;
                if (request.Cancelled) continue;
                request.Started = true;
            }
            request.Run();
        }
    }

    /// <summary>Something waits for the engine thread (a request, input, a movie's frame): wake it.</summary>
    private void QueueWork()
    {
        Volatile.Write(ref _workQueued, 1);
        _frameSignal.Set();
    }

    private sealed class Request(Func<object?> work)
    {
        public readonly ManualResetEventSlim Done = new(false);
        public bool Started;     // under _turnLock
        public bool Cancelled;   // under _turnLock
        public object? Result;
        public Exception? Error;

        public void Run()
        {
            try { Result = work(); }
            catch (Exception error) { Error = error; }
            finally { Done.Set(); }
        }
    }
}
