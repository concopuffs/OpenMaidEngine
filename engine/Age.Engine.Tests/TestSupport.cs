using System.Collections.Generic;
using System.Linq;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;

/// <summary>Shared test doubles: a host that records observable effects, and an in-memory script
/// provider for synthetic call-script targets.</summary>
internal class RecordingHost : IHost
{
    public int Waits;
    public int Presents;
    public int TransitionWaits;
    public int InputCallbackFrames;
    public bool MessageSkip;
    public bool ScriptMessageSkip;
    public bool PhysicalMessageSkip;
    public bool AdvReadSkip;
    public readonly List<(int Offset, string Text)> Lines = new();
    public readonly List<(int Slot, int X, int Y)> TextCursors = new();
    public readonly List<(int Surface, int X, int Y, string Text)> SurfaceStrings = new();
    public readonly List<AdvTextHistoryRenderBatch> HistoryRenders = new();
    public readonly Dictionary<int, AdvTextHistoryRenderBatch> ActiveHistoryRenders = new();
    public int HistoryPresentationEnds;
    public readonly List<int> ClearedTextLayouts = new();
    public readonly List<SurfaceRectFill> SurfaceFills = new();
    public readonly List<SurfaceRectCopy> SurfaceCopies = new();
    public readonly List<(long First, long Count)> PresentedRanges = new();
    public readonly List<(int Slot, int SourceX, int SourceY, int Width, int Height, int X, int Y)>
        TextureDraws = new();
    public readonly List<AdvWaitIndicatorConfig> WaitIndicators = new();
    public readonly List<bool> WaitIndicatorEnabledChanges = new();
    public readonly List<int> PublishedAdvTextLayouts = new();
    public readonly List<long> SleptDurations = new();
    public readonly List<(long Resource, int Channel)> SfxLoads = new();
    public readonly List<long> Voices = new();
    public readonly List<(long Id, int PlaybackVariant)> VoiceRequests = new();
    public readonly List<(long Id, int PlaybackVariant, long DelayMs)> ScheduledVoiceRequests = new();
    public readonly List<long> VoiceBgmDuckControls = new();
    public readonly List<int> SfxStarts = new();
    public readonly List<(int Channel, int StartMode)> SfxStartRequests = new();
    public readonly List<(int Channel, int StartMode, long DelayMs)> ScheduledSfxStarts = new();
    public readonly List<int> SfxReleases = new();
    public readonly List<long> BgmTracks = new();
    public readonly List<(long Track, int StartMode)> BgmRestartRequests = new();
    public int BgmStops;
    public readonly List<(int Target, long Duration)> BgmFades = new();
    public readonly List<(int Category, int BasisPoints)> AudioVolumeChanges = new();
    public readonly List<(int Category, bool Enabled)> AudioRouteChanges = new();
    public readonly List<(long Resource, int Surface, long Flags, long SyncMask)> Movies = new();
    public readonly List<(long Resource, int Surface, long Flags, long SyncMask, long PositionMs)>
        PositionedMovies = new();
    public readonly List<MovieMaskTransitionRequest> MovieMaskTransitions = new();
    public System.Action? OnPlayMovie;
    public long? MovieStopTimeMs;
    public readonly HashSet<int> ActiveMovieSurfaces = new();
    public readonly List<(long Resource, int Surface, long Flags)> ModalMovies = new();
    public readonly List<int> ClearedRenderTargets = new();
    public readonly List<(int First, int Count)> ReleasedSurfaceRanges = new();
    public readonly List<(int Surface, long Interval, SurfaceBlackFadeDirection Direction)> SurfaceBlackFades = new();
    public readonly List<(int Source, int Target, long Interval)> SurfaceCrossfades = new();
    public readonly List<bool> SurfaceBlackFadeForceEndpoints = new();
    public readonly List<bool> SurfaceCrossfadeForceEndpoints = new();
    public readonly List<(long Resource, int Slot)> Textures = new();
    public readonly List<bool> MessageSkipChanges = new();
    public readonly List<bool> PhysicalMessageSkipChanges = new();
    public readonly List<string> Warnings = new();
    public readonly List<DiagnosticMessage> Diagnostics = new();
    public System.Action<DiagnosticMessage>? OnDiagnosticMessage;
    public readonly List<FullwidthTextEditRequest> FullwidthTextEdits = new();
    public System.Func<FullwidthTextEditRequest, FullwidthTextEditResult>? OnFullwidthTextEdit;
    public readonly List<long> CursorResources = new();
    public readonly List<bool> AdvPagePresentationSuspended = new();
    public readonly List<(AdvLiveTextRun Run, int GlyphDelayMilliseconds)> LiveTextRuns = new();
    public readonly Dictionary<int, RgbaImage> SurfacePixels = new();
    public int MessageGlyphDelayMilliseconds { get; private set; } = 50;
    public int CursorClearCount;
    public int SceneContextResets;
    public void ReportWarning(string message) => Warnings.Add(message);
    public void ShowDiagnosticMessage(DiagnosticMessage message)
    {
        Diagnostics.Add(message);
        OnDiagnosticMessage?.Invoke(message);
    }
    public FullwidthTextEditResult EditFullwidthString(FullwidthTextEditRequest request)
    {
        FullwidthTextEdits.Add(request);
        return OnFullwidthTextEdit?.Invoke(request) ?? new(false, request.CurrentText);
    }
    public void ShowText(int offset, string text) => Lines.Add((offset, text));
    public void ShowText(AdvLiveTextRun run, int glyphDelayMilliseconds)
    {
        Lines.Add((run.SourceOffset, run.Text));
        LiveTextRuns.Add((run, glyphDelayMilliseconds));
    }
    public void SetMessageGlyphDelayMilliseconds(int milliseconds)
        => MessageGlyphDelayMilliseconds = milliseconds;
    public RgbaImage? CaptureSurfacePixels(int slot)
        => SurfacePixels.TryGetValue(slot, out var image) ? image : null;
    public bool ReplaceSurfacePixels(int slot, RgbaImage image)
    {
        SurfacePixels[slot] = image;
        return true;
    }
    public void SetAdvTextCursor(int layoutSlot, int x, int y) => TextCursors.Add((layoutSlot, x, y));
    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text)
        => SurfaceStrings.Add((surfaceSlot, x, y, text));
    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text, AdvTextStyle style)
        => SurfaceStrings.Add((surfaceSlot, x, y, text));
    public void ClearRenderedAdvTextLayout(int layoutSlot)
    {
        ClearedTextLayouts.Add(layoutSlot);
        ActiveHistoryRenders.Remove(layoutSlot);
    }
    public void RenderTextHistory(AdvTextHistoryRenderBatch batch)
    {
        HistoryRenders.Add(batch);
        ActiveHistoryRenders[batch.LayoutSlot] = batch;
    }
    public void EndTextHistoryPresentation()
    {
        HistoryPresentationEnds++;
        ActiveHistoryRenders.Clear();
    }
    public int MessageWindowAlphaSetting { get; set; }
    public void SetMessageWindowAlphaSetting(int value) => MessageWindowAlphaSetting = value;
    public void FillSurfaceRect(SurfaceRectFill fill) => SurfaceFills.Add(fill);
    public void CopySurfaceRect(SurfaceRectCopy copy) => SurfaceCopies.Add(copy);
    public void PresentObjectRange(GfxState gfx, long firstHandle, long count)
        => PresentedRanges.Add((firstHandle, count));
    public void ConfigureAdvWaitIndicator(AdvWaitIndicatorConfig config) => WaitIndicators.Add(config);
    public void SetAdvWaitIndicatorEnabled(bool enabled) => WaitIndicatorEnabledChanges.Add(enabled);
    public void PublishAdvTextLayout(int layoutSlot) => PublishedAdvTextLayouts.Add(layoutSlot);
    public void SetAdvPagePresentationSuspended(bool suspended)
        => AdvPagePresentationSuspended.Add(suspended);
    public void WaitForInput() => Waits++;
    public virtual void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
    {
        while (serviceInputCallback()) { }
        WaitForInput();
    }
    public virtual void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                                     Func<AdvAutoWaitState> autoWaitState)
        => WaitForInput(layoutSlot, serviceInputCallback);
    public void InputCallbackCompleted(GfxState gfx) => InputCallbackFrames++;
    public virtual long InputClockMilliseconds => Environment.TickCount64;
    public void SetCursorResource(long resourceId) => CursorResources.Add(resourceId);
    public void ClearCursorResource() => CursorClearCount++;
    public virtual void Sleep(long duration) => SleptDurations.Add(duration);
    public virtual void FrameYield() { }
    public void ResetSceneContext() => SceneContextResets++;
    public bool IsMessageSkipActive => MessageSkip;
    public void SetMessageSkipActive(bool active)
    {
        ScriptMessageSkip = active;
        MessageSkip = ScriptMessageSkip || PhysicalMessageSkip;
        MessageSkipChanges.Add(MessageSkip);
    }
    public void SetPhysicalMessageSkipActive(bool active)
    {
        PhysicalMessageSkip = active;
        MessageSkip = ScriptMessageSkip || PhysicalMessageSkip;
        PhysicalMessageSkipChanges.Add(active);
    }
    public bool IsAdvReadSkipActive => AdvReadSkip;
    public void PresentFrame(GfxState gfx)
    {
        Presents++;
        gfx.StartForegroundTransitions(100);
        gfx.CompleteForegroundTransitions(100);
    }
    public void WaitForForegroundTransition(GfxState gfx)
    {
        TransitionWaits++;
        gfx.StartForegroundTransitions(100);
        gfx.CompleteForegroundTransitions(100);
    }
    public void FadeSurfaceWithBlack(
        GfxState gfx, int surface, long intervalArgument, SurfaceBlackFadeDirection direction,
        bool forceEndpoint = false)
    {
        SurfaceBlackFades.Add((surface, intervalArgument, direction));
        SurfaceBlackFadeForceEndpoints.Add(forceEndpoint);
    }
    public void CrossfadeSurfaces(
        GfxState gfx, int sourceSurface, int targetSurface, long intervalArgument,
        bool forceEndpoint = false)
    {
        SurfaceCrossfades.Add((sourceSurface, targetSurface, intervalArgument));
        SurfaceCrossfadeForceEndpoints.Add(forceEndpoint);
    }
    public void CreateTexture(int slot, int w, int h) { }
    public void SetTexture(long resId, int slot) => Textures.Add((resId, slot));
    public void ClearRenderTarget(int surfaceSlot) => ClearedRenderTargets.Add(surfaceSlot);
    public void ReleaseSurfaceRange(int firstSlot, int count) => ReleasedSurfaceRanges.Add((firstSlot, count));
    public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy)
        => TextureDraws.Add((slot, sx, sy, w, h, dx, dy));
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
    public void PlayBgm(long id) => BgmTracks.Add(id);
    public void RestartBgm(long id, int startMode) => BgmRestartRequests.Add((id, startMode));
    public void StopBgm() => BgmStops++;
    public void PlayVoice(long id) => Voices.Add(id);
    public void PlayVoice(long id, int playbackVariant)
    {
        Voices.Add(id);
        VoiceRequests.Add((id, playbackVariant));
    }
    public void SetVoiceBgmDuckControl(long flags) => VoiceBgmDuckControls.Add(flags);
    public void ScheduleVoicePlayback(long id, int playbackVariant, long delayMs)
        => ScheduledVoiceRequests.Add((id, playbackVariant, delayMs));
    public void LoadSoundEffect(long resourceId, int channel) => SfxLoads.Add((resourceId, channel));
    public void StartSoundEffect(int channel) => StartSoundEffect(channel, 0);
    public void StartSoundEffect(int channel, int startMode)
    {
        SfxStarts.Add(channel);
        SfxStartRequests.Add((channel, startMode));
    }
    public void ScheduleSoundEffectStart(int channel, int startMode, long delayMs)
        => ScheduledSfxStarts.Add((channel, startMode, delayMs));
    public void ReleaseSoundEffect(int channel) => SfxReleases.Add(channel);
    public void FadeBgm(int targetPercent, long durationMs) => BgmFades.Add((targetPercent, durationMs));
    public void ApplyAudioVolume(int category, int basisPoints)
        => AudioVolumeChanges.Add((category, basisPoints));
    public void ApplyAudioRouteEnabled(int category, bool enabled)
        => AudioRouteChanges.Add((category, enabled));
    public long? PlayMovieToSurface(long resourceId, int surfaceSlot, long movieFlags, long syncMask)
    {
        Movies.Add((resourceId, surfaceSlot, movieFlags, syncMask));
        OnPlayMovie?.Invoke();
        return MovieStopTimeMs;
    }
    public long? PlayMovieToSurfaceAtPosition(
        long resourceId, int surfaceSlot, long movieFlags, long syncMask, long positionMs)
    {
        PositionedMovies.Add((resourceId, surfaceSlot, movieFlags, syncMask, positionMs));
        OnPlayMovie?.Invoke();
        return MovieStopTimeMs;
    }
    public void PlayMovieMaskTransition(GfxState gfx, MovieMaskTransitionRequest request)
    {
        MovieMaskTransitions.Add(request);
        gfx.QueueMovieMaskTransition(request);
        gfx.CompleteMovieMaskTransition(request.SurfaceSlot);
    }
    public bool IsMovieSurfaceActive(int surfaceSlot) => ActiveMovieSurfaces.Contains(surfaceSlot);
    public void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags)
        => ModalMovies.Add((resourceId, surfaceSlot, movieFlags));
}

internal sealed class MapProvider : IScriptProvider
{
    private readonly Dictionary<long, Script> _m;
    private readonly Dictionary<string, Script> _byName;
    public MapProvider(Dictionary<long, Script> m, Dictionary<string, Script>? byName = null)
    {
        _m = m;
        _byName = byName ?? new(StringComparer.OrdinalIgnoreCase);
    }
    public Script? GetById(long id) => _m.TryGetValue(id, out var s) ? s : null;
    public Script? GetByName(string name) => _byName.TryGetValue(name, out var s) ? s : null;
}

/// <summary>A controlled test double: every call-script id resolves to the same script (typically a
/// no-op that just exits). Lets a test run a real script with call-script handling ON while isolating
/// it from the real subroutines' game-state dependencies.</summary>
internal sealed class AnyProvider : IScriptProvider
{
    private readonly Script _s;
    public AnyProvider(Script s) => _s = s;
    public Script? GetById(long id) => _s;
}

/// <summary>Captures every trace event for assertions; TracingSteps is settable so a test can
/// exercise the Step gate both ways.</summary>
internal sealed class RecordingTraceSink : ITraceSink
{
    public bool TracingSteps { get; init; }
    public readonly List<TraceEvent> Events = new();
    public void Emit(in TraceEvent e) => Events.Add(e);
    public List<long> CallScriptIds =>
        Events.Where(e => e.Kind == TraceEventKind.CallScript).Select(e => e.Id).ToList();
}
