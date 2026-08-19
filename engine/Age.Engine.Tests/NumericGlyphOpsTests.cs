using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class NumericGlyphOpsTests
{
    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand S(int index) => new(2, index);
    private static Operand G(int address) => new(3, address);
    private static Operand L(int address) => new(9, address);
    private static Operand LS(int address) => new(11, address);
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    private static VirtualMachine RunNumericDraw(int value, int capacity, int flags)
    {
        var table = T();
        var scene = ScriptAssembler.Assemble(table, "NUMERIC-GLYPHS", new List<(int, Operand[])>
        {
            (0x1f9, new[] { I(0x123), I(0x48), I(0) }),
            (0x13a, new[] { I(0), I(0x48), I(0x189), I(0x165), I(22), I(30) }),
            (0x23b, new[] { I(1000), I(0), I(value), I(100), I(20), I(capacity), I(flags) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, table, new RecordingHost());
        vm.Run();
        return vm;
    }

    [Fact]
    public void DecimalGlyphDrawRightAlignsAndBindsLeastSignificantDigitFirst()
    {
        var vm = RunNumericDraw(42, 3, 0);

        Assert.Equal("exit", vm.HaltReason);
        var visible = vm.Gfx.SnapshotVisibleObjects();
        Assert.Equal(2, visible.Count);
        Assert.Equal((1000L, 0x189 + 2 * 22, 144),
            (visible[0].Handle, visible[0].SrcX, visible[0].DstX));
        Assert.Equal((1001L, 0x189 + 4 * 22, 122),
            (visible[1].Handle, visible[1].SrcX, visible[1].DstX));
        Assert.All(visible, item =>
        {
            Assert.Equal(0x123, item.SurfaceResId);
            Assert.Equal((0x165, 22, 30, 20), (item.SrcY, item.W, item.H, item.DstY));
        });
    }

    [Theory]
    [InlineData(1, 3, 144, 122, 100)] // zero padded, right aligned
    [InlineData(4, 2, 122, 100, -1)]  // left aligned to the used width
    [InlineData(2, 2, 133, 111, -1)]  // centered within the three-digit capacity
    public void DecimalGlyphFlagsMatchNativePlacement(int flags, int expectedCount,
        int firstX, int secondX, int thirdX)
    {
        var visible = RunNumericDraw(42, 3, flags).Gfx.SnapshotVisibleObjects();

        Assert.Equal(expectedCount, visible.Count);
        Assert.Equal(firstX, visible[0].DstX);
        Assert.Equal(secondX, visible[1].DstX);
        if (thirdX >= 0) Assert.Equal(thirdX, visible[2].DstX);
    }

    [Fact]
    public void Sys4433ExtendedStyleAppliesIndependentSourceAndDestinationSpacing()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var scene = ScriptAssembler.Assemble(table, "SPACED-NUMERIC-GLYPHS",
            new List<(int, Operand[])>
            {
                (0x1f9, new[] { I(0x123), I(0x48), I(0) }),
                (0x2da, new[] { I(0), I(0x48), I(10), I(20), I(8), I(12), I(2), I(3) }),
                (0x23b, new[] { I(1000), I(0), I(42), I(100), I(30), I(3), I(0) }),
                Exit(),
            }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, table, new RecordingHost(),
            compatibility: new VmCompatibilityContext("kamidori", "SYS4433"));

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Null(vm.CompatibilityFailure);
        var visible = vm.Gfx.SnapshotVisibleObjects();
        Assert.Equal(2, visible.Count);
        Assert.Equal((1000L, 10 + 2 * 10, 100 + 2 * 11),
            (visible[0].Handle, visible[0].SrcX, visible[0].DstX));
        Assert.Equal((1001L, 10 + 4 * 10, 100 + 1 * 11),
            (visible[1].Handle, visible[1].SrcX, visible[1].DstX));
        Assert.All(visible, item =>
            Assert.Equal((0x123, 20, 8, 12, 30),
                         (item.SurfaceResId, item.SrcY, item.W, item.H, item.DstY)));
    }

    [Fact]
    public void RedrawingAShorterValueErasesStaleDigitObjects()
    {
        var table = T();
        var scene = ScriptAssembler.Assemble(table, "NUMERIC-REDRAW", new List<(int, Operand[])>
        {
            (0x1f9, new[] { I(0x123), I(7), I(0) }),
            (0x13a, new[] { I(2), I(7), I(10), I(20), I(8), I(12) }),
            (0x23b, new[] { I(200), I(2), I(999), I(5), I(6), I(3), I(0) }),
            (0x23b, new[] { I(200), I(2), I(7), I(5), I(6), I(3), I(0) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, table, new RecordingHost());

        vm.Run();

        var visible = Assert.Single(vm.Gfx.SnapshotVisibleObjects());
        Assert.Equal((200L, 5 + 2 * 8, 10 + 7 * 8), (visible.Handle, visible.DstX, visible.SrcX));
    }

    [Fact]
    public void InvalidOrUnregisteredStylesHaltInsteadOfSilentlyDroppingTheDraw()
    {
        var table = T();
        var invalidRegistration = ScriptAssembler.Assemble(table, "BAD-NUMERIC-STYLE",
            new List<(int, Operand[])>
            {
                (0x13a, new[] { I(11), I(1), I(0), I(0), I(8), I(12) }),
                Exit(),
            }, System.Array.Empty<string>());
        var unregisteredDraw = ScriptAssembler.Assemble(table, "MISSING-NUMERIC-STYLE",
            new List<(int, Operand[])>
            {
                (0x23b, new[] { I(1), I(0), I(7), I(0), I(0), I(1), I(0) }),
                Exit(),
            }, System.Array.Empty<string>());
        var sys4433 = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var invalidExtendedRegistration = ScriptAssembler.Assemble(
            sys4433, "BAD-EXTENDED-NUMERIC-STYLE",
            new List<(int, Operand[])>
            {
                (0x2da, new[] { I(11), I(1), I(0), I(0), I(8), I(12), I(1), I(1) }),
                Exit(),
            }, System.Array.Empty<string>());

        var invalidVm = new VirtualMachine(invalidRegistration, table, new RecordingHost());
        var unregisteredVm = new VirtualMachine(unregisteredDraw, table, new RecordingHost());
        var invalidExtendedVm = new VirtualMachine(
            invalidExtendedRegistration, sys4433, new RecordingHost(),
            compatibility: new VmCompatibilityContext("kamidori", "SYS4433"));
        invalidVm.Run();
        unregisteredVm.Run();
        invalidExtendedVm.Run();

        Assert.Equal("numeric-glyph-style-index-out-of-range:11", invalidVm.HaltReason);
        Assert.Equal("numeric-glyph-style-unregistered:0", unregisteredVm.HaltReason);
        Assert.Equal("numeric-glyph-style-index-out-of-range:11", invalidExtendedVm.HaltReason);
        Assert.Null(invalidExtendedVm.CompatibilityFailure);
    }

    [Fact]
    public void HalfByteStringLengthUsesNativeCp932BytesForLiteralAndLocalStrings()
    {
        var table = T();
        var scene = ScriptAssembler.Assemble(table, "HALF-BYTE-LENGTH", new List<(int, Operand[])>
        {
            (0x1a6, new[] { L(0), S(0) }),
            (0x55, new[] { G(100), L(0) }),
            (0x55, new[] { LS(0), S(1) }),
            (0x1a6, new[] { L(1), LS(0) }),
            (0x55, new[] { G(101), L(1) }),
            Exit(),
        }, new[] { "AB姫", "リリィ" });
        var vm = new VirtualMachine(scene, table, new RecordingHost());

        vm.Run();

        Assert.Equal(2, vm.Globals[100]); // four CP932 bytes, not three .NET chars
        Assert.Equal(3, vm.Globals[101]); // six CP932 bytes
    }

    [Fact]
    public void ByteStringLengthUsesNativeCp932BytesAndStopsAtNul()
    {
        var table = T();
        var scene = ScriptAssembler.Assemble(table, "BYTE-LENGTH", new List<(int, Operand[])>
        {
            (0x2c5, new[] { L(0), S(0) }),
            (0x55, new[] { G(100), L(0) }),
            (0x55, new[] { LS(0), S(1) }),
            (0x2c5, new[] { L(1), LS(0) }),
            (0x55, new[] { G(101), L(1) }),
            (0x2c5, new[] { L(2), S(2) }),
            (0x55, new[] { G(102), L(2) }),
            Exit(),
        }, new[] { "AB姫", "リリィ", "AB\0姫" });
        var vm = new VirtualMachine(scene, table, new RecordingHost());

        vm.Run();

        Assert.Equal(4, vm.Globals[100]); // two ASCII + one double-byte CP932 glyph
        Assert.Equal(6, vm.Globals[101]); // three double-byte CP932 glyphs through a string pointer
        Assert.Equal(2, vm.Globals[102]); // native strlen stops before the embedded NUL
    }
}
