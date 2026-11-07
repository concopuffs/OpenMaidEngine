using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepAdvService(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "refresh-adv-skip-input-state": // 0xfa: end fast-forward unless action 6 is held
                // op_0xfa_refresh_adv_skip_input_state@0x416de0 polls the held mask, which carries no Skip injection:
                // without action 6 held it clears run-state 0x08000000. Persistent Skip starts it again at the next
                // polling text wait (op 0x72), and the read state waits for the next page.
                ReconcilePhysicalFastForward();
                if (!PhysicalSkipActionHeld) SetScriptFastForward(false);
                DiscardHeldTriggersUnlessFastForward();   // it polls the input latch and discards it, as op 0x72 does
                return pc + 1;
            case "u0041B290":
            case "set-message-skip": // 0x88: persistent all-message fast-forward service state
                _messageSkipEnabled = Read(a[0]) != 0;
                _messageSkipInjected = _messageSkipEnabled;   // it writes ctx+0x13dc and ctx+0x550fc alike
                _messageSkipServiceActive = _messageSkipEnabled;
                _host.SetMessageSkipActive(_messageSkipServiceActive);
                if (!_messageSkipEnabled) EndPhysicalFastForward();   // op_0x88_set_message_skip clears run-state 0x8000000
                return pc + 1;
            case "u00414E50": // 0x19a: persistent state used by the SO001 active overlay
                Write(a[0], _messageSkipEnabled ? 1 : 0); return pc + 1;
            case "u00414E80":
            case "suspend-adv-skip-service": // 0x19b: preserve the toggle while leaving ADV presentation
                _advSkipServiceEnabled = false;
                _messageSkipInjected = false;   // ctx+0x13dc; the toggle (ctx+0x550fc) survives
                _messageSkipServiceActive = false;
                _host.SetMessageSkipActive(false);
                EndPhysicalFastForward();   // it clears run-state 0x8000000, however started
                return pc + 1;
            case "u00414EC0":
            case "resume-adv-skip-service": // 0x19c: recompute active fast-forward on ADV entry
                _advSkipServiceEnabled = true;
                // ctx+0x13dc follows the toggle; a read-skip-only resume leaves it as it was.
                if (_messageSkipEnabled) _messageSkipInjected = true;
                else if (!_advReadSkipState) _messageSkipInjected = false;
                _messageSkipServiceActive =
                    _messageSkipEnabled || _advReadSkipState || _host.IsAdvReadSkipActive;
                _host.SetMessageSkipActive(_messageSkipServiceActive);
                EndPhysicalFastForward();   // it rebuilds run-state 0x8000000 from persistent Skip and read-skip only
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
                // op_0x1ca_set_read_message_skip@0x41f880 only writes the setting; the next page reads it.
                _sharedProfile.ReadMessageSkipEnabled = Read(a[0]) != 0;
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
                SetAutoMessageTime((int)Read(a[0]), Read(a[1]));
                return pc + 1;
            case "u00415670":
            case "block-mark":
            case "reset-message-voice-state": // 0x1bc resets native per-message voice/queued-voice state
                // Native stops channel 12, drops the Skip-queued voice and clears voice_mute_next_flags.
                _host.StopVoice();
                _voiceMuteNextFlags = 0;
                _autoVoicePending = false; return pc + 1;
            case "u00415BF0":
            case "reset-message-skip-input": // 0x101 clears transient input/run bits, not op 0x88 state
                // Native also writes the CancelMesSkipOnClick press/release state ctx+0x6da80 = 0, and polls the input
                // latch, discarding it.
                _skipClickCancelState = 0;
                Interlocked.Exchange(ref _skipClickCancelTriggers, 0);
                TakeHeldTriggers();
                // It clears run-state 0x8000000 and sets ctx+0x6da74, so a held action 6 starts no reveal's fast-forward
                // until a reveal runs with it released.
                EndPhysicalFastForward();
                lock (_physicalFastForwardLock) _revealPhysicalSkipSuppressed = true;
                return pc + 1;
            default:
                throw new InvalidOperationException($"Non-ADV-service opcode routed to ADV-service handler: {label}");
        }
    }
}
