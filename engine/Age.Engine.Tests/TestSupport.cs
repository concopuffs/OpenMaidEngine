using System.Collections.Generic;
using System.Linq;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;

/// <summary>Shared test doubles: a host that records observable effects, and an in-memory script
/// provider for synthetic call-script targets.</summary>
internal class RecordingHost : IHost
{
    public int Waits;
    public int Presents;
    public int TransitionWaits;
    public int InputCallbackFrames;
    public bool MessageSkip;
    public bool AdvReadSkip;
    public readonly List<(int Offset, string Text)> Lines = new();
    public readonly List<(int Slot, int X, int Y)> TextCursors = new();
    public readonly List<(int Surface, int X, int Y, string Text)> SurfaceStrings = new();
    public readonly List<AdvWaitIndicatorConfig> WaitIndicators = new();
    public readonly List<long> SleptDurations = new();
    public readonly List<(long Resource, int Channel)> SfxLoads = new();
    public readonly List<int> SfxStarts = new();
    public readonly List<int> SfxReleases = new();
    public readonly List<(int Target, long Duration)> BgmFades = new();
    public readonly List<(long Resource, int Surface, long Flags, long SyncMask)> Movies = new();
    public readonly List<bool> MessageSkipChanges = new();
    public void ShowText(int offset, string text) => Lines.Add((offset, text));
    public void SetAdvTextCursor(int layoutSlot, int x, int y) => TextCursors.Add((layoutSlot, x, y));
    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text)
        => SurfaceStrings.Add((surfaceSlot, x, y, text));
    public void ConfigureAdvWaitIndicator(AdvWaitIndicatorConfig config) => WaitIndicators.Add(config);
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
    public void Sleep(long duration) => SleptDurations.Add(duration);
    public void FrameYield() { }
    public bool IsMessageSkipActive => MessageSkip;
    public void SetMessageSkipActive(bool active)
    {
        MessageSkip = active;
        MessageSkipChanges.Add(active);
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
    public void CreateTexture(int slot, int w, int h) { }
    public void SetTexture(long resId, int slot) { }
    public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy) { }
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
    public void PlayBgm(long id) { }
    public void PlayVoice(long id) { }
    public void LoadSoundEffect(long resourceId, int channel) => SfxLoads.Add((resourceId, channel));
    public void StartSoundEffect(int channel) => SfxStarts.Add(channel);
    public void ReleaseSoundEffect(int channel) => SfxReleases.Add(channel);
    public void FadeBgm(int targetPercent, long durationMs) => BgmFades.Add((targetPercent, durationMs));
    public void PlayMovieToSurface(long resourceId, int surfaceSlot, long movieFlags, long syncMask)
        => Movies.Add((resourceId, surfaceSlot, movieFlags, syncMask));
}

internal sealed class MapProvider : IScriptProvider
{
    private readonly Dictionary<long, Script> _m;
    public MapProvider(Dictionary<long, Script> m) => _m = m;
    public Script? GetById(long id) => _m.TryGetValue(id, out var s) ? s : null;
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
