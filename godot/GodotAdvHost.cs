using System.Collections.Generic;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Sys4;

public sealed class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly ResourceMap _res;
    private readonly string _scene;                       // e.g. "SC0000" — for section_base
    private readonly Dictionary<int, string?> _slotBmp = new();          // slot -> pre-converted BMP path
    // slot -> dims. Slot 0 is the primary/screen surface (800x600), normally created at engine boot which
    // the single-scene harness skips; seed it so the first CG's anchor math stays correct (not 0x0).
    private readonly Dictionary<int, (int W, int H)> _slotDims = new() { { 0, (800, 600) } };
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

    public volatile int Pages;   // VM-thread page counter (incremented before IsWaiting so shot-gating can't race)

    public void WaitForInput()
    {
        Pages++;
        _main.CallDeferred("PageBreak");
        IsWaiting = true;
        _gate.Wait();
        IsWaiting = false;
        _main.CallDeferred("ClearPage");
    }

    // called from the main thread (click) or the selftest auto-clicker
    public void SignalInput() { if (_gate.CurrentCount == 0) _gate.Release(); }

    // ---- texture ops (run on the VM thread; marshal Godot node work to the main thread) ----
    public void CreateTexture(int slot, int width, int height) { _slotBmp[slot] = null; _slotDims[slot] = (width, height); }

    public void SetTexture(long resourceId, int slot)
    {
        var asset = _res.Resolve(_scene, resourceId);
        var bmp = asset != null ? ResourceMap.TexturePath(asset) : null;
        _slotBmp[slot] = bmp;
        _slotDims[slot] = BmpHeader.ReadDims(bmp);   // synchronous: dims from the header, no Godot Image
    }

    // Dims are read from the BMP header on the VM thread so the bytecode's geometry math (which calls this
    // synchronously right after set-texture) sees the real size. Pixels are blitted later on the main thread.
    public (int Width, int Height) GetTextureSize(int slot)
        => _slotDims.TryGetValue(slot, out var d) ? (d.W, d.H) : (0, 0);

    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY)
    {
        if (_slotBmp.TryGetValue(slot, out var bmp) && bmp != null)
            _main.CallDeferred("BlitSlot", bmp, srcX, srcY, width, height, dstX, dstY);
    }

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
