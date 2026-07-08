namespace Age.Engine.Vm;

/// <param name="HaltAtWaitForInput">When true, the VM halts (reason "wait-for-input") at op 0x72 instead
/// of calling the host and continuing. This is the FAITHFUL headless semantics: with no player to click,
/// "the scene is waiting for input" means stop here — not pretend the click already happened and plow on
/// through every prompt into code no real playthrough reaches (which is what made a headless SC0000 spin
/// 493k× in the name-entry poll loop). Leave false for interactive frontends that really block on input
/// (Godot) and for the dialogue-coverage sweep that deliberately walks every page.</param>
public sealed record VmOptions(int EmitCap = 2, long MaxSteps = 2_000_000, int CallDepthCap = 64,
                               bool HaltAtWaitForInput = false);
