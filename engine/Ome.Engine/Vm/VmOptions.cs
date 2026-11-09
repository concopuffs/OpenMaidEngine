namespace Ome.Engine.Vm;

/// <param name="HaltAtWaitForInput">When true, the VM halts (reason "wait-for-input") at op 0x72 instead
/// of calling the host and continuing. This is the FAITHFUL headless semantics: with no player to click,
/// "the scene is waiting for input" means stop here — not pretend the click already happened and plow on
/// through every prompt into code no real playthrough reaches (which is what made a headless SC0000 spin
/// 493k× in the name-entry poll loop). Leave false for interactive frontends that really block on input
/// (Godot) and for the dialogue-coverage sweep that deliberately walks every page.</param>
/// <param name="IgnoreExitRequests">Debug-only divergence: treat op 0x1 as a no-op so unreachable
/// post-exit bytecode can be explored. Leave false for native-faithful execution.</param>
/// <param name="NativeStringCodePage">Encoding used when an opcode measures the engine's byte-string
/// representation. SYS4 defaults to CP932; another container frontend can select its own code page.</param>
/// <param name="CreateObject">Native set:CreateObject (SYS4INI CREATEOBJECT): the renderer backends AGE creates, one bit
/// per DrawMode (<see cref="Sys4.Sys4StartupSettings.CreateObject"/>); the default, 2, is both shipped games' object
/// backend. Bit 1 arms <paramref name="AutoFreeTex"/>.</param>
/// <param name="AutoFreeTex">Native set:AutoFreeTex (SYS4INI AUTOFREETEX, registered default 0). Under CreateObject bit 1,
/// bit 1 releases all 1000 surfaces before a numbered load restores its flagged records, so only those come back; bit 0
/// releases them at op 0x9's root reload (the scene reset itself keeps every surface). Kamidori sets 3;
/// Himegari has none, so its initialized system textures survive a numbered load.</param>
/// <param name="NoSaveDat">Native set:NoSaveDat profile setting. This suppresses the clean-shutdown
/// shared SAVE.DAT/RT.DAT flush only; successful numbered saves still flush both shared files.</param>
/// <param name="CancelMessageSkipOnClick">Native set:CancelMesSkipOnClick (SYS4INI CANCELMESSKIPONCLICK).
/// Nonzero: a completed action-4 click during active fast-forward cancels Skip; 2 also runs CALLBACK_SETTING.BIN.</param>
/// <param name="FastForwardOpMicroseconds">Wall-clock cost of one op while ADV fast-forward is active (native runs one
/// op per main-loop iteration). Zero, with <paramref name="FastForwardPageMicroseconds"/> zero, disables pacing
/// (headless tools and tests); interactive frontends use <see cref="NativeFastForwardOpMicroseconds"/>.</param>
/// <param name="WheelKeyUpAction">Native set:WheelKeyUp (SYS4INI WHEELKEYUP, registered default 3): the logical action
/// a wheel-up raises while an ADV text wait polls (<see cref="VirtualMachine.ApplyMouseWheel"/>).</param>
/// <param name="WheelKeyDownAction">Native set:WheelKeyDown (SYS4INI WHEELKEYDOWN, registered default 1).</param>
/// <param name="CoexistMessageSkip">Native set:CoexistMesSkip (SYS4INI COEXISTMESSKIP, registered default 0). With
/// it off, the native menu's Skip toggle clears Auto and its Auto toggle clears Skip; the script ops ignore it.</param>
/// <param name="FastForwardPageMicroseconds">Additional fast-forward cost of each text wait (op 0x72) passed while
/// fast-forward is active; interactive frontends use <see cref="NativeFastForwardPageMicroseconds"/>.</param>
public sealed record VmOptions(int EmitCap = 2, long MaxSteps = 2_000_000, int CallDepthCap = 64,
                               bool HaltAtWaitForInput = false, bool IgnoreExitRequests = false,
                               int NativeStringCodePage = 932, int CreateObject = 2,
                               int AutoFreeTex = 0, bool NoSaveDat = false,
                               int CancelMessageSkipOnClick = 0, int FastForwardOpMicroseconds = 0,
                               int WheelKeyUpAction = 3, int WheelKeyDownAction = 1,
                               bool CoexistMessageSkip = false, int FastForwardPageMicroseconds = 0)
{
    /// <summary>Native fast-forward cost per op. Text waits timed with Frida under held Ctrl (2026-11-07) fit 15 µs
    /// per op plus <see cref="NativeFastForwardPageMicroseconds"/> per page: Kamidori's SC0000 (34,851 ops, 140 pages,
    /// 865 ms) and Himegari's opening pages (12 µs/op, 2.75 ms/page) agree (docs/engine-re.md, "Fast-forward
    /// pace").</summary>
    public const int NativeFastForwardOpMicroseconds = 15;

    /// <summary>Native fast-forward cost per text page beyond its ops (see <see cref="NativeFastForwardOpMicroseconds"/>).
    /// </summary>
    public const int NativeFastForwardPageMicroseconds = 2400;
}
