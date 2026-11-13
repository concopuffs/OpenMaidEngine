using System;

namespace Ome.Engine.Natives;

/// <summary>
/// The two requests that end the run's current work from outside the script: a stop (the window closes) and a return
/// to title (the 終了 dialog's タイトルに戻る, which native's WM_CLOSE path answers with op_0x9_reset_scene_and_reload_root).
/// One copy, which the interpreter reads at each instruction boundary and the waits read to end at once (engine layering
/// plan §3.3). The front end's thread asks; the flags are volatile, and asking wakes every wait.
/// </summary>
public sealed class StopAndReload
{
    private volatile bool _stopRequested;
    private volatile bool _reloadRequested;
    private Action? _wake;

    /// <summary>A stop was asked for; it stays asked for.</summary>
    public bool StopRequested => _stopRequested;

    /// <summary>A return to title was asked for and the reload has not reset the scene yet.</summary>
    public bool ReloadRequested => _reloadRequested;

    /// <summary>Stop: the interpreter ends at its next instruction boundary, and every wait ends now.</summary>
    public void RequestStop()
    {
        _stopRequested = true;
        _wake?.Invoke();
    }

    /// <summary>Return to title: every wait ends now, and the interpreter reloads the root at its next instruction
    /// boundary.</summary>
    public void RequestReload()
    {
        _reloadRequested = true;
        _wake?.Invoke();
    }

    /// <summary>The engine thread: the reload is resetting the scene, so waits wait again.</summary>
    public void ReloadTaken() => _reloadRequested = false;

    /// <summary>The waits: how a request wakes them.</summary>
    internal void WakeWith(Action wake) => _wake = wake ?? throw new ArgumentNullException(nameof(wake));
}
