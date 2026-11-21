using System.Text;

namespace Ome.Engine.Sys4;

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

    /// <summary>
    /// AGE 4.38D's SBSubstr (op 0x2c7): <paramref name="count"/> bytes of the string from byte
    /// <paramref name="start"/>, kept from splitting a double-byte character. A start in the middle of one moves past
    /// its second byte (<paramref name="startMoved"/>), and an end in the middle of one drops its first byte
    /// (<paramref name="endMoved"/>); native traces a warning for each. A start at or past the string's end, compared
    /// unsigned, or a count below 1 gives the empty string. Its lead bytes are 0x81 to 0x9f and 0xe0 up.
    /// </summary>
    public static string ByteSubstring(string value, int start, int count, Encoding encoding,
                                       out bool startMoved, out bool endMoved)
    {
        startMoved = endMoved = false;
        byte[] bytes = EncodeCString(value, encoding);
        if (unchecked((uint)start) >= (uint)bytes.Length || count < 1) return "";
        if (start > 0 && LeadByteEndsAt(bytes, start) is { } first && first < bytes.Length)
        {
            startMoved = true;
            start++;
            count--;
        }
        int end = unchecked(start + count);
        if (end > 0 && LeadByteEndsAt(bytes, end) is { } last && last < bytes.Length)
        {
            endMoved = true;
            count--;
        }
        int available = bytes.Length - start;
        if (available <= 0 || count <= 0) return "";
        return encoding.GetString(bytes, start, System.Math.Min(count, available));

        // Native walks the bytes before the limit; the count it stopped at, when the last byte walked is the first of
        // a double-byte character, and null otherwise.
        static int? LeadByteEndsAt(byte[] bytes, int limit)
        {
            bool lead = false;
            int index = 0;
            for (; index < limit && index < bytes.Length; index++)
                lead = !lead && (bytes[index] is >= 0x81 and <= 0x9f || bytes[index] >= 0xe0);
            return lead ? index : null;
        }
    }

    /// <summary>
    /// AGE 4.24J's and 4.33G's op 0x2c7: std::string's substr(<paramref name="start"/>, <paramref name="count"/>) of the
    /// string's bytes, with no care for double-byte characters. The count is unsigned, so a negative one takes the rest
    /// of the string. A start past the end, compared unsigned, is null: native throws out_of_range there. A start at the
    /// end gives the empty string. An interval that cuts a double-byte character keeps the lone byte, as native does;
    /// the VM's <see cref="Sys4TextEncoding"/> holds it as a private-use character.
    /// </summary>
    public static string? PlainByteSubstring(string value, int start, int count, Encoding encoding)
    {
        byte[] bytes = EncodeCString(value, encoding);
        if (unchecked((uint)start) > (uint)bytes.Length) return null;
        int available = bytes.Length - start;
        int length = unchecked((uint)count) < (uint)available ? count : available;
        return encoding.GetString(bytes, start, length);
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
