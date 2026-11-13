using System;
using Ome.Engine.Persistence;

namespace Ome.Engine.Hosting;

// SYS4REG.INI's [message] keys the host still carries: it seeds the Auto delays into the skip and auto native at
// construction and writes the message settings back at shutdown.
public sealed partial class EngineHost
{
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
}
