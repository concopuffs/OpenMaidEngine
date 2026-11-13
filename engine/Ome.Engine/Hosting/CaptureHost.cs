using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Hosting;

/// <summary>A headless host for tools and tests: it records the diagnostic boxes and, as it hears the VM's text native,
/// each run shown.</summary>
public sealed class CaptureHost : IHost, ITextListener
{
    public List<(int Offset, string Text)> Emitted { get; } = new();
    public List<DiagnosticMessage> Diagnostics { get; } = new();
    public void ShowDiagnosticMessage(DiagnosticMessage message) => Diagnostics.Add(message);
    public void RunShown(AdvLiveTextRun run, int glyphDelayMilliseconds) => Emitted.Add((run.SourceOffset, run.Text));
    public void HiddenRunShown(AdvLiveTextRun run) => Emitted.Add((run.SourceOffset, run.Text));
    public void WaitForInput() { }
    public void Sleep(long duration) { }
    public void FrameYield() { }
}
