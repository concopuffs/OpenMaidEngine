using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // The movie native (Ome.Engine/Natives/Movies.cs) plays every movie op but 0x20f's modal wait, which is the input
    // and waits native's (IInputAndWaits.PlayModalMovieToSurface).
    private readonly Movies _movies;

    // 0x20f (packed resource)(surface)(movie flags)
    private StepResult OpPlayModalMovieToSurface(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (MovieSurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        long resourceId = Read(a[0]);
        int surfaceSlot = (int)Read(a[1]);
        // Modal and non-modal paths share packed resolution and retained-surface composition.
        // The waits' distinct entry point owns only 0x20f's blocking lifecycle. Native attaches its
        // renderer to the existing mutable target; it does not reload that surface with resource
        // id/movie bytes or apply color key 0. Keep the created surface's no-key state so MPEG
        // black remains opaque.
        OfferHeldTriggersToModalMovie();
        _waits.PlayModalMovieToSurface(resourceId, surfaceSlot, Read(a[2]));
        return StepResult.Next;
    }

    // 0x236 (resource)(surface)(movie flags)(start delay ms)
    private StepResult OpPlayMovieToSurface(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (MovieSurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        long resourceId = Read(a[0]);
        int surfaceSlot = (int)Read(a[1]);
        // Graph construction is synchronous. Keep the existing created surface blank during that
        // boundary; publishing the movie resource first would let a concurrent compositor mistake
        // the MPEG payload's .AGF name for a still image before the movie native registers/decodes it.
        long? stopTimeMs = _movies.PlayToSurface(_cur.Script.Name, resourceId, surfaceSlot, Read(a[2]), Read(a[3]));
        // Native replaces the pixels of the already-created surface without changing its resource
        // identity or color key. The movie native's playback-to-surface binding resolves the live frame.
        // Native never encounters a missing system decoder for shipped assets. If the front end's decoder
        // cannot open one, model the valid movie as completing immediately: BTL feeds this
        // value into its effect timeline, where zero is a safe duration and -1 is not meaningful.
        Gfx.SetMovieStopTime(surfaceSlot, stopTimeMs ?? 0);
        return StepResult.Next; // native cmd size 9 resumes at the next instruction; playback is asynchronous
    }

    // 0x241 (+ initial position ms)
    private StepResult OpPlayMovieToSurfaceAtPosition(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (MovieSurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        long resourceId = Read(a[0]);
        int surfaceSlot = unchecked((int)Read(a[1]));
        long? stopTimeMs = _movies.PlayToSurfaceAtPosition(
            _cur.Script.Name, resourceId, surfaceSlot, Read(a[2]), Read(a[3]), Read(a[4]));
        // As with 0x236, seeking changes decoder position but not the graph's stop metadata.
        Gfx.SetMovieStopTime(surfaceSlot, stopTimeMs ?? 0);
        return StepResult.Next;
    }

    // ---- gfx command-buffer ops (VM-internal GfxState; docs/engine-re.md op-contract table) ----
    // 0x23f (out_stop_time_ms)(surface_slot)
    private StepResult OpQuerySurfaceStopTimeMs(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (MovieSurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        int surfaceSlot = (int)Read(a[1]);
        if (!Gfx.TryGetMovieStopTime(surfaceSlot, out long? stopTimeMs))
        {
            Write(a[0], -1); // native null CMovieToTexture slot
            return StepResult.Next;
        }
        if (!stopTimeMs.HasValue)
        {
            _waits.ReportWarning(
                $"movie stop-time unavailable {_cur.Script.Name}@0x{ins.Offset:x} " +
                $"surface={surfaceSlot}; returning -1");
            Write(a[0], -1);
            return StepResult.Next;
        }
        Write(a[0], stopTimeMs.Value);
        return StepResult.Next;
    }

    // 0x23a (out)(surface slot)
    private StepResult OpQueryMovieSurfaceActive(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (MovieSurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        Write(a[0], _movies.IsSurfaceActive((int)Read(a[1])) ? 1 : 0); return StepResult.Next;
    }

    // 0x246 (surface slot)(rate percent)
    private StepResult OpSetMoviePlaybackRate(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (MovieSurfaceSlotRefused(ins, Read(a[0])) is { } refused) return refused;
        int surfaceSlot = unchecked((int)Read(a[0]));
        // The native handler checks the CMovieToTexture surface pointer before dereferencing
        // IMediaPosition. The port's movie metadata entry is the equivalent live graph marker.
        if (Gfx.TryGetMovieStopTime(surfaceSlot, out _))
            _movies.SetPlaybackRate(surfaceSlot, Read(a[1]) / 100.0);
        return StepResult.Next;
    }

    // 0x24d: captured retained range + movie green-channel mask
    private StepResult OpPlayMovieMaskTransition(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (SurfaceSlotRefused(ins, Read(a[1])) is { } refused) return refused;
        _movies.PlayMaskTransition(_cur.Script.Name, new MovieMaskTransitionRequest(
            Read(a[0]), unchecked((int)Read(a[1])),
            Read(a[2]), unchecked((int)Read(a[3])),
            unchecked((int)Read(a[4])), unchecked((int)Read(a[5])),
            unchecked((int)Read(a[6])), unchecked((int)Read(a[7])),
            Read(a[8]), Read(a[9]), Read(a[10]), Read(a[11])));
        return StepResult.Next;
    }
}
