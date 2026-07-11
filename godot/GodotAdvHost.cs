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
    private readonly object _imageLock = new();
    private readonly Dictionary<int, RgbaImage?> _images = new();        // raw catalog id -> decoded pixels
    private readonly Dictionary<long, (RgbaImage Image, string Name, int RawIndex)> _movieFrames = new();
    private readonly Dictionary<int, long> _movieBySurface = new();
    private readonly HashSet<long> _completedMovies = new();
    private readonly string?[] _sfxNames = new string?[10];              // SC0000 native channel subset
    // slot -> dims. Slot 0 is the primary/screen surface (800x600), normally created at engine boot which
    // the single-scene harness skips; seed it so the first CG's anchor math stays correct (not 0x0).
    private readonly Dictionary<int, (int W, int H)> _slotDims = new() { { 0, (800, 600) } };
    private readonly SemaphoreSlim _gate = new(0, 1);
    private readonly Age.Engine.Hosting.FrameClock _clock;
    private readonly GodotTimelineLog? _timeline;
    private readonly System.Threading.AutoResetEvent _frameSignal = new(false);
    private volatile bool _stopping;
    private readonly object _textLock = new();
    private readonly Dictionary<int, SurfaceTextDraw> _surfaceText = new();
    private string _advText = "";
    private int _advTextX = 100, _advTextY = 47;
    private long _advTextStartedMs;
    private bool _advTextForceComplete;
    private GfxState? _foregroundGfx;
    public volatile bool IsWaiting;
    public volatile bool IsTransitionWaiting;
    public volatile bool IsSleeping;
    public volatile bool IsTextRevealing;
    private int _presentRequested = 1;
    private long _transitionStartedAtMs = -1;
    public long TransitionStartedAtMs => System.Threading.Interlocked.Read(ref _transitionStartedAtMs);
    public readonly List<(int Offset, string Text)> Captured = new();

    public GodotAdvHost(Main main, ResourceMap res, string scene, Age.Engine.Hosting.FrameClock clock,
                        GodotTimelineLog? timeline = null)
    {
        _main = main; _res = res; _scene = scene; _clock = clock;
        _timeline = timeline;
    }

    public void ShowText(int offset, string text)
    {
        Captured.Add((offset, text));
        lock (_textLock)
        {
            _advText = text;
            _advTextStartedMs = _clock.NowMs;
            _advTextForceComplete = false;
            IsTextRevealing = text.Length > 0;
        }
        _timeline?.State("text-reveal", new()
        {
            ["offset"] = $"0x{offset:x}", ["x"] = _advTextX, ["y"] = _advTextY,
            ["glyphs"] = text.Length, ["delay_ms"] = 50,
        });
        while (IsTextRevealing && !_stopping)
        {
            lock (_textLock)
            {
                if (_advTextForceComplete || _clock.NowMs - _advTextStartedMs >= text.Length * 50L)
                    IsTextRevealing = false;
            }
            if (IsTextRevealing) _frameSignal.WaitOne(50);
        }
        _timeline?.State("running", new() { ["text_reveal_complete"] = true });
    }

    public void SetAdvTextCursor(int layoutSlot, int x, int y)
    {
        lock (_textLock) { _advTextX = x; _advTextY = y; }
        _timeline?.Event("text-cursor", new() { ["slot"] = layoutSlot, ["x"] = x, ["y"] = y });
    }

    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text)
    {
        lock (_textLock) _surfaceText[surfaceSlot] = new SurfaceTextDraw(x, y, text);
        _timeline?.Event("draw-string", new() { ["surface"] = surfaceSlot, ["x"] = x, ["y"] = y, ["text"] = text });
    }

    public (string Text, int X, int Y, int VisibleGlyphs, bool Revealing) SnapshotAdvText()
    {
        lock (_textLock)
        {
            int visible = _advTextForceComplete || !IsTextRevealing
                ? _advText.Length
                : (int)System.Math.Clamp((_clock.NowMs - _advTextStartedMs) / 50L + 1, 0, _advText.Length);
            return (_advText, _advTextX, _advTextY, visible, IsTextRevealing);
        }
    }

    public bool TryGetSurfaceText(int surfaceSlot, out SurfaceTextDraw draw)
    { lock (_textLock) return _surfaceText.TryGetValue(surfaceSlot, out draw); }

    public volatile int Pages;   // VM-thread page counter (incremented before IsWaiting so shot-gating can't race)

    public void WaitForInput()
    {
        Pages++;
        _main.CallDeferred("PageBreak");
        // Publish retained mutations accumulated before the wait once. A static input wait is not itself a
        // reason to rebuild the 800x600 background every frame; ambient channels are queried separately.
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        IsWaiting = true;
        _timeline?.State("input-wait", new() { ["page"] = Pages });
        _gate.Wait();
        IsWaiting = false;
        _timeline?.State("running", new() { ["input"] = "auto-or-user" });
        lock (_textLock)
        {
            _advText = "";
            _advTextX = 100;
            _advTextY = 47;
        }
        _main.CallDeferred("ClearPage");
    }

    // Called from the main thread (click) or an auto-clicker. A transition click is consumed by
    // the foreground lifecycle; it never pre-arms or advances the following stable input wait.
    public void SignalInput()
    {
        if (IsTextRevealing)
        {
            lock (_textLock) _advTextForceComplete = true;
            _timeline?.State("text-reveal-forced-complete", new());
            _frameSignal.Set();
            return;
        }
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
        if (started == 0 && !gfx.HasActiveTimedPresentation(_clock.NowMs) && !HasActiveMoviePresentation()) return;
        _foregroundGfx = gfx;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, _clock.NowMs);
        IsTransitionWaiting = true;
        _timeline?.State("transition-start", new() { ["count"] = started });
        int lastBucket = -1;
        while ((gfx.HasActiveTimedPresentation(_clock.NowMs) || HasActiveMoviePresentation()) && !_stopping)
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
        // The active query becomes false at the exact transition/movie endpoint. Publish that terminal sample
        // once so the last visible frame cannot remain fractionally incomplete.
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        IsTransitionWaiting = false;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, -1);
        _foregroundGfx = null;
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
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
    }

    // Native retained-object writes are not front-buffer writes. Publish explicit/service-boundary dirtiness
    // once, then continue only while the sampled retained scene can actually change. Text reveal is a separate
    // Godot Label; waiting/sleeping alone do not alter background pixels.
    public bool ShouldRecomposite(GfxState gfx)
        => System.Threading.Interlocked.Exchange(ref _presentRequested, 0) != 0 ||
           gfx.HasActiveVisualPresentation(_clock.NowMs);

    public void Stop()
    {
        _stopping = true;
        lock (_textLock) _advTextForceComplete = true;
        if (_gate.CurrentCount == 0) _gate.Release();
        _frameSignal.Set();
    }

    // Main thread, once per rendered frame: releases a VM thread parked in Sleep or a presentation/input wait.
    public void PulseFrame() => _frameSignal.Set();

    // Native presentation trace: ordinary opcode bursts run to the next service boundary in a few
    // milliseconds and are not frame-paced. Pacing belongs to 0x21c, sleep, and input waits below.
    public void FrameYield() { }

    // op 0xc8: block the VM background thread while the main-thread compositor keeps presenting retained state.
    // Time-based sibling of WaitForInput's suspend. The native op arms a non-blocking main-loop-polled timer;
    // blocking this throwaway task thread is behaviorally equivalent given our threading model. Operand is
    // MILLISECONDS (docs/engine-re.md sleep section + opcodes.toml 0xc8). Headless CLI hosts no-op it (parity).
    public double SleepScale = 1.0;   // --sleep-scale <f>: debug multiplier for explicit op-0xc8 holds only
    // Wait on the unified FrameClock timebase (not Thread.Sleep) so the Speed multiplier scales
    // sleeps together with retained presentation clocks. Main._Process advances the clock + pulses each frame.
    public void Sleep(long duration)
    {
        long ms = (long)System.Math.Clamp(duration * SleepScale, 0, 60_000);   // cap so a pathological script can't hang the window
        long deadline = _clock.NowMs + ms;
        _timeline?.State("sleep", new() { ["duration_ms"] = ms, ["deadline_ms"] = deadline });
        // A sleep is a service boundary: make preceding retained writes visible once even when no animation
        // channel is active during the hold.
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        IsSleeping = true;
        while (_clock.NowMs < deadline)
        {
            if (_stopping) break;
            _frameSignal.WaitOne(50);
        }
        IsSleeping = false;
        _timeline?.State("running", new() { ["sleep_complete"] = true });
    }

    // ---- texture ops (run on the VM thread; marshal Godot node work to the main thread) ----
    public bool TraceOps;   // --gfx-log: print set-texture/create-texture slot assignments (diagnose slot collisions)

    public void CreateTexture(int slot, int width, int height)
    {
        lock (_textLock) _surfaceText.Remove(slot);
        _slotDims[slot] = (width, height);
        if (TraceOps) Godot.GD.Print($"[op] create-texture slot={slot} {width}x{height}");
    }

    public void SetTexture(long resourceId, int slot)
    {
        lock (_textLock) _surfaceText.Remove(slot);
        var asset = _res.ResolveTexture(_scene, resourceId);
        var image = asset != null ? Decode(asset) : null;
        _slotDims[slot] = image != null ? (image.Width, image.Height) : (0, 0);
        if (TraceOps) Godot.GD.Print($"[op] set-texture slot={slot} resId=0x{resourceId:x} -> {(asset?.Name ?? "<none>")}");
    }

    // AGF is decoded synchronously on the VM thread so geometry queried immediately afterward sees real dims.
    public (int Width, int Height) GetTextureSize(int slot)
        => _slotDims.TryGetValue(slot, out var d) ? (d.W, d.H) : (0, 0);

    // Retained render model: draw-texture updates GfxState (object -> surface bind); Main._Process composites
    // the visible objects each frame in ascending-handle order. No immediate blit here.
    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }

    /// <summary>Resolve a gfx surface through scene-local or universal raw-id addressing and decode it
    /// from the loose-first asset store.</summary>
    public (RgbaImage Image, string Name, int AssetId, bool IsDynamic)? ResolveResIdTexture(long resId)
    {
        lock (_imageLock)
            if (_movieFrames.TryGetValue(resId, out var movie))
                return (movie.Image, movie.Name, movie.RawIndex, true);
        var asset = _res.ResolveTexture(_scene, resId);
        var image = asset != null ? Decode(asset) : null;
        return asset != null && image != null ? (image, asset.Name, asset.RawIndex, false) : null;
    }

    public void PlayMovieToSurface(long resourceId, int surfaceSlot, long movieFlags, long syncMask)
    {
        var asset = _res.Resolve(_scene, resourceId);
        if (asset == null) { Godot.GD.Print($"movie unresolved {_scene}:0x{resourceId:x}"); return; }
        try
        {
            var movie = _res.ReadMovie(asset);
            ReleaseSurface(surfaceSlot);
            lock (_imageLock)
            {
                _movieBySurface[surfaceSlot] = resourceId;
                _completedMovies.Remove(resourceId);
            }
            _slotDims[surfaceSlot] = (800, 600); // SC0000 creates this native-sized surface immediately beforehand.
            _timeline?.Event("movie-start", new()
            {
                ["resource"] = resourceId, ["surface"] = surfaceSlot, ["file"] = movie.Name,
                ["flags"] = movieFlags, ["sync_mask"] = syncMask,
            });
            _main.CallDeferred("PlayMovie", movie.Bytes, movie.Name, resourceId, asset.RawIndex);
        }
        catch (System.Exception e) { Godot.GD.Print($"movie read failed {asset.Name}: {e.Message}"); }
    }

    public void ReleaseSurface(int slot)
    {
        long resourceId;
        lock (_imageLock)
        {
            if (!_movieBySurface.Remove(slot, out resourceId)) return;
            if (!_completedMovies.Contains(resourceId))
            {
                _movieBySurface[slot] = resourceId;
                return; // SC0000 prepares following static surfaces before 0x21c; the movie remains retained.
            }
            _movieFrames.Remove(resourceId);
            _completedMovies.Remove(resourceId);
        }
        _timeline?.Event("movie-stop", new() { ["resource"] = resourceId, ["surface"] = slot });
        _main.CallDeferred("StopMovie", resourceId);
    }

    // Main-thread decoder handoff. Replacing the newest frame mirrors the native texture renderer's
    // sample callback: the retained object keeps its surface binding while only the surface pixels change.
    public void PublishMovieFrame(long resourceId, string name, int rawIndex, RgbaImage frame)
    {
        lock (_imageLock) _movieFrames[resourceId] = (frame, name, rawIndex);
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
    }

    public void NotifyMovieCompleted(long resourceId)
    {
        lock (_imageLock)
            if (!_completedMovies.Add(resourceId)) return;
        _timeline?.Event("movie-complete", new() { ["resource"] = resourceId });
        _frameSignal.Set();
    }

    private bool HasActiveMoviePresentation()
    {
        lock (_imageLock)
            foreach (long resourceId in _movieBySurface.Values)
                if (!_completedMovies.Contains(resourceId)) return true;
        return false;
    }

    private RgbaImage? Decode(AssetEntry asset)
    {
        lock (_imageLock)
        {
            if (_images.TryGetValue(asset.RawIndex, out var cached)) return cached;
            try { return _images[asset.RawIndex] = _res.DecodeTexture(asset); }
            catch (System.Exception e)
            {
                Godot.GD.Print($"AGF decode failed {asset.Name}: {e.Message}");
                _images[asset.RawIndex] = null;
                return null;
            }
        }
    }

    // ---- audio ops (OGG plays natively in Godot) ----
    // BGM: addressed by direct name (BGM{id:D3}.OGG), NOT the manifest. Voice: via the per-scene manifest.
    public void PlayBgm(long id)
    {
        var asset = _res.ResolveBgm(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("bgm", new() { ["id"] = id, ["file"] = audio?.Name });
        if (audio != null) _main.CallDeferred("PlayBgm", audio.Bytes, audio.Name);
    }

    public void PlayVoice(long id)
    {
        var asset = _res.Resolve(_scene, id);
        var audio = asset != null ? LoadAudio(asset) : null;
        if (audio != null) _main.CallDeferred("PlayVoice", audio.Bytes, audio.Name);
    }

    public void LoadSoundEffect(long resourceId, int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length) return;
        var asset = _res.Resolve(_scene, resourceId);
        var audio = asset != null ? LoadAudio(asset) : null;
        _sfxNames[channel] = audio?.Name;
        _timeline?.Event("sfx-load", new() { ["resource"] = resourceId, ["channel"] = channel,
            ["file"] = audio?.Name });
        if (audio != null) _main.CallDeferred("LoadSoundEffect", audio.Bytes, audio.Name, channel);
    }

    public void StartSoundEffect(int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        _timeline?.Event("sfx-start", new() { ["channel"] = channel,
            ["file"] = _sfxNames[channel] });
        _main.CallDeferred("StartSoundEffect", channel);
    }

    public void ReleaseSoundEffect(int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length) return;
        _timeline?.Event("sfx-release", new() { ["channel"] = channel,
            ["file"] = _sfxNames[channel] });
        _sfxNames[channel] = null;
        _main.CallDeferred("ReleaseSoundEffect", channel);
    }

    private AudioPayload? LoadAudio(AssetEntry asset)
    {
        try { return _res.ReadAudio(asset); }
        catch (System.Exception e)
        {
            Godot.GD.Print($"audio read failed {asset.Name}: {e.Message}");
            return null;
        }
    }

    public void FadeBgm(int targetPercent, long durationMs)
    {
        long ms = System.Math.Clamp(durationMs, 0, 60_000);
        double realSeconds = ms / 1000.0 / System.Math.Max(0.05, _clock.Speed);
        _timeline?.State("bgm-fade", new() { ["target_percent"] = targetPercent, ["duration_ms"] = ms });
        _main.CallDeferred("FadeBgm", targetPercent, realSeconds);
        long deadline = _clock.NowMs + ms;
        IsSleeping = true;
        while (_clock.NowMs < deadline && !_stopping) _frameSignal.WaitOne(50);
        IsSleeping = false;
        _timeline?.State("running", new() { ["bgm_fade_complete"] = true });
    }
}

public readonly record struct SurfaceTextDraw(int X, int Y, string Text);
