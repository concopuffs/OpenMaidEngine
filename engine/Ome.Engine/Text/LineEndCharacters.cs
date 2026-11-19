using System.Text;
using Ome.Engine.Sys4;

namespace Ome.Engine.Text;

/// <summary>
/// How an engine build chooses the characters a horizontal ADV layout keeps on the line they overflow
/// (docs/engine-re.md, "Kamidori against Arterial, triaged"): where they come from differs between builds, so the
/// build names the source (docs/superpowers/specs/2026-11-12-engine-layering-design.md §7.3, form 3).
/// </summary>
public abstract class LineEndRule
{
    private LineEndRule() { }

    /// <summary>The same three whatever the catalog says: 4.24J's and 4.33G's layout builders
    /// (adv_text_append_horizontal_glyph_records@0x45b710 in 4.33G) compare the glyph's bytes with <c>81 41</c>,
    /// <c>81 42</c> and <c>81 76</c>.</summary>
    public static LineEndRule FixedThree { get; } = new Fixed();

    /// <summary>The catalog trailer's <c>JUSTIFYCHAR</c> values and nothing else: 4.38D's builder calls
    /// text_is_justify_character@0x457950, which compares the glyph's bytes with each entry of a list
    /// engine_initialize@0x416c00 fills from those pairs. A catalog without the key keeps no character.</summary>
    public static LineEndRule CatalogList { get; } = new Catalog();

    /// <summary>The characters for an install's settings.</summary>
    public abstract LineEndCharacters For(Sys4StartupSettings settings);

    private sealed class Fixed : LineEndRule
    {
        public override LineEndCharacters For(Sys4StartupSettings settings) => LineEndCharacters.FixedThree;
    }

    private sealed class Catalog : LineEndRule
    {
        public override LineEndCharacters For(Sys4StartupSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return LineEndCharacters.Of(settings.JustifyCharacters);
        }
    }
}

/// <summary>
/// The characters a horizontal ADV layout does not wrap before: one that crosses the right bound stays on its line
/// (<see cref="RetainedGlyphLayoutEngine"/>). A build picks them through its <see cref="LineEndRule"/>.
/// </summary>
public sealed class LineEndCharacters
{
    private readonly HashSet<ushort> _codes;

    private LineEndCharacters(IEnumerable<ushort> codes) => _codes = [.. codes];

    /// <summary>、 。 and 」 (<see cref="LineEndRule.FixedThree"/>).</summary>
    public static LineEndCharacters FixedThree { get; } = new([0x8141, 0x8142, 0x8176]);

    /// <summary>No character: every glyph that crosses the bound wraps.</summary>
    public static LineEndCharacters None { get; } = new([]);

    /// <summary>
    /// The characters <paramref name="entries"/> name (<see cref="LineEndRule.CatalogList"/>). Native compares a
    /// glyph's one or two CP932 bytes with a whole entry, so an entry that is not exactly one character matches no
    /// glyph and is left out, as is a character CP932 lacks.
    /// </summary>
    public static LineEndCharacters Of(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Encoding cp932 = Sys4TextEncoding.Strict;
        var codes = new List<ushort>();
        foreach (string entry in entries)
        {
            byte[] bytes;
            try { bytes = cp932.GetBytes(entry ?? ""); }
            catch (EncoderFallbackException) { continue; }
            if (bytes.Length == 1) codes.Add(bytes[0]);
            else if (bytes.Length == 2 && bytes[0] is >= 0x81 and <= 0x9f or >= 0xe0 and <= 0xfc)
                codes.Add((ushort)(bytes[0] << 8 | bytes[1]));
        }
        return new LineEndCharacters(codes);
    }

    /// <summary>The CP932 codes, a single byte as its value and a double byte as lead × 256 + trail.</summary>
    public IReadOnlyCollection<ushort> Codes => _codes;

    /// <summary>Whether a glyph with this CP932 code stays on the line it overflows.</summary>
    public bool Contains(ushort cp932) => _codes.Contains(cp932);
}
