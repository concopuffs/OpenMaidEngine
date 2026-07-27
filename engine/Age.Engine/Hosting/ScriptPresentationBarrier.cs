using System;
using System.Threading;

namespace Age.Engine.Hosting;

/// <summary>
/// Keeps one VM opcode burst atomic with respect to the interactive compositor. The VM owns the
/// write side while script code is running; the render thread samples through the read side only
/// after the VM reaches a presentation, sleep, or input service boundary.
/// </summary>
public sealed class ScriptPresentationBarrier
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private int _scriptDepth;

    public void EnterScript()
    {
        if (_scriptDepth++ == 0)
            _lock.EnterWriteLock();
    }

    public void ExitScript()
    {
        if (_scriptDepth <= 0)
            throw new InvalidOperationException("No script burst is active.");
        if (--_scriptDepth == 0)
            _lock.ExitWriteLock();
    }

    /// <summary>Temporarily expose the completed burst state while a host service is parked.</summary>
    public bool SuspendScript()
    {
        if (_scriptDepth <= 0 || !_lock.IsWriteLockHeld) return false;
        _lock.ExitWriteLock();
        return true;
    }

    public void ResumeScript(bool suspended)
    {
        if (suspended) _lock.EnterWriteLock();
    }

    public bool TryEnterPresentation()
        => !_lock.IsWriteLockHeld && _lock.TryEnterReadLock(0);

    public void ExitPresentation() => _lock.ExitReadLock();
}
