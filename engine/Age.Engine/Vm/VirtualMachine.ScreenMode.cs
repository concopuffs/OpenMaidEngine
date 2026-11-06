using Age.Engine.Model;

namespace Age.Engine.Vm;

/// <summary>
/// AGE's screen mode, as far as the VM sees it (docs/engine-re.md, "Screen mode"). The frontend owns the window and
/// performs every switch: the maximize box, Alt+Enter, AGERC's ﾌﾙｽｸﾘｰﾝ/ｳｲﾝﾄﾞｳ commands, the fullscreen strip and op
/// 0x2cf. Native display_toggle_screen_mode_and_menu_bar@0x412df0 ends a switch by launching CALLBACK_WINDOW.BIN
/// through the script launch queue; the port's frontend queues it here, and the VM runs it as it runs
/// CALLBACK_SETTING.BIN: at the next instruction boundary outside a statement block, or from a text wait's input
/// service.
/// </summary>
public sealed partial class VirtualMachine
{
    private const string WindowCallbackScript = "CALLBACK_WINDOW.BIN";

    private bool _windowCallbackPending;      // CALLBACK_WINDOW.BIN queued by a screen-mode switch

    /// <summary>A screen-mode switch happened: queue CALLBACK_WINDOW.BIN (script_launch_by_name in
    /// display_toggle_screen_mode_and_menu_bar). Safe from the frontend thread.</summary>
    public void RequestWindowCallback() => QueueMenuRequest(new MenuRequest(MenuToggle.ScreenModeSwitched));

    /// <summary>Op 0x2cf (op_0x2cf_handler@0x425370): operand 0 asks for fullscreen and 1 for a window; any other
    /// value does nothing.</summary>
    private int StepScreenMode(IReadOnlyList<Operand> a, int pc)
    {
        long mode = Read(a[0]);
        if (mode is 0 or 1) _host.RequestScreenMode(windowed: mode == 1);
        return pc + 1;
    }

    /// <summary>The next queued callback script, in the order native's launch queue would hold them.</summary>
    private string? TakePendingCallbackScript()
    {
        if (_settingCallbackPending)
        {
            _settingCallbackPending = false;
            return SettingCallbackScript;
        }
        if (_windowCallbackPending)
        {
            _windowCallbackPending = false;
            return WindowCallbackScript;
        }
        return null;
    }
}
