namespace Ome.Engine.Vm;

/// <summary>
/// Where the interpreter goes after one instruction (docs/superpowers/specs/2026-11-12-engine-layering-design.md §4):
/// on to the next instruction, to a jump target, or out of the step loop. A handler returns one.
/// </summary>
internal readonly struct StepResult
{
    private readonly int _target;

    private StepResult(StepKind kind, int target)
    {
        Kind = kind;
        _target = target;
    }

    public StepKind Kind { get; }

    /// <summary>The instruction after this one.</summary>
    public static StepResult Next => new(StepKind.Next, 0);

    /// <summary>The run ends; <see cref="VirtualMachine.HaltReason"/> says why.</summary>
    public static StepResult Halt => new(StepKind.Halt, 0);

    /// <summary>The frame returns: <c>ret</c> with an empty call stack, or an exit.</summary>
    public static StepResult FrameReturn => new(StepKind.FrameReturn, 0);

    /// <summary>A wait's callback returns to the wait that ran it.</summary>
    public static StepResult HotspotReturn => new(StepKind.HotspotReturn, 0);

    /// <summary>The root script reloads (タイトルに戻る).</summary>
    public static StepResult RootReload => new(StepKind.RootReload, 0);

    /// <summary>The instruction at index <paramref name="instruction"/> of the running script. An index outside the
    /// script ends the frame, as running off its end does.</summary>
    public static StepResult Jump(int instruction) => new(StepKind.Jump, instruction);

    /// <summary>Whether execution goes on in the same frame (<see cref="StepKind.Next"/> or
    /// <see cref="StepKind.Jump"/>).</summary>
    public bool Continues => Kind is StepKind.Next or StepKind.Jump;

    /// <summary>The instruction index to run after the one at <paramref name="pc"/>, when <see cref="Continues"/>.</summary>
    public int NextPc(int pc) => Kind switch
    {
        StepKind.Next => pc + 1,
        StepKind.Jump => _target,
        _ => throw new InvalidOperationException($"a {Kind} step has no next instruction"),
    };

    public override string ToString() => Kind == StepKind.Jump ? $"Jump({_target})" : Kind.ToString();
}

/// <summary>The kinds of <see cref="StepResult"/>.</summary>
internal enum StepKind : byte
{
    Next,
    Jump,
    Halt,
    FrameReturn,
    HotspotReturn,
    RootReload,
}
