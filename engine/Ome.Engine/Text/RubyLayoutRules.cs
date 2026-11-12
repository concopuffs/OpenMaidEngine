using Ome.Engine.Sys4;

namespace Ome.Engine.Text;

/// <summary>
/// How an engine build chooses its furigana rules (op 0x196; docs/engine-re.md, "Furigana"): the algorithm differs
/// between builds, so each is one implementation and the build names it (docs/superpowers/specs/2026-11-12-engine-layering-design.md
/// §7.3, form 3).
/// </summary>
public abstract class RubyRule
{
    private RubyRule() { }

    /// <summary>The same offsets whatever the settings: 4.24J's adv_text_draw_ruby_over_row@0x456b40 and History's
    /// text_history_render_records@0x4526c0.</summary>
    public static RubyRule FixedOffsets { get; } = new Fixed();

    /// <summary>Offsets chosen by <c>message:RubyShiftMode</c> and <c>set:DrawMode</c>: 4.33G's
    /// adv_text_draw_ruby_over_row_sys4433@0x4590d0 and its History renderer @0x456490.</summary>
    public static RubyRule ShiftModes { get; } = new ShiftMode();

    /// <summary>The rules for a RubyShiftMode and a DrawMode.</summary>
    public abstract RubyLayoutRules For(int rubyShiftMode, int drawMode);

    /// <summary>The rules for SYS4INI's settings (DRAWMODE, RUBYSHIFTMODE). <paramref name="rubyShiftMode"/> is
    /// message:RubyShiftMode as loaded, when a settings file is in use
    /// (<see cref="Persistence.Sys4RegIniStore.RubyShiftMode"/>).</summary>
    public RubyLayoutRules For(Sys4StartupSettings settings, int? rubyShiftMode = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return For(rubyShiftMode ?? settings.RubyShiftMode, settings.DrawMode);
    }

    private sealed class Fixed : RubyRule
    {
        public override RubyLayoutRules For(int rubyShiftMode, int drawMode) => RubyLayoutRules.FixedOffsets;
    }

    private sealed class ShiftMode : RubyRule
    {
        public override RubyLayoutRules For(int rubyShiftMode, int drawMode)
            => RubyLayoutRules.ShiftModes(rubyShiftMode, drawMode);
    }
}

/// <summary>
/// The furigana rules the text natives apply (op 0x196; docs/engine-re.md, "Furigana"). A build picks them through its
/// <see cref="RubyRule"/>.
/// </summary>
public sealed record RubyLayoutRules
{
    /// <summary><see cref="RubyRule.FixedOffsets"/>'s rules (4.24J).</summary>
    public static RubyLayoutRules FixedOffsets { get; } = new()
    {
        HalfWidthAwareSpacing = false,
        LiveTopOffset = 1,
        HistoryTopOffset = 1,
        RaiseByEffectInMode3 = true,
        ChainEveryRubyRecord = false,
    };

    /// <summary>
    /// <see cref="RubyRule.ShiftModes"/>'s rules (4.33G) for its <c>message:RubyShiftMode</c> and <c>set:DrawMode</c>:
    /// the live ruby starts one pixel lower only when DrawMode is not 1 and RubyShiftMode is 0; History never adds the
    /// pixel; a mode-3 effect raises the ruby by its y offset only when RubyShiftMode is 0. (With RubyShiftMode 0 and
    /// DrawMode 1, History's sprite publication also samples its source one pixel lower, which the port does not
    /// model: no shipped game on this build runs that way.)
    /// </summary>
    public static RubyLayoutRules ShiftModes(int rubyShiftMode, int drawMode) => new()
    {
        HalfWidthAwareSpacing = true,
        LiveTopOffset = drawMode != 1 && rubyShiftMode == 0 ? 1 : 0,
        HistoryTopOffset = 0,
        RaiseByEffectInMode3 = rubyShiftMode == 0,
        ChainEveryRubyRecord = true,
    };

    /// <summary>The gap divides by <c>(bytes + 3) >> 1</c> instead of <c>bytes / 2 + 1</c>, a single-byte ruby
    /// character is followed by half the gap, and a control character draws as a space (4.33G).</summary>
    public bool HalfWidthAwareSpacing { get; init; }

    /// <summary>Pixels added to <c>baseTop + lfHeight</c> for the live ruby.</summary>
    public int LiveTopOffset { get; init; }

    /// <summary>Pixels added to <c>rowTop + lfHeight</c> for History's ruby.</summary>
    public int HistoryTopOffset { get; init; }

    /// <summary>Whether a mode-3 effect raises the ruby by its y offset.</summary>
    public bool RaiseByEffectInMode3 { get; init; }

    /// <summary>Every ruby record gets chain flag 1 and the pending record resets to 0, so a run's whole ruby
    /// publishes in one tick and the next run starts its own (4.33G). Otherwise (4.24J) the record chains after each
    /// ruby character.</summary>
    public bool ChainEveryRubyRecord { get; init; }
}
