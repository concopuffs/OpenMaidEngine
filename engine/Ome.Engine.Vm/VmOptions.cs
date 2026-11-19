namespace Ome.Engine.Vm;

/// <param name="EmitCap">Headless runaway-line guard: the VM halts (<c>LOOP:line@…</c>) when one frame shows the same
/// authored inline string more than this many times. Native AGE has no such limit; the interactive session passes
/// <see cref="int.MaxValue"/> (GodotVmOptions).</param>
/// <param name="HaltAtWaitForInput">When true, the VM halts (reason "wait-for-input") at op 0x72, and at op 0xfa
/// when it would wait, instead of calling the waits and continuing. This is the FAITHFUL headless semantics: with no player to click,
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
/// <param name="WheelKeyUpAction">Native set:WheelKeyUp (SYS4INI WHEELKEYUP, registered default 3): the logical action
/// a wheel-up raises while an ADV text wait polls (<see cref="VirtualMachine.ApplyMouseWheel"/>).</param>
/// <param name="WheelKeyDownAction">Native set:WheelKeyDown (SYS4INI WHEELKEYDOWN, registered default 1).</param>
/// <param name="GameVersion">Native set:GameVersion, the game's version text: SYS4INI GAMEVERSION, or with VERREGPOS
/// the installer's version from the registry (<c>Ome.Engine.Frontend.Shell.GameVersionRule</c>, which the front end
/// applies). AGE 4.38D's op 0x2eb reads it; 創刻のアテリアル's title shows it.</param>
/// <remarks>CancelMesSkipOnClick, CoexistMesSkip and the fast-forward pace are the skip and auto native's
/// (<c>Ome.Engine.Natives.SkipAndAutoSettings</c>), and NoSaveDat is the save and settings native's
/// (<c>Ome.Engine.Natives.SavesAndSettings</c>).</remarks>
public sealed record VmOptions(int EmitCap = 2, long MaxSteps = 2_000_000, int CallDepthCap = 64,
                               bool HaltAtWaitForInput = false, bool IgnoreExitRequests = false,
                               int NativeStringCodePage = 932, int CreateObject = 2,
                               int AutoFreeTex = 0,
                               int WheelKeyUpAction = 3, int WheelKeyDownAction = 1,
                               string GameVersion = "")
{
    /// <summary>The selected profile's id, which labels an unsupported-opcode diagnostic
    /// (<see cref="UnsupportedOpcodeDiagnostic.ProfileId"/>). No behaviour reads it.</summary>
    public string ProfileId { get; init; } = "unselected";

    /// <summary>Probe mode (<c>--probe</c>): an unsupported opcode is reported and skipped instead of halting the
    /// run.</summary>
    public bool ProbeMode { get; init; }
}
