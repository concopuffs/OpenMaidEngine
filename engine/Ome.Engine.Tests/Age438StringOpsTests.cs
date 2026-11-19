using System;
using System.Collections.Generic;
using System.Text;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// AGE 4.38D's small opcodes on 創刻のアテリアル's boot route (vm-map/opcodes.toml): 0x2c7, native's SBSubstr, a byte
/// substring kept from splitting a double-byte character; 0x2eb, the game's version text; 0x2ec, a string as an
/// integer; and 0x2e9, the count the Auto delay leaves out.
/// </summary>
public class Age438StringOpsTests
{
    private const int Imm = 0, GlobalInt = 3, GlobalString = 5;
    private const int Exit = 0x2, ByteSubstring = 0x2c7, CountOffset = 0x2e9, GameVersion = 0x2eb, StringToInt = 0x2ec;

    private static Operand I(long value) => new(Imm, value);
    private static Operand G(int address) => new(GlobalInt, address);
    private static Operand GS(int address) => new(GlobalString, address);

    private static (VirtualMachine Vm, RecordingWaits Waits) Run(VmOptions? options, Action<VirtualMachine>? seed,
                                                                 params (int, Operand[])[] instructions)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4438");
        var ops = new List<(int, Operand[])>(instructions) { (Exit, Array.Empty<Operand>()) };
        var waits = new RecordingWaits();
        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "STRINGS.BIN", ops, []), table, waits,
                                    options ?? new VmOptions());
        seed?.Invoke(vm);
        vm.Run();
        return (vm, waits);
    }

    [Theory]
    // "Ver漢字ab": V e r, then two double-byte characters at bytes 3 to 6, then a b.
    [InlineData(0, 3, "Ver", false, false)]
    [InlineData(3, 4, "漢字", false, false)]
    [InlineData(4, 3, "字", true, false)]        // starts inside 漢: its second byte is skipped, and the count is one less
    [InlineData(3, 3, "漢", false, true)]        // ends inside 字: its first byte is dropped
    [InlineData(4, 2, "", true, true)]           // both ends inside a character: nothing is left
    [InlineData(7, 99, "ab", false, false)]      // a count past the end takes the rest
    [InlineData(9, 1, "", false, false)]         // a start at the end
    [InlineData(-1, 2, "", false, false)]        // a negative start is past the end, compared unsigned
    [InlineData(0, 0, "", false, false)]
    public void ByteSubstringKeepsDoubleByteCharactersWhole(int start, int count, string expected, bool startMoved,
                                                            bool endMoved)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string result = Cp932Text.ByteSubstring("Ver漢字ab", start, count, Encoding.GetEncoding(932),
                                                out bool movedStart, out bool movedEnd);

        Assert.Equal((expected, startMoved, endMoved), (result, movedStart, movedEnd));
    }

    [Fact]
    public void TheSubstringOpcodeWritesTheStringAndReportsNativesTraces()
    {
        var (vm, waits) = Run(null, vm => vm.GlobalStrings[0x10] = "Ver漢字ab",
            (ByteSubstring, [GS(0x11), GS(0x10), I(0), I(3)]),
            (ByteSubstring, [GS(0x12), GS(0x10), I(4), I(3)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(("Ver", "字"), (vm.GlobalStrings[0x11], vm.GlobalStrings[0x12]));
        string warning = Assert.Single(waits.Warnings);
        Assert.Contains("関数：SBSubstr 警告：文字列が全角文字の途中から切り出されるため", warning);
    }

    [Fact]
    public void TheVersionTextIsTheOptionsAndItsFirstNumberIsAnInteger()
    {
        // TITLE.BIN reads the version and takes its major number, as native's atol reads a string.
        var (vm, _) = Run(new VmOptions(GameVersion: "2.00.0022"), vm => vm.GlobalStrings[0x12] = "  -17abc",
            (GameVersion, [GS(0x10)]),
            (StringToInt, [G(0x300), GS(0x10)]),
            (StringToInt, [G(0x301), GS(0x12)]),
            (StringToInt, [G(0x302), GS(0x13)]),
            (CountOffset, [I(4)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal("2.00.0022", vm.GlobalStrings[0x10]);
        Assert.Equal((2L, -17L, 0L), (vm.Globals[0x300], vm.Globals[0x301], vm.Globals.GetValueOrDefault(0x302)));
        Assert.Equal(4, vm.SkipAndAuto.AutoMessageCountOffset);
        vm.SkipAndAuto.ResetScene();        // scene_context_init_reset zeroes the count
        Assert.Equal(0, vm.SkipAndAuto.AutoMessageCountOffset);
    }
}
