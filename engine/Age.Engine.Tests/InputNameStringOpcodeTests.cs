using System.Text;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class InputNameStringOpcodeTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand S(int index) => new(2, index);
    private static Operand G(int address) => new(3, address);
    private static Operand GS(int address) => new(5, address);
    private static Operand LI(int index) => new(9, index);
    private static Operand LS(int index) => new(11, index);
    private static Operand LSP(int index) => new(14, index);
    private static (int, Operand[]) Exit() => (0x2, []);

    static InputNameStringOpcodeTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void CharacterLengthAndSubstringUseCp932CharacterBoundaries()
    {
        Assert.Equal("cp932-character-length", Table.Label(0x2c6));
        Assert.Equal(4, Cp932Text.CharacterLength("AリｱB", Encoding.GetEncoding(932)));
        int lookup = Table.ByLabel("lookup-array")!.Value;
        int move = Table.ByLabel("mov")!.Value;
        Script script = ScriptAssembler.Assemble(Table, "INPUTNAME_HELPERS.BIN",
        [
            (move, [LS(0), GS(0x700)]),
            (0x2c6, [G(0x500), LS(0)]),
            (lookup, [LSP(0), GS(0x600), I(0)]),
            (0x2c8, [LSP(0), LS(0), I(0), I(1)]),
            (lookup, [LSP(1), GS(0x600), I(1)]),
            (0x2c8, [LSP(1), LS(0), I(1), I(1)]),
            (lookup, [LSP(2), GS(0x600), I(2)]),
            (0x2c8, [LSP(2), LS(0), I(2), I(99)]),
            Exit(),
        ], []);
        var trace = new RecordingTraceSink { TracingSteps = true };
        var vm = new VirtualMachine(script, Table, new RecordingHost(), sink: trace);
        vm.GlobalStrings[0x700] = "AリｱB";

        vm.Run();

        Assert.Equal(new[] { move, 0x2c6, lookup, 0x2c8, lookup, 0x2c8, lookup, 0x2c8, 0x2 },
            trace.Events.Where(e => e.Kind == TraceEventKind.Step).Select(e => e.Opcode));
        Assert.Equal(4, vm.Globals[0x500]);
        Assert.Equal("A", vm.GlobalStrings[0x600]);
        Assert.Equal("リ", vm.GlobalStrings[0x601]);
        Assert.Equal("ｱB", vm.GlobalStrings[0x602]);
    }

    [Fact]
    public void SubstringReproducesNativeEndClamp()
    {
        int lookup = Table.ByLabel("lookup-array")!.Value;
        int move = Table.ByLabel("mov")!.Value;
        Script script = ScriptAssembler.Assemble(Table, "INPUTNAME_CLAMP.BIN",
        [
            (move, [LS(0), S(0)]),
            (lookup, [LSP(0), GS(0x610), I(0)]),
            (0x2c8, [LSP(0), LS(0), I(0), I(0)]),
            Exit(),
        ], ["魔王"]);
        var vm = new VirtualMachine(script, Table, new RecordingHost());

        vm.Run();

        Assert.Equal("魔王", vm.GlobalStrings[0x610]);
    }

    [Fact]
    public void ModalEditorAcceptsReplacementAndPreservesInitialOperand()
    {
        int move = Table.ByLabel("mov")!.Value;
        Script script = ScriptAssembler.Assemble(Table, "INPUTNAME_ACCEPT.BIN",
        [
            (move, [LS(0), S(0)]),
            (move, [LS(1), S(1)]),
            (0x144, [LS(0), LS(1)]),
            (move, [GS(0x620), LS(0)]),
            (move, [GS(0x621), LS(1)]),
            Exit(),
        ], ["魔王", "魔王"]);
        var host = new RecordingHost
        {
            OnFullwidthTextEdit = _ => new(true, "リリィ"),
        };
        var vm = new VirtualMachine(script, Table, host);

        vm.Run();

        Assert.Equal(new FullwidthTextEditRequest("魔王", "魔王"),
                     Assert.Single(host.FullwidthTextEdits));
        Assert.Equal("リリィ", vm.GlobalStrings[0x620]);
        Assert.Equal("魔王", vm.GlobalStrings[0x621]);
    }

    [Fact]
    public void ModalEditorCancelLeavesResultOperandUnchanged()
    {
        int move = Table.ByLabel("mov")!.Value;
        Script script = ScriptAssembler.Assemble(Table, "INPUTNAME_CANCEL.BIN",
        [
            (move, [LS(0), S(0)]),
            (move, [LS(1), S(1)]),
            (0x144, [LS(0), LS(1)]),
            (move, [GS(0x630), LS(0)]),
            Exit(),
        ], ["魔王", "初期値"]);
        var host = new RecordingHost
        {
            OnFullwidthTextEdit = _ => new(false, "破棄される文字列"),
        };
        var vm = new VirtualMachine(script, Table, host);

        vm.Run();

        Assert.Equal("魔王", vm.GlobalStrings[0x630]);
    }

    [Theory]
    [InlineData("", FullwidthTextValidationError.None)]
    [InlineData("リリィ", FullwidthTextValidationError.None)]
    [InlineData("一二三四五六七八", FullwidthTextValidationError.None)]
    [InlineData("一二三四五六七八九", FullwidthTextValidationError.TooLong)]
    [InlineData("ＡB", FullwidthTextValidationError.NonDoubleByteCharacter)]
    [InlineData("ｱ", FullwidthTextValidationError.NonDoubleByteCharacter)]
    public void FullwidthValidationMatchesAgercAcceptanceRules(
        string text, FullwidthTextValidationError expected)
    {
        Assert.Equal(expected, Cp932Text.ValidateFullwidthName(text));
    }
}
