using System.Text;
namespace Ome.Engine.Sys4;
/// <summary>
/// A script's inline (operand type 2) strings: dwords stored XOR 0xFFFFFFFF, bytes up to the first NUL. Native AGE
/// reads every byte before the NUL (vm_operand_read_string_buffer@0x418ff0 in SYS4433) and checks none, so every
/// such string decodes, through <see cref="Sys4TextEncoding"/>, which encodes it back byte for byte. Only a string
/// with no NUL within the limit is refused.
/// </summary>
public static class Sys4StringCodec
{
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
        return (Sys4TextEncoding.Strict.GetString(s), (s.Length / 4) + 1);
    }
}
