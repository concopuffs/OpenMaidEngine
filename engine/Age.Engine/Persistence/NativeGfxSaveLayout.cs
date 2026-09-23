namespace Age.Engine.Persistence;

/// <summary>Native layout-3 graphics framing, selected by the persistence profile or file header.</summary>
public sealed record NativeGfxSaveLayout
{
    public const int BaseRecordSize = 0x2d4;
    public const int RotationCacheRecordSize = 0x2e4;

    private NativeGfxSaveLayout(int recordSize) => RecordSize = recordSize;

    public int RecordSize { get; }
    public bool HasRotationCache => RecordSize == RotationCacheRecordSize;
    // Native copies RecordSize bytes, then advances a DWORD pointer by RecordSize.
    public int EntryStrideBytes => (RecordSize + 1) * 4;
    public int AllocationDwordsPerObject => RecordSize + 4;
    public int AllocationConstantDwords => RecordSize + 13;

    public static bool IsSupported(int recordSize)
        => recordSize is BaseRecordSize or RotationCacheRecordSize;

    public static NativeGfxSaveLayout FromRecordSize(int recordSize)
        => IsSupported(recordSize) ? new(recordSize)
            : throw new InvalidDataException($"Unsupported native gfx record size 0x{recordSize:x}.");
}
