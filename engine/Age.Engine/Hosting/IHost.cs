namespace Age.Engine.Hosting;
public interface IHost
{
    void ShowText(int offset, string text);
    void CallScript(long id);
    void OnStub(int opcode);
    void WaitForInput();
}
