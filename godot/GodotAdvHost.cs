using System.Collections.Generic;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;

public sealed class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly ResourceMap _res;
    private readonly string _scene;                       // e.g. "SC0000" — for section_base
    private readonly Dictionary<int, string?> _slotBmp = new();          // slot -> pre-converted BMP path
    // slot -> dims. Slot 0 is the primary/screen surface (800x600), normally created at engine boot which
    // the single-scene harness skips; seed it so the first CG's anchor math stays correct (not 0x0).
    private readonly Dictionary<int, (int W, int H)> _slotDims = new() { { 0, (800, 600) } };
    private readonly SemaphoreSlim _gate = new(0, 1);
    private readonly Age.Engine.Hosting.FrameClock _clock;
    private readonly Age.Engine.Hosting.WallClockOpPacer _opPacer;
    private readonly GodotTimelineLog? _timeline;
    private readonly System.Threading.AutoResetEvent _frameSignal = new(false);
    private volatile bool _stopping;
    private GfxState? _foregroundGfx;
    public volatile bool IsWaiting;
    public volatile bool IsTransitionWaiting;
    private long _transitionStartedAtMs = -1;
    public long TransitionStartedAtMs => System.Threading.Interlocked.Read(ref _transitionStartedAtMs);
    public readonly List<(int Offset, string Text)> Captured = new();

    public GodotAdvHost(Main main, ResourceMap res, string scene, Age.Engine.Hosting.FrameClock clock,
                        GodotTimelineLog? timeline = null)
    {
        _main = main; _res = res; _scene = scene; _clock = clock;
        _timeline = timeline;
        _opPacer = new Age.Engine.Hosting.WallClockOpPacer(clock);
    }

    public void ShowText(int offset, string text)
    {
        Captured.Add((offset, text));
        _main.CallDeferred("AppendLine", text);
    }

    public volatile int Pages;   // VM-thread page counter (incremented before IsWaiting so shot-gating can't race)

    public void WaitForInput()
    {
        Pages++;
        _main.CallDeferred("PageBreak");
        IsWaiting = true;
        _timeline?.State("input-wait", new() { ["page"] = Pages });
        _gate.Wait();
        IsWaiting = false;
        _timeline?.State("running", new() { ["input"] = "auto-or-user" });
        _opPacer.Reset();
        _main.CallDeferred("ClearPage");
    }

    // Called from the main thread (click) or an auto-clicker. A transition click is consumed by
    // the foreground lifecycle; it never pre-arms or advances the following stable input wait.
    public void SignalInput()
    {
        if (IsTransitionWaiting && _foregroundGfx != null)
        {
            int completed = _foregroundGfx.CompleteForegroundTransitions(_clock.NowMs);
            if (completed > 0)
            {
                _timeline?.State("transition-forced-complete", new() { ["count"] = completed });
                _frameSignal.Set();
                return;
            }
        }
        if (IsWaiting && _gate.CurrentCount == 0) _gate.Release();
    }

    public void WaitForForegroundTransition(GfxState gfx)
    {
        int started = gfx.StartForegroundTransitions(_clock.NowMs);
        if (started == 0 && !gfx.HasActiveForegroundTransitions(_clock.NowMs)) return;
        _foregroundGfx = gfx;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, _clock.NowMs);
        IsTransitionWaiting = true;
        _timeline?.State("transition-start", new() { ["count"] = started });
        int lastBucket = -1;
        while (gfx.HasActiveForegroundTransitions(_clock.NowMs) && !_stopping)
        {
            var active = gfx.SnapshotForegroundTransitions(_clock.NowMs);
            int bucket = active.Count == 0 ? 100 : (int)System.Math.Floor(active[0].Progress * 10);
            if (bucket != lastBucket)
            {
                lastBucket = bucket;
                _timeline?.State("transition-progress", new()
                {
                    ["progress"] = active.Count == 0 ? 1.0 : active[0].Progress,
                    ["forced"] = active.Count != 0 && active[0].Forced,
                });
            }
            _frameSignal.WaitOne(50);
        }
        IsTransitionWaiting = false;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, -1);
        _foregroundGfx = null;
        _opPacer.Reset();
        _timeline?.State("running", new() { ["transition_complete"] = true });
    }

    public void PresentFrame(GfxState gfx)
    {
        int started = gfx.StartForegroundTransitions(_clock.NowMs);
        int completed = gfx.CompleteForegroundTransitions(_clock.NowMs);
        if (started > 0 || completed > 0)
            _timeline?.State("transition-skip-complete", new()
            {
                ["started"] = started, ["completed"] = completed,
            });
    }

    public void Stop()
    {
        _stopping = true;
        if (_gate.CurrentCount == 0) _gate.Release();
        _frameSignal.Set();
    }

    // Main thread, once per rendered frame: releases a VM thread parked in FrameYield/Sleep.
    public void PulseFrame() => _frameSignal.Set();

    // Called once per executed opcode (IHost.FrameYield). After a frame's worth of ops (the clock's
    // budget), block the VM background thread until Main._Process advances the clock — throttling the
    // interpreter to ~budget ops per rendered frame (the native engine's rate-limited cadence).
    public void FrameYield()
    {
        _opPacer.OpcodeCompleted();
        while (!_opPacer.CanRunNext && !_stopping)
            _frameSignal.WaitOne(50);
    }

    // op 0xc8: block the VM background thread so the main-thread compositor (Main.Recomposite in _Process)
    // presents the current retained GfxState — this is what makes the sleep-paced opening burst animate.
    // Time-based sibling of WaitForInput's suspend. The native op arms a non-blocking main-loop-polled timer;
    // blocking this throwaway task thread is behaviorally equivalent given our threading model. Operand is
    // MILLISECONDS (docs/engine-re.md sleep section + opcodes.toml 0xc8). Headless CLI hosts no-op it (parity).
    public double SleepScale = 1.0;   // --sleep-scale <f>: debug multiplier to slow/speed the paced opening for inspection
    // Wait on the unified FrameClock timebase (not Thread.Sleep) so a future Speed multiplier scales
    // sleeps together with the throttle and the tween. Main._Process advances the clock + pulses each frame.
    public void Sleep(long duration)
    {
        long ms = (long)System.Math.Clamp(duration * SleepScale, 0, 60_000);   // cap so a pathological script can't hang the window
        long deadline = _clock.NowMs + ms;
        _timeline?.State("sleep", new() { ["duration_ms"] = ms, ["deadline_ms"] = deadline });
        while (_clock.NowMs < deadline)
        {
            if (_stopping) break;
            _frameSignal.WaitOne(50);
        }
        _opPacer.Reset();
        _timeline?.State("running", new() { ["sleep_complete"] = true });
    }

    // ---- texture ops (run on the VM thread; marshal Godot node work to the main thread) ----
    public bool TraceOps;   // --gfx-log: print set-texture/create-texture slot assignments (diagnose slot collisions)

    public void CreateTexture(int slot, int width, int height)
    {
        _slotBmp[slot] = null; _slotDims[slot] = (width, height);
        if (TraceOps) Godot.GD.Print($"[op] create-texture slot={slot} {width}x{height}");
    }

    public void SetTexture(long resourceId, int slot)
    {
        var asset = _res.Resolve(_scene, resourceId);
        var bmp = asset != null ? ResourceMap.TexturePath(asset) : null;
        _slotBmp[slot] = bmp;
        _slotDims[slot] = BmpHeader.ReadDims(bmp);   // synchronous: dims from the header, no Godot Image
        if (TraceOps) Godot.GD.Print($"[op] set-texture slot={slot} resId=0x{resourceId:x} -> {(bmp != null ? System.IO.Path.GetFileName(bmp) : "<none>")}");
    }

    // Dims are read from the BMP header on the VM thread so the bytecode's geometry math (which calls this
    // synchronously right after set-texture) sees the real size. Pixels are blitted later on the main thread.
    public (int Width, int Height) GetTextureSize(int slot)
        => _slotDims.TryGetValue(slot, out var d) ? (d.W, d.H) : (0, 0);

    // Retained render model: draw-texture updates GfxState (object -> surface bind); Main._Process composites
    // the visible objects each frame in ascending-handle order. No immediate blit here.
    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }

    /// <summary>Resolve a gfx surface's resId to its pre-converted BMP path (Main's per-frame compositor
    /// resolves each visible object's surface through this).</summary>
    public string? ResolveResIdTexture(long resId)
    {
        var asset = _res.Resolve(_scene, resId);
        return asset != null ? ResourceMap.TexturePath(asset) : null;
    }

    // ---- audio ops (OGG plays natively in Godot) ----
    // BGM: addressed by direct name (BGM{id:D3}.OGG), NOT the manifest. Voice: via the per-scene manifest.
    public void PlayBgm(long id)
    {
        var path = _res.BgmPathById(id);
        _timeline?.Event("bgm", new() { ["id"] = id, ["file"] = path != null ? System.IO.Path.GetFileName(path) : null });
        if (path != null) _main.CallDeferred("PlayBgm", path);
    }

    public void PlayVoice(long id)
    {
        var asset = _res.Resolve(_scene, id);
        var path = asset != null ? ResourceMap.AudioPath(asset) : null;
        if (path != null) _main.CallDeferred("PlayVoice", path);
    }
}
