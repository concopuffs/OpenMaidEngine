using System.Buffers.Binary;
using System.Text;

namespace Ome.Engine.Sys4;

/// <summary>
/// Writes an S4AC append catalog (<c>APPENDnn.AAI</c>) and its paired archive (<c>APPENDnn.ALF</c>), the layout
/// <see cref="Sys4AssetCatalog"/> reads and AGE mounts (docs/asset-resolution-re.md, "AAI append mount").
/// <list type="bullet">
/// <item>Header: 8-byte magic, the base catalog's 256-byte title, the selector at 0x108, the directory's expanded
/// size at 0x10c and 0x110 (the installed APPEND01 repeats it), its packed size at 0x114, the LZSS stream at 0x118.</item>
/// <item>Directory: archive count and 256-byte archive names, then file count and 80-byte records (64-byte name,
/// archive index, file number, offset, size).</item>
/// <item>Archive: the files concatenated from offset 0, as in the installed APPEND01.ALF.</item>
/// </list>
/// Record <c>n</c> is addressed by packed id <c>(selector &lt;&lt; 24) | n</c>; native op 0x143 runs record 0 of every
/// mounted catalog at INIT2.
/// </summary>
public static class Sys4AppendCatalogWriter
{
    private const int HeaderSize = 0x118, TitleSize = 256, ArchiveNameSize = 256, RecordSize = 80, NameSize = 64;

    public sealed record File(string Name, byte[] Bytes);

    /// <param name="magic">The base catalog family's append magic, such as <c>"S4AC422 "</c> (eight bytes).</param>
    /// <param name="title">The base catalog's title; AGE mounts only an append catalog of the same game.</param>
    public static (byte[] Catalog, byte[] Archive) Write(string magic, string title, int selector, string archiveName,
                                                         IReadOnlyList<File> files)
    {
        if (selector is <= 0 or >= 0x80) throw new ArgumentOutOfRangeException(nameof(selector), "selector must be 1..127");
        if (files.Count == 0) throw new ArgumentException("an append catalog needs at least one file", nameof(files));
        Encoding cp932 = Cp932();

        var archive = new MemoryStream();
        var directory = new byte[4 + ArchiveNameSize + 4 + files.Count * RecordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(directory, 1);
        Put(directory.AsSpan(4, ArchiveNameSize), cp932.GetBytes(archiveName), "archive name");
        int p = 4 + ArchiveNameSize;
        BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(p), (uint)files.Count);
        p += 4;
        for (int k = 0; k < files.Count; k++, p += RecordSize)
        {
            Span<byte> row = directory.AsSpan(p, RecordSize);
            Put(row[..NameSize], cp932.GetBytes(files[k].Name), $"record name {files[k].Name}");
            BinaryPrimitives.WriteUInt32LittleEndian(row[64..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(row[68..], (uint)k);
            BinaryPrimitives.WriteUInt32LittleEndian(row[72..], checked((uint)archive.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(row[76..], checked((uint)files[k].Bytes.Length));
            archive.Write(files[k].Bytes);
        }

        byte[] packed = LzssEncoder.EncodeOrVerbatim(directory);
        if (packed.Length >= directory.Length)
            throw new InvalidOperationException("the catalog directory did not compress; AGE expects an LZSS stream");
        var catalog = new byte[HeaderSize + packed.Length];
        byte[] magicBytes = Encoding.ASCII.GetBytes(magic);
        if (magicBytes.Length != 8 || !magic.StartsWith("S4AC", StringComparison.Ordinal))
            throw new ArgumentException("append magic must be eight bytes starting with S4AC", nameof(magic));
        magicBytes.CopyTo(catalog, 0);
        Put(catalog.AsSpan(8, TitleSize), cp932.GetBytes(title), "title");
        BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(0x108), (uint)selector);
        BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(0x10c), (uint)directory.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(0x110), (uint)directory.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(0x114), (uint)packed.Length);
        packed.CopyTo(catalog, HeaderSize);
        return (catalog, archive.ToArray());
    }

    /// <summary>A NUL-terminated field; the text must leave room for the terminator.</summary>
    private static void Put(Span<byte> field, byte[] text, string what)
    {
        if (text.Length >= field.Length) throw new ArgumentException($"{what} is too long for its {field.Length}-byte field");
        text.CopyTo(field);
    }

    private static Encoding Cp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
