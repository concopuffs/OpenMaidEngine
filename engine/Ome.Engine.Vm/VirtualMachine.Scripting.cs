using Ome.Engine.Diagnostics;
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

    bool IInterpreterAccess.IsNumberedSave(int opcode)
        => _t.TryGetDefinition(opcode, out OpcodeDefinition definition) && definition.CanonicalLabel == "save-numbered-slot";

    // ---- maid.vm.call (Lua scripting spec §10) ----

    /// <summary>A refusal inside a Lua script call: the innermost call catches it and fails with its message, so it
    /// never passes a <c>maid.vm.call</c>.</summary>
    private sealed class ModCallRefusedException(string message) : Exception(message);

    // The Lua script calls running, and the step count the innermost one's budget ends at (no call: never).
    private int _modCalls;
    private long _modCallStepsEnd = long.MaxValue, _modCallBudget;

    void IInterpreterAccess.Call(string name, long instructions)
    {
        Script script = _provider?.GetByName(name + ".BIN")
                        ?? throw new ScriptAccessException($"calling {name} failed: no script {name}.BIN is installed");
        if (_depth >= _o.CallDepthCap)
            throw new ScriptAccessException($"calling {name} failed: frames nest {_depth} deep, the cap is {_o.CallDepthCap}");
        (bool, int) block = _tick.StatementBlock;
        long outerEnd = _modCallStepsEnd, outerBudget = _modCallBudget;
        _modCallBudget = Math.Max(0, instructions);
        _modCallStepsEnd = Math.Min(outerEnd, Steps + _modCallBudget);
        _modCalls++;
        FrameOutcome outcome;
        try
        {
            outcome = RunFrame(new ExecFrame(script, EntryIndex(script)), FrameCause.ModCall, script.PackedId);
        }
        catch (ModCallRefusedException refused)
        {
            // Spec §10: the cells keep what the script did; the statement block goes back as the call found it.
            _tick.RestoreStatementBlock(block);
            throw new ScriptAccessException($"calling {name} failed: {refused.Message}");
        }
        finally
        {
            _modCalls--;
            _modCallStepsEnd = outerEnd;
            _modCallBudget = outerBudget;
        }
        // Spec §10, "How it unwinds": the outcome becomes the interpreter's own unwind, which passes every Lua frame
        // (the session keeps the exception past pcall) and reaches the hooked instruction's frame as call-script's would.
        switch (outcome)
        {
            case FrameOutcome.Halted: throw new VmHaltException();
            case FrameOutcome.RootReload: throw new RootReloadRequestedException();
            case FrameOutcome.ExitRequested: throw new ProcessExitRequestedException();
        }
    }

    /// <summary>The waits refuse while a Lua script call runs (<see cref="ModCallWaits"/>).</summary>
    internal void RefuseWaitInModCall(string member)
    {
        if (_modCalls > 0) throw new ModCallRefusedException($"{Here()} waits ({member}), and a called script cannot wait");
    }

    /// <summary>A script error inside a Lua script call would open AGE's error box, a wait: the call fails with the
    /// error's text instead (spec §10).</summary>
    private void RefuseScriptErrorInModCall(string text)
    {
        if (_modCalls > 0) throw new ModCallRefusedException($"{Here()} raised a script error: {text}");
    }

    /// <summary>Spec §10: before an instruction of a Lua script call runs anything, its hooks included, a numbered save or
    /// load, or op 0xae while a restore is pending, refuses the call; so does the call's budget.</summary>
    private void RefuseInstructionInModCall(Instruction instruction)
    {
        if (Steps > _modCallStepsEnd)
            throw new ModCallRefusedException(
                $"the call ran past its budget of {_modCallBudget:N0} script instructions at {Here()}");
        if (!_t.TryGetDefinition(instruction.Opcode, out OpcodeDefinition definition)) return;
        if ((definition.Leaves & OpcodeLeaves.Saves) != 0)
            throw new ModCallRefusedException($"{Here()} saves, and a called script cannot save");
        if ((definition.Leaves & OpcodeLeaves.Loads) != 0)
            throw new ModCallRefusedException($"{Here()} loads a save, and a called script cannot load");
        if ((definition.Leaves & OpcodeLeaves.Restores) != 0 && _saves.RestoringFrames != null)
            throw new ModCallRefusedException(
                $"{Here()} continues the load's restore, and a called script cannot while a restore is pending");
    }

    /// <summary>The running instruction, as a refusal names it: <c>SUB.BIN@0x10 (op 0xc8 sleep)</c>.</summary>
    private string Here()
    {
        ExecFrame frame = _cur;
        IReadOnlyList<Instruction> code = frame.Script.Instructions;
        if ((uint)frame.Pc >= (uint)code.Count) return frame.Script.Name;
        Instruction instruction = code[frame.Pc];
        string label = _t.TryGetDefinition(instruction.Opcode, out OpcodeDefinition definition)
            ? " " + definition.CanonicalLabel
            : "";
        return $"{frame.Script.Name}@0x{instruction.Offset:x} (op 0x{instruction.Opcode:x}{label})";
    }
}
