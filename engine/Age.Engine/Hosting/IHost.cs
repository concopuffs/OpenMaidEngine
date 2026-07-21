using Age.Engine.Model;

namespace Age.Engine.Hosting;

public readonly record struct AdvWaitIndicatorConfig(
    int LayoutSlot, int X, int Y, int SurfaceSlot,
    int SourceX, int SourceY, int CellWidth, int CellHeight,
    int TerminalFrame, long FramePeriodMs);

public readonly record struct AdvAutoWaitState(
    bool Enabled, bool VoicePending, long PostVoiceDelayMs, long UnvoicedDelayMs);

public readonly record struct SurfaceRectFill(
    int SurfaceSlot, int X, int Y, int Width, int Height, int Alpha, long Rgb);

public interface IHost
{
    // Script-local resource ids resolve against the currently executing frame's SYS4INI section.
    // Interactive hosts track this stack; headless hosts may keep the no-op/default identity behavior.
    void EnterScriptContext(string scriptName) { }
    void ExitScriptContext() { }
    long ResolveTextureResourceId(long resourceId) => resourceId;
    void ShowText(int offset, string text);
    // Native ADV text subsystem: op 0x7a updates the selected layout's last 20-byte cursor record;
    // op 0x204 rasterizes a string into a numbered surface before 0x1fb binds that surface.
    void SetAdvTextCursor(int layoutSlot, int x, int y) { }
    void DrawStringToSurface(int surfaceSlot, int x, int y, string text) { }
    void DrawStringToSurface(int surfaceSlot, int x, int y, string text, AdvTextStyle style)
        => DrawStringToSurface(surfaceSlot, x, y, text);
    void ClearRenderedAdvTextLayout(int layoutSlot) { }
    void RenderTextHistory(AdvTextHistoryRenderBatch batch) { }
    // History render batches are transient bindings, unlike the retained backlog itself. HISTORY.BIN's
    // recording re-enable at exit ends that presentation and drops every bound target layout.
    void EndTextHistoryPresentation() { }
    int MessageWindowAlphaSetting => 0;
    void FillSurfaceRect(SurfaceRectFill fill) { }
    void PresentObjectRange(GfxState gfx, long firstHandle, long count) { }
    void ConfigureAdvWaitIndicator(AdvWaitIndicatorConfig config) { }
    // Op 0x1ce explicitly starts/stops the same animated marker that op 0x72 starts for an ADV wait.
    void SetAdvWaitIndicatorEnabled(bool enabled) { }
    // Op 0x20a republishes one retained ADV text layout and includes the current marker frame when active.
    void PublishAdvTextLayout(int layoutSlot) { }
    // Op 0x199 temporarily yields the active ADV page into its registered hide-window coroutine.
    // The retained scene continues to render, but the text layout and its wait marker are suspended
    // until op 0x7c restores the saved page PC.
    void SetAdvPagePresentationSuspended(bool suspended) { }
    void WaitForInput();
    void WaitForInput(int layoutSlot) => WaitForInput();
    // Interactive hosts service script callbacks on the VM thread while the enclosing ADV page remains
    // parked. The callback returns true while another queued input callback is ready to run.
    void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
    {
        while (serviceInputCallback()) { }
        WaitForInput(layoutSlot);
    }
    void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                      Func<AdvAutoWaitState> autoWaitState)
        => WaitForInput(layoutSlot, serviceInputCallback);
    void WakeInputCallbackService() { }
    void InputCallbackCompleted(GfxState gfx) { }
    // Generic AGE input-callback services (ops 0xcc/0xcd, 0xfb/0xff/0x100, 0x108).
    // Interactive hosts expose the same monotonic clock used by their frame scheduler.
    long InputClockMilliseconds => Environment.TickCount64;
    void SetCursorResource(long resourceId) { }
    void ClearCursorResource() { }
    void Sleep(long duration);
    void FrameYield();
    // Native op 0x9 resets scene-owned host services before reloading root script resource 0.
    // Global banks, engine configuration, decoded-asset caches, and persistent profile state survive.
    void ResetSceneContext() { }
    // Native 0x1c7/0x1cc query two distinct ADV skip channels. Headless and non-interactive
    // hosts default to normal playback; the Godot host supplies the live interactive values.
    void SetMessageSkipActive(bool active) { }
    bool IsMessageSkipActive => false;
    bool IsAdvReadSkipActive => false;
    // Normal playback reaches op 0x21c and parks until a queued 0x223 transition completes. The
    // read/message-skip branch reaches op 0x20c and presents the completed endpoint immediately.
    void WaitForForegroundTransition(GfxState gfx) { }
    void PresentFrame(GfxState gfx) { }
    void CreateTexture(int slot, int width, int height);
    void SetTexture(long resourceId, int slot);
    void ReleaseSurface(int slot) { }
    /// <summary>Clear the selected target's pixels; -1 denotes the main backbuffer.</summary>
    void ClearRenderTarget(int surfaceSlot) { }
    void ReleaseSurfaceRange(int firstSlot, int count)
    {
        for (int slot = firstSlot; slot < firstSlot + count; slot++) ReleaseSurface(slot);
    }
    void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY);
    (int Width, int Height) GetTextureSize(int slot);
    void PlayBgm(long id);
    void PlayVoice(long id);
    // Native voice playback retains a second start argument: ordinary dialogue passes 0,
    // while History replay (0x1bd) passes 1. Existing non-audio hosts may ignore it.
    void PlayVoice(long id, int playbackVariant) => PlayVoice(id);
    // Native op 0x1cf stores a transient control mask. Bit 0 suppresses the automatic
    // BGM attenuation normally applied when a voice starts.
    void SetVoiceBgmDuckControl(long flags) { }
    void LoadSoundEffect(long resourceId, int channel) { }
    void StartSoundEffect(int channel) { }
    // Native SetDelay (op 0x2bf) starts an already-loaded channel after delayMs.
    // startMode is forwarded to the same worker used by immediate SFX starts.
    void ScheduleSoundEffectStart(int channel, int startMode, long delayMs) { }
    void ReleaseSoundEffect(int channel) { }
    void FadeBgm(int targetPercent, long durationMs) { }
    // Native op 0x236 binds a DirectShow movie decoder to an existing retained texture surface.
    // Playback is non-modal: the VM advances to the following instruction while the host publishes frames.
    void PlayMovieToSurface(long resourceId, int surfaceSlot, long movieFlags, long syncMask) { }
    // Native op 0x20f uses a universal raw-catalog id and parks script execution until the movie
    // reaches EOF or the player cancels it. The decoder remains asynchronous; the interactive host
    // owns the modal wait so its render loop can continue publishing frames.
    void PlayModalMovieToSurface(long rawResourceId, int surfaceSlot, long movieFlags) { }
}
