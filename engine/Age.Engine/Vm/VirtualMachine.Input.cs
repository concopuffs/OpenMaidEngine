using Age.Engine.Hosting;
using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepInput(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "wait-for-input":
                RefreshAdvReadSkipState();
                // Faithful headless: no player => halt here rather than plow past every prompt (see VmOptions).
                if (_o.HaltAtWaitForInput) { HaltReason ??= "wait-for-input"; return HALT; }
                // The native ADV chrome is a coroutine: after an earlier 0x93 cancellation its shared
                // registration pass runs again before a stable message wait. Our blocking host models that
                // scheduler boundary by re-arming the frame's retained definitions here.
                bool wakeAtWait = false;
                lock (_interactiveLock)
                {
                    if (_cur.Hotspots.HasDefinitions && !_cur.Hotspots.Armed)
                    {
                        _interactiveFrame = _cur;
                        wakeAtWait = _cur.Hotspots.Arm(_pointerX, _pointerY);
                    }
                }
                if (wakeAtWait) _host.WakeInputCallbackService();
                _host.WaitForInput((int)Read(a[0]), ServiceHotspotCallback,
                    () => new AdvAutoWaitState(_autoMessageEnabled, _autoVoicePending,
                                               _autoMessageTime0Ms, _autoMessageTime1Ms));
                _sharedProfile.ReadText.QueueMessage(
                    _cur.Script.PackedId, CurrentReadMessageIndex(),
                    _cur.Script.ReadMessageOffsets.Count);
                return pc + 1;
            case "u0041BEB0":
            case "register-hotspot-callbacks": // 0x90: inclusive rect + enter/leave/activate local callbacks
                lock (_interactiveLock)
                    _cur.Hotspots.Register((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                                           (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]));
                return pc + 1;
            case "u00415040":
            case "cancel-hotspot-wait": // 0x93
                lock (_interactiveLock)
                {
                    _cur.Hotspots.Reset();
                    if (ReferenceEquals(_interactiveFrame, _cur)) _interactiveFrame = null;
                }
                return pc + 1;
            case "u00415090":
            case "arm-hotspot-wait": // 0x94
            {
                bool wake;
                lock (_interactiveLock)
                {
                    _interactiveFrame = _cur;
                    wake = _cur.Hotspots.Arm(_pointerX, _pointerY);
                }
                if (wake) _host.WakeInputCallbackService();
                return pc + 1;
            }
            case "u0041C150":
            case "bind-hotspot-key": // 0x97: bind a configured logical action to this record
                lock (_interactiveLock)
                    _cur.Hotspots.BindKey((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                                          (int)Read(a[3]), (int)Read(a[4]));
                return pc + 1;
            case "u0041B210":
            case "set-cursor-resource": // 0x86: raw indexed .CUR resource
                _host.SetCursorResource(Read(a[0])); return pc + 1;
            case "u00414D10":
            case "clear-cursor-resource": // 0x87
                _host.ClearCursorResource(); return pc + 1;
            case "mouse_callback":
            case "register-mouse-callback": // 0xcc (poll interval ms, local target dword offset)
                _cur.MouseCallbackIntervalMs = System.Math.Max(0, Read(a[0]));
                _cur.MouseCallbackTarget = (int)Read(a[1]);
                _cur.MouseCallbackNextAtMs = _host.InputClockMilliseconds + _cur.MouseCallbackIntervalMs;
                lock (_interactiveLock) _rawInputFrame = _cur;
                return pc + 1;
            case "get-input-type":
            case "dispatch-mouse-callback": // 0xcd
            {
                long now = _host.InputClockMilliseconds;
                if (_cur.MouseCallbackTarget < 0 || now < _cur.MouseCallbackNextAtMs) return pc + 1;
                _cur.MouseCallbackNextAtMs = now + _cur.MouseCallbackIntervalMs;
                if (!_cur.Script.IndexByOffset.TryGetValue(_cur.MouseCallbackTarget, out int target))
                    return pc + 1;
                _cur.CallStack.Add(pc + 1);
                return target;
            }
            case "joy_callback":
            case "register-joy-callback": // 0xfb (input index, local target dword offset)
            {
                int index = (int)Read(a[0]);
                if ((uint)index < 32) _cur.InputCallbackTargets[index] = (int)Read(a[1]);
                return pc + 1;
            }
            case "u0041E360":
            case "set-input-action-count": // 0xfe: actions [0,count), no-input callback at count
            {
                int count = unchecked((int)Read(a[0]));
                if (!InputBindings.SetActionCount(count))
                {
                    HaltReason ??= $"input-action-count-out-of-range:{count}";
                    return HALT;
                }
                return pc + 1;
            }
            case "u00415A10":
            case "poll-joy-callback-input": // 0xff
                _cur.PendingInputCallbackMask = InputBindings.PollActionMask()
                    | Volatile.Read(ref _heldInputCallbackMask)
                    | Interlocked.Exchange(ref _queuedInputCallbackMask, 0);
                _cur.InputCallbackScanIndex = 0;
                return pc + 1;
            case "u00415A60":
            case "dispatch-joy-callbacks": // 0x100
            {
                int actionCount = InputBindings.ActionCount;
                if (_cur.PendingInputCallbackMask == 0)
                {
                    int idleTarget = _cur.InputCallbackTargets[actionCount];
                    if (idleTarget < 0 || !_cur.Script.IndexByOffset.TryGetValue(idleTarget, out int target))
                        return pc + 1;
                    _cur.CallStack.Add(pc + 1);
                    return target;
                }
                while (_cur.InputCallbackScanIndex < actionCount)
                {
                    int index = _cur.InputCallbackScanIndex++;
                    if ((_cur.PendingInputCallbackMask & (1 << index)) == 0) continue;
                    int targetOffset = _cur.InputCallbackTargets[index];
                    if (targetOffset < 0 || !_cur.Script.IndexByOffset.TryGetValue(targetOffset, out int target))
                        continue;
                    // Resume on op 0x100 so another simultaneously active input can dispatch.
                    _cur.CallStack.Add(pc);
                    return target;
                }
                return pc + 1;
            }
            case "u0041E500":
            case "map-joystick-button": // 0x107: button slot N emits action N+4
                InputBindings.MapJoystickButton(unchecked((int)Read(a[0])), unchecked((int)Read(a[1])));
                return pc + 1;
            case "u00415E70":
            case "get-mouse-button-state": // 0x108
                Write(a[0], Volatile.Read(ref _mouseButtonState)); return pc + 1;
            case "u00415F10":
            case "consume-mouse-wheel-delta": // 0x10d
                Write(a[0], Interlocked.Exchange(ref _mouseWheelDelta, 0)); return pc + 1;
            case "u00415EC0":
            case "get-cursor-virtual": // 0x109
            {
                int x, y;
                lock (_interactiveLock) { x = _pointerX; y = _pointerY; }
                Write(a[0], x == int.MinValue ? 0 : x);
                Write(a[1], y == int.MinValue ? 0 : y);
                return pc + 1;
            }
            case "u0041E540":
            case "set-cursor-virtual": // 0x10a: update AGE state immediately, then request the native warp
            {
                int x = (int)Read(a[0]);
                int y = (int)Read(a[1]);
                UpdatePointer(x, y);
                _host.WarpCursor(x, y);
                return pc + 1;
            }
            case "u0041E5A0":
            case "map-mouse-button": // 0x10b: physical button -> slot, polled action is slot+4
                InputBindings.MapMouseButton(unchecked((int)Read(a[0])), unchecked((int)Read(a[1])));
                return pc + 1;
            case "u0041E5E0":
            case "map-keyboard-scancode": // 0x10c: logical action <- DIK translated through native VK table
            {
                int action = unchecked((int)Read(a[0]));
                int dik = unchecked((int)Read(a[1]));
                if (!InputBindings.MapKeyboardScanCode(action, dik))
                {
                    HaltReason ??= $"keyboard-action-out-of-range:{action}";
                    return HALT;
                }
                return pc + 1;
            }
            default:
                throw new InvalidOperationException($"Non-input opcode routed to input handler: {label}");
        }
    }
}
