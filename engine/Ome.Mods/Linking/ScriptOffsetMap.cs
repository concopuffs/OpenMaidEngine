using Ome.Script.Patch;

namespace Ome.Mods.Linking;

/// <summary>Base instructions from <see cref="Start"/> up to <see cref="End"/> (dword offsets), each moved by
/// <see cref="Delta"/> dwords in the linked script.</summary>
public readonly record struct OffsetRun(int Start, int End, int Delta);

/// <summary>
/// Where a patched script's base instructions are in the linked script (localization spec §6.1): runs of consecutive
/// base instructions, each moved by one distance. A catalog entry's <c>at</c> names a base offset; the text loader
/// maps it through this table. An offset outside every run belongs to an instruction a patch's region replaced. An
/// unpatched script needs no table, since its offsets are the base offsets.
/// </summary>
public sealed class ScriptOffsetMap
{
    public ScriptOffsetMap(IReadOnlyList<OffsetRun> runs)
    {
        for (int k = 0; k < runs.Count; k++)
            if (runs[k].End <= runs[k].Start || (k > 0 && runs[k].Start < runs[k - 1].End))
                throw new ArgumentException("runs must be non-empty, sorted and disjoint", nameof(runs));
        Runs = runs;
    }

    public IReadOnlyList<OffsetRun> Runs { get; }

    /// <summary>The linked offset of base offset <paramref name="baseOffset"/>, or null when a patch replaced the
    /// instruction there.</summary>
    public int? Map(int baseOffset)
    {
        int low = 0, high = Runs.Count - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            OffsetRun run = Runs[middle];
            if (baseOffset < run.Start) high = middle - 1;
            else if (baseOffset >= run.End) low = middle + 1;
            else return baseOffset + run.Delta;
        }
        return null;
    }

    /// <summary>The table of a patch result: each instruction the applier kept from the base carries its base offset
    /// (<c>OriginalOffset</c>), and its linked offset is the sum of the sizes before it.</summary>
    public static ScriptOffsetMap From(OmsPatchResult result)
    {
        var code = result.Script.Code;
        if (code.Count != result.Provenance.Count)
            throw new ArgumentException("the patch result has one provenance per instruction", nameof(result));
        var runs = new List<OffsetRun>();
        int linked = 0;
        for (int i = 0; i < code.Count; i++)
        {
            int size = code[i].SizeInDwords;
            if (result.Provenance[i].Origin == OmsPatchOrigin.Base && code[i].OriginalOffset is { } start)
            {
                int delta = linked - start;
                if (runs.Count > 0 && runs[^1].End == start && runs[^1].Delta == delta)
                    runs[^1] = runs[^1] with { End = start + size };
                else runs.Add(new OffsetRun(start, start + size, delta));
            }
            linked += size;
        }
        // Layout keeps base order, so the runs come sorted; a region joins its neighbours' gaps.
        runs.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new ScriptOffsetMap(runs);
    }

    /// <summary>The runs as <c>link.json</c> stores them: <c>[start, end, delta]</c>.</summary>
    public IReadOnlyList<int[]> ToArrays() => Runs.Select(run => new[] { run.Start, run.End, run.Delta }).ToList();

    public static ScriptOffsetMap FromArrays(IEnumerable<int[]> arrays)
        => new(arrays.Select(run => run.Length == 3
            ? new OffsetRun(run[0], run[1], run[2])
            : throw new InvalidDataException("an offset run is [start, end, delta]")).ToList());
}
