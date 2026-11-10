using System;
using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

public class CoreScalarAndScreenTransitionOpsTests
{
    private const int Imm = 0, GlobalInt = 3, LocalInt = 9;

    [Fact]
    public void ZeroIntRange_ClearsEveryConsecutiveDestinationCell()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        (int, Operand[]) Mov(int address, long value)
            => (0x55, new[] { new Operand(GlobalInt, address), new Operand(Imm, value) });
        var ops = new List<(int, Operand[])>
        {
            Mov(0x2e49, 7), Mov(0x2e4a, 8), Mov(0x2e4b, 9),
            (0x6c, new[] { new Operand(GlobalInt, 0x2e49), new Operand(Imm, 3) }),
            (0x2, Array.Empty<Operand>()),
        };

        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "ZERO_RANGE", ops, Array.Empty<string>()), table,
                                    new RecordingHost());
        vm.Run();

        Assert.All(Enumerable.Range(0x2e49, 3), address => Assert.Equal(0, vm.Globals[address]));
    }

    // Cell addresses are checked 32-bit sums. One that passes int.MaxValue halts the run with the instruction's
    // location, where the overflow used to end the VM thread with an exception.
    [Fact]
    public void AnIndexedAddressPastInt32MaxHaltsWithTheInstructionLocation()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var ops = new List<(int, Operand[])>
        {
            (0x55, new[] { new Operand(GlobalInt, 0x100), new Operand(Imm, 1) }),
            // zero-int-range: the second cell of G[0x7fffffff] is int.MaxValue + 1.
            (0x6c, new[] { new Operand(GlobalInt, int.MaxValue), new Operand(Imm, 2) }),
            (0x55, new[] { new Operand(GlobalInt, 0x101), new Operand(Imm, 1) }),
            (0x2, Array.Empty<Operand>()),
        };
        Script script = ScriptAssembler.Assemble(table, "OVERFLOW.BIN", ops, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Run();

        Assert.Equal($"operand-overflow:OVERFLOW.BIN@0x{script.Instructions[1].Offset:x}:op 0x6c", vm.HaltReason);
        Assert.Equal(1, vm.Globals[0x100]);
        Assert.False(vm.Globals.ContainsKey(0x101));
    }

    // A count past int.MaxValue in an op that converts it checked (copy-dwords) halts the same way.
    [Fact]
    public void ACheckedCountPastInt32MaxHaltsWithTheInstructionLocation()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var ops = new List<(int, Operand[])>
        {
            (0x1b0, new[] { new Operand(GlobalInt, 0x100), new Operand(GlobalInt, 0x200), new Operand(Imm, 0x80000000L) }),
            (0x55, new[] { new Operand(GlobalInt, 0x101), new Operand(Imm, 1) }),
            (0x2, Array.Empty<Operand>()),
        };
        Script script = ScriptAssembler.Assemble(table, "OVERFLOW.BIN", ops, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Run();

        Assert.Equal($"operand-overflow:OVERFLOW.BIN@0x{script.Instructions[0].Offset:x}:op 0x1b0", vm.HaltReason);
        Assert.False(vm.Globals.ContainsKey(0x101));
    }

    // Native 0x53/0x54 are CDQ+IDIV: the quotient truncates toward zero and the remainder takes the
    // dividend's sign. Kamidori SETTOOLBOX computes an empty category's page length as ((0-1) mod 18)+1,
    // which must be 0; a floored remainder (17) fills a full page with zero-id items.
    [Theory]
    [InlineData(-1, 18, 0, -1)]
    [InlineData(-19, 18, -1, -1)]
    [InlineData(7, -2, -3, 1)]
    [InlineData(-7, -2, 3, -1)]
    [InlineData(35, 18, 1, 17)]
    public void DivAndMod_TruncateTowardZeroLikeNativeIdiv(long dividend, long divisor, long quotient, long remainder)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // Script immediates are non-negative; negate through `sub dst 0 x` as the game scripts do.
        (int, Operand[]) Load(int local, long value) => value < 0
            ? (0x51, new[] { new Operand(LocalInt, local), new Operand(Imm, 0), new Operand(Imm, -value) })
            : (0x55, new[] { new Operand(LocalInt, local), new Operand(Imm, value) });
        var ops = new List<(int, Operand[])>
        {
            Load(1, dividend),
            Load(2, divisor),
            (0x53, new[] { new Operand(GlobalInt, 0x100), new Operand(LocalInt, 1), new Operand(LocalInt, 2) }),
            (0x54, new[] { new Operand(GlobalInt, 0x101), new Operand(LocalInt, 1), new Operand(LocalInt, 2) }),
            (0x2, Array.Empty<Operand>()),
        };

        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "DIV_MOD", ops, Array.Empty<string>()), table,
                                    new RecordingHost());
        vm.Run();

        Assert.Equal(quotient, vm.Globals[0x100]);
        Assert.Equal(remainder, vm.Globals[0x101]);
    }

    // Native cells are 32-bit: arithmetic wraps and compares are signed. A port cell is wider, so a result past
    // 2^31 must come back as the negative value native holds.
    [Fact]
    public void IntegerArithmeticWrapsAndComparesAt32Bits()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        Operand L(int slot) => new(LocalInt, slot);
        Operand G(int address) => new(GlobalInt, address);
        Operand I(long value) => new(Imm, value);
        var ops = new List<(int, Operand[])>
        {
            (0x55, new[] { L(1), I(0x7fffffff) }),
            (0x50, new[] { G(0x100), L(1), I(1) }),            // add wraps to int.MinValue
            (0x5c, new[] { G(0x101), G(0x100), I(0) }),        // lt: the wrapped sum is negative
            (0x55, new[] { L(2), I(0) }),
            (0x135, new[] { L(2), I(31) }),                    // bit-set 31: the sign bit
            (0x5c, new[] { G(0x102), L(2), I(0) }),
            (0x59, new[] { G(0x103), I(1), I(31) }),           // shl 1,31 is int.MinValue
            (0x58, new[] { G(0x104), G(0x103), I(31) }),       // sar keeps the sign: -1
            (0x52, new[] { G(0x105), I(0x10000), I(0x10000) }),// mul wraps to 0
            (0x5a, new[] { G(0x106), I(0xffffffffL), G(0x104) }), // an unsigned immediate equals -1
            (0x5e, new[] { G(0x107), I(0x80000000L), I(0) }),  // and is less than zero as a cell value
            (0x2, Array.Empty<Operand>()),
        };

        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "WRAP32", ops, Array.Empty<string>()), table,
                                    new RecordingHost());
        vm.Run();

        Assert.Equal(int.MinValue, vm.Globals[0x100]);
        Assert.Equal(1, vm.Globals[0x101]);
        Assert.Equal(1, vm.Globals[0x102]);
        Assert.Equal(int.MinValue, vm.Globals[0x103]);
        Assert.Equal(-1, vm.Globals[0x104]);
        Assert.Equal(0, vm.Globals[0x105]);
        Assert.Equal(1, vm.Globals[0x106]);
        Assert.Equal(0, vm.Globals[0x107]);
    }

    [Fact]
    public void RandomModulo_WritesAValueInsideTheNativeRange()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var ops = new List<(int, Operand[])>
        {
            (0x60, new[] { new Operand(LocalInt, 0), new Operand(Imm, 4) }),
            (0x55, new[] { new Operand(GlobalInt, 0x100), new Operand(LocalInt, 0) }),
            (0x2, Array.Empty<Operand>()),
        };

        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "RANDOM_MODULO", ops, Array.Empty<string>()), table,
                                    new RecordingHost());
        vm.Run();

        Assert.InRange(vm.Globals[0x100], 0, 3);
    }

    [Fact]
    public void KamidoriDisplayBackgroundColorReachesGraphicsState()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var script = ScriptAssembler.Assemble(table, "BACKGROUND-COLOR",
            new List<(int, Operand[])>
            {
                (0x25a, new[] { new Operand(Imm, 0x123456) }),
                (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost(),
            compatibility: new("kamidori", "SYS4433"));

        vm.Run();

        Assert.Equal(0x123456, vm.Gfx.DisplayBackgroundColor);
    }

    [Fact]
    public void BlackSurfaceFades_ForwardCapturedSlotTimingAndDirection()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "SURFACE_BLACK_FADES", new List<(int, Operand[])>
        {
            (0x21, new[] { new Operand(Imm, 1), new Operand(Imm, 30) }),
            (0x22, new[] { new Operand(Imm, 2), new Operand(Imm, 65) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal(
            new[]
            {
                (1, 30L, SurfaceBlackFadeDirection.FromBlack),
                (2, 65L, SurfaceBlackFadeDirection.ToBlack),
            },
            host.SurfaceBlackFades);
        Assert.Equal(480, LegacyScreenTransitionTiming.DurationMilliseconds(30));
        Assert.Equal(1024, LegacyScreenTransitionTiming.DurationMilliseconds(65));
    }

    [Fact]
    public void CrossfadeSurfaces_ForwardsBothCapturedSlotsAndTimingArgument()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "SURFACE_CROSSFADE", new List<(int, Operand[])>
        {
            (0x25, new[] { new Operand(Imm, 1), new Operand(Imm, 2), new Operand(Imm, 10) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal((1, 2, 10L), Assert.Single(host.SurfaceCrossfades));
        Assert.Equal(160, LegacyScreenTransitionTiming.DurationMilliseconds(10));
        Assert.Equal(480, LegacyScreenTransitionTiming.DurationMilliseconds(30));
        Assert.Equal(1024, LegacyScreenTransitionTiming.DurationMilliseconds(65));
    }

    // ROOM's 0x25 fades its menu in and out over an unchanged room. The flattened target is blended over the
    // flattened source, so pixels the two surfaces share hold steady; fading each target object over a
    // progress-alpha black fill instead dimmed the whole frame mid-transition.
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void CrossfadeComposite_KeepsPixelsSharedBySourceAndTargetSteady(double progress)
    {
        byte[] room = { 40, 30, 60, 255 };                       // the room background, in both surfaces
        byte[] menuSource = { 40, 30, 60, 255, 40, 30, 60, 255 };
        byte[] menuTarget = { 40, 30, 60, 255, 250, 200, 20, 255 }; // second pixel: a menu button appears
        byte[] blended = (byte[])menuTarget.Clone();

        LegacyScreenTransitionComposite.BlendTargetOverSource(
            blended, menuSource, LegacyScreenTransitionComposite.Alpha(progress));

        Assert.Equal(room, blended[..4]);
        int a = LegacyScreenTransitionComposite.Alpha(progress);
        Assert.Equal((byte)((40 * (256 - a) + 250 * a) >> 8), blended[4]);
        Assert.Equal((byte)((60 * (256 - a) + 20 * a) >> 8), blended[6]);
        Assert.Equal(255, blended[7]);
    }

    [Fact]
    public void CrossfadeComposite_EndpointsAreTheSourceAndTheTarget()
    {
        byte[] source = { 10, 20, 30, 255 };
        byte[] target = { 200, 100, 0, 255 };
        byte[] start = (byte[])target.Clone();
        byte[] end = (byte[])target.Clone();

        LegacyScreenTransitionComposite.BlendTargetOverSource(start, source, LegacyScreenTransitionComposite.Alpha(0));
        LegacyScreenTransitionComposite.BlendTargetOverSource(end, source, LegacyScreenTransitionComposite.Alpha(1));

        Assert.Equal(source, start);
        Assert.Equal(target, end);
        Assert.Equal(256, LegacyScreenTransitionComposite.Alpha(1.5));
    }

    [Fact]
    public void KamidoriWhiteSurfaceFade_ForwardsCapturedSlotAndTimingArgument()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var script = ScriptAssembler.Assemble(table, "SURFACE_WHITE_FADE", new List<(int, Operand[])>
        {
            (0x24, new[] { new Operand(Imm, 1), new Operand(Imm, 80) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host,
            compatibility: new("kamidori", "SYS4433")).Run();

        Assert.Equal((1, 80L), Assert.Single(host.SurfaceWhiteFades));
        Assert.Equal(1280, LegacyScreenTransitionTiming.DurationMilliseconds(80));
        Assert.False(Assert.Single(host.SurfaceWhiteFadeForceEndpoints));
    }

    [Fact]
    public void KamidoriPatternedSurfaceReveals_MapSelectorsAndNativeTiming()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var script = ScriptAssembler.Assemble(table, "PATTERNED-SURFACE-REVEALS",
            new List<(int, Operand[])>
            {
                (0x27, new[]
                {
                    new Operand(Imm, 2), new Operand(Imm, 40),
                    new Operand(Imm, 64), new Operand(Imm, 3),
                }),
                (0x28, new[]
                {
                    new Operand(Imm, 2), new Operand(Imm, 15),
                    new Operand(Imm, 64), new Operand(Imm, 0),
                }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host,
            compatibility: new("kamidori", "SYS4433")).Run();

        Assert.Equal(new[]
        {
            new SurfacePatternTransitionRequest(
                2, 40, 64, SurfacePatternTransitionMode.HorizontalStripsBottomToTop),
            new SurfacePatternTransitionRequest(
                2, 15, 64, SurfacePatternTransitionMode.StaggeredVerticalStripsLeftToRight),
        }, host.SurfacePatternTransitions);
        Assert.Equal(new[] { false, false }, host.SurfacePatternForceEndpoints);
        Assert.Equal(360, LegacyScreenTransitionTiming.PatternDurationMilliseconds(
            1024, 576, 40, 64, SurfacePatternTransitionMode.HorizontalStripsBottomToTop));
        Assert.Equal(1185, LegacyScreenTransitionTiming.PatternDurationMilliseconds(
            1024, 576, 15, 64,
            SurfacePatternTransitionMode.StaggeredVerticalStripsLeftToRight));
        Assert.Equal(1080, LegacyScreenTransitionTiming.PatternDurationMilliseconds(
            1024, 576, 15, 64,
            SurfacePatternTransitionMode.StaggeredHorizontalStripsTopToBottom));
        Assert.Equal(0, LegacyScreenTransitionTiming.PatternDurationMilliseconds(
            1024, 576, 15, 64, (SurfacePatternTransitionMode)12));
    }

    [Fact]
    public void ActiveAdvSkip_ForcesLegacyScreenTransitionEndpointsAtDispatch()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "SKIPPED_SCREEN_TRANSITIONS",
            new List<(int, Operand[])>
            {
                (0x21, new[] { new Operand(Imm, 1), new Operand(Imm, 30) }),
                (0x25, new[]
                {
                    new Operand(Imm, 1), new Operand(Imm, 2), new Operand(Imm, 10),
                }),
                (0x28, new[]
                {
                    new Operand(Imm, 2), new Operand(Imm, 15),
                    new Operand(Imm, 64), new Operand(Imm, 0),
                }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost { MessageSkip = true };

        new VirtualMachine(script, table, host).Run();

        Assert.Equal(new[] { true }, host.SurfaceBlackFadeForceEndpoints);
        Assert.Equal(new[] { true }, host.SurfaceCrossfadeForceEndpoints);
        Assert.Equal(new[] { true }, host.SurfacePatternForceEndpoints);
    }
}
