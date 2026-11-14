using Ome.Engine.Model;

namespace Ome.Engine.Natives;

/// <summary>
/// Native set:CancelMesSkipOnClick (SYS4INI CANCELMESSKIPONCLICK). adv_interpreter_tick@0x410fb0 runs only while
/// ADV fast-forward (run-state 0x08000000) is active, and with a nonzero setting it runs a press/release state
/// machine (ctx+0x6da80) on logical action 4 (bit 0x10) before dispatching the next op:
/// <list type="bullet">
/// <item>bit set: the bit is removed from the tick's mask, and state 1 becomes 2;</item>
/// <item>bit clear in state 2: state 0, input_flush_action_triggers, run-state 0x08000000 and both all-message Skip
/// fields (ctx+0x13dc, ctx+0x550fc) cleared, message:ReadTextSkip set to 0; with value 2 CALLBACK_SETTING.BIN is
/// queued through script_launch_queue_enqueue@0x40f820 and the tick returns without dispatching;</item>
/// <item>bit clear otherwise: state 1.</item>
/// </list>
/// A nonzero setting also withholds the tick's hotspot hover/click dispatch during fast-forward, so the front end routes
/// an action-4 click to <see cref="TryLatchCancelClick"/> instead of hotspots or page advance. This native runs the
/// state machine and clears its own state; the interpreter flushes the input, clears the setting and queues the
/// callback (spec §5.4).
/// </summary>
public sealed partial class SkipAndAuto
{
    private const int AdvanceActionMask = 1 << InputBindings.AdvanceAction;


    /// <summary>True while a completed action-4 click would cancel fast-forward instead of reaching hotspots.</summary>
    public bool ClickCancelArmed => Settings.CancelMessageSkipOnClick != 0 && _fastForward;

    /// <summary>Latch an action-4 click trigger for the state machine (routed on the engine thread). False when the setting is
    /// off or fast-forward is inactive, in which case the click keeps its ordinary routing.</summary>
    public bool TryLatchCancelClick()
    {
        if (!ClickCancelArmed) return false;
        Interlocked.Increment(ref _scene.ClickCancelTriggers);
        return true;
    }

    /// <summary>One adv_interpreter_tick pass of the state machine; nothing with the setting off.</summary>
    /// <returns>True when a release in state 2 cancels Skip: the caller flushes the input triggers, clears
    /// message:ReadTextSkip and calls <see cref="CancelMessageSkipFromClick"/>.</returns>
    public bool ServiceClickCancel(InputBindings input)
    {
        if (Settings.CancelMessageSkipOnClick == 0) return false;
        if (!_fastForward)
        {
            Interlocked.Exchange(ref _scene.ClickCancelTriggers, 0);
            return false;
        }
        bool clicked = Interlocked.Exchange(ref _scene.ClickCancelTriggers, 0) != 0;
        bool held = (input.PollActionMask() & AdvanceActionMask) != 0;
        if (held || clicked)
        {
            if (_scene.ClickCancelState == 1) _scene.ClickCancelState = 2;
            return false;
        }
        if (_scene.ClickCancelState == 2) return true;
        _scene.ClickCancelState = 1;
        return false;
    }

    /// <summary>The cancel's own part: state 0, both Skip fields, read skip and run-state 0x08000000 cleared. A held
    /// action 6 starts fast-forward again at the next reveal.</summary>
    public void CancelMessageSkipFromClick()
    {
        _scene.ClickCancelState = 0;
        _scene.MessageSkipEnabled = false;
        _scene.MessageSkipInjected = false;
        _scene.ReadSkipActive = false;
        SetScriptFastForward(false);
        EndPhysicalFastForward();
    }
}
