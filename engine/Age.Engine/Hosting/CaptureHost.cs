namespace Age.Engine.Hosting;
public sealed class CaptureHost : IHost
{
    public List<(int Offset, string Text)> Emitted { get; } = new();
    public int CallScriptCount { get; private set; }
    public Dictionary<int, int> Stubs { get; } = new();
    public void ShowText(int offset, string text) => Emitted.Add((offset, text));
    public void CallScript(long id) => CallScriptCount++;
    public void OnStub(int opcode) { Stubs.TryGetValue(opcode, out var c); Stubs[opcode] = c + 1; }
    public void WaitForInput() { }
    public void CreateTexture(int slot, int width, int height) { }
    public void SetTexture(long resourceId, int slot) { }
    public void DrawTexture(int slot, int x, int y, int width, int height) { }
}
