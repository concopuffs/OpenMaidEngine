namespace Age.Engine.Vm;

/// <summary>
/// Native AGE's input latch (docs/engine-re.md, "Effect skipping and the input latch"). A mapped click
/// (input_latch_mouse_click@0x485e20, ctx+0x13e0) or key stays latched until a service polls it through
/// input_poll_triggered_action_mask@0x460880, which takes every latched trigger at once. The port hands a trigger to
/// its consumer as it arrives wherever a native service would poll it at once (a text wait, a reveal, a movie, an
/// effect under system:EffectSkipOnClick). This latch keeps the triggers that arrive while nothing polls, which are:
/// <list type="bullet">
/// <item>an effect service that does not poll. That is any effect unless EffectSkipOnClick is 1, and a run-state-0x400
/// effect while service-flags bit 0 is set. The frontend holds them with <see cref="HoldTriggers"/>;</item>
/// <item>an ADV coroutine yield (handlers A and B and the hidden-window wait), through
/// <see cref="TryLatchAdvCoroutineYieldTrigger"/>.</item>
/// </list>
/// The next native poll takes the latch:
/// <list type="bullet">
/// <item>the hidden-window wait: any trigger ends it;</item>
/// <item>a text reveal's first tick, unless ctx+0x55108 is set: action 4 finishes it;</item>
/// <item>a modal movie's first tick: action 4 cancels it;</item>
/// <item>a text wait resumed by op 0x7c: the triggers count as fresh input.</item>
/// </list>
/// These discard it:
/// <list type="bullet">
/// <item>op 0x72's entry, op 0xfa, and a hotspot callback's return to its wait (the return re-runs op 0x72), unless
/// fast-forward is on;</item>
/// <item>always: op 0x101, input_flush_action_triggers@0x405380 (legacy fade starts, op 0xd5, hotspot dispatch, the
/// skip-click cancel) and a scene reset.</item>
/// </list>
/// </summary>
public sealed partial class VirtualMachine
{
    /// <summary>Logical action 4, the left click in both games: it advances a page, finishes a reveal, skips an effect
    /// (EffectSkipOnClick 1) and cancels a modal movie.</summary>
    public const int AdvanceAction = 4;

    /// <summary>Logical action 5, Space in both games: at a polling text wait it enters the frame's handler A, which
    /// hides the window (adv_input_service_poll@0x411230).</summary>
    public const int AdvWaitCoroutineYieldAction = 5;

    private const int AdvanceTrigger = 1 << AdvanceAction;

    private int _heldTriggers;   // logical action bits; bit 31 is the native menu's hide-window request

    /// <summary>The triggers the latch holds (logical action bits).</summary>
    public int HeldTriggers => Volatile.Read(ref _heldTriggers);

    /// <summary>Hold triggered logical actions (bit = 1 &lt;&lt; action) in the latch while nothing polls it. The
    /// frontend calls this for a trigger that arrives during an effect service that does not poll.</summary>
    public void HoldTriggers(int actionMask)
    {
        if (actionMask == 0) return;
        int before, after;
        do
        {
            before = Volatile.Read(ref _heldTriggers);
            after = before | actionMask;
        } while (Interlocked.CompareExchange(ref _heldTriggers, after, before) != before);
        _host.WakeInputCallbackService();
    }

    private int TakeHeldTriggers() => Interlocked.Exchange(ref _heldTriggers, 0);

    /// <summary>Op 0x72's entry and op 0xfa poll the latch and discard it unless run-state 0x8000000
    /// (fast-forward) is on.</summary>
    private void DiscardHeldTriggersUnlessFastForward()
    {
        if (!AdvFastForwardActive) TakeHeldTriggers();
    }

    /// <summary>adv_text_reveal_service_tick@0x406fa0 polls the latch on every tick, so its first tick takes what the
    /// latch holds. A held action 4 finishes the reveal and sets ctx+0x55108, so the page's later reveals publish at
    /// once, without polling, until op 0x72. Under fast-forward, or with no glyph delay, no reveal runs, so nothing
    /// polls.</summary>
    private void OfferHeldTriggersToReveal()
    {
        if (_messageGlyphDelayMilliseconds <= 0 || AdvFastForwardActive || _host.TextRevealsFinishedUntilWait) return;
        if ((TakeHeldTriggers() & AdvanceTrigger) != 0) _host.FinishTextRevealsUntilWait();
    }

    /// <summary>The modal movie service (run-state 0x2000, engine_main_tick_with_exception_policy@0x41205d) polls on
    /// its first tick: a held action 4 stops the movie. It does not read EffectSkipOnClick.</summary>
    private void OfferHeldTriggersToModalMovie()
    {
        if ((TakeHeldTriggers() & AdvanceTrigger) != 0) _host.CancelNextModalMovie();
    }

    /// <summary>adv_input_service_poll@0x411230 at a text wait that resumed without re-running op 0x72. Op 0x7c
    /// returns to the wait that Space parked, and the wait takes what handler B's effects held. The poll handles the
    /// held triggers as fresh input. An op-0x97 bound action runs its record first. Then action 5 enters handler A
    /// again. Then action 4 activates the current hit or, with none, advances the page. Activations and the yield are
    /// queued for the caller's dispatch.</summary>
    private void RouteHeldTriggersAtTextWait()
    {
        if (AdvFastForwardActive) return;   // op 0x72 leaves the wait unarmed (no 0x80000000): nothing polls
        int held = TakeHeldTriggers() & int.MaxValue;
        if (held == 0) return;
        if (TryActivateInputActions(held)) return;
        if ((held & (1 << AdvWaitCoroutineYieldAction)) != 0 && TryRequestAdvWaitCoroutineYield()) return;
        if ((held & AdvanceTrigger) == 0) return;
        bool activated;
        lock (_interactiveLock) activated = _interactiveFrame?.Hotspots.Activate() == true;
        if (!activated) _host.EndInputWait();
    }
}
