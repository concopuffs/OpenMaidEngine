namespace Age.Engine.Sys4;

/// <summary>Eushully's 4 KiB-ring LZSS stream used by SYS4 catalogs and AGF sections.</summary>
public static class LzssDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> source, int expectedSize, string name = "LZSS")
    {
        if (expectedSize < 0) throw new InvalidDataException($"{name}: negative expanded size");
        var frame = new byte[0x1000];
        var result = new byte[expectedSize];
        int framePos = 0xfee, input = 0, output = 0;
        while (output < expectedSize)
        {
            if (input >= source.Length) throw new InvalidDataException($"{name}: stream ended early");
            int control = source[input++];
            for (int bit = 1; bit <= 0x80 && output < expectedSize; bit <<= 1)
            {
                if ((control & bit) != 0)
                {
                    if (input >= source.Length) throw new InvalidDataException($"{name}: truncated literal");
                    byte value = source[input++];
                    result[output++] = value;
                    frame[framePos] = value;
                    framePos = (framePos + 1) & 0xfff;
                }
                else
                {
                    if (input > source.Length - 2) throw new InvalidDataException($"{name}: truncated back-reference");
                    int lo = source[input++], hi = source[input++];
                    int readPos = ((hi & 0xf0) << 4) | lo;
                    int length = 3 + (hi & 0x0f);
                    for (int j = 0; j < length && output < expectedSize; j++)
                    {
                        byte value = frame[readPos++ & 0xfff];
                        result[output++] = value;
                        frame[framePos] = value;
                        framePos = (framePos + 1) & 0xfff;
                    }
                }
            }
        }
        return result;
    }
}
