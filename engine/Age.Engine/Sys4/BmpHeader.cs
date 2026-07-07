namespace Age.Engine.Sys4;

/// <summary>
/// Reads pixel dimensions from a BMP file header (BITMAPINFOHEADER: width at byte 18, height at byte 22,
/// both little-endian int32; height may be negative for top-down bitmaps). Used to give the VM the
/// texture size that opcode 0x208 (get-texture-size) needs, without decoding pixels. Our textures are
/// pre-converted BMPs (tools/convert_agf.py).
/// </summary>
public static class BmpHeader
{
    public static (int Width, int Height) ReadDims(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return (0, 0);
        try
        {
            var b = new byte[26];
            using var fs = File.OpenRead(path);
            if (fs.Read(b, 0, 26) < 26 || b[0] != (byte)'B' || b[1] != (byte)'M') return (0, 0);
            int w = System.BitConverter.ToInt32(b, 18);
            int h = System.BitConverter.ToInt32(b, 22);
            return (System.Math.Abs(w), System.Math.Abs(h));
        }
        catch { return (0, 0); }
    }
}
