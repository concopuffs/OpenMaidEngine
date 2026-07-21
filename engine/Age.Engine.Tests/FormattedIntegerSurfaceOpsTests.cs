using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class FormattedIntegerSurfaceOpsTests
{
    [Fact]
    public void DrawEnpLevelPatternDrawsHalfWidthValueAtRightAlignedPosition()
    {
        var host = Run(
            (0x75, new[] { I(18) }),
            (0x205, new[] { I(0x51), I(0x3f), I(0x3c), I(80), I(3), I(0x10000) }));

        Assert.Equal((0x51, 0x48, 0x3c, "80"), Assert.Single(host.SurfaceStrings));
    }

    [Fact]
    public void ZeroPaddingConsumesTheWholeFieldWithoutMovingTheAnchor()
    {
        var host = Run(
            (0x75, new[] { I(18) }),
            (0x205, new[] { I(0x51), I(100), I(20), I(7), I(3), I(0x10001) }));

        Assert.Equal((0x51, 100, 20, "007"), Assert.Single(host.SurfaceStrings));
    }

    [Fact]
    public void FullWidthLeftAlignedFormattingUsesCp932GlyphForms()
    {
        var host = Run(
            (0x75, new[] { I(22) }),
            (0x205, new[] { I(0x2a), I(40), I(22), I(61), I(3), I(0x04) }));

        Assert.Equal((0x2a, 40, 22, "６１"), Assert.Single(host.SurfaceStrings));
    }

    private static RecordingHost Run(params (int Opcode, Operand[] Args)[] instructions)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var body = new List<(int, Operand[])>(instructions) { (0x2, Array.Empty<Operand>()) };
        var script = ScriptAssembler.Assemble(table, "FORMAT_INTEGER", body, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        return host;
    }

    private static Operand I(long value) => new(0, value);
}
