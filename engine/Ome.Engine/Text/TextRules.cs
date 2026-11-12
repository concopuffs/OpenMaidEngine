namespace Ome.Engine.Text;

/// <summary>
/// The text rules an engine build gives the text natives (docs/superpowers/specs/2026-11-12-engine-layering-design.md
/// §7.3). Each member is one behaviour, named for what it does; the builds that differ are declared in
/// <c>Vm/EngineBuilds.cs</c>.
/// </summary>
/// <param name="GlyphCoverage">How the glyph blit turns a gray-4 coverage value into alpha: in sixteenths
/// (text_blit_glyph_bitmap@0x458c80 in 4.24J) or seventeenths (text_blit_glyph_bitmap_sys4433@0x45cff0 in 4.33G);
/// <see cref="GlyphCoverageAlpha"/>.</param>
/// <param name="Ruby">Where furigana are placed, how they are spaced and how they publish
/// (<see cref="RubyRule"/>; docs/engine-re.md, "Furigana").</param>
/// <param name="FuriganaArmsGlyphTimer">Whether op 0x196 re-arms the glyph timer (<c>ctx+0x5f630</c>) as op 0x6e
/// does. 4.33G's handler does; 4.24J's op_0x196_handler@0x41f2a0 does not, so its ruby run keeps the previous run's
/// reveal phase (docs/engine-re.md, "The reveal clock").</param>
public sealed record TextRules(GlyphCoverageAlpha GlyphCoverage, RubyRule Ruby, bool FuriganaArmsGlyphTimer);
