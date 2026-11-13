using Godot;
using Ome.Engine.Hosting;
using Ome.Engine.Natives;

/// <summary>
/// <see cref="IFrontEnd"/>, the calls the lifted <see cref="EngineHost"/> makes (engine layering spec, milestone 4), and
/// <see cref="IShellDialogs"/>, the dialogs the shell-services native opens (milestone 9). Each command is deferred to
/// the main thread exactly as the host deferred it before the lift; the immediate members run on the caller's thread.
/// </summary>
public partial class Main : IFrontEnd, IShellDialogs
{
    void IFrontEnd.PageBreak() => CallDeferred(MethodName.PageBreak);
    void IFrontEnd.ClearPage() => CallDeferred(MethodName.ClearPage);
    void IFrontEnd.SetAgeCursor(byte[] rgba, int width, int height, int hotspotX, int hotspotY)
        => CallDeferred(MethodName.SetAgeCursor, rgba, width, height, hotspotX, hotspotY);
    void IFrontEnd.ClearAgeCursor() => CallDeferred(MethodName.ClearAgeCursor);
    void IFrontEnd.WarpAgeCursor(int virtualX, int virtualY) => CallDeferred(MethodName.WarpAgeCursor, virtualX, virtualY);

    void IFrontEnd.Print(string message) => GD.Print(message);
    void IFrontEnd.Warn(string message) => GD.PushWarning(message);

    // A dialog is not a Variant, so these defer a callable rather than a method name.
    void IShellDialogs.ShowDiagnostic(DiagnosticBox box) => Callable.From(() => ShowAgeDiagnostic(box)).CallDeferred();
    void IShellDialogs.ShowScriptError(ScriptErrorBox box) => Callable.From(() => ShowAgeScriptError(box)).CallDeferred();
    void IShellDialogs.ShowTextEntry(TextEntry entry)
        => Callable.From(() => ShowAgeFullwidthTextEditor(entry)).CallDeferred();
}
