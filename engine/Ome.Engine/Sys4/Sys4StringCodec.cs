using System.Text;
namespace Ome.Engine.Sys4;
/// <summary>
/// A script's inline (operand type 2) strings: dwords stored XOR 0xFFFFFFFF, CP932 up to the first NUL. Native AGE
/// reads every byte before the NUL (vm_operand_read_string_buffer@0x418ff0 in SYS4433) and its glyph builder lays out
/// each byte below 0x80 as a single-byte character, control characters included
/// (adv_text_append_horizontal_glyph_records@0x45b710); GDI draws the font's fallback glyph for them. So a control
/// character is a character here too. Double-byte characters must use a JIS X 0208 lead byte (0x81-0x9F, 0xE0-0xEA)
/// and a valid trail byte. The rest is refused: CP932's extension and user-defined lead bytes 0xED-0xFC, the unused
/// 0xEB-0xEC, 0x80, 0xA0 and 0xFD-0xFF. No install's script holds one (docs/backlog.md).
/// </summary>
public static class Sys4StringCodec
{
    private static readonly Encoding Cp932;
    static Sys4StringCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp932 = Encoding.GetEncoding(932);
    }

    public static (string? Text, int NDwords) Decode(IReadOnlyList<uint> dwords, int start, int limit = 4096)
    {
        int n = dwords.Count, end = Math.Min(start + limit, n);
        var raw = new List<byte>();
        bool sawNul = false;
        for (int j = start; j < end; j++)
        {
            uint x = dwords[j] ^ 0xFFFFFFFFu;
            raw.Add((byte)(x & 0xFF)); raw.Add((byte)((x >> 8) & 0xFF));
            raw.Add((byte)((x >> 16) & 0xFF)); raw.Add((byte)((x >> 24) & 0xFF));
            if (raw[^1] == 0 || raw[^2] == 0 || raw[^3] == 0 || raw[^4] == 0) { sawNul = true; break; }
        }
        if (!sawNul) return (null, 0);
        int nul = raw.IndexOf(0);
        byte[] s = raw.GetRange(0, nul < 0 ? raw.Count : nul).ToArray();
        if (s.Length < 1) return ("", 1);
        int i = 0, chars = 0;
        while (i < s.Length)
        {
            byte b = s[i];
            if (b < 0x80 || (b >= 0xA1 && b <= 0xDF)) { i++; chars++; }   // ASCII and control, half-width katakana
            else if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xEA))
            {
                if (i + 1 < s.Length && s[i + 1] >= 0x40 && s[i + 1] <= 0xFC && s[i + 1] != 0x7F) { i += 2; chars++; }
                else return (null, 0);
            }
            else return (null, 0);
        }
        if (chars < 1) return (null, 0);
        string text;
        try { text = Cp932.GetString(s); } catch { return (null, 0); }
        return (text, (s.Length / 4) + 1);
    }
}
