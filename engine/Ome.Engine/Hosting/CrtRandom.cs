namespace Ome.Engine.Hosting;

/// <summary>
/// AGE's C runtime generator: MSVC <c>rand()</c>, one per engine. Native seeds it from the tick count in its context
/// constructor and draws op 0x60, the leaf effect and other engine work from it (docs/engine-re.md, "Op 0x60
/// randomness"). The port draws op 0x60 and Lua's <c>math.random</c> from it when no <c>rand-seed</c> is set, and the
/// title's leaves always, so the leaves advance op 0x60's stream as they do on native
/// (docs/superpowers/specs/2026-11-20-title-leaves-design.md §3, §7). Composition gives the graphics native and script
/// memory the same one. Engine thread only.
/// </summary>
public sealed class CrtRandom
{
    private uint _state;

    public CrtRandom(uint seed) => _state = seed;

    /// <summary>A generator seeded from the tick count, as native's <c>srand</c> seeds it.</summary>
    public static CrtRandom FromTickCount() => new(unchecked((uint)Environment.TickCount));

    /// <summary>The draws taken so far.</summary>
    public long Draws { get; private set; }

    /// <summary>MSVC <c>rand()</c>: advance the state and return its bits 16 to 30 (0 to 0x7fff).</summary>
    public int Next()
    {
        _state = unchecked(_state * 0x343FDu + 0x269EC3u);
        Draws++;
        return (int)((_state >> 16) & 0x7FFF);
    }
}
