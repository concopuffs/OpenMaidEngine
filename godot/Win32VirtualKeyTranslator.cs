using Godot;

/// <summary>Translate Godot's layout-independent physical key identity into the Win32 VK namespace
/// used by AGE's native DIK translation table and GetAsyncKeyState poller.</summary>
internal static class Win32VirtualKeyTranslator
{
    public static bool TryTranslate(InputEventKey input, out int virtualKey)
    {
        Key key = input.PhysicalKeycode is not (Key.None or Key.Unknown)
            ? input.PhysicalKeycode : input.Keycode;
        long value = (long)key;
        if (value is >= (long)Key.Key0 and <= (long)Key.Key9
            || value is >= (long)Key.A and <= (long)Key.Z)
        {
            virtualKey = (int)value;
            return true;
        }

        virtualKey = key switch
        {
            Key.Backspace => 0x08,
            Key.Tab => 0x09,
            Key.Enter or Key.KpEnter => 0x0d,
            Key.Shift => 0x10,
            Key.Ctrl => 0x11,
            Key.Alt => 0x12,
            Key.Pause => 0x13,
            Key.Capslock => 0x14,
            Key.Escape => 0x1b,
            Key.Space => 0x20,
            Key.Pageup => 0x21,
            Key.Pagedown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Print or Key.Sysreq => 0x2c,
            Key.Insert => 0x2d,
            Key.Delete => 0x2e,
            Key.Meta => input.Location == KeyLocation.Right ? 0x5c : 0x5b,
            Key.Menu => 0x5d,
            Key.Kp0 => 0x60,
            Key.Kp1 => 0x61,
            Key.Kp2 => 0x62,
            Key.Kp3 => 0x63,
            Key.Kp4 => 0x64,
            Key.Kp5 => 0x65,
            Key.Kp6 => 0x66,
            Key.Kp7 => 0x67,
            Key.Kp8 => 0x68,
            Key.Kp9 => 0x69,
            Key.KpMultiply => 0x6a,
            Key.KpAdd => 0x6b,
            Key.KpSubtract => 0x6d,
            Key.KpPeriod => 0x6e,
            Key.KpDivide => 0x6f,
            Key.F1 => 0x70,
            Key.F2 => 0x71,
            Key.F3 => 0x72,
            Key.F4 => 0x73,
            Key.F5 => 0x74,
            Key.F6 => 0x75,
            Key.F7 => 0x76,
            Key.F8 => 0x77,
            Key.F9 => 0x78,
            Key.F10 => 0x79,
            Key.F11 => 0x7a,
            Key.F12 => 0x7b,
            Key.Numlock => 0x90,
            Key.Scrolllock => 0x91,
            Key.Semicolon or Key.Colon => 0xba,
            Key.Equal or Key.Plus => 0xbb,
            Key.Comma or Key.Less => 0xbc,
            Key.Minus or Key.Underscore => 0xbd,
            Key.Period or Key.Greater => 0xbe,
            Key.Slash or Key.Question => 0xbf,
            Key.Quoteleft or Key.Asciitilde => 0xc0,
            Key.Bracketleft or Key.Braceleft => 0xdb,
            Key.Backslash or Key.Bar => 0xdc,
            Key.Bracketright or Key.Braceright => 0xdd,
            Key.Apostrophe or Key.Quotedbl => 0xde,
            _ => 0,
        };
        return virtualKey != 0;
    }
}
