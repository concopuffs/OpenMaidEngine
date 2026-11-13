using Ome.Engine.Model;

namespace Ome.Engine.Vm;

/// <summary>
/// AGE's screen mode, as far as the interpreter sees it (docs/engine-re.md, "Screen mode"). The shell-services native
/// owns the mode and decides every switch (Ome.Engine/Natives/ShellServices.ScreenMode.cs); the front end shows it. A
/// switch queues CALLBACK_WINDOW.BIN at the native, which the VM runs as it runs CALLBACK_SETTING.BIN: at the next
/// instruction boundary outside a statement block, or from a text wait's input service.
/// </summary>
public sealed partial class VirtualMachine
{
    /// <summary>Op 0x2cf (op_0x2cf_handler@0x425370): operand 0 asks for fullscreen and 1 for a window; any other
    /// value does nothing. The front end switches at its next frame.</summary>
    private StepResult OpRequestScreenMode(Instruction ins, int pc)
    {
        long mode = Read(ins.Args[0]);
        if (mode is 0 or 1) _shell.RequestScreenMode(windowed: mode == 1);
        return StepResult.Next;
    }
}
