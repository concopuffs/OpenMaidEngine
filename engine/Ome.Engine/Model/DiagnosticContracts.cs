namespace Ome.Engine.Model;

/// <summary>A synchronous AGE-owned diagnostic prompt after native body/context formatting.</summary>
public readonly record struct DiagnosticMessage(string Caption, string Text);

/// <summary>The answer to an interpreter error box (age_show_message_box mode 7, MessageBoxA style 0x112). AGE's
/// packer thunk maps IDRETRY to 2 and IDIGNORE to 4 for the main loop's policy; every other button exits.</summary>
public enum ScriptErrorChoice
{
    /// <summary>中止: the game closes (a forced WM_CLOSE, which saves the shared profile).</summary>
    Abort,
    /// <summary>再試行: the loop restarts with the pc unchanged; nothing is re-executed.</summary>
    Retry,
    /// <summary>無視: the frame's pc advances by its last decoded instruction's length, then the loop restarts.</summary>
    Ignore,
}
