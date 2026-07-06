using System.Collections.Generic;
using System.Threading;
using Age.Engine.Hosting;

public sealed class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly SemaphoreSlim _gate = new(0, 1);
    public volatile bool IsWaiting;
    public readonly List<(int Offset, string Text)> Captured = new();

    public GodotAdvHost(Main main) => _main = main;

    public void ShowText(int offset, string text)
    {
        Captured.Add((offset, text));
        _main.CallDeferred("AppendLine", text);
    }

    public void WaitForInput()
    {
        _main.CallDeferred("PageBreak");
        IsWaiting = true;
        _gate.Wait();
        IsWaiting = false;
        _main.CallDeferred("ClearPage");
    }

    // called from the main thread (click) or the selftest auto-clicker
    public void SignalInput() { if (_gate.CurrentCount == 0) _gate.Release(); }

    public void CallScript(long id) { }
    public void OnStub(int opcode) { }
}
