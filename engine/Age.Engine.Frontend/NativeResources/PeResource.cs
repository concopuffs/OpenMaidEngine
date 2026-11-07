using System.Buffers.Binary;

namespace Age.Engine.Frontend.NativeResources;

/// <summary>A resource's name: a numeric id (MAKEINTRESOURCE) or a string name such as <c>IDI_ICON1</c>. String
/// names compare case-insensitively, as FindResource does.</summary>
public readonly record struct ResourceName(int? Id, string? Name)
{
    public static implicit operator ResourceName(int id) => new(id, null);
    public static implicit operator ResourceName(string name) => new(null, name);

    public override string ToString() => Id?.ToString() ?? Name ?? "";
}

/// <summary>Finds resources in a 32- or 64-bit PE image without loading it: the type, name and language levels of
/// the resource directory, first language.</summary>
public static class PeResource
{
    public const int RtIcon = 3, RtMenu = 4, RtDialog = 5, RtGroupIcon = 14;

    /// <summary>The resource's bytes, or null when the image has no such resource or is not a PE image.</summary>
    public static byte[]? Read(ReadOnlySpan<byte> pe, int type, ResourceName name)
    {
        try
        {
            return TryFind(pe, type, name, out int offset, out int size) ? pe.Slice(offset, size).ToArray() : null;
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Find resource <paramref name="type"/>/<paramref name="name"/> (first language) and return its data's
    /// file offset and size.</summary>
    public static bool TryFind(ReadOnlySpan<byte> pe, int type, ResourceName name, out int offset, out int size)
    {
        offset = size = 0;
        if (pe.Length < 0x40 || pe[0] != 'M' || pe[1] != 'Z') return false;
        int header = BinaryPrimitives.ReadInt32LittleEndian(pe[0x3c..]);
        if (header < 0 || header > pe.Length - 24) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(pe[header..]) != 0x00004550) return false;
        int sections = BinaryPrimitives.ReadUInt16LittleEndian(pe[(header + 6)..]);
        int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(pe[(header + 20)..]);
        int optional = header + 24;
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(pe[optional..]);
        int directories = optional + magic switch
        {
            0x10b => 96,
            0x20b => 112,
            _ => throw new InvalidDataException("unknown PE optional header"),
        };
        uint resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(pe[(directories + 2 * 8)..]);
        if (resourceRva == 0) return false;
        int sectionTable = optional + optionalSize;

        static int FileOffset(ReadOnlySpan<byte> pe, int sectionTable, int sections, uint rva)
        {
            for (int i = 0; i < sections; i++)
            {
                int s = sectionTable + 40 * i;
                uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(pe[(s + 8)..]);
                uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(pe[(s + 12)..]);
                uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(pe[(s + 16)..]);
                uint rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(pe[(s + 20)..]);
                if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, rawSize))
                    return checked((int)(rawPointer + (rva - virtualAddress)));
            }
            throw new InvalidDataException($"RVA 0x{rva:x} is in no section");
        }

        int root = FileOffset(pe, sectionTable, sections, resourceRva);

        // Each level is an IMAGE_RESOURCE_DIRECTORY: named entries, then id entries. The high bit of an entry's name
        // marks a string name (a counted UTF-16 string at that offset); the high bit of its target marks a
        // subdirectory. Offsets are relative to the resource root.
        static int? Entry(ReadOnlySpan<byte> pe, int root, int directory, ResourceName? wanted)
        {
            int named = BinaryPrimitives.ReadUInt16LittleEndian(pe[(directory + 12)..]);
            int ids = BinaryPrimitives.ReadUInt16LittleEndian(pe[(directory + 14)..]);
            for (int i = 0; i < named + ids; i++)
            {
                int e = directory + 16 + 8 * i;
                uint name = BinaryPrimitives.ReadUInt32LittleEndian(pe[e..]);
                uint target = BinaryPrimitives.ReadUInt32LittleEndian(pe[(e + 4)..]);
                if (wanted is { } want && !Matches(pe, root, name, want)) continue;
                return unchecked((int)target);
            }
            return null;
        }

        static bool Matches(ReadOnlySpan<byte> pe, int root, uint name, ResourceName want)
        {
            bool isString = (name & 0x80000000) != 0;
            if (want.Id is int id) return !isString && name == id;
            if (!isString || want.Name is null) return false;
            int at = root + (int)(name & 0x7fffffff);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(pe[at..]);
            if (length != want.Name.Length) return false;
            for (int i = 0; i < length; i++)
            {
                char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(pe[(at + 2 + 2 * i)..]);
                if (char.ToUpperInvariant(c) != char.ToUpperInvariant(want.Name[i])) return false;
            }
            return true;
        }

        if (Entry(pe, root, root, type) is not int typeEntry || typeEntry >= 0) return false;
        if (Entry(pe, root, root + (typeEntry & 0x7fffffff), name) is not int nameEntry || nameEntry >= 0) return false;
        if (Entry(pe, root, root + (nameEntry & 0x7fffffff), null) is not int languageEntry || languageEntry < 0)
            return false;
        int data = root + languageEntry;
        uint dataRva = BinaryPrimitives.ReadUInt32LittleEndian(pe[data..]);
        size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(pe[(data + 4)..]));
        offset = FileOffset(pe, sectionTable, sections, dataRva);
        return offset >= 0 && size >= 4 && offset <= pe.Length - size;
    }
}
