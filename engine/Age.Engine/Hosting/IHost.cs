namespace Age.Engine.Hosting;
public interface IHost
{
    void ShowText(int offset, string text);
    void CallScript(long id);
    void OnStub(int opcode);
    void WaitForInput();
    void CreateTexture(int slot, int width, int height);
    void SetTexture(long resourceId, int slot);
    void DrawTexture(int slot, int x, int y, int width, int height);
}
