namespace Ome.Engine.Vm;

/// <summary>One op-0x60 call site's draws under a seed: the script and the op's dword offset.</summary>
public sealed record DeterministicRandomSite(string Script, uint PackedId, int Offset, int Draws);

/// <summary>What op 0x60 drew under one seed: the seed (null when the game drew its own values), the total and each
/// call site's count.</summary>
public sealed record DeterministicRandomSummary(uint? Seed, int Draws, IReadOnlyList<DeterministicRandomSite> Sites);

/// <summary>Op 0x60's deterministic mode, for comparing the port with native AGE (drive step <c>rand-seed</c>;
/// docs/engine-re.md, "Op 0x60 randomness"). Native draws from the CRT's shared generator, seeded from the tick count
/// and also drawn by the main tick and transitions, and the port from <see cref="System.Random.Shared"/>, so the two
/// never agree on their own. Under a seed both targets give every call site, a script's packed id P and the op's dword
/// offset O, its own MSVC <c>rand()</c> stream starting from <c>seed ^ (P * 0x9E3779B1 + O * 0x85EBCA77)</c>, mod 2^32.
/// The k-th draw at a site is then the same on both targets whatever else ran, and a site drawn a different number
/// of times on the two (a timed loop) cannot shift any other. tools/drive/native-agent.js computes the same streams
/// inside native AGE.</summary>
public sealed partial class VirtualMachine
{
    private readonly object _deterministicRandomLock = new();
    private uint? _deterministicRandomSeed;
    private int _deterministicRandomDraws;
    private readonly Dictionary<(uint PackedId, int Offset), DeterministicRandomStream> _deterministicRandomSites = new();

    private sealed class DeterministicRandomStream(string script, uint state)
    {
        public string Script { get; } = script;
        public uint State { get; set; } = state;
        public int Draws { get; set; }
    }

    /// <summary>Start drawing op 0x60 from <paramref name="seed"/>'s streams, or from the game's own generator when
    /// null. Every stream restarts. Returns what was drawn since the previous call.</summary>
    public DeterministicRandomSummary SeedDeterministicRandom(uint? seed)
    {
        lock (_deterministicRandomLock)
        {
            var sites = _deterministicRandomSites
                .Select(entry => new DeterministicRandomSite(entry.Value.Script, entry.Key.PackedId, entry.Key.Offset,
                                                             entry.Value.Draws))
                .OrderBy(site => site.Script, StringComparer.Ordinal).ThenBy(site => site.Offset)
                .ToArray();
            var summary = new DeterministicRandomSummary(_deterministicRandomSeed, _deterministicRandomDraws, sites);
            _deterministicRandomSeed = seed;
            _deterministicRandomDraws = 0;
            _deterministicRandomSites.Clear();
            return summary;
        }
    }

    /// <summary>A stream's first state for the site at <paramref name="offset"/> in script <paramref name="packedId"/>.
    /// </summary>
    public static uint DeterministicRandomInitialState(uint seed, uint packedId, int offset)
        => seed ^ unchecked(packedId * 0x9E3779B1u + (uint)offset * 0x85EBCA77u);

    /// <summary>MSVC <c>rand()</c>: advance <paramref name="state"/> and return its bits 16..30 (0..0x7fff).</summary>
    public static int DeterministicRandomNext(ref uint state)
    {
        state = unchecked(state * 0x343FDu + 0x269EC3u);
        return (int)((state >> 16) & 0x7FFF);
    }

    /// <summary>The value op 0x60 at <paramref name="pc"/> of the current frame draws before taking its bound.</summary>
    private int DrawOp60(int pc)
    {
        lock (_deterministicRandomLock)
        {
            if (_deterministicRandomSeed is not uint seed) return System.Random.Shared.Next(0x8000);
            Model.Script script = _cur.Script;
            int offset = script.Instructions[pc].Offset;
            if (!_deterministicRandomSites.TryGetValue((script.PackedId, offset), out DeterministicRandomStream? stream))
            {
                stream = new DeterministicRandomStream(script.Name,
                    DeterministicRandomInitialState(seed, script.PackedId, offset));
                _deterministicRandomSites.Add((script.PackedId, offset), stream);
            }
            uint state = stream.State;
            int value = DeterministicRandomNext(ref state);
            stream.State = state;
            stream.Draws++;
            _deterministicRandomDraws++;
            return value;
        }
    }
}
