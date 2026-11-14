using Godot;
using Ome.Engine.Devices;
using Ome.Engine.Model;
using Ome.Engine.Natives;

/// <summary>
/// <see cref="ICursorDevice"/>, the AGE cursor the input and waits native shows, and <see cref="IShellDialogs"/>, the
/// dialogs the shell-services native opens (engine layering milestone 9). Each command is deferred to the main thread
/// (spec §6.2: a device call is a command).
/// </summary>
public partial class Main : ICursorDevice, IShellDialogs
{
    void ICursorDevice.SetCursor(RgbaImage image, int hotspotX, int hotspotY)
        => CallDeferred(MethodName.SetAgeCursor, image.Pixels, image.Width, image.Height, hotspotX, hotspotY);
    void ICursorDevice.ClearCursor() => CallDeferred(MethodName.ClearAgeCursor);
    void ICursorDevice.WarpCursor(int x, int y) => CallDeferred(MethodName.WarpAgeCursor, x, y);

    // A dialog is not a Variant, so these defer a callable rather than a method name.
    void IShellDialogs.ShowDiagnostic(DiagnosticBox box) => Callable.From(() => ShowAgeDiagnostic(box)).CallDeferred();
    void IShellDialogs.ShowScriptError(ScriptErrorBox box) => Callable.From(() => ShowAgeScriptError(box)).CallDeferred();
    void IShellDialogs.ShowTextEntry(TextEntry entry)
        => Callable.From(() => ShowAgeFullwidthTextEditor(entry)).CallDeferred();
}
