using Ome.Engine.Model;

namespace Ome.Engine.Vm;

// Indices a script hands a handler (docs/engine-re.md, "Indices outside their tables"). Every handler that indexes one
// of native's tables with an operand checks it here first, with native's own test for that handler.
public sealed partial class VirtualMachine
{
    /// <summary>What native does with an index its own test refuses.</summary>
    private enum Refusal
    {
        /// <summary>It does not test the index.</summary>
        Unchecked,
        /// <summary>It skips the op without a word.</summary>
        Silent,
        /// <summary>It writes a message to the host trace and skips the op: here a warning.</summary>
        Trace,
    }

    /// <summary>
    /// Native's test of one index operand. <see cref="Admits"/> is one past the largest index the test lets through,
    /// and <see cref="PassesNegative"/> says whether a negative index passes it (a signed <c>i &lt; n</c> test). An
    /// index the test lets through outside the table (one past the table's end below <see cref="Admits"/>, a negative
    /// one past a signed test, any one when there is no test) reads or writes outside native's table.
    /// <see cref="Message"/> is native's trace.
    /// </summary>
    private readonly record struct IndexTest(Refusal Refusal, long Admits, bool PassesNegative, string? Message)
    {
        /// <summary>No test: native reads or writes at any index.</summary>
        public static IndexTest Unchecked { get; } = new(Refusal.Unchecked, long.MaxValue, true, null);

        /// <summary>Indices from 0 below <paramref name="admits"/> pass (and negative ones, for a signed test); the
        /// rest are skipped without a word.</summary>
        public static IndexTest Silent(long admits, bool signed = false) => new(Refusal.Silent, admits, signed, null);

        /// <summary>As <see cref="Silent"/>, but native traces <paramref name="message"/>.</summary>
        public static IndexTest Traced(string message, long admits, bool signed = false)
            => new(Refusal.Trace, admits, signed, message);

        public bool Passes(long index) => index < 0 ? PassesNegative : index < Admits;
    }

    /// <summary>
    /// Null when <paramref name="index"/> lies in the table, 0 to <paramref name="length"/> - 1. Otherwise the step the
    /// handler returns: an index native's <paramref name="test"/> refuses skips the op, silently or with a warning that
    /// carries native's trace, as native does; an index the test lets through would read or write outside native's
    /// table, so the run halts, naming the handler and the index. <paramref name="what"/> names the operand.
    /// </summary>
    private StepResult? IndexRefused(Instruction ins, string what, long index, int length, IndexTest test)
    {
        if (index >= 0 && index < length) return null;
        string range = $"0..{length - 1}";
        if (test.Refusal == Refusal.Unchecked || test.Passes(index))
        {
            HaltReason ??= $"index-out-of-range: {At(ins)}: {what} {index} is outside {range}, where AGE reads or "
                           + "writes outside its table";
            return StepResult.Halt;
        }
        if (test.Refusal == Refusal.Trace) ReportSkipped(ins, what, index, range, test.Message!);
        return StepResult.Next;
    }

    /// <summary>The warning for a value native's test refuses with a trace, skipping the op.</summary>
    private void ReportSkipped(Instruction ins, string what, long value, string range, string trace)
        => _waits.ReportWarning($"{At(ins)}: {what} {value} is outside {range}; skipped, as AGE skips it with the "
                                + $"trace 「{trace}」");

    /// <summary>An instruction as a halt reason or a warning names it: its script, offset, handler and opcode.
    /// </summary>
    private string At(Instruction ins)
        => $"{_cur.Script.Name}@0x{ins.Offset:x} {OpcodeName(ins.Opcode)} (op 0x{ins.Opcode:x})";

    /// <summary>A command's argument error as native words it, <c>SetKeyTotalの引数が不正です．</c>, for a trace or
    /// a box.</summary>
    private static AgeBoxMessage BadArgument(string command) => AgeBoxMessage.Keyed(AgeBoxText.BadArgument, command);

    /// <summary>Native's trace of a command's argument error.</summary>
    private static string BadArgumentTrace(string command) => BadArgument(command).Text(AgeBoxText.JapaneseText);

    /// <summary>
    /// A script error native throws with flags 0x10005, raised while the instruction at <paramref name="pc"/> runs:
    /// the はい/いいえ box (age_show_message_box mode 5). はい goes on to the next instruction, as native's main loop
    /// does with its answer 4 (driven 2026-11-16); いいえ ends the run. The box has no 再試行, so the instruction never
    /// runs again and the step is the next one's.
    /// </summary>
    private StepResult YesNoScriptError(AgeBoxMessage error, int pc)
    {
        if (ShowScriptError(error, pc, AgeBoxButtons.YesNo) == ScriptErrorChoice.Abort) throw new ScriptErrorException();
        return StepResult.Next;
    }

    /// <summary>
    /// Native's surfaces, 0 to 999 (the retained graphics records and the surface pointers beside them). The surface
    /// ops index both unchecked: ops 0x20b and 0x32 mark the destination's record dirty before they look for its
    /// surface (docs/engine-re.md, "Indices outside their tables").
    /// </summary>
    private StepResult? SurfaceSlotRefused(Instruction ins, long slot)
        => IndexRefused(ins, "surface slot", slot, GfxState.SurfaceRecordCount, IndexTest.Unchecked);

    /// <summary>
    /// Native's surfaces, 0 to 999, as the movie ops (`0x20f`, `0x236`, `0x241`, `0x23a`, `0x23f`, `0x246`) and the
    /// thumbnail surface (`0x1ae` operand 3, `0x1af` operand 3) index them: no test in either image, so the port halts
    /// where native would read or write another context field as a pointer (`engine-re.md`, "The 28 ops' native
    /// tests").
    /// </summary>
    private StepResult? MovieSurfaceSlotRefused(Instruction ins, long slot)
        => IndexRefused(ins, "surface slot", slot, GfxState.SurfaceRecordCount, IndexTest.Unchecked);

    /// <summary>
    /// Native's transition surfaces, 0 to 999, as `0x21`, `0x22`, `0x24`, `0x25` (both), `0x27` and `0x28` reach them
    /// through `screen_transition_begin` → `gfx_surface_get_size` in the shipped draw mode: tested below 1000
    /// unsigned and traced 「関数：GetTextureSize エラー：不正なtexture %d」, the transition continuing with size
    /// 0×0 (`engine-re.md`, "The 28 ops' native tests").
    /// </summary>
    private StepResult? TransitionSurfaceSlotRefused(Instruction ins, long slot)
        => IndexRefused(ins, "surface slot", slot, GfxState.SurfaceRecordCount,
                       IndexTest.Traced("関数：GetTextureSize エラー：不正なtexture ", 1000));

    /// <summary>
    /// Native's draw-string surfaces (`0x204`, `0x205`), the bound draw surface (`0x1fb` operand 2) and the indicator
    /// surfaces (`0x73`/`0x2bc` operand 4): no test at use — `draw_string_to_surface` and the composite-time
    /// `gfx_object_blit_d3d9` index `[gfx+0xa590+slot*4]` unchecked, gated only on the entry being non-null — so the
    /// port halts (`engine-re.md`, "The 28 ops' native tests").
    /// </summary>
    private StepResult? DrawSurfaceSlotRefused(Instruction ins, long slot)
        => IndexRefused(ins, "surface slot", slot, GfxState.SurfaceRecordCount, IndexTest.Unchecked);

    private string OpcodeName(int opcode)
        => _t.TryGetDefinition(opcode, out OpcodeDefinition definition) ? definition.CanonicalLabel : "?";
}
