using System;
using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// Each handler that indexes one of native's tables with an operand, driven past the table's end (docs/engine-re.md,
/// "Indices outside their tables"). It halts where native does not test the index, skips where native's test refuses
/// it (with native's trace as a warning where native writes one), and raises the box where native throws. A handler
/// whose native test has not been read raises the はい/いいえ box (docs/backlog.md).
/// </summary>
public sealed class IndexBoundsTests
{
    /// <summary>What the instruction does with the value.</summary>
    public enum Past
    {
        /// <summary>The run halts, naming the handler and the index.</summary>
        Halts,
        /// <summary>The instruction is skipped with a warning that carries native's trace.</summary>
        Traces,
        /// <summary>The run goes on without a word.</summary>
        Continues,
        /// <summary>The はい/いいえ box: はい skips the instruction, いいえ ends the run.</summary>
        AsksYesNo,
        /// <summary>The 中止/再試行/無視 box.</summary>
        AsksAbortRetryIgnore,
    }

    private const int Marker = 0x200;
    private static readonly Lazy<OpcodeTable> Himegari = new(() => OpcodeTableJson.Load(Paths.OpcodesJson));
    private static readonly Lazy<OpcodeTable> Kamidori = new(() => OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433"));

    private static Operand I(long value) => new(0, value);
    private static Operand G(int address) => new(3, address);

    private const string SurfaceRange = "が範囲 0..999 の外です．";
    private const string LayoutRange = "が範囲 0..9 の外です．";

    /// <summary>(opcode, argument number, value, what happens, a text the halt reason, warning or box carries).
    /// </summary>
    public static TheoryData<int, int, long, Past, string> Cases()
    {
        var cases = new TheoryData<int, int, long, Past, string>();
        // ADV text layouts, 0 to 9: no test in native.
        foreach (int op in new[] { 0x6e, 0x6f, 0x70, 0x71, 0x73, 0x79, 0x7a, 0x82, 0x196, 0x198, 0x1c1, 0x1d1, 0x20a,
                                   0x212, 0x213 })
            cases.Add(op, 1, 10, Past.Halts, "layout slot 10 is outside 0..9");
        // Surfaces, 0 to 999: no test in native.
        foreach ((int op, int argument) in new[] { (0x1f8, 1), (0x1f9, 2), (0x1fa, 1), (0x207, 1), (0x207, 2),
                                                   (0x20b, 1), (0x223, 2), (0x249, 2), (0x24d, 2), (0x24f, 2),
                                                   (0x258, 1), (0x32, 1), (0x32, 2) })
            cases.Add(op, argument, 1000, Past.Halts, "surface slot 1000 is outside 0..999");
        cases.Add(0x216, 2, 2000, Past.Halts, "surface record 2000 is outside 0..1999");
        // Numeric glyph styles, 0 to 9: native's test admits 0 to 10 and traces the rest.
        foreach ((int op, int argument) in new[] { (0x13a, 1), (0x2da, 1), (0x23b, 2) })
        {
            cases.Add(op, argument, 10, Past.Halts, "glyph style 10 is outside 0..9");
            cases.Add(op, argument, 11, Past.Traces, "CGNumberの引数が不正です．");
        }
        cases.Add(0x23b, 2, 0, Past.Traces, "glyph style 0 is not registered");
        // Queues and stacks, 0 to 9: native's test admits 0 to 10 and traces the rest.
        foreach ((int op, string command) in new[] { (0x132, "ResetQ"), (0x133, "AddQ"), (0x134, "GetQ"),
                                                     (0x137, "ResetStack"), (0x138, "Push"), (0x139, "Pop") })
        {
            cases.Add(op, 1, 10, Past.Halts, "id 10 is outside 0..9");
            cases.Add(op, 1, 11, Past.Traces, $"{command}の引数が不正です．");
        }
        // Bits, 0 to 31, traced.
        cases.Add(0x135, 2, 32, Past.Traces, "SetBitの引数が不正です．");
        cases.Add(0x136, 2, 32, Past.Traces, "RemBitの引数が不正です．");
        cases.Add(0x13f, 3, 32, Past.Traces, "GetBitの引数が不正です．");
        cases.Add(0x13f, 3, -1, Past.Traces, "GetBitの引数が不正です．");
        cases.Add(0x64, 2, 100_000, Past.Halts, "body offset 100000 is outside");
        cases.Add(0x60, 2, 0, Past.AsksYesNo, "RANDOMの最大値が0になっています．");
        // Sound channels: the sound manager's 13, of which the sound effects' 10.
        cases.Add(0xb4, 2, 10, Past.Halts, "sound channel 10 is outside 0..9");
        cases.Add(0xb4, 2, 13, Past.AsksAbortRetryIgnore, "WAVファイル 0 の読み込みに失敗しました");
        cases.Add(0xb5, 1, 13, Past.Traces, "関数：dsPlay エラー：不正なSound番号[13]です");
        cases.Add(0xba, 1, 13, Past.Traces, "関数：dsPlay エラー：不正なSound番号[13]です");
        cases.Add(0xb6, 1, 10, Past.Halts, "sound channel 10 is outside 0..9");
        cases.Add(0x2bf, 1, 10, Past.Traces, "関数：SetDelay エラー：不正なSound番号です");
        cases.Add(0x2bf, 1, -1, Past.Halts, "sound channel -1 is outside 0..9");
        cases.Add(0x1be, 2, 13, Past.Traces, "関数：dsIsPlaying エラー：不正なSound番号です");
        cases.Add(0xc5, 1, 5, Past.Traces, "GetVolumeの引数が不正です．");
        cases.Add(0xc6, 1, 5, Past.Traces, "SetVolumeの引数が不正です．");
        cases.Add(0xc7, 1, 5, Past.Traces, "GetSoundModeの引数が不正です．");
        cases.Add(0x1ba, 1, 5, Past.Traces, "SetSoundModeの引数が不正です．");
        // Input actions, 0 to 31.
        cases.Add(0xfb, 1, 32, Past.AsksYesNo, "SetKeyJumpの引数が不正です．");
        cases.Add(0xfe, 1, 32, Past.AsksYesNo, "SetKeyTotalの引数が不正です．");
        cases.Add(0x107, 1, 32, Past.Continues, "");
        cases.Add(0x10b, 1, 32, Past.Continues, "");
        cases.Add(0x10b, 2, 32, Past.Halts, "physical button 32 is outside 0..31");
        cases.Add(0x10c, 1, 32, Past.AsksYesNo, "SetKeyMultiの引数が不正です．");
        cases.Add(0x10c, 2, 256, Past.Halts, "scan code 256 is outside 0..255");
        // Script frames, 0 to 39.
        cases.Add(0x6, 2, 40, Past.Halts, "frame slot 40 is outside 0..39");
        cases.Add(0x8, 1, 40, Past.Halts, "frame slot 40 is outside 0..39");
        cases.Add(0x8, 1, 5, Past.AsksYesNo, "この階層にはファイルが読み込まれていません．Depth=5");
        cases.Add(0x1d3, 4, -1, Past.Halts, "history record -1 is negative");
        cases.Add(0x1d4, 4, -1, Past.Halts, "history record -1 is negative");
        // Handlers whose native test has not been read.
        foreach (int op in new[] { 0x72, 0x80, 0x2bc })
            cases.Add(op, 1, 10, Past.AsksYesNo, "の第1引数 10 " + LayoutRange);
        foreach ((int op, int argument) in new[] { (0x21, 1), (0x22, 1), (0x24, 1), (0x25, 1), (0x25, 2), (0x27, 1),
                                                   (0x28, 1), (0x73, 4), (0x2bc, 4), (0x1fb, 2), (0x204, 1),
                                                   (0x205, 1), (0x208, 1), (0x20f, 2), (0x236, 2), (0x241, 2),
                                                   (0x23a, 2), (0x23f, 2), (0x246, 1), (0x1ae, 3), (0x1af, 3) })
        {
            cases.Add(op, argument, 1000, Past.AsksYesNo, $"の第{argument}引数 1000 " + SurfaceRange);
            if (op != 0x208) cases.Add(op, argument, -1, Past.AsksYesNo, $"の第{argument}引数 -1 " + SurfaceRange);
        }
        cases.Add(0x208, 1, -2, Past.AsksYesNo, "の第1引数 -2 " + SurfaceRange);
        foreach ((int op, int argument) in new[] { (0x19e, 2), (0x19f, 2), (0x1a0, 2), (0x1a1, 2), (0x1ab, 2),
                                                   (0x1ac, 2), (0x1ac, 3), (0x1ae, 2), (0x1af, 2) })
            cases.Add(op, argument, -1, Past.AsksYesNo, $"の第{argument}引数 -1 が負の値です．");
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AHandlerPastItsTableDoesWhatNativeDoes(int op, int argument, long value, Past past, string text)
    {
        (VirtualMachine vm, RecordingWaits host) = Run(op, argument, value);

        switch (past)
        {
            case Past.Halts:
                Assert.StartsWith("index-out-of-range: PAST@0x0 ", vm.HaltReason);
                Assert.Contains($"(op 0x{op:x}): ", vm.HaltReason);
                Assert.Contains(text, vm.HaltReason);
                Assert.Equal(0, vm.Globals.GetValueOrDefault(Marker));
                break;
            case Past.Traces:
                Assert.Equal("exit", vm.HaltReason);
                Assert.Contains(text, Assert.Single(host.Warnings));
                Assert.Equal(1, vm.Globals.GetValueOrDefault(Marker));
                break;
            case Past.Continues:
                Assert.Equal("exit", vm.HaltReason);
                Assert.Empty(host.Warnings);
                Assert.Empty(host.Diagnostics);
                Assert.Equal(1, vm.Globals.GetValueOrDefault(Marker));
                break;
            case Past.AsksYesNo:
            case Past.AsksAbortRetryIgnore:
                // No answer queued: 中止 or いいえ ends the run.
                Assert.StartsWith("script-error: ", vm.HaltReason);
                Assert.Contains(text, vm.HaltReason);
                DiagnosticMessage box = Assert.Single(host.Diagnostics);
                Assert.Equal(past == Past.AsksYesNo ? AgeBoxButtons.YesNo : AgeBoxButtons.AbortRetryIgnore,
                             box.Parts!.Buttons);
                Assert.Equal(0, vm.Globals.GetValueOrDefault(Marker));

                // はい or 無視 skips the instruction.
                (VirtualMachine skipped, _) = Run(op, argument, value, ScriptErrorChoice.Ignore);
                Assert.Equal("exit", skipped.HaltReason);
                Assert.Null(skipped.ScriptError);
                Assert.Equal(1, skipped.Globals.GetValueOrDefault(Marker));
                break;
        }
    }

    /// <summary>One instruction with <paramref name="value"/> at <paramref name="argument"/> and a cell elsewhere,
    /// then a marker and the exit. An op Himegari's table does not have runs on 4.33G's.</summary>
    private static (VirtualMachine, RecordingWaits) Run(int op, int argument, long value,
                                                       ScriptErrorChoice? answer = null)
    {
        bool himegari = Himegari.Value.Argc(op) >= 0;
        OpcodeTable table = himegari ? Himegari.Value : Kamidori.Value;
        int argc = table.Argc(op);
        Assert.InRange(argument, 1, argc);
        Operand[] operands = Enumerable.Range(0, argc).Select(i => i == argument - 1 ? I(value) : G(0x300 + i))
                                       .ToArray();
        var script = ScriptAssembler.Assemble(table, "PAST", new List<(int, Operand[])>
        {
            (op, operands),
            (0x55, new[] { G(Marker), I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingWaits();
        if (answer is { } choice) host.ScriptErrorAnswers.Enqueue(choice);
        var vm = himegari
            ? new VirtualMachine(script, table, host)
            : new VirtualMachine(script, table, host, build: EngineBuilds.Age433G);
        vm.Run();
        return (vm, host);
    }

    [Fact]
    public void FourThreeThreeGFindsNothingForANegativeHistoryRecord()
    {
        foreach (int op in new[] { 0x1d3, 0x1d4 })
        {
            OpcodeTable table = Kamidori.Value;
            Operand[] operands = Enumerable.Range(0, table.Argc(op)).Select(i => i == 3 ? I(-1) : G(0x300 + i))
                                           .ToArray();
            var script = ScriptAssembler.Assemble(table, "PAST", new List<(int, Operand[])>
            {
                (op, operands),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
            var vm = new VirtualMachine(script, table, new RecordingWaits(), build: EngineBuilds.Age433G);

            vm.Run();

            Assert.Equal("exit", vm.HaltReason);
        }
    }

    [Theory]
    [InlineData(0x216, 2, -1)]    // op 0x215's "no such object", which Kamidori's ALLMAP and SAVE pass on
    [InlineData(0x20d, 1, 1000)]  // the backbuffer, as is any slot outside 0..999
    [InlineData(0x20d, 1, -1)]    // which both games' retail scripts pass
    [InlineData(0x208, 1, -1)]    // op 0x215's "no such object", which HIDEWIN.BIN passes on untested
    public void AValueTheOpGivesAMeaningIsNotPastItsTable(int op, int argument, long value)
    {
        (VirtualMachine vm, RecordingWaits host) = Run(op, argument, value);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Empty(host.Diagnostics);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Marker));
        if (op == 0x20d) Assert.Equal(-1, vm.Gfx.CurrentRenderTargetSlot);
    }

    /// <summary>input_hotspot_poll_bound_action_callback tests the action's low five bits, so action 39 answers
    /// action 7; a negative action never fires.</summary>
    [Theory]
    [InlineData(39, 7, true)]
    [InlineData(-1, 31, false)]
    public void ABoundActionPastThirtyOneIsItsLowFiveBits(int action, int pressed, bool fires)
    {
        var table = Himegari.Value;
        const int activateTarget = 31;   // 0x90 (15) + 0x97 (11) + 0x94 (1) + 0x72 (3) + exit (1)
        var script = ScriptAssembler.Assemble(table, "ACTION", new List<(int, Operand[])>
        {
            (0x90, new[] { I(10), I(20), I(20), I(20), I(-1), I(-1), I(activateTarget) }),
            (0x97, new[] { I(10), I(20), I(20), I(20), I(action) }),
            (0x94, Array.Empty<Operand>()),
            (0x72, new[] { I(1) }),
            (0x2, Array.Empty<Operand>()),
            (0x55, new[] { G(Marker), I(1) }),
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new PressHost(pressed);
        var vm = new VirtualMachine(script, table, host);
        host.Vm = vm;

        vm.Run();

        Assert.Equal(fires, host.Consumed);
        Assert.Equal(fires ? 1 : 0, vm.Globals.GetValueOrDefault(Marker));
    }

    private sealed class PressHost(int bit) : RecordingWaits
    {
        public VirtualMachine Vm = null!;
        public bool Consumed;

        public override void WaitForInput(Func<bool> serviceInputCallback)
        {
            Consumed = Vm.TryActivateInputActions(1 << bit);
            while (serviceInputCallback()) { }
        }
    }
}
