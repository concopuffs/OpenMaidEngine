using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

/// <summary>A write to a watched cell (Lua scripting spec §7.3), as script memory's setters see it: an integer cell's
/// old and new value, or a string cell's.</summary>
public readonly record struct CellWrite(CellKind Kind, int Address, long OldNumber, long NewNumber, string? OldText,
                                        string? NewText);

/// <summary>
/// The cells the plan's watches cover (Lua scripting spec §7.5), which script memory's setters test: a paged bitmap
/// per bank, so a write costs one lookup and a broad watch on a table costs a bit per cell. A page covers 65,536
/// cells; one no watch reaches is null. A write it covers goes to <c>record</c>, the scripting native's.
/// </summary>
public sealed class WatchedCells
{
    private const int PageShift = 16;
    private const int PageWords = 1 << (PageShift - 6);

    private readonly ulong[]?[] _integers;
    private readonly ulong[]?[] _strings;
    private readonly Action<CellWrite> _record;

    public WatchedCells(IEnumerable<(CellKind Kind, int Address, int Count)> ranges, Action<CellWrite> record)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
        var list = ranges.ToList();
        _integers = Pages(list.Where(range => range.Kind == CellKind.Integer));
        _strings = Pages(list.Where(range => range.Kind == CellKind.String));
    }

    public bool Integer(int address) => Test(_integers, address);

    public bool String(int address) => Test(_strings, address);

    internal void Record(in CellWrite write) => _record(write);

    private static bool Test(ulong[]?[] pages, int address)
        => (uint)(address >> PageShift) < (uint)pages.Length && pages[address >> PageShift] is { } page
           && (page[(address >> 6) & (PageWords - 1)] & (1UL << address)) != 0;

    private static ulong[]?[] Pages(IEnumerable<(CellKind Kind, int Address, int Count)> ranges)
    {
        var list = ranges.Where(range => range.Count > 0 && range.Address >= 0).ToList();
        if (list.Count == 0) return [];
        long last = list.Max(range => (long)range.Address + range.Count - 1);
        var pages = new ulong[]?[(int)(last >> PageShift) + 1];
        foreach (var (_, address, count) in list)
            for (long cell = address; cell < (long)address + count; cell++)
            {
                ulong[] page = pages[cell >> PageShift] ??= new ulong[PageWords];
                page[(cell >> 6) & (PageWords - 1)] |= 1UL << (int)(cell & 63);
            }
        return pages;
    }
}
