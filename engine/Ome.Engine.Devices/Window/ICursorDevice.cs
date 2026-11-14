using Ome.Engine.Model;

namespace Ome.Engine.Devices;

/// <summary>
/// The pointer's cursor over the game (engine layering spec §6.1, "Input": set and move the cursor). The input and
/// waits native decodes the cursor a script names and tells the device to show it.
/// <para>Called on the engine thread. Each call is a command (spec §6.2): the front end applies it on its own thread,
/// in the order sent.</para>
/// </summary>
public interface ICursorDevice
{
    /// <summary>Show <paramref name="image"/> as the pointer, its hotspot at (<paramref name="hotspotX"/>,
    /// <paramref name="hotspotY"/>) of the image.</summary>
    void SetCursor(RgbaImage image, int hotspotX, int hotspotY);

    /// <summary>Show the system's arrow again.</summary>
    void ClearCursor();

    /// <summary>Move the pointer to (<paramref name="x"/>, <paramref name="y"/>) of the game's screen.</summary>
    void WarpCursor(int x, int y);
}
