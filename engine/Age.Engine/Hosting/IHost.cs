namespace Age.Engine.Hosting;
public interface IHost
{
    void ShowText(int offset, string text);
    void WaitForInput();
    void CreateTexture(int slot, int width, int height);
    void SetTexture(long resourceId, int slot);
    void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY);
    (int Width, int Height) GetTextureSize(int slot);
    void PlayBgm(long id);
    void PlayVoice(long id);
}
