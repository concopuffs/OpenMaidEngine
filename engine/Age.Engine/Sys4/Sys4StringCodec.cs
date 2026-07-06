using System.Text;
namespace Age.Engine.Sys4;
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
            if (b >= 0x20 && b <= 0x7E) { i++; chars++; }
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
