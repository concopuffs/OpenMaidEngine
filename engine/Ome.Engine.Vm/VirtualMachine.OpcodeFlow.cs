using Ome.Engine.Model;

namespace Ome.Engine.Vm;

/// <summary>An instruction did what its opcode's control facts deny (Lua scripting spec §7.1): the registry's
/// <c>leaves</c> for it is wrong, or its handler changed. Run <c>tools/opcode_flow.py --apply</c>.</summary>
public sealed class OpcodeFlowException(string message) : Exception(message);

/// <summary>
/// The check of each instruction against its opcode's control facts (vm-map/opcodes.toml <c>leaves</c>, Lua scripting
/// spec §7.1), which the link reads to refuse a <c>skip</c> and to warn of an <c>after</c> hook across a wait. It is on
/// when the <see cref="CheckSwitch"/> AppContext switch is, as the engine's tests set it, and costs nothing otherwise.
/// It sees what the handler returned, whether it ran a frame or a wait's callback, and the exceptions that end the run
/// or start a load's restore. Whether a handler waits rests on the derivation: the engine blocks in one place.
/// </summary>
public sealed partial class VirtualMachine
{
    /// <summary>The AppContext switch that turns the check on.</summary>
    public const string CheckSwitch = "Ome.Engine.CheckOpcodeFlow";

    private readonly bool _checkOpcodeFlow = AppContext.TryGetSwitch(CheckSwitch, out bool on) && on;

    // Instruction loops entered: a frame's body or a wait's callback. An instruction during which it grows ran code.
    private long _loopsEntered;

    /// <summary><see cref="Step"/>, with the instruction checked against its opcode's facts.</summary>
    private StepResult CheckedStep(Instruction ins, int pc)
    {
        long loopsBefore = _loopsEntered;
        StepResult step;
        try
        {
            step = Step(ins, pc);
        }
        catch (Exception error) when (error is RootReloadRequestedException or ProcessExitRequestedException
                                               or NumberedRestoreRequestedException)
        {
            // An exception that ends the run or the scene is `ends`, a load's restore `loads`.
            CheckFlow(ins, (error is NumberedRestoreRequestedException ? OpcodeLeaves.Loads : OpcodeLeaves.Ends)
                           | (_loopsEntered != loopsBefore ? OpcodeLeaves.Calls : OpcodeLeaves.None));
            throw;
        }
        OpcodeLeaves left = step.Kind switch
        {
            StepKind.Next => OpcodeLeaves.Falls,
            StepKind.Jump => OpcodeLeaves.Jumps,
            StepKind.FrameReturn or StepKind.HotspotReturn or StepKind.RootReload => OpcodeLeaves.Ends,
            _ => OpcodeLeaves.None,   // a halt ends the run whatever the opcode
        };
        CheckFlow(ins, left | (_loopsEntered != loopsBefore ? OpcodeLeaves.Calls : OpcodeLeaves.None));
        return step;
    }

    private void CheckFlow(Instruction ins, OpcodeLeaves left)
    {
        if (left == OpcodeLeaves.None || !_t.TryGetDefinition(ins.Opcode, out OpcodeDefinition definition)
            || definition.Waits == null)
            return;   // an opcode the port does not implement has no facts
        OpcodeLeaves denied = left & ~definition.Leaves;
        if (denied != OpcodeLeaves.None)
            throw new OpcodeFlowException(
                $"{_cur.Script.Name}@0x{ins.Offset:x}: op 0x{ins.Opcode:x} {definition.CanonicalLabel} left by {denied}, "
                + $"and its facts say {definition.Leaves} (vm-map/opcodes.toml; run tools/opcode_flow.py --apply)");
    }
}
