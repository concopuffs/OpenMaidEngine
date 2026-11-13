namespace Ome.Engine.Hosting;

public interface ILifecycleHost
{
    // The VM enters and leaves a script's frame: the presentation barrier counts frames, and --timeline-log records them.
    void EnterScriptContext(string scriptName) { }
    void ExitScriptContext(string scriptName) { }

    void Sleep(long duration);

    // Native op-0xd5 pacing sleeps inside run-state 0x40 without publishing retained gfx state.
    // Interactive hosts must keep this distinct from presentation-capable script op-0xc8 sleep.
    void WaitForTimedCallbackDeadline(long duration) => Sleep(duration);

    void FrameYield();

    // Native op 0x9 resets scene-owned host services before reloading root script resource 0. The movie native has
    // stopped the movies, and the surfaces stay; the VM releases them afterwards when set:AutoFreeTex asks.
    // Global banks, engine configuration, decoded-asset caches, and persistent profile state survive.
    void ResetSceneContext() { }

    // A frontend return-to-title request (VirtualMachine.RequestRootReload): end every blocking wait now so
    // the VM reaches an opcode boundary. Waits stay interrupted until the reset calls ResetSceneContext.
    void InterruptForRootReload() { }
}
