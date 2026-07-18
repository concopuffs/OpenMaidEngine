using Age.Engine.Model;

namespace Age.Engine.Hosting;

public readonly record struct AdvWaitIndicatorConfig(
    int LayoutSlot, int X, int Y, int SurfaceSlot,
    int SourceX, int SourceY, int CellWidth, int CellHeight,
    int TerminalFrame, long FramePeriodMs);

public readonly record struct AdvAutoWaitState(
    bool Enabled, bool VoicePending, long PostVoiceDelayMs, long UnvoicedDelayMs);

public interface IHost
{
    void ShowText(int offset, string text);
    // Native ADV text subsystem: op 0x7a updates the selected layout's last 20-byte cursor record;
    // op 0x204 rasterizes a string into a numbered surface before 0x1fb binds that surface.
    void SetAdvTextCursor(int layoutSlot, int x, int y) { }
    void DrawStringToSurface(int surfaceSlot, int x, int y, string text) { }
    void ConfigureAdvWaitIndicator(AdvWaitIndicatorConfig config) { }
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
    void Sleep(long duration);
    void FrameYield();
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
    void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY);
    (int Width, int Height) GetTextureSize(int slot);
    void PlayBgm(long id);
    void PlayVoice(long id);
    void LoadSoundEffect(long resourceId, int channel) { }
    void StartSoundEffect(int channel) { }
    void ReleaseSoundEffect(int channel) { }
    void FadeBgm(int targetPercent, long durationMs) { }
    // Native op 0x236 binds a DirectShow movie decoder to an existing retained texture surface.
    // Playback is non-modal: the VM advances to the following instruction while the host publishes frames.
    void PlayMovieToSurface(long resourceId, int surfaceSlot, long movieFlags, long syncMask) { }
}
