namespace Age.Engine.Vm;

/// <summary>
/// Fast-forward from logical action 6 (Ctrl, C and Backspace in both games): native run-state 0x08000000 when a player
/// input sets it, as opposed to persistent Skip (op 0x88) or read-skip, which the script channel models. Native sets the
/// bit from action 6 in two places only (docs/engine-re.md, "Physical fast-forward"):
/// <list type="bullet">
/// <item>adv_input_service_poll@0x411230, a polling text wait: the triggered mask (input_poll_triggered_action_mask,
/// a non-repeat key-down) holds action 6. A key held since before the wait does not count;</item>
/// <item>adv_text_reveal_service_tick@0x406fa0, a reveal tick: the held mask has action 6 and ctx+0x6da74 is clear. Op
/// 0x101 sets ctx+0x6da74, and only a reveal tick without action 6 held clears it, so after a control-strip callback
/// (or TITLE's and FIELD's op 0x101) a held key starts nothing until it is released during a reveal.</item>
/// </list>
/// Nothing else starts it. Releasing action 6 ends it (adv_interpreter_tick, op 0x72 and op 0xfa test the held mask),
/// and so do op 0x19b, op 0x19c (which restores only persistent Skip and read-skip), op 0x101, op 0x88(0), the
/// click cancel and scene_context_init_reset. A key held through one of those does not restart fast-forward at the
/// next text wait: Kamidori with Ctrl held stops at SC2000@0xaf5, after FIELD's op 0x101 and SC2000's op 0x19b.
/// </summary>
public sealed partial class VirtualMachine
{
    private const int FastForwardActionMask = 1 << 6;

    private readonly object _physicalFastForwardLock = new();
    private bool _physicalSkipActionHeld;           // action 6 as the last input change left it
    private bool _revealPhysicalSkipSuppressed;     // ctx+0x6da74
    private volatile bool _advTextRevealing;        // run-state 0x20000000: a reveal is running in the host

    private bool PhysicalSkipActionHeld => (InputBindings.PollActionMask() & FastForwardActionMask) != 0;

    /// <summary>A physical input changed. A key-down of action 6 at a polling text wait, or action 6 held during a
    /// reveal that is not suppressed, starts fast-forward; a release ends it.</summary>
    private void OnPhysicalSkipInputChanged()
    {
        lock (_physicalFastForwardLock)
        {
            bool held = PhysicalSkipActionHeld;
            bool pressed = held && !_physicalSkipActionHeld;
            _physicalSkipActionHeld = held;
            if (!held)
            {
                if (_advTextRevealing) _revealPhysicalSkipSuppressed = false;   // the next reveal tick is a normal one
                _host.SetPhysicalMessageSkipActive(false);
            }
            else if ((pressed && _advTextWaitPolling) || (_advTextRevealing && !_revealPhysicalSkipSuppressed))
            {
                _host.SetPhysicalMessageSkipActive(true);
            }
        }
    }

    /// <summary>The first tick of a reveal: action 6 held starts fast-forward unless op 0x101 suppressed it; otherwise
    /// the tick is a normal one and lifts the suppression.</summary>
    private void ServicePhysicalSkipAtRevealStart()
    {
        lock (_physicalFastForwardLock)
        {
            if (!PhysicalSkipActionHeld) _revealPhysicalSkipSuppressed = false;
            else if (!_revealPhysicalSkipSuppressed) _host.SetPhysicalMessageSkipActive(true);
        }
    }

    /// <summary>adv_interpreter_tick, op 0x72 and op 0xfa: fast-forward from action 6 lasts only while it is held.</summary>
    private void ReconcilePhysicalFastForward()
    {
        lock (_physicalFastForwardLock)
            if (!PhysicalSkipActionHeld) _host.SetPhysicalMessageSkipActive(false);
    }

    /// <summary>An op or service that clears run-state 0x08000000 whatever the input holds.</summary>
    private void EndPhysicalFastForward()
    {
        lock (_physicalFastForwardLock) _host.SetPhysicalMessageSkipActive(false);
    }

    /// <summary>A text wait took a latched action-6 trigger (op 0x7c resumed it): native sets run-state 0x08000000 and
    /// the page passes. With action 6 already up, the next interpreter tick clears the bit again, so only the page
    /// passes.</summary>
    private void StartPhysicalFastForwardFromLatchedTrigger()
    {
        lock (_physicalFastForwardLock)
        {
            if (PhysicalSkipActionHeld) _host.SetPhysicalMessageSkipActive(true);
            else _host.EndInputWait();
        }
    }
}
