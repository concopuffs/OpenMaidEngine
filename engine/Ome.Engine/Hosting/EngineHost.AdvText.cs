using System;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;

namespace Ome.Engine.Hosting;

// Text is the text native's (Natives/TextManager*.cs). The host keeps the reveal wait, which blocks the engine thread
// until the native says the run is out, and its part of the page wait (EngineHost.PresentationInput.cs), until
// milestone 11; a click, fast-forward and a stop finish the native's reveal.
public sealed partial class EngineHost
{
    private readonly TextManager _text;

    /// <summary>
    /// Seed AutoMessageTime0/1 (into the skip and auto native) from the save native's SYS4REG.INI [message], native
    /// defaults when absent; the text native reads MessageSpeed, MesWinAlpha and UseAntiFont itself. Changes are written
    /// back by <see cref="FlushMessageSettings"/> at shutdown, with ReadTextSkip, as AGE rewrites SYS4REG.INI when it
    /// quits. The constructor runs it, before the VM is constructed.
    /// </summary>
    private void SeedMessageSettings(Sys4RegIniStore store)
    {
        _skip.SetAutoMessageTime(0, store.LoadMessageInt(
            Sys4RegIniStore.AutoMessageTime0Key, Sys4RegIniStore.DefaultAutoMessageTime0));
        _skip.SetAutoMessageTime(1, store.LoadMessageInt(
            Sys4RegIniStore.AutoMessageTime1Key, Sys4RegIniStore.DefaultAutoMessageTime1));
    }

    /// <summary>The shutdown write of the message settings into the save native's SYS4REG.INI: the text native's
    /// MessageSpeed and MesWinAlpha, the skip and auto native's Auto delays, and message:ReadTextSkip, which the shared
    /// profile carried while the game ran.</summary>
    public void FlushMessageSettings()
    {
        // Unconditional, like AGE on quit: the write also leaves every key AGE binds (Sys4RegIniStore).
        if (_saves.Settings is not { } store) return;
        bool readTextSkip = _saves.Profile.ReadMessageSkipEnabled;
        try
        {
            _text.WriteSettings(store);
            // The registry keeps a 32-bit value: op 0x1b9's operand wraps as AGE's int does.
            store.SaveMessageInt(Sys4RegIniStore.AutoMessageTime0Key,
                                 unchecked((int)_skip.AutoMessageTime0Ms));
            store.SaveMessageInt(Sys4RegIniStore.AutoMessageTime1Key,
                                 unchecked((int)_skip.AutoMessageTime1Ms));
            store.SaveReadTextSkip(readTextSkip);
        }
        catch (Exception error) when (
            error is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            _frontEnd.Warn($"[settings] message settings save failed: {error.Message}");
        }
    }

    /// <summary>
    /// The reveal wait: the engine thread services the text native's reveal at each engine tick until its last glyph is
    /// out, a click or fast-forward finishes it, or the engine stops. Presentation is requested, not awaited: a frame
    /// wait would make the poll late and drop boundaries.
    /// </summary>
    public void WaitForTextReveal(GfxState gfx, TextManager text)
    {
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            _graphics.RequestPresentation();
            while (!_stopping && text.ServiceReveal(gfx) is int waitMs)
                WaitAndTick(_frameSignal, waitMs);
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        _timeline?.State("running", new() { ["text_reveal_complete"] = true });
    }
}
