using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Hosting;

/// <summary>
/// What text needs of a host: its reveal wait. Text itself is the text native's (<see cref="TextManager"/>); the waits
/// that block the engine thread stay with the host until engine layering milestone 11.
/// </summary>
public interface IAdvHost
{
    /// <summary>
    /// Run-state 0x20000000's wait: block the engine thread while <paramref name="text"/> reveals the run it just
    /// armed, servicing <see cref="TextManager.ServiceReveal"/> at each engine tick until the run is out, a click or
    /// fast-forward finishes it, or the engine stops. A host with no clock publishes the run at once.
    /// </summary>
    void WaitForTextReveal(GfxState gfx, TextManager text)
    {
        text.CompleteReveal();
        while (text.ServiceReveal(gfx) != null) { }
    }
}
