namespace Ome.Engine.Hosting;

/// <summary>The movie op that blocks: op 0x20f's modal wait. The movie native plays every other movie op
/// (<see cref="Natives.Movies"/>); this wait stays with the host until milestone 11 moves the waits.</summary>
public interface IMovieHost
{
    // Native op 0x20f uses a universal packed id and parks script execution until the movie
    // reaches EOF or the player cancels it. The decoder remains asynchronous; the interactive host
    // owns the modal wait so its render loop can continue publishing frames.
    void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags) { }
    // The modal movie service polls native's input latch on its first tick, and a held action 4 stops the movie: the
    // next PlayModalMovieToSurface returns as cancelled at once (VirtualMachine.InputLatch.cs).
    void CancelNextModalMovie() { }
}
