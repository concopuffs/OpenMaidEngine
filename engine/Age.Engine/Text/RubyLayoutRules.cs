using Age.Engine.Sys4;

namespace Age.Engine.Text;

/// <summary>
/// An engine revision's furigana rules (op 0x196; docs/engine-re.md, "Furigana"). SYS4422 (Himegari) draws a ruby
/// with adv_text_draw_ruby_over_row@0x456b40 and History's text_history_render_records@0x4526c0; SYS4433 (Kamidori)
/// with adv_text_draw_ruby_over_row_sys4433@0x4590d0 and its History renderer @0x456490.
/// </summary>
public sealed record RubyLayoutRules
{
    /// <summary>Himegari's engine.</summary>
    public static RubyLayoutRules Sys4422 { get; } = new()
    {
        Sys4433Spacing = false,
        LiveTopOffset = 1,
        HistoryTopOffset = 1,
        RaiseByEffectInMode3 = true,
        ChainEveryRubyRecord = false,
        ArmsGlyphTimer = false,
    };

    /// <summary>
    /// SYS4433 with its <c>message:RubyShiftMode</c> and <c>set:DrawMode</c>: the live ruby starts one pixel lower
    /// only when DrawMode is not 1 and RubyShiftMode is 0; History never adds the pixel; a mode-3 effect raises the
    /// ruby by its y offset only when RubyShiftMode is 0. (With RubyShiftMode 0 and DrawMode 1, History's sprite
    /// publication also samples its source one pixel lower, which the port does not model: no shipped SYS4433 game
    /// runs that way.)
    /// </summary>
    public static RubyLayoutRules Sys4433(int rubyShiftMode, int drawMode) => new()
    {
        Sys4433Spacing = true,
        LiveTopOffset = drawMode != 1 && rubyShiftMode == 0 ? 1 : 0,
        HistoryTopOffset = 0,
        RaiseByEffectInMode3 = rubyShiftMode == 0,
        ChainEveryRubyRecord = true,
        ArmsGlyphTimer = true,
    };

    /// <summary>The rules for a profile's engine ABI and its SYS4INI settings (DRAWMODE, RUBYSHIFTMODE).
    /// <paramref name="rubyShiftMode"/> is message:RubyShiftMode as loaded, when a settings file is in use
    /// (<see cref="Persistence.Sys4RegIniStore.RubyShiftMode"/>).</summary>
    public static RubyLayoutRules For(string engineAbiId, Sys4StartupSettings settings, int? rubyShiftMode = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return IsSys4433(engineAbiId) ? Sys4433(rubyShiftMode ?? settings.RubyShiftMode, settings.DrawMode) : Sys4422;
    }

    /// <summary>The rules for an engine ABI with registered-default settings, for callers that read only what does not
    /// depend on settings (<see cref="ArmsGlyphTimer"/>).</summary>
    public static RubyLayoutRules ForEngine(string engineAbiId)
        => IsSys4433(engineAbiId) ? Sys4433(rubyShiftMode: 0, drawMode: 0) : Sys4422;

    private static bool IsSys4433(string engineAbiId) => EngineRevisions.IsSys4433(engineAbiId);

    /// <summary>SYS4433's spacing: the gap divides by <c>(bytes + 3) >> 1</c> instead of <c>bytes / 2 + 1</c>, a
    /// single-byte ruby character is followed by half the gap, and a control character draws as a space.</summary>
    public bool Sys4433Spacing { get; init; }

    /// <summary>Pixels added to <c>baseTop + lfHeight</c> for the live ruby.</summary>
    public int LiveTopOffset { get; init; }

    /// <summary>Pixels added to <c>rowTop + lfHeight</c> for History's ruby.</summary>
    public int HistoryTopOffset { get; init; }

    /// <summary>Whether a mode-3 effect raises the ruby by its y offset.</summary>
    public bool RaiseByEffectInMode3 { get; init; }

    /// <summary>SYS4433 gives every ruby record chain flag 1 and resets the pending record to 0, so a run's whole ruby
    /// publishes in one tick and the next run starts its own. SYS4422 chains the record after each ruby
    /// character instead.</summary>
    public bool ChainEveryRubyRecord { get; init; }

    /// <summary>Whether op 0x196 re-arms the glyph timer as op 0x6e does (SYS4433's handler does; SYS4422's does
    /// not).</summary>
    public bool ArmsGlyphTimer { get; init; }
}
