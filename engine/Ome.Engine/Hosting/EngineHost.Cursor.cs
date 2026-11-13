using System;

namespace Ome.Engine.Hosting;

// Ops 0x86, 0x87 and 0x10a: the AGE cursor, which the front end shows.
public sealed partial class EngineHost
{
    public void SetCursorResource(long resourceId)
    {
        var asset = _res.ResolveCursor(resourceId);
        if (asset == null) return;
        try
        {
            var cursor = _res.DecodeCursor(asset);
            _frontEnd.SetAgeCursor(cursor.Image.Pixels, cursor.Image.Width, cursor.Image.Height,
                                   cursor.HotspotX, cursor.HotspotY);
            _timeline?.Event("cursor-set", new() { ["resource"] = resourceId, ["asset"] = asset.Name });
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"cursor decode {asset.Name}: {ex.Message}");
        }
    }

    public void ClearCursorResource()
    {
        _frontEnd.ClearAgeCursor();
        _timeline?.Event("cursor-clear", new());
    }

    public void WarpCursor(int virtualX, int virtualY)
    {
        // Script execution is on the engine thread; the front end's input and display belong to its own thread.
        _frontEnd.WarpAgeCursor(virtualX, virtualY);
        _timeline?.Event("cursor-warp", new() { ["x"] = virtualX, ["y"] = virtualY });
    }
}
