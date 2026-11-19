namespace Ome.Engine.Text;

/// <summary>
/// The text rules an engine build gives the text natives (docs/superpowers/specs/2026-11-12-engine-layering-design.md
/// §7.3). Each member is one behaviour, named for what it does; the builds that differ are declared in
/// <c>Ome.Engine.Vm/EngineBuilds.cs</c>.
/// </summary>
/// <param name="GlyphCoverage">How the glyph blit turns a gray-4 coverage value into alpha: in sixteenths
/// (text_blit_glyph_bitmap@0x458c80 in 4.24J) or seventeenths (text_blit_glyph_bitmap_sys4433@0x45cff0 in 4.33G);
/// <see cref="GlyphCoverageAlpha"/>.</param>
/// <param name="Ruby">Where furigana are placed, how they are spaced and how they publish
/// (<see cref="RubyRule"/>; docs/engine-re.md, "Furigana").</param>
/// <param name="FuriganaArmsGlyphTimer">Whether op 0x196 re-arms the glyph timer (<c>ctx+0x5f630</c>) as op 0x6e
/// does. 4.33G's handler does; 4.24J's op_0x196_handler@0x41f2a0 does not, so its ruby run keeps the previous run's
/// reveal phase (docs/engine-re.md, "The reveal clock").</param>
/// <param name="HistoryLookupTestsSign">Whether ops 0x1d3 and 0x1d4 refuse a negative record index as they refuse one
/// past the last record, finding nothing. 4.33G's text_history_find_typed_value@0x4501b0 and
/// text_history_find_pair@0x450270 do; 4.24J's @0x450840 and @0x4509f0 test only the upper end, and the negative index
/// fails a checked subscript, which ends the process (docs/engine-re.md, "Indices outside their tables").</param>
/// <param name="LineEnd">Which characters a horizontal ADV layout keeps on the line they overflow: a fixed three
/// (4.24J, 4.33G) or the catalog's <c>JUSTIFYCHAR</c> list (4.38D); <see cref="LineEndRule"/>.</param>
public sealed record TextRules(GlyphCoverageAlpha GlyphCoverage, RubyRule Ruby, bool FuriganaArmsGlyphTimer,
                               bool HistoryLookupTestsSign, LineEndRule LineEnd);
