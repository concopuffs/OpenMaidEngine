using Godot;
using Ome.Engine.Hosting;
using Ome.Engine.Model;

/// <summary>
/// <see cref="IFrontEnd"/>, the calls the lifted <see cref="EngineHost"/> makes (engine layering spec, milestone 4).
/// Each command is deferred to the main thread exactly as the host deferred it before the lift; the immediate members
/// run on the caller's thread.
/// </summary>
public partial class Main : IFrontEnd
{
    bool IFrontEnd.TryPlayMovie(byte[] mpegBytes, string assetName, long playbackId, long resourceId, int assetId,
                                long movieFlags, long initialPositionMs, long startDelayMs,
                                long? presentationDurationMs, out long? stopTimeMs)
        => TryPlayMovie(mpegBytes, assetName, playbackId, resourceId, assetId, movieFlags, initialPositionMs,
                        startDelayMs, presentationDurationMs, out stopTimeMs);
    void IFrontEnd.QueueMoviePlaybackRate(long playbackId, double rate) => QueueMoviePlaybackRate(playbackId, rate);
    void IFrontEnd.StopMovie(long playbackId) => CallDeferred(MethodName.StopMovie, playbackId);

    void IFrontEnd.PageBreak() => CallDeferred(MethodName.PageBreak);
    void IFrontEnd.ClearPage() => CallDeferred(MethodName.ClearPage);
    void IFrontEnd.SetAgeCursor(byte[] rgba, int width, int height, int hotspotX, int hotspotY)
        => CallDeferred(MethodName.SetAgeCursor, rgba, width, height, hotspotX, hotspotY);
    void IFrontEnd.ClearAgeCursor() => CallDeferred(MethodName.ClearAgeCursor);
    void IFrontEnd.WarpAgeCursor(int virtualX, int virtualY) => CallDeferred(MethodName.WarpAgeCursor, virtualX, virtualY);

    // A DiagnosticMessage is not a Variant, so these two defer a callable rather than a method name.
    void IFrontEnd.ShowAgeDiagnostic(DiagnosticMessage message) => Callable.From(() => ShowAgeDiagnostic(message)).CallDeferred();
    void IFrontEnd.ShowAgeScriptError(DiagnosticMessage message)
        => Callable.From(() => ShowAgeScriptError(message)).CallDeferred();
    void IFrontEnd.ShowAgeFullwidthTextEditor(string initialText)
        => CallDeferred(MethodName.ShowAgeFullwidthTextEditor, initialText);
    void IFrontEnd.RequestScreenModeFromScript(bool windowed) => RequestScreenModeFromScript(windowed);

    void IFrontEnd.Print(string message) => GD.Print(message);
    void IFrontEnd.Warn(string message) => GD.PushWarning(message);
}
