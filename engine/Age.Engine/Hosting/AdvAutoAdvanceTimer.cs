using Age.Engine.Model;

namespace Age.Engine.Hosting;

/// <summary>
/// Native ADV Auto timing policy. Unvoiced waits use AutoMessageTime1; voiced waits park until voice
/// completion and then use AutoMessageTime0. The host supplies a monotonic clock and live voice state.
/// </summary>
public sealed class AdvAutoAdvanceTimer
{
    private bool _armed;
    private bool _waitingForVoice;
    private long _deadlineMs;

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
            _waitingForVoice = state.VoicePending && voiceActive;
            if (!_waitingForVoice)
                _deadlineMs = nowMs + EffectiveDelay(
                    state.VoicePending ? state.PostVoiceDelayMs : state.UnvoicedDelayMs);
        }

        if (_waitingForVoice)
        {
            if (voiceActive) return false;
            _waitingForVoice = false;
            _deadlineMs = nowMs + EffectiveDelay(state.PostVoiceDelayMs);
        }

        return nowMs >= _deadlineMs;
    }

    // Native callers substitute 100 ms when either configuration getter returns zero.
    private static long EffectiveDelay(long configuredMs)
        => configuredMs == 0 ? 100 : System.Math.Max(1, configuredMs);
}
