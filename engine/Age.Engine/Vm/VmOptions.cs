namespace Age.Engine.Vm;

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
/// <param name="CreateObject">Native set:CreateObject profile setting. Together with
/// <paramref name="AutoFreeTextures"/>, controls the optional all-surface release before numbered load.</param>
/// <param name="AutoFreeTextures">Native set:AutoFreeTex profile setting. Himegari defaults this off,
/// so initialized system textures survive a numbered load unless an explicit reload record replaces them.</param>
/// <param name="NoSaveDat">Native set:NoSaveDat profile setting. This suppresses the clean-shutdown
/// shared SAVE.DAT/RT.DAT flush only; successful numbered saves still flush both shared files.</param>
public sealed record VmOptions(int EmitCap = 2, long MaxSteps = 2_000_000, int CallDepthCap = 64,
                               bool HaltAtWaitForInput = false, bool IgnoreExitRequests = false,
                               int NativeStringCodePage = 932, bool CreateObject = true,
                               bool AutoFreeTextures = false, bool NoSaveDat = false);
