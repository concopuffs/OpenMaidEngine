namespace Age.Engine.Hosting;

public interface ILifecycleHost
{
    // Script context is retained for diagnostics/page location; resource operands are universal packed ids.
    void EnterScriptContext(string scriptName) { }
    void ExitScriptContext() { }

    void Sleep(long duration);

    // Native op-0xd5 pacing sleeps inside run-state 0x40 without publishing retained gfx state.
    // Interactive hosts must keep this distinct from presentation-capable script op-0xc8 sleep.
    void WaitForTimedCallbackDeadline(long duration) => Sleep(duration);

    void FrameYield();

    // Native op 0x9 resets scene-owned host services before reloading root script resource 0.
    // Global banks, engine configuration, decoded-asset caches, and persistent profile state survive.
    void ResetSceneContext() { }
}
