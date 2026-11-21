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
/// <param name="ExtendedGlyphFace">The face that draws a character whose Shift-JIS code is 0xF040 to 0xF9FC, the
/// user-defined area, whatever face the text is in (<see cref="ExtendedFaceGlyphRasterizer"/>). 4.38D's glyph
/// drawers select <c>AGE Extend</c>, a font the game ships in its catalog; null draws such a character in the
/// text's own face (4.24J, 4.33G).</param>
/// <param name="AspectCalibration">What aspect mode 1 divides a font's narrower black box by to set its width
/// (<see cref="Text.AspectCalibration"/>). 4.24J has no aspect mode and never reads it.</param>
/// <param name="PublisherGrowsGlyphCells">Whether the glyph publisher makes a glyph object's source rectangle taller
/// than the glyph's record when a text effect is on, by the effect's vertical offset as the text manager holds it at
/// that moment. 4.33G's and 4.38D's publishers do (adv_text_publish_next_glyph@0x4509f0 and @0x4541b0 once for a
/// record that is not ruby; adv_text_publish_layout@0x4503b0 and @0x453990 once, and once more in effect mode 3; op
/// 0x82's redraw as the layout publisher, to each record that is not ruby; History's renderer once more than the record
/// in effect mode 3). 4.24J's (@0x451220, @0x450c80) bind the record's rectangle as it is (docs/engine-re.md, "The
/// glyph publisher grows a cell").</param>
/// <param name="PublisherMovesAliasedEffectGlyphs">Whether the glyph publishers move a glyph object that is not ruby
/// left and up by the effect offsets in effect mode 3 while glyphs are not antialiased (<c>message:UseAntiFont</c> 0,
/// the text manager's +0x548 in these builds). 4.33G's and 4.38D's do, all four: the timed and layout publishers, op
/// 0x82's redraw and History's renderer; 4.24J's do not, so its text drawn with AntiFont off lands one pixel right and
/// down (docs/engine-re.md, "AntiFont off in 4.33G and 4.38D").</param>
/// <param name="SubstringKeepsDoubleByteCharacters">Whether op 0x2c7 keeps a double-byte character whole when the
/// byte interval cuts it. 4.38D's handler is SBSubstr (op_0x2c7_byte_substring@0x42fe10). 4.24J's (@0x42c2f0) and
/// 4.33G's (@0x42d220) are std::string's substr of the bytes, with no care for such a character, and they throw
/// out_of_range for a start past the end, which ends the program (docs/engine-re.md, "The substring").</param>
public sealed record TextRules(GlyphCoverageAlpha GlyphCoverage, RubyRule Ruby, bool FuriganaArmsGlyphTimer,
                               bool HistoryLookupTestsSign, LineEndRule LineEnd, string? ExtendedGlyphFace,
                               AspectCalibration AspectCalibration, bool PublisherGrowsGlyphCells,
                               bool PublisherMovesAliasedEffectGlyphs = false,
                               bool SubstringKeepsDoubleByteCharacters = false);
