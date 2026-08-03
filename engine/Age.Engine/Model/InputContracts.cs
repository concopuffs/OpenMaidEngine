namespace Age.Engine.Model;

public readonly record struct AdvAutoWaitState(
    bool Enabled, bool VoicePending, long PostVoiceDelayMs, long UnvoicedDelayMs);

/// <summary>AGERc command 10's synchronous full-width text edit request and result.</summary>
public readonly record struct FullwidthTextEditRequest(string CurrentText, string InitialText);
public readonly record struct FullwidthTextEditResult(bool Accepted, string Text);
