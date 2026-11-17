using System;
using System.Collections.Generic;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

public class BattleFrontierOpsTests
{
    private const int Immediate = 0, InlineString = 2, GlobalInt = 3, GlobalString = 5,
                      LocalString = 11;

    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(Immediate, value);
    private static Operand G(int address) => new(GlobalInt, address);

    [Fact]
    public void SurfaceRectCopy_CopiesTheSourceRectangleToTheDestination()
    {
        var table = Table();
        var script = ScriptAssembler.Assemble(table, "SURFACE_COPY", new List<(int, Operand[])>
        {
            (0x207, new[] { I(72), I(67), I(12), I(15), I(3), I(4), I(30), I(33) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingWaits());
        // Source 72: each pixel's red is its x and green its y. Destination 67: blank.
        vm.Graphics.PublishSurfacePixels(72, GraphicsTestSupport.Image(20, 20, (x, y) => [(byte)x, (byte)y, 0, 255]));
        vm.Graphics.CreateSurface(67, 40, 40);

        vm.Run();

        // (12, 15) 3 x 4 lands at (30, 33).
        Assert.Equal(new byte[] { 12, 15, 0, 255 }, vm.Graphics.PixelAt(67, 30, 33));
        Assert.Equal(new byte[] { 14, 18, 0, 255 }, vm.Graphics.PixelAt(67, 32, 36));
        Assert.Equal(new byte[4], vm.Graphics.PixelAt(67, 33, 33));
        Assert.Equal(new byte[4], vm.Graphics.PixelAt(67, 30, 37));
    }

    [Fact]
    public void ScaledSurfaceRectCopy_ScalesIntoTheDestinationRectangle()
    {
        var table = Table();
        var script = ScriptAssembler.Assemble(table, "SCALED_SURFACE_COPY", new List<(int, Operand[])>
        {
            (0x32, new[] { I(2), I(193), I(0), I(0), I(1024), I(576),
                            I(0), I(0), I(384), I(216) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingWaits());
        // Source 2: the left half red, the right half blue. Destination 193: blank and larger than the copy.
        vm.Graphics.PublishSurfacePixels(2, GraphicsTestSupport.Image(1024, 576,
            (x, y) => x < 512 ? [255, 0, 0, 255] : [0, 0, 255, 255]));
        vm.Graphics.CreateSurface(193, 400, 300);

        vm.Run();

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, vm.Graphics.PixelAt(193, 0, 0));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, vm.Graphics.PixelAt(193, 190, 215));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, vm.Graphics.PixelAt(193, 193, 0));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, vm.Graphics.PixelAt(193, 383, 215));
        Assert.Equal(new byte[4], vm.Graphics.PixelAt(193, 384, 0));
        Assert.Equal(new byte[4], vm.Graphics.PixelAt(193, 0, 216));
    }

    [Theory]
    [InlineData(100, 50, 40, 100, true)]    // the width shrinks and the height grows
    [InlineData(40, 100, 100, 50, true)]    // the width grows and the height shrinks
    [InlineData(100, 50, 40, 20, false)]    // both shrink
    [InlineData(40, 20, 100, 50, false)]    // both grow
    public void ScaledSurfaceRectCopy_GoesThroughScratchSurface32WhenTheAxesDisagree(
        int width, int height, int destinationWidth, int destinationHeight, bool throughScratch)
    {
        var table = Table();
        var script = ScriptAssembler.Assemble(table, "SCALED_SURFACE_COPY", new List<(int, Operand[])>
        {
            (0x32, new[] { I(2), I(193), I(0), I(0), I(width), I(height),
                            I(0), I(0), I(destinationWidth), I(destinationHeight) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingWaits());
        vm.Graphics.PublishSurfacePixels(2, GraphicsTestSupport.Image(width, height, (x, y) => [255, 0, 0, 255]));
        vm.Graphics.CreateSurface(193, 120, 120);
        vm.Graphics.CreateSurface(32, 10, 10);   // a script's surface in the scratch slot

        vm.Run();

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, vm.Graphics.PixelAt(193, 0, 0));
        Assert.Equal(new byte[] { 255, 0, 0, 255 },
                     vm.Graphics.PixelAt(193, destinationWidth - 1, destinationHeight - 1));
        // Native creates the scratch surface over slot 32 and releases it after the copy.
        Assert.Equal(throughScratch ? (0, 0) : (10, 10), vm.Graphics.SurfaceSize(32));
        Assert.Equal(-1, vm.Gfx.QueryField(32));
    }

    [Fact]
    public void ScalarBattleHelpers_PreserveNativeSignedBehaviorAndStringAliasing()
    {
        var table = Table();
        var script = ScriptAssembler.Assemble(table, "BATTLE_SCALARS", new List<(int, Operand[])>
        {
            (0x191, new[] { G(0x100), I(int.MinValue) }),
            (0x193, new[] { new Operand(GlobalString, 0x500), new Operand(GlobalString, 0x500),
                            new Operand(InlineString, 0) }),
            (0x1c8, new[] { new Operand(LocalString, 0), I(-42) }),
            (0x55, new[] { new Operand(GlobalString, 0x501), new Operand(LocalString, 0) }),
            (0x2, Array.Empty<Operand>()),
        }, new[] { "/99" });
        var vm = new VirtualMachine(script, table, new RecordingWaits());
        vm.GlobalStrings[0x500] = "17";

        vm.Run();

        Assert.Equal(int.MinValue, vm.Globals[0x100]);
        Assert.Equal("17/99", vm.GlobalStrings[0x500]);
        Assert.Equal("-42", vm.GlobalStrings[0x501]);
    }

    [Fact]
    public void StringOperandsReadNumbersAsFullwidthText()
    {
        // Kamidori FIELD@0x8957 builds 「素材Ｎ個を採取袋に入れた！」 with concat "素材" (local-ptr), an integer.
        var table = Table();
        var script = ScriptAssembler.Assemble(table, "NUMBER_TEXT", new List<(int, Operand[])>
        {
            (0x193, new[] { new Operand(GlobalString, 0x500), new Operand(InlineString, 0), G(0x100) }),
            (0x193, new[] { new Operand(GlobalString, 0x501), I(-7), new Operand(InlineString, 1) }),
            (0x192, new[] { new Operand(GlobalString, 0x502), G(0x101) }),
            (0x193, new[] { new Operand(GlobalString, 0x503), new Operand(InlineString, 1),
                            new Operand(1, BitConverter.SingleToInt32Bits(1.5f)) }),
            (0x1c8, new[] { new Operand(GlobalString, 0x504), I(-7) }),
            (0x2, Array.Empty<Operand>()),
        }, new[] { "素材", "個" });
        var vm = new VirtualMachine(script, table, new RecordingWaits());
        vm.SeedGlobal(0x100, 12);
        vm.SeedGlobal(0x101, 3);

        vm.Run();

        Assert.Equal("素材１２", vm.GlobalStrings[0x500]);
        Assert.Equal("－７個", vm.GlobalStrings[0x501]);
        Assert.Equal("３", vm.GlobalStrings[0x502]);
        Assert.Equal("個１？５０００００", vm.GlobalStrings[0x503]);
        Assert.Equal("-7", vm.GlobalStrings[0x504]); // op 0x1c8 formats ASCII itself
    }

    [Fact]
    public void MonotonicAndFrameTimeOps_StoreNativeLowDwordSamples()
    {
        var table = Table();
        var host = new SequencedClockHost(0x1_0000_0005, 100, 116);
        var script = ScriptAssembler.Assemble(table, "BATTLE_CLOCK", new List<(int, Operand[])>
        {
            (0xd0, new[] { G(0x100) }),
            (0x23c, Array.Empty<Operand>()),
            (0x23c, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(5, vm.Globals[0x100]);
        Assert.Equal(100u, vm.Gfx.PreviousFrameTimeMilliseconds);
        Assert.Equal(116u, vm.Gfx.CurrentFrameTimeMilliseconds);
    }

    [Fact]
    public void MovieActivityAndAnimationServiceFlags_ControlBattlePollingAndReset()
    {
        var table = Table();
        var host = new RecordingWaits();
        var script = ScriptAssembler.Assemble(table, "BATTLE_CONTROL", new List<(int, Operand[])>
        {
            (0x236, new[] { I(0x2be3), I(7), I(0), I(0) }),   // a movie plays on surface 7, none on 8
            (0x23a, new[] { G(0x100), I(7) }),
            (0x23a, new[] { G(0x101), I(8) }),
            (0x238, new[] { I(400) }),
            (0x24e, new[] { I(2) }),
            (0x243, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new MovieRig().Vm(script, table, host);

        vm.Run();

        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Equal(0, vm.Globals[0x101]);
        Assert.Equal(400, vm.Gfx.AnimClockDurationTicks);
        Assert.Equal(1, vm.Gfx.AnimClockGeneration);
        Assert.Equal(2, vm.Gfx.AnimationServiceFlags);
    }

    [Fact]
    public void DelayedCombatVoice_ForwardsAllSchedulingOperands()
    {
        var table = Table();
        var rig = new AudioRig();
        var script = ScriptAssembler.Assemble(table, "BATTLE_VOICE", new List<(int, Operand[])>
        {
            (0x2c0, new[] { I(123), I(0), I(275) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());

        rig.Vm(script, table, new RecordingWaits()).Run();

        // The delay counts from the first tick, which the run's last instruction boundary was.
        rig.Advance(274);
        Assert.Empty(rig.Device.Voices);
        rig.Advance(1);
        Assert.Equal([(123L, false)], rig.Device.Voices);
        Assert.Equal(0, rig.Audio.VoicePlaybackVariant);
    }

    private sealed class SequencedClockHost : RecordingWaits
    {
        private readonly Queue<long> _samples;
        public SequencedClockHost(params long[] samples) => _samples = new Queue<long>(samples);
        public override long InputClockMilliseconds => _samples.Dequeue();
    }
}
