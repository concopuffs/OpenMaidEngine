using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepAdvService(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "u0041B290":
            case "set-message-skip": // 0x88: persistent all-message fast-forward service state
                _messageSkipEnabled = Read(a[0]) != 0;
                _messageSkipServiceActive = _messageSkipEnabled;
                _host.SetMessageSkipActive(_messageSkipServiceActive);
                return pc + 1;
            case "u00414E50": // 0x19a: persistent state used by the SO001 active overlay
                Write(a[0], _messageSkipEnabled ? 1 : 0); return pc + 1;
            case "u00414E80":
            case "suspend-adv-skip-service": // 0x19b: preserve the toggle while leaving ADV presentation
                _messageSkipServiceActive = false;
                _host.SetMessageSkipActive(false);
                return pc + 1;
            case "u00414EC0":
            case "resume-adv-skip-service": // 0x19c: recompute active fast-forward on ADV entry
                _messageSkipServiceActive =
                    _messageSkipEnabled || _advReadSkipState || _host.IsAdvReadSkipActive;
                _host.SetMessageSkipActive(_messageSkipServiceActive);
                RefreshPhysicalMessageSkipState();
                return pc + 1;
            case "get-message-skip": // 0x1c7: persistent Skip or host-supplied Ctrl fast-forward
                // Native persistent state and the independently polled physical action-6 channel both
                // re-arm the transient run-state bit consumed by this query.
                Write(a[0], _messageSkipServiceActive || _host.IsMessageSkipActive ? 1 : 0); return pc + 1;
            case "get-adv-read-skip-state": // 0x1cc: per-message read/click skip service state
            case "get-adv-service-state":   // compatibility with pre-recovery generated tables
                Write(a[0], _advReadSkipState || _host.IsAdvReadSkipActive ? 1 : 0); return pc + 1;
            case "u0041B9B0":
            case "set-read-message-skip": // 0x1ca: engine setting message:ReadTextSkip
                _sharedProfile.ReadMessageSkipEnabled = Read(a[0]) != 0;
                RefreshAdvReadSkipState();
                return pc + 1;
            case "u00414FD0":
            case "get-read-message-skip": // 0x1cb
                Write(a[0], _sharedProfile.ReadMessageSkipEnabled ? 1 : 0);
                return pc + 1;
            case "u00414F60":
            case "get-auto-message": // 0x1b6: VM service state used by the ADV redraw callback
                Write(a[0], _autoMessageEnabled ? 1 : 0); return pc + 1;
            case "u0041B640":
            case "set-auto-message": // 0x1b7
                _autoMessageEnabled = Read(a[0]) != 0; return pc + 1;
            case "u0041B670":
            case "get-auto-message-time": // 0x1b8 (selector 0=post-voice Time0, 1=unvoiced Time1, out)
                Write(a[1], Read(a[0]) == 0 ? _autoMessageTime0Ms : _autoMessageTime1Ms); return pc + 1;
            case "u0041B710":
            case "set-auto-message-time": // 0x1b9 (selector, milliseconds)
                if (Read(a[0]) == 0) _autoMessageTime0Ms = Read(a[1]);
                else if (Read(a[0]) == 1) _autoMessageTime1Ms = Read(a[1]);
                return pc + 1;
            case "u00415670":
            case "block-mark":
            case "reset-message-voice-state": // 0x1bc resets native per-message voice/queued-voice state
                _autoVoicePending = false; return pc + 1;
            case "u00415BF0":
            case "reset-message-skip-input": // 0x101 clears transient input/run bits, not op 0x88 state
                return pc + 1;
            default:
                throw new InvalidOperationException($"Non-ADV-service opcode routed to ADV-service handler: {label}");
        }
    }
}
