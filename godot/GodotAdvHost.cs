using System.Collections.Generic;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Sys4;

public sealed class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly ResourceMap _res;
    private readonly string _scene;                       // e.g. "SC0000" — for section_base
    private readonly Dictionary<int, string?> _slotBmp = new();   // slot -> pre-converted BMP path
    private readonly SemaphoreSlim _gate = new(0, 1);
    public volatile bool IsWaiting;
    public readonly List<(int Offset, string Text)> Captured = new();

    public GodotAdvHost(Main main, ResourceMap res, string scene)
    {
        _main = main; _res = res; _scene = scene;
    }

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

    // ---- texture ops (run on the VM thread; marshal Godot node work to the main thread) ----
    public void CreateTexture(int slot, int width, int height) => _slotBmp[slot] = null;

    public void SetTexture(long resourceId, int slot)
    {
        var asset = _res.Resolve(_scene, resourceId);
        _slotBmp[slot] = asset != null ? ResourceMap.TexturePath(asset) : null;
    }

    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY)
    {
        if (_slotBmp.TryGetValue(slot, out var bmp) && bmp != null)
            _main.CallDeferred("DrawSlot", slot, bmp, dstX, dstY, width, height);
    }

    // Temporary stub — replaced by the real BMP-header-backed impl in Task 3 (blit compositor).
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);

    // ---- audio ops (OGG plays natively in Godot) ----
    // BGM: addressed by direct name (BGM{id:D3}.OGG), NOT the manifest. Voice: via the per-scene manifest.
    public void PlayBgm(long id)
    {
        var path = _res.BgmPathById(id);
        if (path != null) _main.CallDeferred("PlayBgm", path);
    }

    public void PlayVoice(long id)
    {
        var asset = _res.Resolve(_scene, id);
        var path = asset != null ? ResourceMap.AudioPath(asset) : null;
        if (path != null) _main.CallDeferred("PlayVoice", path);
    }
}
