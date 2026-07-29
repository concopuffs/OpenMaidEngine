using System.Text;

namespace Age.Engine.Vm;

/// <summary>
/// EngineCtx-lifetime accumulator used by opcodes 0x1b2 through 0x1b4. GameSession shares this
/// state across fresh scene VMs just as native AGE retains the embedded string across script frames.
/// </summary>
public sealed class DiagnosticOutputState
{
    private readonly StringBuilder _text = new();

    public string PendingText => _text.ToString();

    internal void Append(string value) => _text.Append(value);
    internal void Clear() => _text.Clear();
}
