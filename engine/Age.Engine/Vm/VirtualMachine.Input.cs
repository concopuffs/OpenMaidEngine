using Age.Engine.Hosting;
using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    // The op-0xcc mouse callback is engine state, not frame state (op 0xcd checks the registrant).
    private int _mouseCallbackTarget = -1;
    private Script? _mouseCallbackOwner;
    private long _mouseCallbackIntervalMs;
    private long _mouseCallbackArmedAtMs;

    private int StepInput(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "wait-for-input":
                RefreshAdvReadSkipState();
                // Faithful headless: no player => halt here rather than plow past every prompt (see VmOptions).
                if (_o.HaltAtWaitForInput) { HaltReason ??= "wait-for-input"; return HALT; }
                // Re-arm definitions that an earlier 0x93 left as a template (HotspotRegistry.Reset). Only
                // direct-scene runs reach this: they force the scene's developer-loop 0x93 at 0x622. On retail
                // paths every 0x93 is re-registered before a text wait (2026-11-04; docs/backlog.md).
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
                bool outerWaitPolling = _advTextWaitPolling;
                _advTextWaitPolling = true;
                try
                {
                    _host.WaitForInput((int)Read(a[0]), ServiceHotspotCallback,
                        () => new AdvAutoWaitState(_autoMessageEnabled, _autoVoicePending,
                                                   _autoMessageTime0Ms, _autoMessageTime1Ms));
                }
                finally
                {
                    _advTextWaitPolling = outerWaitPolling;
                }
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
                // op_0xcc_register_mouse_callback@0x420f70: one engine-wide registration (target +0x5f684, owner
                // +0x5f6ac, interval timer +0x5f320). A registration from any frame replaces the earlier one.
                _mouseCallbackTarget = (int)Read(a[1]);
                _mouseCallbackOwner = _cur.Script;
                _mouseCallbackIntervalMs = System.Math.Max(0, Read(a[0]));
                _mouseCallbackArmedAtMs = _host.InputClockMilliseconds;
                lock (_interactiveLock) _rawInputFrame = _cur;
                return pc + 1;
            case "get-input-type":
            case "dispatch-mouse-callback": // 0xcd
            {
                // op_0xcd_dispatch_mouse_callback@0x417e10: once the interval has passed (at once under fast-forward),
                // restart it, push the return pc and jump to the target. A target registered by another script raises
                // Depth with the return pc still pushed, so 再試行 and 無視 resume with it on the stack.
                long now = _host.InputClockMilliseconds;
                if (now - _mouseCallbackArmedAtMs < _mouseCallbackIntervalMs
                    && !(_messageSkipServiceActive || _host.IsMessageSkipActive))
                    return pc + 1;
                _mouseCallbackArmedAtMs = now;
                if (_mouseCallbackTarget == -1) return pc + 1;
                _cur.CallStack.Add(pc + 1);
                if (_mouseCallbackOwner is { } owner && !IsSameScript(owner, _cur.Script))
                    return RaiseInstructionScriptError(DepthErrorText(owner), pc);
                if (_cur.Script.IndexByOffset.TryGetValue(_mouseCallbackTarget, out int target)) return target;
                _cur.CallStack.RemoveAt(_cur.CallStack.Count - 1);
                return pc + 1;
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
            case "get-joystick-button-count": // 0x106: active-device capability, zero when absent
                Write(a[0], System.Math.Max(0, _host.JoystickButtonCount)); return pc + 1;
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

/// <summary>Where <see cref="VirtualMachine.ApplyMouseWheel"/> sent one wheel delta.</summary>
public enum MouseWheelOutcome
{
    /// <summary>Accumulated for op 0x10d.</summary>
    Accumulated,
    /// <summary>Queued the hotspot callback bound to the wheel's logical action.</summary>
    Activated,
    /// <summary>The frontend should advance the page or finish the text reveal, as a click does.</summary>
    Advance,
    /// <summary>Consumed with no effect.</summary>
    Dropped,
}
