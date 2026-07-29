using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class DiagnosticOutputOpcodeTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand S(int index) => new(2, index);
    private static Operand G(int address) => new(3, address);
    private static (int, Operand[]) Exit() => (0x2, []);

    [Fact]
    public void TrioPreservesNativeAppendShowClearAndPostClearOrdering()
    {
        Script scene = ScriptAssembler.Assemble(Table, "SYSTEM4.BIN",
        [
            (0x1b2, [S(0)]),
            (0x1b2, [G(0x100)]),
            (0x1b3, []),
            (0x1b4, []),
            (0x1b3, []),
            (0x1b2, [S(1)]),
            (0x1b4, []),
            Exit(),
        ], ["invalid mode=", "tail"]);
        var host = new RecordingHost();
        VirtualMachine? vm = null;
        var pendingAtPresentation = new List<string>();
        host.OnDiagnosticMessage = _ => pendingAtPresentation.Add(vm!.PendingDiagnosticText);
        vm = new VirtualMachine(scene, Table, host);
        vm.Globals[0x100] = -3;

        vm.Run();

        Assert.Equal(
        [
            "invalid mode=-3\r\n",
            "\r\ntail",
        ], pendingAtPresentation);
        Assert.Equal(2, host.Diagnostics.Count);
        Assert.All(host.Diagnostics, message => Assert.Equal("エラーが発生しました", message.Caption));
        Assert.Equal(
            "invalid mode=-3\r\n" +
            "\n\nデバック情報：\n" +
            "FILE=SYSTEM4.BIN ADDRESS=7 LINE=-1 COMMAND=-(436) DEPTH=0\n",
            host.Diagnostics[0].Text);
        Assert.Equal(
            "\r\ntail" +
            "\n\nデバック情報：\n" +
            "FILE=SYSTEM4.BIN ADDRESS=C LINE=-1 COMMAND=-(436) DEPTH=0\n",
            host.Diagnostics[1].Text);
        Assert.Equal("", vm.PendingDiagnosticText);
    }

    [Fact]
    public void GenericFormatterCoversIntegerFloatStringAndPointerOperandFamilies()
    {
        Script scene = ScriptAssembler.Assemble(Table, "FORMAT_DIAGNOSTIC.BIN",
        [
            (0x1b2, [I(unchecked((long)(uint)int.MinValue))]),
            (0x1b2, [S(0)]),
            (0x1b2, [new Operand(4, 0x10)]),
            (0x1b2, [S(1)]),
            (0x1b2, [new Operand(6, 0x20)]),
            (0x1b2, [S(2)]),
            (0x1b2, [new Operand(8, 0x21)]),
            (0x1b4, []),
            Exit(),
        ], [":", "/", "/"]);
        var host = new CaptureHost();
        var vm = new VirtualMachine(scene, Table, host);
        vm.GlobalFloats[0x10] = BitConverter.SingleToInt32Bits(1.25f);
        vm.Globals[0x20] = 0x30;
        vm.Globals[0x30] = -9;
        vm.Globals[0x21] = 0x31;
        vm.GlobalStrings[0x31] = "pointer text";

        vm.Run();

        DiagnosticMessage diagnostic = Assert.Single(host.Diagnostics);
        Assert.StartsWith("-2147483648:1.250000/-9/pointer text", diagnostic.Text);
    }

    [Fact]
    public void GameSessionCarriesEngineCtxAccumulatorAcrossFreshSceneVms()
    {
        var session = new GameSession();
        Script field = ScriptAssembler.Assemble(Table, "FIELD.BIN",
        [
            (0x1b2, [S(0)]),
            (0x1b3, []),
            Exit(),
        ], ["field diagnostic"]);
        Script system = ScriptAssembler.Assemble(Table, "SYSTEM4.BIN",
        [
            (0x1b2, [S(0)]),
            (0x1b4, []),
            Exit(),
        ], ["system diagnostic"]);
        var host = new CaptureHost();

        session.RunScene(field, Table, new CaptureHost());
        Assert.Equal("field diagnostic\r\n", session.DiagnosticOutput.PendingText);
        session.RunScene(system, Table, host);

        Assert.StartsWith("field diagnostic\r\nsystem diagnostic", Assert.Single(host.Diagnostics).Text);
        Assert.Equal("", session.DiagnosticOutput.PendingText);
    }
}
