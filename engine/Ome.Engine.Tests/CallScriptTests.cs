using System.Collections.Generic;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;
using Ome.Engine.Natives;

public class CallScriptTests
{
    // Opcodes (from build/opcodes.json): exit=0x2, call-script=0x3(argc1), mov=0x55(argc2).
    // Operand types: imm=0, global-int=3, local-int=9.
    private const uint OP_EXIT = 0x2, OP_CALLSCRIPT = 0x3, OP_MOV = 0x55, OP_SETTEXTURE = 0x1f9;

    private class NullWaits : IInputAndWaits
    {
        public virtual void EnterScriptContext(string scriptName) { }
        public virtual void ExitScriptContext(string scriptName) { }
        public void ShowText(int o, string t) { }
        public void WaitForInput() { }
        public void Sleep(long duration) { }
        public void FrameYield() { }
        public void CreateTexture(int s, int w, int h) { }
        public virtual void SetTexture(long r, int s) { }
        public void DrawTexture(int s, int sx, int sy, int w, int h, int dx, int dy) { }
        public (int Width, int Height) GetTextureSize(int s) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    private sealed class MapProvider : IScriptProvider
    {
        private readonly Dictionary<long, Script> _m;
        public MapProvider(Dictionary<long, Script> m) => _m = m;
        public Script? GetById(long id) => _m.TryGetValue(id, out var s) ? s : null;
    }

    private sealed class ContextHost : NullWaits
    {
        private readonly Stack<string> _contexts = new();
        public List<string> Events { get; } = new();
        public List<(long ResourceId, int Slot)> Textures { get; } = new();

        public override void EnterScriptContext(string scriptName)
        {
            _contexts.Push(scriptName);
            Events.Add($"enter:{scriptName}");
        }

        // The VM names the script it leaves; a name other than the one entered last shows in the event.
        public override void ExitScriptContext(string scriptName)
        {
            string entered = _contexts.Pop();
            Events.Add(entered == scriptName ? $"exit:{scriptName}" : $"exit:{scriptName}!={entered}");
        }

        public override void SetTexture(long resourceId, int slot) => Textures.Add((resourceId, slot));
    }

    // Build a Script from raw dwords via the real loader (guarantees identical decode).
    private static Script Asm(OpcodeTable t, string name, params uint[] body)
    {
        var bytes = new byte[0x3C + body.Length * 4];
        System.Text.Encoding.ASCII.GetBytes("SYS4422 ").CopyTo(bytes, 0);
        // fields[8] (F8 = code end) at header offset 8 + 8*4 = 0x28; set to body length (all code).
        System.BitConverter.GetBytes(body.Length).CopyTo(bytes, 8 + 8 * 4);
        for (int i = 0; i < body.Length; i++) System.BitConverter.GetBytes(body[i]).CopyTo(bytes, 0x3C + i * 4);
        return Sys4Loader.Parse(bytes, t, name);
    }

    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);

    [Fact]
    public void CalleeRunsAndControlResumesAfterTheCall()
    {
        var t = Table();
        // Callee (id 5): mov g[0x10] = 7, then exit.
        var callee = Asm(t, "CALLEE", OP_MOV, 3, 0x10, 0, 7, OP_EXIT);
        // Caller: call-script 5 ; mov g[0x11] = g[0x10] ; exit.
        var caller = Asm(t, "CALLER",
            OP_CALLSCRIPT, 0, 5,
            OP_MOV, 3, 0x11, 3, 0x10,
            OP_EXIT);
        var host = new NullWaits();
        var sink = new RecordingTraceSink();
        var vm = new VirtualMachine(caller, t, host, null, new MapProvider(new() { [5] = callee }), sink);
        vm.Run();
        Assert.Equal(7, vm.Globals[0x10]);           // callee wrote a shared global
        Assert.Equal(7, vm.Globals[0x11]);           // caller read it AFTER the call returned
        Assert.Equal("exit", vm.HaltReason);         // top-level exit
        Assert.Contains(5L, sink.CallScriptIds);     // dispatch observed via the trace sink
    }

    [Fact]
    public void MissingProviderFallsBackToStub()
    {
        var t = Table();
        var caller = Asm(t, "CALLER", OP_CALLSCRIPT, 0, 5, OP_EXIT);
        var host = new NullWaits();
        var sink = new RecordingTraceSink();
        var vm = new VirtualMachine(caller, t, host, null, null, sink);   // no provider
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);        // did not halt on the call; stub + continue
        Assert.Contains(5L, sink.CallScriptIds);
    }

    private const string RetryCancelPrompt =
        "\n\nもう一度実行しますか？\n\n再試行：もう一度このコマンドを実行します．\n        (ただし正常に動作しない場合があります)\n"
        + "キャンセル：プログラムを終了します．\n";

    [Fact]
    public void AnIdThatOpensNothingAsksToRetryOrCancelAndCancelEndsTheRun()
    {
        // script_frame_load_resource catches the resource open's error in its mode-3 box, without the debug block;
        // キャンセル makes op 0x3 throw Command_Exit_Exception.
        var t = Table();
        var caller = Asm(t, "CALLER", OP_CALLSCRIPT, 0, 99, OP_MOV, 3, 0x10, 0, 7, OP_EXIT);
        var waits = new RecordingWaits();
        var vm = new VirtualMachine(caller, t, waits, null, new MapProvider(new()));

        vm.Run();

        DiagnosticMessage box = Assert.Single(waits.Diagnostics);
        Assert.Equal(AgeBoxButtons.RetryCancel, box.Parts!.Buttons);
        Assert.Equal("エラーが発生しました", box.Caption);
        Assert.Equal("ファイル番号 99 は不正な値です．" + RetryCancelPrompt, box.Text);
        Assert.Equal("script-error: ファイル番号 99 は不正な値です．", vm.HaltReason);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x10));
    }

    [Fact]
    public void RetryOpensTheScriptAgain()
    {
        var t = Table();
        var callee = Asm(t, "CALLEE", OP_MOV, 3, 0x10, 0, 7, OP_EXIT);
        var caller = Asm(t, "CALLER", OP_CALLSCRIPT, 0, 5, OP_EXIT);
        var waits = new RecordingWaits();
        waits.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Retry);
        waits.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Retry);
        var provider = new AppearingProvider(callee, failures: 2);
        var vm = new VirtualMachine(caller, t, waits, null, provider);

        vm.Run();

        Assert.Equal(2, waits.Diagnostics.Count);
        Assert.Equal(3, provider.Opens);
        Assert.Equal(7, vm.Globals[0x10]);
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void AnUnresolvedIdThePortCannotFollowHalts()
    {
        var t = Table();
        var caller = Asm(t, "CALLER", OP_CALLSCRIPT, 0, 99, OP_EXIT);
        var vm = new VirtualMachine(caller, t, new NullWaits(), null, new SilentProvider());
        vm.Run();
        Assert.StartsWith("callscript-unresolved", vm.HaltReason);
    }

    [Fact]
    public void AFortyFirstFrameShowsTheFatalBox()
    {
        // op_0x3_call_script tests the frame index (> 38) before anything else and throws flags 0x10001.
        var t = Table();
        var self = Asm(t, "SELF", OP_MOV, 3, 0x10, 0, 1, OP_CALLSCRIPT, 0, 5, OP_EXIT);
        var waits = new RecordingWaits();
        waits.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Retry);   // the fatal box has only OK
        var vm = new VirtualMachine(self, t, waits, new VmOptions(CallDepthCap: 64),
                                    new MapProvider(new() { [5] = self }));

        vm.Run();

        DiagnosticMessage box = Assert.Single(waits.Diagnostics);
        Assert.Equal(AgeBoxButtons.Fatal, box.Parts!.Buttons);
        Assert.Equal("続行不可能なエラーが発生しました", box.Caption);
        Assert.Equal(39, box.Parts.Location.Depth);
        Assert.StartsWith("[続行不可能なエラー]\n\nファイルの階層が深すぎます．最大は40です．\n\nデバック情報：\n", box.Text);
        Assert.EndsWith("DEPTH=39\n\n\nこれ以上プログラムを実行できません．\nプログラムを終了します．\n", box.Text);
        Assert.Equal("script-error: ファイルの階層が深すぎます．最大は40です．", vm.HaltReason);
    }

    [Fact]
    public void TheProviderSaysWhatAgesResourceOpenSaysForAnIdOfASyntheticCatalog()
    {
        // Record 0 a script, 1 a placeholder (no file), 2 not a script; no append catalog is mounted.
        var catalog = Sys4AssetCatalog.Parse(SyntheticCatalog.Build(["A.BIN", "@", "B.CG"]));
        var provider = new Sys4ScriptProvider(Table(), catalog, new Sys4AssetStore(catalog, "."));
        string Text(long id) => provider.LoadFailure(id)?.Text(AgeBoxText.JapaneseText) ?? "(none)";

        Assert.Equal("ファイル番号 3 は不正な値です．", Text(3));
        Assert.Equal("@ を開くことが出来ません．\nERROR CODE = 2", Text(1));
        Assert.Equal("(none)", Text(2));            // the loader's header check, not the open
        Assert.Equal("拡張ファイル情報ファイル 1 は読み込まれていません．", Text(0x01000000));
        Assert.Equal("(none)", Text(0x80000000));   // below native's table of catalogs
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void TheProviderSaysWhatAgesResourceOpenSaysForAnIdItFindsNothingFor()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var provider = new Sys4ScriptProvider(Table(), catalog,
                                              new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir));
        string Text(long id) => provider.LoadFailure(id)?.Text(AgeBoxText.JapaneseText) ?? "(none)";
        int count = catalog.RawSlots.Count;

        Assert.Equal($"ファイル番号 {count} は不正な値です．", Text(count));
        Assert.Equal("拡張ファイル情報ファイル 127 は読み込まれていません．", Text(0x7f000000));
        Assert.Equal("(none)", Text(0x80000000));   // below native's table of catalogs
        AssetEntry image = catalog.Files.First(e => !e.Name.EndsWith(".BIN", System.StringComparison.OrdinalIgnoreCase));
        Assert.Equal("(none)", Text(image.PackedId));   // the loader's header check, not the open
        foreach (var (selector, append) in catalog.AppendPacks)
            Assert.Equal($"拡張ファイル ERROR を開くことが出来ません．{append.RawSlots.Count}",
                         Text(((long)selector << 24) | (uint)append.RawSlots.Count));
        if (catalog.RawSlots.FirstOrDefault(e => e.IsPlaceholder) is { } placeholder)
            Assert.Equal($"{placeholder.Name} を開くことが出来ません．\nERROR CODE = 2", Text(placeholder.RawIndex));
    }

    // A provider whose script opens only after some failures, as a file put back while the loader's box waits.
    private sealed class AppearingProvider(Script script, int failures) : IScriptProvider
    {
        public int Opens { get; private set; }
        public Script? GetById(long id) => ++Opens > failures ? script : null;
    }

    // A provider that cannot say what AGE would do for an id it does not resolve.
    private sealed class SilentProvider : IScriptProvider
    {
        public Script? GetById(long id) => null;
        public AgeBoxMessage? LoadFailure(long id) => null;
    }

    [Fact]
    public void LocalsDoNotLeakBetweenCallerAndCallee()
    {
        var t = Table();
        // Callee writes LOCAL-int 0 = 42 (mov to local-int, type 9), then exit.
        var callee = Asm(t, "CALLEE", OP_MOV, 9, 0, 0, 42, OP_EXIT);
        // Caller sets local-int 0 = 1, calls, then copies its own local-int 0 to global 0x20.
        var caller = Asm(t, "CALLER",
            OP_MOV, 9, 0, 0, 1,                  // l[0] = 1
            OP_CALLSCRIPT, 0, 5,                 // call-script 5 (callee sets ITS local 0 = 42)
            OP_MOV, 3, 0x20, 9, 0,               // g[0x20] = l[0]
            OP_EXIT);
        var vm = new VirtualMachine(caller, t, new NullWaits(), null, new MapProvider(new() { [5] = callee }));
        vm.Run();
        Assert.Equal(1, vm.Globals[0x20]);   // caller's local 0 unchanged by callee's local 0
    }

    [Fact]
    public void PackedTextureIdsRemainUnchangedAcrossNestedFrames()
    {
        var t = Table();
        var callee = Asm(t, "CALLEE",
            OP_SETTEXTURE, 0, 7, 0, 2, 0, uint.MaxValue,
            OP_EXIT);
        var caller = Asm(t, "CALLER",
            OP_SETTEXTURE, 0, 7, 0, 1, 0, uint.MaxValue,
            OP_CALLSCRIPT, 0, 5,
            OP_SETTEXTURE, 0, 7, 0, 3, 0, uint.MaxValue,
            OP_EXIT);
        var host = new ContextHost();
        var vm = new VirtualMachine(caller, t, host, null, new MapProvider(new() { [5] = callee }));

        vm.Run();

        Assert.Equal(new long?[] { 7, 7, 7 }, new[] { 1, 2, 3 }.Select(vm.Graphics.SurfaceResource));
        Assert.Equal(new[] { "enter:CALLER", "enter:CALLEE", "exit:CALLEE", "exit:CALLER" }, host.Events);
    }
}
