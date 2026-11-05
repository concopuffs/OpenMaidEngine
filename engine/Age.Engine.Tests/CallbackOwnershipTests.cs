using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

/// <summary>
/// Native keeps the op-0xcc mouse callback, the op-0x199 coroutine yield and the op-0xd3..0xd5 timed sequence in
/// engine-wide state with the script that registered each one. Every dispatch first compares that owner with the
/// running frame's script and otherwise stops with "Depth が不正です &lt;running&gt; != &lt;owner&gt;" (ops 0xcd, 0x7c
/// and timed_callback_sequence_tick; docs/engine-re.md). Each test arms the callback in OUTER and dispatches it from
/// INNER, a script OUTER calls.
/// </summary>
public class CallbackOwnershipTests
{
    private const int T_IMM = 0, T_GINT = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(T_IMM, value);
    private static Operand G(int address) => new(T_GINT, address);

    private static string DepthErrorPrefix(int address, int opcode)
        => "Depth が不正です INNER != OUTER\n\nデバック情報：\n"
           + $"FILE=INNER ADDRESS={address:X} LINE=-1 COMMAND=-({opcode}) DEPTH=1\n";

    private static VirtualMachine Build(Script outer, Script inner, RecordingHost host)
        => new(outer, Table, host, provider: new MapProvider(new() { [5] = inner }));

    private static Script MouseCallbackOuter()
        => ScriptAssembler.Assemble(Table, "OUTER", new List<(int, Operand[])>
        {
            (0xcc, new[] { I(0), I(9) }),               // 0: interval 0, target 9
            (0x3, new[] { I(5) }),                      // 5: call INNER
            (0x2, Array.Empty<Operand>()),              // 8
            (0x55, new[] { G(0x120), I(1) }),           // 9: the callback
            (0x5, Array.Empty<Operand>()),              // 14
        }, Array.Empty<string>());

    private static Script MouseCallbackInner()
        => ScriptAssembler.Assemble(Table, "INNER", new List<(int, Operand[])>
        {
            (0xcd, Array.Empty<Operand>()),                     // 0
            (0x50, new[] { G(0x121), G(0x121), I(1) }),       // 1
            (0x5, Array.Empty<Operand>()),                      // 8
        }, Array.Empty<string>());

    [Fact]
    public void MouseCallbackRegisteredByTheCallerIsADepthErrorAtTheCalleesDispatch()
    {
        var host = new RecordingHost();
        host.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Retry); // runs op 0xcd again; interval 0, so it is due again
        var vm = Build(MouseCallbackOuter(), MouseCallbackInner(), host);

        vm.Run();

        Assert.Equal("script-error: Depth が不正です INNER != OUTER", vm.HaltReason);
        Assert.Equal(2, host.Diagnostics.Count);
        Assert.All(host.Diagnostics, shown => Assert.StartsWith(DepthErrorPrefix(0, 0xcd), shown.Text));
        Assert.Equal(vm.ScriptError, host.Diagnostics[^1]);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x120)); // the caller's callback never ran in the callee
    }

    [Fact]
    public void MouseCallbackDepthErrorIgnoredResumesPastTheDispatchWithItsReturnPcStillPushed()
    {
        // Op 0xcd pushes the return pc before its owner check. After 無視 the dispatch's successor runs, and its ret
        // pops that stale return pc, which runs the successor again before the frame returns.
        var host = new RecordingHost();
        host.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Ignore);
        var vm = Build(MouseCallbackOuter(), MouseCallbackInner(), host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Single(host.Diagnostics);
        Assert.Null(vm.ScriptError);
        Assert.Equal(2, vm.Globals.GetValueOrDefault(0x121));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x120));
    }

    [Fact]
    public void CoroutineResumeSavedByTheCallerIsADepthErrorThatRetryRaisesAgain()
    {
        // OUTER's op 0x199 saves the resume and enters handler A, which calls INNER; INNER's op 0x7c cannot take it.
        var outer = ScriptAssembler.Assemble(Table, "OUTER", new List<(int, Operand[])>
        {
            (0x7b, new[] { I(7), I(10) }),              // 0: handler A 7, handler B 10
            (0x199, Array.Empty<Operand>()),            // 5
            (0x2, Array.Empty<Operand>()),              // 6
            (0x3, new[] { I(5) }),                      // 7: handler A calls INNER
            (0x2, Array.Empty<Operand>()),              // 10
        }, Array.Empty<string>());
        var inner = ScriptAssembler.Assemble(Table, "INNER", new List<(int, Operand[])>
        {
            (0x7c, Array.Empty<Operand>()),             // 0
            (0x55, new[] { G(0x130), I(1) }),           // 1
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        host.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Retry); // nothing changed before the throw: raised again
        var vm = Build(outer, inner, host);

        vm.Run();

        Assert.Equal("script-error: Depth が不正です INNER != OUTER", vm.HaltReason);
        Assert.Equal(2, host.Diagnostics.Count);
        Assert.All(host.Diagnostics, shown => Assert.StartsWith(DepthErrorPrefix(0, 0x7c), shown.Text));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x130));
        Assert.Equal(new[] { true }, host.AdvPagePresentationSuspended); // the failed resume never unsuspends
    }

    [Fact]
    public void TimedSequenceStartedByTheCallerIsADepthErrorThatRepeatsUntilAbort()
    {
        // OUTER's sequence dispatches entry 0, whose callback calls INNER. INNER's op 0xd5 finds the engine-wide
        // sequence mid-way and would dispatch OUTER's entry 1. Op 0xd5 records length 0, so 再試行 and 無視 both run
        // it again and raise the error again.
        var outer = ScriptAssembler.Assemble(Table, "OUTER", new List<(int, Operand[])>
        {
            (0xd3, Array.Empty<Operand>()),                     // 0
            (0xd4, new[] { I(0), I(3), I(14), I(14) }),       // 1: two events + look-ahead sentinel
            (0xd5, new[] { I(-1) }),                            // 10
            (0x2, Array.Empty<Operand>()),                      // 13
            (0x3, new[] { I(5) }),                              // 14: the callback calls INNER
            (0x5, Array.Empty<Operand>()),                      // 17
        }, Array.Empty<string>());
        var inner = ScriptAssembler.Assemble(Table, "INNER", new List<(int, Operand[])>
        {
            (0xd5, new[] { I(-1) }),                            // 0
            (0x55, new[] { G(0x140), I(1) }),                   // 3
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        host.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Retry);
        host.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Ignore);
        var vm = Build(outer, inner, host);

        vm.Run();

        Assert.Equal("script-error: Depth が不正です INNER != OUTER", vm.HaltReason);
        Assert.Equal(3, host.Diagnostics.Count);
        Assert.All(host.Diagnostics, shown => Assert.StartsWith(DepthErrorPrefix(0, 0xd5), shown.Text));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x140));
    }

    [Fact]
    public void MouseCallbackRegisteredByANestedScriptReplacesTheCallersRegistration()
    {
        // One engine-wide registration: INNER registers its own callback and returns, so OUTER's later dispatch
        // finds INNER's registration and stops with the reverse Depth error.
        var outer = ScriptAssembler.Assemble(Table, "OUTER", new List<(int, Operand[])>
        {
            (0xcc, new[] { I(0), I(10) }),              // 0
            (0x3, new[] { I(5) }),                      // 5: INNER registers its own callback
            (0xcd, Array.Empty<Operand>()),             // 8
            (0x2, Array.Empty<Operand>()),              // 9
            (0x55, new[] { G(0x150), I(1) }),           // 10: OUTER's callback
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var inner = ScriptAssembler.Assemble(Table, "INNER", new List<(int, Operand[])>
        {
            (0xcc, new[] { I(0), I(6) }),               // 0
            (0x5, Array.Empty<Operand>()),              // 5
            (0x55, new[] { G(0x151), I(1) }),           // 6: INNER's callback
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = Build(outer, inner, host);

        vm.Run();

        Assert.Equal("script-error: Depth が不正です OUTER != INNER", vm.HaltReason);
        DiagnosticMessage shown = Assert.Single(host.Diagnostics);
        Assert.StartsWith("Depth が不正です OUTER != INNER\n\nデバック情報：\n"
                          + "FILE=OUTER ADDRESS=8 LINE=-1 COMMAND=-(205) DEPTH=0\n", shown.Text);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x150));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x151));
    }
}
