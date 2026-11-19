using System;
using System.Collections.Generic;
using System.IO;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// AGE 4.38D's per-character Auto delay (docs/engine-re.md, "Kamidori against Arterial, triaged"): the settings
/// message:AutoMessagePitch0/1 and message:AutoMessageOption, ops 0x2e6 to 0x2e8 and 0x2ea that read and write them,
/// and the delay op_0x72_wait_for_input@0x41de50 arms.
/// </summary>
public class AutoMessagePitchTests
{
    private const int Imm = 0, GlobalInt = 3;
    private const int Exit = 0x2, GetPitch = 0x2e6, SetPitch = 0x2e7, SetOption = 0x2e8, CountOffset = 0x2e9,
                      GetOption = 0x2ea, SetAutoTime = 0x1b9;

    private static Operand I(long value) => new(Imm, value);
    private static Operand G(int address) => new(GlobalInt, address);

    private static (VirtualMachine Vm, RecordingWaits Waits) Run(params (int, Operand[])[] instructions)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4438");
        var ops = new List<(int, Operand[])>(instructions) { (Exit, Array.Empty<Operand>()) };
        var waits = new RecordingWaits();
        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "AUTO.BIN", ops, []), table, waits, new VmOptions());
        vm.Run();
        return (vm, waits);
    }

    [Fact]
    public void TheOpcodesReadAndWriteThePitchesAndTheOption()
    {
        var (vm, waits) = Run(
            (GetPitch, [I(0), G(0x300)]),               // registered default 0
            (SetPitch, [I(0), I(30)]),
            (SetPitch, [I(1), I(45)]),
            (GetPitch, [I(0), G(0x301)]),
            (GetPitch, [I(1), G(0x302)]),
            (GetOption, [G(0x303)]),
            (SetOption, [I(3)]),
            (GetOption, [G(0x304)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Empty(waits.Warnings);
        Assert.Equal((0L, 30L, 45L, 0L, 3L),
            (vm.Globals.GetValueOrDefault(0x300), vm.Globals[0x301], vm.Globals[0x302],
             vm.Globals.GetValueOrDefault(0x303), vm.Globals[0x304]));
    }

    [Fact]
    public void ASelectorThatIsNeitherZeroNorOneReadsAndWritesNothing()
    {
        var (vm, waits) = Run(
            (SetPitch, [I(1), I(45)]),
            (SetPitch, [I(2), I(99)]),
            (GetPitch, [I(2), G(0x300)]),
            (GetPitch, [I(1), G(0x301)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.False(vm.Globals.ContainsKey(0x300));
        Assert.Equal(45L, vm.Globals[0x301]);
        Assert.Equal(2, waits.Warnings.Count);
        Assert.All(waits.Warnings, warning => Assert.Contains("GetAutoMesPiの引数が不正です．", warning));
    }

    [Fact]
    public void WithNoPitchTheDelaysAreTheOlderBuilds()
    {
        var (vm, _) = Run((SetAutoTime, [I(0), I(750)]), (SetAutoTime, [I(1), I(2250)]), (CountOffset, [I(4)]));

        // 4.24J and 4.33G have no pitch: the page's length changes nothing.
        Assert.Equal(new AdvAutoWaitState(false, false, 750, 2250), vm.SkipAndAuto.AutoWaitStateFor(0));
        Assert.Equal(new AdvAutoWaitState(false, false, 750, 2250), vm.SkipAndAuto.AutoWaitStateFor(60));
    }

    [Fact]
    public void TheUnvoicedDelayAddsThePitchForEachCountedCharacter()
    {
        var (vm, _) = Run(
            (SetAutoTime, [I(1), I(2000)]), (SetPitch, [I(1), I(50)]), (SetPitch, [I(0), I(70)]), (CountOffset, [I(4)]));

        // AutoMessageTime1 + AutoMessagePitch1 × (characters − op 0x2e9's count); a voiced page still waits for its voice.
        AdvAutoWaitState state = vm.SkipAndAuto.AutoWaitStateFor(24);
        Assert.Equal(2000 + 50 * (24 - 4), state.UnvoicedDelayMs);
        Assert.Equal(500, state.PostVoiceDelayMs);
        Assert.Null(state.VoicedTimerMs);

        vm.SkipAndAuto.ResetScene();        // the scene reset zeroes the count, and keeps the settings
        Assert.Equal(2000 + 50 * 24, vm.SkipAndAuto.AutoWaitStateFor(24).UnvoicedDelayMs);
    }

    [Fact]
    public void OptionBitZeroGivesAVoicedPageATimerOfItsOwn()
    {
        var (vm, _) = Run(
            (SetAutoTime, [I(0), I(400)]), (SetPitch, [I(0), I(70)]), (SetOption, [I(1)]), (CountOffset, [I(2)]));

        Assert.Equal(400 + 70 * (12 - 2), vm.SkipAndAuto.AutoWaitStateFor(12).VoicedTimerMs);

        var (even, _) = Run((SetPitch, [I(0), I(70)]), (SetOption, [I(2)]));
        Assert.Null(even.SkipAndAuto.AutoWaitStateFor(12).VoicedTimerMs);   // only bit 0 is read
    }

    [Fact]
    public void TheDelayIsComputedInThirtyTwoBits()
    {
        var (vm, _) = Run((SetAutoTime, [I(1), I(2000)]), (SetPitch, [I(1), I(int.MaxValue)]));

        Assert.Equal(unchecked(2000 + int.MaxValue * 3), vm.SkipAndAuto.AutoWaitStateFor(3).UnvoicedDelayMs);
    }

    [Fact]
    public void AVoicedTimerRunsFromTheWaitsStartWhetherOrNotTheVoicePlays()
    {
        var timer = new AdvAutoAdvanceTimer();
        var state = new AdvAutoWaitState(true, true, 500, 2000, VoicedTimerMs: 1200);

        Assert.False(timer.Poll(state, voiceActive: true, 1000));
        Assert.False(timer.Poll(state, voiceActive: false, 1600));   // the voice's end arms nothing
        Assert.False(timer.Poll(state, voiceActive: false, 2199));
        Assert.True(timer.Poll(state, voiceActive: false, 2200));

        var playing = new AdvAutoAdvanceTimer();
        Assert.False(playing.Poll(state, voiceActive: true, 1000));
        Assert.True(playing.Poll(state, voiceActive: true, 2200));    // due while the voice still plays
    }

    [Fact]
    public void AVoicedTimerOfZeroIsAHundredMillisecondsAndAnUnvoicedPageIgnoresIt()
    {
        var zero = new AdvAutoAdvanceTimer();
        var voiced = new AdvAutoWaitState(true, true, 500, 2000, VoicedTimerMs: 0);
        Assert.False(zero.Poll(voiced, true, 1000));
        Assert.False(zero.Poll(voiced, true, 1099));
        Assert.True(zero.Poll(voiced, true, 1100));

        var unvoiced = new AdvAutoAdvanceTimer();
        var state = new AdvAutoWaitState(true, false, 500, 2000, VoicedTimerMs: 100);
        Assert.False(unvoiced.Poll(state, false, 1000));
        Assert.False(unvoiced.Poll(state, false, 2999));
        Assert.True(unvoiced.Poll(state, false, 3000));
    }

    [Fact]
    public void ThePagesCharactersAreTheGlyphsOfTheLayoutTheWaitIsOn()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand S(int index) => new(2, index);
        Script script = ScriptAssembler.Assemble(table, "AUTO_COUNT",
            new List<(int, Operand[])>
            {
                (0x70, [I(1), I(100), I(50), I(10), I(20)]),
                (0x79, [I(1), I(2), I(3)]),
                (0x71, [I(1)]),
                (0x213, [I(1), I(100), I(10)]),
                (0x6e, [I(0), S(0)]),
                (0x6e, [I(0), S(1)]),
                (Exit, Array.Empty<Operand>()),
            },
            ["A", "BC"]);
        VirtualMachine vm = TestText.Vm(script, table, new RecordingWaits(), TestText.Glyphs(TestText.Fixed(4, 4)));

        vm.Run();

        // Native counts the layout's records less its pending one: one per glyph drawn.
        Assert.Equal(3, vm.Text.WaitLayoutCharacterCount);       // no page wait yet: the current layout
        vm.Text.BeginPageWait(1);
        Assert.Equal(3, vm.Text.WaitLayoutCharacterCount);
        vm.Text.BeginPageWait(2);                                // a layout nothing was drawn into
        Assert.Equal(0, vm.Text.WaitLayoutCharacterCount);
    }

    [Fact]
    public void TheSettingsFileKeepsThePitchesOnlyWhereTheBuildBindsThem()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ome-auto-pitch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // 4.38D binds the three keys: they are read at startup and written at shutdown.
            string path = Path.Combine(directory, "438", Sys4RegIniStore.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "[message]\r\nAutoMessagePitch0=11\r\nAutoMessagePitch1=22\r\nAutoMessageOption=1\r\n");
            var store = new Sys4RegIniStore(path, 0, settingsRules: EngineBuilds.Age438D.Rules.Settings);
            var auto = SkipAndAuto.WithSystemClock();
            auto.ReadSettings(store);
            Assert.Equal((11, 22, 1), (auto.AutoMessagePitch(0), auto.AutoMessagePitch(1), auto.AutoMessageOption));

            auto.SetAutoMessagePitch(1, 33);
            auto.WriteSettings(store);
            string written = File.ReadAllText(path);
            Assert.Contains("AutoMessagePitch1=33", written);
            Assert.Contains("AutoMessageOption=1", written);

            // 4.33G binds none of them: a value in the file is not a setting, and none is written.
            string older = Path.Combine(directory, "433", Sys4RegIniStore.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(older)!);
            File.WriteAllText(older, "[message]\r\nAutoMessagePitch1=22\r\n");
            var olderStore = new Sys4RegIniStore(older, 0, settingsRules: EngineBuilds.Age433G.Rules.Settings);
            var olderAuto = SkipAndAuto.WithSystemClock();
            olderAuto.ReadSettings(olderStore);
            Assert.Equal(0, olderAuto.AutoMessagePitch(1));
            olderAuto.WriteSettings(olderStore);
            Assert.DoesNotContain("AutoMessagePitch0", File.ReadAllText(older));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
