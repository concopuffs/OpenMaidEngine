namespace Ome.Engine.Model;

/// <summary>A synchronous AGE-owned diagnostic prompt after native body/context formatting. <see cref="Caption"/>
/// and <see cref="Text"/> are AGE's Japanese; <see cref="Parts"/>, when the VM built the box, let a host present it in
/// another language (<see cref="AgeBoxText.Compose"/>).</summary>
public readonly record struct DiagnosticMessage(string Caption, string Text, AgeBoxParts? Parts = null);

/// <summary>What the main loop does after an interpreter error box: age_show_message_box returns 1, 2 or 4 and the
/// policy at 0x412689 acts on it. Mode 7 (style 0x112) maps IDRETRY to 2 and IDIGNORE to 4, mode 5 (style 0x14) IDYES
/// to 4; every other button exits (<see cref="AgeBoxText.ButtonChoices"/>).</summary>
public enum ScriptErrorChoice
{
    /// <summary>中止: the game closes (a forced WM_CLOSE, which saves the shared profile).</summary>
    Abort,
    /// <summary>再試行: the loop restarts with the pc unchanged; nothing is re-executed.</summary>
    Retry,
    /// <summary>無視: the frame's pc advances by its last decoded instruction's length, then the loop restarts.</summary>
    Ignore,
}
