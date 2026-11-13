namespace Ome.Engine.Natives;

/// <summary>
/// What the input and waits native tells a front end about its waits, as notifications only: nothing it is told
/// changes what a wait does. The front end counts pages and clears its status line here.
/// </summary>
public interface IWaitListener
{
    /// <summary>A text wait reached its page: <paramref name="page"/> counts the run's text pages from 1.</summary>
    void PageWaitStarted(int page) { }

    /// <summary>The page left the screen: its text wait ended, or the scene was reset.</summary>
    void PageCleared() { }
}
