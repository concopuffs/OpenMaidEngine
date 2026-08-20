using Age.Engine.Model;

namespace Age.Engine.Hosting;

public interface IAdvHost
{
    void ShowText(int offset, string text);
    void ShowText(AdvLiveTextRun run, int glyphDelayMilliseconds)
        => ShowText(run.SourceOffset, run.Text);
    AdvRetainedTextRunResult? ShowText(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvLiveTextRun run,
        int glyphDelayMilliseconds)
    {
        ShowText(run, glyphDelayMilliseconds);
        return null;
    }
    int MessageGlyphDelayMilliseconds => 50;
    void SetMessageGlyphDelayMilliseconds(int milliseconds) { }
    // Native ADV text subsystem: op 0x7a updates the selected layout's last 20-byte cursor record;
    // op 0x204 rasterizes a string into a numbered surface before 0x1fb binds that surface.
    void SetAdvTextCursor(int layoutSlot, int x, int y) { }
    void DrawStringToSurface(int surfaceSlot, int x, int y, string text) { }
    void DrawStringToSurface(int surfaceSlot, int x, int y, string text, AdvTextStyle style)
        => DrawStringToSurface(surfaceSlot, x, y, text);
    // SYS4433 op 0x2de exposes AGE's installed-font-family cache to scripts. Return a stable
    // non-negative cache index for an available/renderable family, or -1 when unavailable.
    int FindFontFamilyIndex(string faceName) => -1;
    // SYS4433 op 0x2dc returns the installed-family vector size, using -1 for an empty cache.
    int FontFamilyCount => -1;
    void ClearRenderedAdvTextLayout(int layoutSlot) { }
    void ResetRenderedAdvTextLayout(
        GfxState gfx, AdvTextLayoutPresentationBinding binding)
        => ClearRenderedAdvTextLayout(binding.LayoutSlot);
    void RenderTextHistory(AdvTextHistoryRenderBatch batch) { }
    bool RenderTextHistory(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvTextHistoryRenderBatch batch)
    {
        RenderTextHistory(batch);
        return false;
    }
    // History render batches are transient bindings, unlike the retained backlog itself. HISTORY.BIN's
    // recording re-enable at exit ends that presentation and drops every bound target layout.
    void EndTextHistoryPresentation() { }
    void EndTextHistoryPresentation(GfxState gfx) => EndTextHistoryPresentation();
    int MessageWindowAlphaSetting => 0;
    void SetMessageWindowAlphaSetting(int value) { }
    void ConfigureAdvWaitIndicator(AdvWaitIndicatorConfig config) { }
    void SetAdvWaitIndicatorFollowLastGlyph(bool enabled) { }
    // Op 0x212 supplies the ordinary retained handle used to publish the configured atlas cell.
    void BindAdvWaitIndicator(
        AdvTextLayoutPresentationBinding binding,
        AdvTextLayoutSnapshot layout) { }
    // Op 0x1ce explicitly starts/stops the same animated marker that op 0x72 starts for an ADV wait.
    void SetAdvWaitIndicatorEnabled(bool enabled) { }
    // Op 0x20a republishes one retained ADV text layout and includes the current marker frame when active.
    void PublishAdvTextLayout(int layoutSlot) { }
    void PublishAdvTextLayout(
        GfxState gfx, AdvTextLayoutPresentationBinding binding)
        => PublishAdvTextLayout(binding.LayoutSlot);
    // Op 0x199 temporarily yields the active ADV page into its registered hide-window coroutine.
    // The retained scene continues to render, but the text layout and its wait marker are suspended
    // until op 0x7c restores the saved page PC.
    void SetAdvPagePresentationSuspended(bool suspended) { }
    void SetAdvPagePresentationSuspended(GfxState gfx, bool suspended)
        => SetAdvPagePresentationSuspended(suspended);
}
