using System.Text;

namespace Age.Engine.Sys4;

public enum FullwidthTextValidationError
{
    None,
    TooLong,
    NonDoubleByteCharacter,
}

/// <summary>AGE's Japanese-locale byte-string operations used by INPUTNAME.</summary>
public static class Cp932Text
{
    public const int NativeNameByteLimit = 16;
    private static readonly Encoding NativeEncoding;

    static Cp932Text()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        NativeEncoding = Encoding.GetEncoding(932);
    }

    private static bool IsLeadByte(byte value)
        => value is >= 0x81 and <= 0x9f or >= 0xe0 and <= 0xfc;

    private static bool IsTrailByte(byte value)
        => value is >= 0x40 and <= 0x7e or >= 0x80 and <= 0xfc;

    private static byte[] EncodeCString(string value, Encoding encoding)
    {
        int nul = value.IndexOf('\0');
        return encoding.GetBytes(nul < 0 ? value : value[..nul]);
    }

    public static int CharacterLength(string value, Encoding encoding)
    {
        byte[] bytes = EncodeCString(value, encoding);
        int count = 0;
        for (int offset = 0; offset < bytes.Length; count++)
        {
            if (IsLeadByte(bytes[offset]) && offset + 1 < bytes.Length
                                          && IsTrailByte(bytes[offset + 1]))
                offset += 2;
            else
                offset++;
        }
        return count;
    }

    public static string Substring(
        string value, int start, int count, Encoding encoding)
    {
        byte[] bytes = EncodeCString(value, encoding);
        var characters = new List<(int Offset, int Length)>();
        for (int offset = 0; offset < bytes.Length;)
        {
            int length = IsLeadByte(bytes[offset]) && offset + 1 < bytes.Length
                                                  && IsTrailByte(bytes[offset + 1])
                ? 2
                : 1;
            characters.Add((offset, length));
            offset += length;
        }

        int end = unchecked(start + count);
        if (end < 1 || end > characters.Count) end = characters.Count;

        using var selected = new MemoryStream();
        for (int index = 0; index < characters.Count; index++)
        {
            if (index < start || index >= end) continue;
            (int offset, int length) = characters[index];
            selected.Write(bytes, offset, length);
        }
        return encoding.GetString(selected.ToArray());
    }

    public static FullwidthTextValidationError ValidateFullwidthName(
        string value, Encoding encoding, int byteLimit = NativeNameByteLimit)
    {
        byte[] bytes = EncodeCString(value, encoding);
        if (bytes.Length > byteLimit) return FullwidthTextValidationError.TooLong;

        for (int offset = 0; offset < bytes.Length; offset += 2)
        {
            if (offset + 1 >= bytes.Length
                || !IsLeadByte(bytes[offset])
                || !IsTrailByte(bytes[offset + 1]))
                return FullwidthTextValidationError.NonDoubleByteCharacter;
        }
        return FullwidthTextValidationError.None;
    }

    public static FullwidthTextValidationError ValidateFullwidthName(
        string value, int byteLimit = NativeNameByteLimit)
        => ValidateFullwidthName(value, NativeEncoding, byteLimit);
}
