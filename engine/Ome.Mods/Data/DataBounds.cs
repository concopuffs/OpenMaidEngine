namespace Ome.Mods.Data;

/// <summary>
/// The bounds of a data file (data patches spec §5.3, §9; plan §4.3): a package names a bounded number of files, and
/// each file is bounded in size, in nesting, in the values it holds and in the length of its keys. They are what limit a
/// delivery's budget, which grows with the document (spec §5.6). The numbers were set from a file that sets every
/// authored cell of the largest record table of the games the engine runs (300,000 values; 318,001 with its tables
/// counted, plan §12.6).
/// </summary>
public static class DataBounds
{
    /// <summary>The data files one package may name.</summary>
    public const int MaxFilesPerPackage = 64;

    /// <summary>The largest data file, in bytes.</summary>
    public const long MaxFileBytes = 16L << 20;

    /// <summary>The values one document may hold: strings, booleans and numbers, each element of an array counted, and
    /// each table and array counting one too (plan §12.6), so that a file of empty tables or arrays has a size. The
    /// reader refuses a document past it before Tomlyn builds anything (<see cref="DataDocumentReader"/>).</summary>
    public const int MaxValues = 400_000;

    /// <summary>How deep tables and arrays may nest, the document's own table counting as the first.</summary>
    public const int MaxDepth = 16;

    /// <summary>The longest key, in characters.</summary>
    public const int MaxKeyLength = 128;
}
