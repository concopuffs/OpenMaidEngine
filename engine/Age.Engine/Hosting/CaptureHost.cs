namespace Age.Engine.Hosting;
public sealed class CaptureHost : IHost
{
    public List<(int Offset, string Text)> Emitted { get; } = new();
    public void ShowText(int offset, string text) => Emitted.Add((offset, text));
    public void WaitForInput() { }
    public void Sleep(long duration) { }
    public void CreateTexture(int slot, int width, int height) { }
    public void SetTexture(long resourceId, int slot) { }
    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
    public void PlayBgm(long id) { }
    public void PlayVoice(long id) { }
}
