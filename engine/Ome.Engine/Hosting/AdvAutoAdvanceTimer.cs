using Ome.Engine.Model;

namespace Ome.Engine.Hosting;

/// <summary>
/// Native ADV Auto timing policy. Unvoiced waits use AutoMessageTime1; voiced waits park until voice
/// completion and then use AutoMessageTime0. The host supplies a monotonic clock and live voice state.
/// AGE 4.38D adds a per-character part to both delays, which arrives already added in the state, and an option under
/// which a voiced wait runs on a timer from its start instead (<see cref="AdvAutoWaitState.VoicedTimerMs"/>;
/// op_0x72_wait_for_input@0x41de50 arms it, and adv_input_service_poll@0x4117c0 then polls it without waiting for
/// the voice).
/// </summary>
public sealed class AdvAutoAdvanceTimer
{
    private readonly bool _unvoicedDeadlineDue;
    private bool _armed;
    private bool _waitingForVoice;
    private long _deadlineMs;

    /// <param name="unvoicedDeadlineDue">The wait arms no timer of its own (op 0xfa): an unvoiced wait is due at its
    /// first poll. A pending voice still waits for its end and AutoMessageTime0.</param>
    public AdvAutoAdvanceTimer(bool unvoicedDeadlineDue = false) => _unvoicedDeadlineDue = unvoicedDeadlineDue;

    public void Reset()
    {
        _armed = false;
        _waitingForVoice = false;
        _deadlineMs = 0;
    }

    public bool Poll(AdvAutoWaitState state, bool voiceActive, long nowMs)
    {
        if (!state.Enabled)
        {
            Reset();
            return false;
        }

        if (!_armed)
        {
            _armed = true;
            _waitingForVoice = state.VoicePending && voiceActive && state.VoicedTimerMs == null;
            if (state.VoicePending && state.VoicedTimerMs is { } voicedTimer)
                _deadlineMs = nowMs + EffectiveDelay(voicedTimer);
            else if (!_waitingForVoice)
                _deadlineMs = !state.VoicePending && _unvoicedDeadlineDue
                    ? nowMs
                    : nowMs + EffectiveDelay(state.VoicePending ? state.PostVoiceDelayMs : state.UnvoicedDelayMs);
        }

        if (_waitingForVoice)
        {
            if (voiceActive) return false;
            _waitingForVoice = false;
            _deadlineMs = nowMs + EffectiveDelay(state.PostVoiceDelayMs);
        }

        return nowMs >= _deadlineMs;
    }

    // Native callers substitute 100 ms for a delay of zero: 4.24J and 4.33G when the setting is zero, 4.38D when
    // the setting plus its per-character part is.
    private static long EffectiveDelay(long configuredMs)
        => configuredMs == 0 ? 100 : System.Math.Max(1, configuredMs);
}
