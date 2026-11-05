namespace Age.Engine.Vm;

/// <summary>A direct-scene run that the natural root hosts, as the F4 developer launcher does
/// (<see cref="VirtualMachine.HostScene"/>).</summary>
/// <remarks>The root (SYSTEM4) starts as after a return to title, so it skips LOGO and OP
/// (<c>get-initial-root-run</c> answers 0, as after native op 0x9). When it calls the coordinator (TITLE), that frame
/// returns before its first instruction with <paramref name="CoordinatorWrites"/>, which stand in for TITLE's
/// developer-menu writes and name the scene. The root then hosts the scene with its own ADV setup, and the run ends
/// when that scene returns to its host.</remarks>
/// <param name="CoordinatorScript">The coordinator's script name, such as <c>TITLE.BIN</c>.</param>
/// <param name="ScenePackedId">The hosted scene's packed script id, which the writes store for the root's
/// <c>call-script</c>.</param>
/// <param name="CoordinatorWrites">The global writes the coordinator's return applies.</param>
public sealed record HostedSceneEntry(
    string CoordinatorScript,
    long ScenePackedId,
    IReadOnlyDictionary<int, long> CoordinatorWrites);
