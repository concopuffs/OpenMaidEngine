using System.Collections.Generic;
using System.Linq;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;

/// <summary>Shared test doubles: a host that records observable effects, and an in-memory script
/// provider for synthetic call-script targets.</summary>
internal sealed class RecordingHost : IHost
{
    public int Waits, CallScripts;
    public readonly List<(int Offset, string Text)> Lines = new();
    public void ShowText(int offset, string text) => Lines.Add((offset, text));
    public void CallScript(long id) => CallScripts++;
    public void OnStub(int opcode) { }
    public void WaitForInput() => Waits++;
    public void CreateTexture(int slot, int w, int h) { }
    public void SetTexture(long resId, int slot) { }
    public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy) { }
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
    public void PlayBgm(long id) { }
    public void PlayVoice(long id) { }
}

internal sealed class MapProvider : IScriptProvider
{
    private readonly Dictionary<long, Script> _m;
    public MapProvider(Dictionary<long, Script> m) => _m = m;
    public Script? GetById(long id) => _m.TryGetValue(id, out var s) ? s : null;
}

/// <summary>A controlled test double: every call-script id resolves to the same script (typically a
/// no-op that just exits). Lets a test run a real script with call-script handling ON while isolating
/// it from the real subroutines' game-state dependencies.</summary>
internal sealed class AnyProvider : IScriptProvider
{
    private readonly Script _s;
    public AnyProvider(Script s) => _s = s;
    public Script? GetById(long id) => _s;
}

/// <summary>Captures every trace event for assertions; TracingSteps is settable so a test can
/// exercise the Step gate both ways.</summary>
internal sealed class RecordingTraceSink : ITraceSink
{
    public bool TracingSteps { get; init; }
    public readonly List<TraceEvent> Events = new();
    public void Emit(in TraceEvent e) => Events.Add(e);
    public List<long> CallScriptIds =>
        Events.Where(e => e.Kind == TraceEventKind.CallScript).Select(e => e.Id).ToList();
}
