using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepRuntimeSetting(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "get-message-window-alpha": // 0x131: process-owned message:MesWinAlpha setting
            case "u00415F70":
                Write(a[0], _messageWindowAlphaSetting);
                return pc + 1;
            case "set-message-window-alpha": // 0x141: paired message:MesWinAlpha configuration setter
            case "u0041FAA0":
                _messageWindowAlphaSetting = (int)Read(a[0]);
                _host.SetMessageWindowAlphaSetting(_messageWindowAlphaSetting);
                return pc + 1;
            case "set-system-menu-enabled": // 0x142: native AGERC menu reentrancy guard
            case "u0041FB10": // pre-reference compatibility
                _systemMenuActionsEnabled = unchecked((int)Read(a[0]));
                return pc + 1;
            case "get-system-menu-show-delay": // 0x148: paired TIMER_SHOWMENU getter
            case "u004160A0": // pre-reference compatibility
                Write(a[0], unchecked((int)_systemMenuShowDelayMilliseconds));
                return pc + 1;
            case "set-system-menu-show-delay": // 0x149: top-edge dwell threshold in milliseconds
            case "u0041FCE0": // pre-reference compatibility
                _systemMenuShowDelayMilliseconds = unchecked((uint)Read(a[0]));
                return pc + 1;
            default:
                throw new InvalidOperationException($"Non-runtime-setting opcode routed to runtime-setting handler: {label}");
        }
    }
}
