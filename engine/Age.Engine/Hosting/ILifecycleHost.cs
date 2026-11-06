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

    // A frontend return-to-title request (VirtualMachine.RequestRootReload): end every blocking wait now so
    // the VM reaches an opcode boundary. Waits stay interrupted until the reset calls ResetSceneContext.
    void InterruptForRootReload() { }

    // Op 0x2cf asks for a screen mode: display_toggle_screen_mode_and_menu_bar@0x412df0's target, true for a window
    // and false for fullscreen. The frontend owns the window, so it switches on its own thread. A switch that
    // happens then queues CALLBACK_WINDOW.BIN through VirtualMachine.RequestWindowCallback, as the native switch
    // launches it.
    void RequestScreenMode(bool windowed) { }
}
