using Ome.Engine.Model;

namespace Ome.Engine.Hosting;
public sealed class CaptureHost : IHost
{
    public List<(int Offset, string Text)> Emitted { get; } = new();
    public List<DiagnosticMessage> Diagnostics { get; } = new();
    public void ShowDiagnosticMessage(DiagnosticMessage message) => Diagnostics.Add(message);
    public void ShowText(int offset, string text) => Emitted.Add((offset, text));
    public void WaitForInput() { }
    public void Sleep(long duration) { }
    public void FrameYield() { }
}
