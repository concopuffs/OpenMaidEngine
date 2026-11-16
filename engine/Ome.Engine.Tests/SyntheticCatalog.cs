using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Ome.Engine.Sys4;

/// <summary>
/// A synthetic SYS4INI.BIN: the catalog header (revision and title, a game's identity), the file list of one archive,
/// and the startup settings, packed as literal LZSS. <c>Sys4StartupSettingsTests</c> forwards to it, and the launcher's
/// tests compile this same file (launcher plan §5.12), so both build an install from one source.
/// </summary>
internal static class SyntheticCatalog
{
    /// <summary>A catalog listing <paramref name="fileNames"/>, each an empty entry of DATA.ALF, under the catalog
    /// revision <paramref name="magic"/> and <paramref name="title"/>.</summary>
    public static byte[] Build(IReadOnlyList<string> fileNames, string magic = "S4IC422 ", string title = "Synthetic")
        => Wrap(Expanded([], includeTrailer: true, fileNames), magic, title);

    /// <summary>The scripts a profile's OMS modules run (<c>run NAME</c>), as catalog names: a synthetic install
    /// lists them so the modules' idioms build.</summary>
    public static string[] ScriptsTheModulesRun(string profile)
        => Directory.EnumerateFiles(Paths.OmsModules(profile), "*.oms", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"\brun ([A-Z][A-Z0-9_]*)\b"))
            .Select(match => match.Groups[1].Value + ".BIN").Distinct().Order(StringComparer.Ordinal).ToArray();

    /// <summary>A catalog with <paramref name="fileNames"/> and the startup settings <paramref name="pairs"/>.</summary>
    public static byte[] Build(IReadOnlyList<string> fileNames, (string Key, string Value)[] pairs, string magic,
                               string title)
        => Wrap(Expanded(pairs, includeTrailer: true, fileNames), magic, title);

    public static byte[] Expanded(
        (string Key, string Value)[] pairs, bool includeTrailer = true, IReadOnlyList<string>? fileNames = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp932 = Encoding.GetEncoding(932);
        fileNames ??= ["@"];
        var blob = new List<byte>();
        AddU32(blob, 1);
        AddFixedString(blob, "DATA.ALF", 256, cp932);
        AddU32(blob, checked((uint)fileNames.Count));
        foreach (string fileName in fileNames)
        {
            AddFixedString(blob, fileName, 64, cp932);
            AddU32(blob, 0);
            AddU32(blob, 0);
            AddU32(blob, 0);
            AddU32(blob, 0);
        }
        if (!includeTrailer) return blob.ToArray();

        AddU32(blob, 0); // VM metadata bytes
        var strings = new List<byte>();
        foreach (var pair in pairs)
        {
            AddCString(strings, pair.Key, cp932);
            AddCString(strings, pair.Value, cp932);
        }
        AddU32(blob, checked((uint)strings.Count));
        if (strings.Count == 0) return blob.ToArray();
        AddU32(blob, checked((uint)pairs.Length));
        blob.AddRange(strings);
        return blob.ToArray();
    }

    public static byte[] Wrap(byte[] expanded, string magic = "S4IC422 ", string title = "Synthetic")
    {
        byte[] packed = LiteralLzss(expanded);
        var catalog = new byte[0x138 + packed.Length];
        Encoding.ASCII.GetBytes(magic).CopyTo(catalog, 0);
        Encoding.GetEncoding(932).GetBytes(title).CopyTo(catalog, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(0x12c, 4),
                                                  checked((uint)expanded.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(0x134, 4),
                                                  checked((uint)packed.Length));
        packed.CopyTo(catalog, 0x138);
        return catalog;
    }

    private static byte[] LiteralLzss(byte[] source)
    {
        var packed = new List<byte>();
        for (int offset = 0; offset < source.Length;)
        {
            int count = Math.Min(8, source.Length - offset);
            packed.Add((byte)((1 << count) - 1));
            for (int i = 0; i < count; i++) packed.Add(source[offset++]);
        }
        return packed.ToArray();
    }

    private static void AddFixedString(List<byte> target, string value, int size, Encoding encoding)
    {
        byte[] bytes = encoding.GetBytes(value);
        if (bytes.Length >= size) throw new ArgumentException("test string is too long");
        target.AddRange(bytes);
        target.AddRange(new byte[size - bytes.Length]);
    }

    private static void AddCString(List<byte> target, string value, Encoding encoding)
    {
        target.AddRange(encoding.GetBytes(value));
        target.Add(0);
    }

    private static void AddU32(List<byte> target, uint value)
    {
        int offset = target.Count;
        target.AddRange(new byte[4]);
        BinaryPrimitives.WriteUInt32LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(target).Slice(offset, 4), value);
    }
}
