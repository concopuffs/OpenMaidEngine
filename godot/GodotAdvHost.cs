using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Text;

public sealed partial class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly ResourceMap _res;
    private readonly string _rootScene;
    private readonly string?[] _sfxNames = new string?[10];              // SC0000 native channel subset
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private readonly Age.Engine.Hosting.FrameClock _clock;
    private readonly GodotTimelineLog? _timeline;
    private readonly PageLocatorState _locator;
    private int _voiceBgmDuckControl;
    private (AudioPayload Audio, int PlaybackVariant)? _queuedSkippedVoice;
    private readonly object _scheduledVoiceLock = new();
    private (AudioPayload Audio, int PlaybackVariant, uint DelayMs, uint? StartMs)? _scheduledVoice;
    public GodotAdvHost(Main main, ResourceMap res, string scene, Age.Engine.Hosting.FrameClock clock,
                        PageLocatorState locator, Sys4LogicalCanvas logicalCanvas,
                        IGlyphMaskRasterizer surfaceTextRasterizer,
                        GodotTimelineLog? timeline = null,
                        bool synchronizeExplicitPresentation = true,
                        int surfaceTextMaskCacheCapacity = 2048)
    {
        _main = main; _res = res; _rootScene = scene; _clock = clock;
        _locator = locator; _timeline = timeline;
        _screenWidth = logicalCanvas.Width;
        _screenHeight = logicalCanvas.Height;
        _slotDims[0] = (_screenWidth, _screenHeight);
        _synchronizeExplicitPresentation = synchronizeExplicitPresentation;
        ArgumentNullException.ThrowIfNull(surfaceTextRasterizer);
        _surfaceTextBackendInfo =
            (surfaceTextRasterizer as IIdentifiedGlyphMaskRasterizer)?.BackendInfo
            ?? throw new ArgumentException(
                "Gameplay glyph rasterizers must identify their policy.",
                nameof(surfaceTextRasterizer));
        _surfaceTextMaskCache = new CachedGlyphMaskRasterizer(
            surfaceTextRasterizer, capacity: surfaceTextMaskCacheCapacity);
        _surfaceTextPixelRenderer =
            new ImmediateSurfaceTextRenderer(
                _surfaceTextMaskCache, _surfaceTextBackendInfo.Policy);
        _retainedGlyphLayoutEngine =
            new RetainedGlyphLayoutEngine(_surfaceTextMaskCache);
    }

    public Sys4LogicalCanvas LogicalCanvas => new(_screenWidth, _screenHeight);
    public void ReportWarning(string message) => System.Console.Error.WriteLine(message);

    public void PlayBgm(long id)
        => DispatchBgm(id, 1, false);

    public void RestartBgm(long id, int startMode)
        => DispatchBgm(id, startMode, true);

    private void DispatchBgm(long id, int startMode, bool forceRestart)
    {
        var asset = _res.ResolveBgm(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("bgm", new()
        {
            ["id"] = id,
            ["file"] = audio?.Name,
            ["start_mode"] = startMode,
            ["force_restart"] = forceRestart,
        });
        if (audio == null) return;
        if (forceRestart)
            _main.CallDeferred("RestartBgm", audio.Bytes, audio.Name, startMode);
        else
            _main.CallDeferred("PlayBgm", audio.Bytes, audio.Name);
    }

    public void StopBgm()
    {
        _timeline?.Event("bgm-stop", new());
        _main.CallDeferred("StopBgm");
    }

    public void PlayVoice(long id) => PlayVoice(id, 0);

    public void PlayVoice(long id, int playbackVariant)
    {
        var asset = _res.ResolveVoice(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("voice", new() { ["id"] = id, ["file"] = audio?.Name,
            ["playback_variant"] = playbackVariant });
        if (audio == null) return;
        bool queuedForSkip;
        bool firstQueued = false;
        lock (_messageSkipLock)
        {
            queuedForSkip = _messageSkipActive;
            if (queuedForSkip)
            {
                firstQueued = _queuedSkippedVoice == null;
                _queuedSkippedVoice = (audio, playbackVariant);
            }
        }
        if (queuedForSkip)
        {
            if (firstQueued) _main.CallDeferred("StopVoiceForMessageSkip");
            return;
        }
        DispatchVoice(audio, playbackVariant);
    }

    private void DispatchVoice(AudioPayload audio, int playbackVariant)
    {
        int generation = _main.QueueVoicePlayback();
        bool duckBgm = (System.Threading.Volatile.Read(ref _voiceBgmDuckControl) & 1) == 0;
        // Godot's stream player has no matching AGE start-mode control. Retain the native
        // variant through dispatch/timeline so that distinction is not erased at the VM seam.
        _main.CallDeferred("PlayVoice", audio.Bytes, audio.Name, generation, duckBgm, 50);
    }

    public void SetVoiceBgmDuckControl(long flags)
    {
        System.Threading.Volatile.Write(ref _voiceBgmDuckControl, unchecked((int)flags));
        _timeline?.State("voice-bgm-duck-control", new() { ["flags"] = flags });
    }

    public void ScheduleVoicePlayback(long id, int playbackVariant, long delayMs)
    {
        var asset = _res.ResolveVoice(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("voice-scheduled", new()
        {
            ["id"] = id, ["file"] = audio?.Name, ["playback_variant"] = playbackVariant,
            ["delay_ms"] = unchecked((uint)delayMs),
        });
        lock (_scheduledVoiceLock)
            _scheduledVoice = audio == null
                ? null
                : (audio, playbackVariant, unchecked((uint)delayMs), null);
    }

    public void LoadSoundEffect(long resourceId, int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length) return;
        var asset = _res.ResolveSoundEffect(resourceId);
        var audio = asset != null ? LoadAudio(asset) : null;
        _sfxNames[channel] = audio?.Name;
        _timeline?.Event("sfx-load", new() { ["resource"] = resourceId, ["channel"] = channel,
            ["file"] = audio?.Name });
        if (audio != null) _main.CallDeferred("LoadSoundEffect", audio.Bytes, audio.Name, channel);
    }

    public void StartSoundEffect(int channel) => StartSoundEffect(channel, 0);

    public void StartSoundEffect(int channel, int startMode)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        _timeline?.Event("sfx-start", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["file"] = _sfxNames[channel] });
        _main.CallDeferred("StartSoundEffect", channel, startMode);
    }

    public void ScheduleSoundEffectStart(int channel, int startMode, long delayMs)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        long ms = System.Math.Clamp(delayMs, 0, 60_000);
        double realSeconds = ms / 1000.0 / System.Math.Max(0.05, _clock.Speed);
        _timeline?.Event("sfx-start-scheduled", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["delay_ms"] = ms, ["file"] = _sfxNames[channel] });
        _main.CallDeferred("ScheduleSoundEffectStart", channel, startMode, realSeconds);
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
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            // Native parks the interpreter in its audio service while the main render loop continues.
            // Publish scene changes accumulated before the fade (notably GAMESTART -> SC0000's black
            // frame), then leave presentation ownership with the compositor for the timed wait.
            RequestSynchronizedPresentation();
            while (_clock.NowMs < deadline && !_stopping) _frameSignal.WaitOne(50);
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
            IsSleeping = false;
        }
        _timeline?.State("running", new() { ["bgm_fade_complete"] = true });
    }

    public void ApplyAudioVolume(int category, int basisPoints)
    {
        _timeline?.State("audio-volume", new()
        {
            ["category"] = category,
            ["basis_points"] = basisPoints,
        });
        _main.CallDeferred("ApplyAudioVolume", category, basisPoints);
    }

    public void ApplyAudioRouteEnabled(int category, bool enabled)
    {
        _timeline?.State("audio-route", new()
        {
            ["category"] = category,
            ["enabled"] = enabled,
        });
        _main.CallDeferred("ApplyAudioRouteEnabled", category, enabled);
    }
}

public sealed record GodotHostDiagnosticSnapshot(
    string CurrentScene, bool IsInputWaiting, bool IsTransitionWaiting, bool IsSleeping,
    bool IsTextRevealing, bool IsModalMovieWaiting, bool IsAdvPagePresentationSuspended,
    bool IsMessageSkipActive, bool IsScreenTransitionActive, long TransitionStartedAtMs,
    IReadOnlyList<MovieSurfaceDiagnostic> MovieSurfaces, IReadOnlyList<long> CompletedMoviePlaybackIds);
