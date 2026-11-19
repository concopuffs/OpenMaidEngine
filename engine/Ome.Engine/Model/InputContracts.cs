namespace Ome.Engine.Model;

/// <summary>What a text wait's Auto timer reads (<c>AdvAutoAdvanceTimer</c>).</summary>
/// <param name="PostVoiceDelayMs">The delay after a pending voice ends.</param>
/// <param name="UnvoicedDelayMs">The delay on a page with no pending voice.</param>
/// <param name="VoicedTimerMs">AGE 4.38D with message:AutoMessageOption bit 0: a page with a pending voice is due this
/// long after the wait began, whether or not the voice still plays. Null waits for the voice and then
/// <paramref name="PostVoiceDelayMs"/>.</param>
public readonly record struct AdvAutoWaitState(
    bool Enabled, bool VoicePending, long PostVoiceDelayMs, long UnvoicedDelayMs, long? VoicedTimerMs = null);

/// <summary>AGERc command 10's synchronous full-width text edit request and result.</summary>
public readonly record struct FullwidthTextEditRequest(string CurrentText, string InitialText);
public readonly record struct FullwidthTextEditResult(bool Accepted, string Text);
