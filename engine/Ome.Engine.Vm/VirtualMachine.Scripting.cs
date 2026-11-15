using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;

namespace Ome.Engine.Vm;

/// <summary>
/// The interpreter's side of <c>maid.vm</c> (docs/superpowers/specs/2026-11-10-lua-scripting-design.md §8.1, §14.1):
/// the frames, the current frame's locals and its instruction's operands, read and written as the opcodes do. The
/// scripting native reaches them only while a hook or a console chunk it runs is on the stack.
/// </summary>
public sealed partial class VirtualMachine : IInterpreterAccess
{
    private readonly Scripting? _scripting;

    /// <summary>Build a session; false when the build halts the run (<c>--lua-errors halt</c>).</summary>
    private bool StartScriptingSession(SessionReason reason)
    {
        if (_scripting?.StartSession(reason) is not { } halt) return true;
        HaltReason ??= halt;
        return false;
    }

    IReadOnlyList<(string Script, int Offset)> IInterpreterAccess.Frames()
    {
        var frames = new (string, int)[_activeExecutionFrames.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            ExecFrame frame = _activeExecutionFrames[i];
            IReadOnlyList<Instruction> code = frame.Script.Instructions;
            frames[i] = (frame.Script.Name, (uint)frame.Pc < (uint)code.Count ? code[frame.Pc].Offset : -1);
        }
        return frames;
    }

    bool IInterpreterAccess.HasFrame => _activeExecutionFrames.Count > 0;

    long IInterpreterAccess.LocalInteger(int index) => Gi(_cur.Locals.I, index);
    void IInterpreterAccess.SetLocalInteger(int index, long value) => _cur.Locals.I[index] = value;
    long IInterpreterAccess.LocalFloat(int index) => Gi(_cur.Locals.F, index);
    void IInterpreterAccess.SetLocalFloat(int index, long bits) => _cur.Locals.F[index] = bits;
    string IInterpreterAccess.LocalString(int index) => Gs(_cur.Locals.S, index);
    void IInterpreterAccess.SetLocalString(int index, string value) => _cur.Locals.S[index] = value;

    CellValue IInterpreterAccess.ReadOperand(Operand operand) => operand.Type switch
    {
        T_FLOAT or T_GFLOAT or T_LFLOAT => CellValue.Float(CellRules.ReadFloat(Read(operand))),
        T_STR or T_GSTR or T_GSTRPTR or T_LSTR or T_LSTRPTR => CellValue.String(ReadStr(operand)),
        _ => CellValue.Integer(CellRules.ReadInteger(Read(operand))),
    };

    void IInterpreterAccess.WriteOperand(Operand operand, CellValue value)
    {
        switch (value.Kind)
        {
            case CellKind.String: WriteStr(operand, value.Text ?? ""); break;
            case CellKind.Float: Write(operand, CellRules.FloatBits(value.Number)); break;
            default: Write(operand, (long)value.Number); break;
        }
    }

    uint? IInterpreterAccess.ScriptId(string name)
        => _provider?.GetByName(name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase) ? name : name + ".BIN")?.PackedId;
}
