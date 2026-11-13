using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0x131: process-owned message:MesWinAlpha setting
    private StepResult OpGetMessageWindowAlpha(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _text.MessageWindowAlphaSetting);
        return StepResult.Next;
    }

    // 0x141: paired message:MesWinAlpha configuration setter
    private StepResult OpSetMessageWindowAlpha(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.SetMessageWindowAlphaSetting((int)Read(a[0]));
        return StepResult.Next;
    }

    // 0x142: native AGERC menu reentrancy guard
    private StepResult OpSetSystemMenuEnabled(Instruction ins, int pc)
    {
        var a = ins.Args;
        _shell.SystemMenuActionsEnabled = unchecked((int)Read(a[0]));
        return StepResult.Next;
    }

    // 0x148: paired TIMER_SHOWMENU getter
    private StepResult OpGetSystemMenuShowDelay(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], unchecked((int)_shell.SystemMenuShowDelayMilliseconds));
        return StepResult.Next;
    }

    // 0x149: top-edge dwell threshold in milliseconds
    private StepResult OpSetSystemMenuShowDelay(Instruction ins, int pc)
    {
        var a = ins.Args;
        _shell.SystemMenuShowDelayMilliseconds = unchecked((uint)Read(a[0]));
        return StepResult.Next;
    }

    // 0x10f: op_0x10f_set_system_key_disable_mask@0x4217a0
    private StepResult OpSetSystemKeyDisableMask(Instruction ins, int pc)
    {
        var a = ins.Args;
        _shell.SystemKeyDisableMask = unchecked((int)Read(a[0]));
        return StepResult.Next;
    }
}
