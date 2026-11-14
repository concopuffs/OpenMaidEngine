using Ome.Engine.Text;

namespace Ome.Engine.Localization;

/// <summary>
/// The length of presented text as the length opcodes report it (localization spec §5.3): native AGE's counts of the
/// same text, through the one width rule the layouts use (<see cref="CharacterCells"/>). For text CP932 encodes, each
/// is exactly what native returns for its bytes; the result depends on no font.
/// </summary>
public static class TextMeasurement
{
    /// <summary>The half cells of <paramref name="text"/>: what <c>strlen</c> (op <c>0x2c5</c>) returns for it.</summary>
    public static int HalfCells(string text) => CharacterCells.HalfCells(text);

    /// <summary>What <c>halve-strlen</c> (op <c>0x1a6</c>) returns for a length in half cells: half, truncated, as
    /// native's.</summary>
    public static int Halve(int halfCells) => halfCells / 2;

    /// <summary>The characters (Unicode scalars) of <paramref name="text"/>: what <c>cp932-character-length</c>
    /// (op <c>0x2c6</c>) returns for it.</summary>
    public static int Characters(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++, count++)
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
        return count;
    }
}
