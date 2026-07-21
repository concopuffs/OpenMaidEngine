namespace Age.Engine.Model;

/// <summary>
/// AGE's process-owned physical-to-logical input map. Scripts configure the logical action count and
/// keyboard/mouse/joystick bindings; op 0xff polls this state and op 0x100 dispatches the resulting mask.
/// Win32 virtual-key values are retained as the keyboard ABI because native op 0x10c translates DIK scan
/// codes through that table before installing a mapping.
/// </summary>
public sealed class InputBindings
{
    public const int MaximumActions = 32;
    public const int DefaultActionCount = 7;
    public const double JoystickAxisThreshold = 0.5;

    private readonly object _lock = new();
    private readonly int[] _keyboardActions = Enumerable.Repeat(-1, 256).ToArray();
    private readonly int[] _mouseButtonSlots = new int[2];
    private readonly int[] _joystickButtons = new int[MaximumActions];
    private readonly HashSet<int> _heldVirtualKeys = new();
    private uint _heldMouseButtons;
    private uint _heldJoystickButtons;
    private double _joystickX;
    private double _joystickY;
    private int _actionCount = DefaultActionCount;

    public InputBindings()
    {
        // input_manager_initialize_defaults@0x460630: seven default actions before SYSTEM4 extends
        // the table to ten. Several physical keys may map to the same logical action.
        MapKeyboardScanCode(0, 0xc8); // Up
        MapKeyboardScanCode(1, 0xcd); // Right
        MapKeyboardScanCode(2, 0xd0); // Down
        MapKeyboardScanCode(3, 0xcb); // Left
        MapKeyboardScanCode(4, 0x1c); // Enter
        MapKeyboardScanCode(5, 0x39); // Space
        MapKeyboardScanCode(6, 0x0e); // Backspace
    }

    public int ActionCount
    {
        get { lock (_lock) return _actionCount; }
    }

    public bool SetActionCount(int count)
    {
        if ((uint)count >= MaximumActions) return false;
        lock (_lock) _actionCount = count;
        return true;
    }

    public bool MapKeyboardScanCode(int action, int dikScanCode)
    {
        if ((uint)action >= MaximumActions) return false;
        int virtualKey = DikToVirtualKey(dikScanCode);
        if (virtualKey == 0) return true; // Native writes VK[0]; GetAsyncKeyState(0) never contributes.
        lock (_lock) _keyboardActions[virtualKey] = action;
        return true;
    }

    public void MapJoystickButton(int buttonSlot, int physicalButton)
    {
        if ((uint)buttonSlot >= MaximumActions) return;
        lock (_lock) _joystickButtons[buttonSlot] = physicalButton;
    }

    public void MapMouseButton(int buttonSlot, int physicalButton)
    {
        if ((uint)buttonSlot >= MaximumActions || (uint)physicalButton >= _mouseButtonSlots.Length) return;
        lock (_lock) _mouseButtonSlots[physicalButton] = buttonSlot;
    }

    public int KeyboardAction(int virtualKey)
    {
        if ((uint)virtualKey >= _keyboardActions.Length) return -1;
        lock (_lock) return _keyboardActions[virtualKey];
    }

    public int MouseAction(int physicalButton)
    {
        if ((uint)physicalButton >= _mouseButtonSlots.Length) return -1;
        lock (_lock) return (_mouseButtonSlots[physicalButton] + 4) & 31;
    }

    public int JoystickButtonActionMask(int physicalButton)
    {
        if ((uint)physicalButton >= 32) return 0;
        lock (_lock) return JoystickButtonActionMaskLocked(physicalButton);
    }

    public void UpdateKeyboardVirtualKey(int virtualKey, bool pressed)
    {
        if ((uint)virtualKey >= _keyboardActions.Length) return;
        lock (_lock)
        {
            if (pressed) _heldVirtualKeys.Add(virtualKey);
            else _heldVirtualKeys.Remove(virtualKey);
        }
    }

    public void UpdateMouseButton(int physicalButton, bool pressed)
    {
        if ((uint)physicalButton >= 32) return;
        lock (_lock) UpdateBit(ref _heldMouseButtons, physicalButton, pressed);
    }

    public void UpdateJoystickButton(int physicalButton, bool pressed)
    {
        if ((uint)physicalButton >= 32) return;
        lock (_lock) UpdateBit(ref _heldJoystickButtons, physicalButton, pressed);
    }

    public void UpdateJoystickAxes(double x, double y)
    {
        lock (_lock)
        {
            _joystickX = Math.Clamp(x, -1.0, 1.0);
            _joystickY = Math.Clamp(y, -1.0, 1.0);
        }
    }

    public void UpdateJoystickAxis(int axis, double value)
    {
        lock (_lock)
        {
            if (axis == 0) _joystickX = Math.Clamp(value, -1.0, 1.0);
            else if (axis == 1) _joystickY = Math.Clamp(value, -1.0, 1.0);
        }
    }

    public int PollActionMask()
    {
        lock (_lock)
        {
            int mask = 0;
            foreach (int virtualKey in _heldVirtualKeys)
            {
                int action = _keyboardActions[virtualKey];
                if ((uint)action < MaximumActions) mask |= 1 << action;
            }

            // Native polls only VK_LBUTTON/VK_RBUTTON. Their zero-initialized slots both produce
            // action 4 until scripts remap one of them.
            for (int button = 0; button < _mouseButtonSlots.Length; button++)
                if ((_heldMouseButtons & (1u << button)) != 0)
                    mask |= 1 << ((_mouseButtonSlots[button] + 4) & 31);

            if (_joystickY < -JoystickAxisThreshold) mask |= 1 << 0;
            else if (_joystickY > JoystickAxisThreshold) mask |= 1 << 2;
            if (_joystickX > JoystickAxisThreshold) mask |= 1 << 1;
            else if (_joystickX < -JoystickAxisThreshold) mask |= 1 << 3;

            for (int physicalButton = 0; physicalButton < 32; physicalButton++)
                if ((_heldJoystickButtons & (1u << physicalButton)) != 0)
                    mask |= JoystickButtonActionMaskLocked(physicalButton);
            return mask;
        }
    }

    private int JoystickButtonActionMaskLocked(int physicalButton)
    {
        int mask = 0;
        // Native loops input_action_count slots and uses x86's masked shift for slot+4.
        for (int slot = 0; slot < _actionCount; slot++)
            if (_joystickButtons[slot] == physicalButton) mask |= 1 << ((slot + 4) & 31);
        return mask;
    }

    private static void UpdateBit(ref uint field, int bit, bool set)
    {
        uint mask = 1u << bit;
        field = set ? field | mask : field & ~mask;
    }

    /// <summary>The DIK-to-VK entries populated by input_initialize_dik_to_vk_table@0x45fc60.</summary>
    public static int DikToVirtualKey(int dik) => dik switch
    {
        0x01 => 0x1b, 0x02 => 0x31, 0x03 => 0x32, 0x04 => 0x33, 0x05 => 0x34,
        0x06 => 0x35, 0x07 => 0x36, 0x08 => 0x37, 0x09 => 0x38, 0x0a => 0x39,
        0x0b => 0x30, 0x0c => 0x6d, 0x0e => 0x08, 0x0f => 0x09,
        0x10 => 0x51, 0x11 => 0x57, 0x12 => 0x45, 0x13 => 0x52, 0x14 => 0x54,
        0x15 => 0x59, 0x16 => 0x55, 0x17 => 0x49, 0x18 => 0x4f, 0x19 => 0x50,
        0x1c => 0x0d, 0x1d => 0x11, 0x1e => 0x41, 0x1f => 0x53, 0x20 => 0x44,
        0x21 => 0x46, 0x22 => 0x47, 0x23 => 0x48, 0x24 => 0x4a, 0x25 => 0x4b,
        0x26 => 0x4c, 0x2a => 0x10, 0x2c => 0x5a, 0x2d => 0x58, 0x2e => 0x43,
        0x2f => 0x56, 0x30 => 0x42, 0x31 => 0x4e, 0x32 => 0x4d, 0x36 => 0x10,
        0x38 => 0x12, 0x39 => 0x20,
        0x3b => 0x70, 0x3c => 0x71, 0x3d => 0x72, 0x3e => 0x73, 0x3f => 0x74,
        0x40 => 0x75, 0x41 => 0x76, 0x42 => 0x77, 0x43 => 0x78, 0x44 => 0x79,
        0x45 => 0x90, 0x46 => 0x91, 0x47 => 0x67, 0x48 => 0x68, 0x49 => 0x69,
        0x4a => 0x6d, 0x4b => 0x64, 0x4c => 0x65, 0x4d => 0x66, 0x4e => 0x6b,
        0x4f => 0x61, 0x50 => 0x62, 0x51 => 0x63, 0x52 => 0x60, 0x53 => 0x6e,
        0x57 => 0x7a, 0x58 => 0x7b, 0x70 => 0x15, 0x79 => 0x1c, 0x7b => 0x1d,
        0x94 => 0x19, 0x9c => 0x0d, 0x9d => 0x11, 0xb5 => 0x6f, 0xb8 => 0x12,
        0xc7 => 0x12, 0xc8 => 0x26, 0xc9 => 0x21, 0xcb => 0x25,
        0xcd => 0x27, 0xcf => 0x23, 0xd0 => 0x28, 0xd1 => 0x22, 0xd2 => 0x2d,
        0xdb => 0x5b, 0xdc => 0x5c, 0xdd => 0x5d,
        _ => 0,
    };
}
