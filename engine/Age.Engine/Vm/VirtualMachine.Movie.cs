using Age.Engine.Hosting;
using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepMovie(string label, Instruction ins, int pc)
    {
        var a = ins.Args;
        switch (label)
        {
            case "play-modal-movie-to-surface": // 0x20f (packed resource)(surface)(movie flags)
            {
                long resourceId = Read(a[0]);
                int surfaceSlot = (int)Read(a[1]);
                // Modal and non-modal paths share packed resolution and retained-surface composition.
                // The distinct host entry point owns only 0x20f's blocking lifecycle. Native attaches its
                // renderer to the existing mutable target; it does not reload that surface with resource
                // id/movie bytes or apply color key 0. Keep the created surface's no-key state so MPEG
                // black remains opaque.
                _host.PlayModalMovieToSurface(resourceId, surfaceSlot, Read(a[2]));
                return pc + 1;
            }
            case "u004221A0":           // pre-reference compatibility
            case "play-movie-to-surface": // 0x236 (resource)(surface)(movie flags)(start delay ms)
            {
                long resourceId = Read(a[0]);
                int surfaceSlot = (int)Read(a[1]);
                // Native SC0000's warm-engine trace evaluates this existing site as surface 0. The bounded
                // single-scene bootstrap assigns its logical layer slot 5, which the immediately following
                // 0x34/0x35 static loads reuse and would therefore evict the movie before presentation.
                // Reproduce the native site assignment without changing the general surface allocator.
                if (ins.Offset == 0x13c8 && _cur.Script.Name.StartsWith("SC0000", StringComparison.OrdinalIgnoreCase)
                    && surfaceSlot != 0)
                {
                    int logicalLayer = (int)Globals.GetValueOrDefault(0x62450);
                    long movieHandle = Globals.GetValueOrDefault(0x62455 + logicalLayer);
                    Gfx.RemapObjectSurface(movieHandle, surfaceSlot, 0);
                    surfaceSlot = 0;
                }
                // Graph construction is synchronous. Keep the existing created surface blank during that
                // boundary; publishing the movie resource first would let a concurrent compositor mistake
                // the MPEG payload's .AGF name for a still image before the host registers/decodes it.
                long? stopTimeMs = _host.PlayMovieToSurface(resourceId, surfaceSlot, Read(a[2]), Read(a[3]));
                // Native replaces the pixels of the already-created surface without changing its resource
                // identity or color key. The host's playback-to-surface binding resolves the live frame.
                // Native never encounters a missing system decoder for shipped assets. If a host backend
                // cannot initialize one, model the valid movie as completing immediately: BTL feeds this
                // value into its effect timeline, where zero is a safe duration and -1 is not meaningful.
                Gfx.SetMovieStopTime(surfaceSlot, stopTimeMs ?? 0);
                return pc + 1; // native cmd size 9 resumes at the next instruction; playback is asynchronous
            }
            case "u00422B80": // pre-reference compatibility
            case "play-movie-to-surface-at-position": // 0x241 (+ initial position ms)
            {
                long resourceId = Read(a[0]);
                int surfaceSlot = unchecked((int)Read(a[1]));
                long? stopTimeMs = _host.PlayMovieToSurfaceAtPosition(
                    resourceId, surfaceSlot, Read(a[2]), Read(a[3]), Read(a[4]));
                // As with 0x236, seeking changes decoder position but not the graph's stop metadata.
                Gfx.SetMovieStopTime(surfaceSlot, stopTimeMs ?? 0);
                return pc + 1;
            }
            // ---- gfx command-buffer ops (VM-internal GfxState; docs/engine-re.md op-contract table) ----
            case "u00422930":                    // pre-reference compatibility
            case "query-surface-stop-time-ms":   // 0x23f (out_stop_time_ms)(surface_slot)
            {
                int surfaceSlot = (int)Read(a[1]);
                if (!Gfx.TryGetMovieStopTime(surfaceSlot, out long? stopTimeMs))
                {
                    Write(a[0], -1); // native null CMovieToTexture slot
                    return pc + 1;
                }
                if (!stopTimeMs.HasValue)
                {
                    _host.ReportWarning(
                        $"movie stop-time unavailable {_cur.Script.Name}@0x{ins.Offset:x} " +
                        $"surface={surfaceSlot}; returning -1");
                    Write(a[0], -1);
                    return pc + 1;
                }
                Write(a[0], stopTimeMs.Value);
                return pc + 1;
            }
            case "query-movie-surface-active": // 0x23a (out)(surface slot)
                Write(a[0], _host.IsMovieSurfaceActive((int)Read(a[1])) ? 1 : 0); return pc + 1;
            case "play-movie-mask-transition": // 0x24d: captured retained range + movie green-channel mask
                _host.PlayMovieMaskTransition(Gfx, new MovieMaskTransitionRequest(
                    Read(a[0]), unchecked((int)Read(a[1])),
                    Read(a[2]), unchecked((int)Read(a[3])),
                    unchecked((int)Read(a[4])), unchecked((int)Read(a[5])),
                    unchecked((int)Read(a[6])), unchecked((int)Read(a[7])),
                    Read(a[8]), Read(a[9]), Read(a[10]), Read(a[11])));
                return pc + 1;
            default:
                throw new InvalidOperationException($"Non-movie opcode routed to movie handler: {label}");
        }
    }
}
