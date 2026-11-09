using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

// The native surface-record tables that numbered saves write (docs/sys4-format-notes.md, surface records):
// workers update the live table in place, op 0x71 snapshots it, and the save writes the snapshot.
public class SurfaceRecordTableTests
{
    private static Operand I(long value) => new(0, value);

    private static GfxState Run(params (int, Operand[])[] ops) => RunOn(OpcodeTableJson.Load(Paths.OpcodesJson), ops);

    private static GfxState RunOn(OpcodeTable table, params (int, Operand[])[] ops)
    {
        var list = new List<(int, Operand[])>(ops) { (0x2, Array.Empty<Operand>()) };
        var vm = new VirtualMachine(
            ScriptAssembler.Assemble(table, "SURFACES", list, Array.Empty<string>()), table, new RecordingHost());
        vm.Run();
        Assert.True(vm.HaltReason == "exit", vm.HaltReason);
        return vm.Gfx;
    }

    private static int[] Record(GfxState gfx, int slot)
    {
        byte[] bytes = gfx.CaptureNativeSurfaceRecords();
        var record = new int[5];
        for (int i = 0; i < 5; i++) record[i] = BitConverter.ToInt32(bytes, slot * 20 + i * 4);
        return record;
    }

    [Fact]
    public void WorkersWriteTheNativeFieldsAndReleaseKeepsKeyAndCreatedFlag()
    {
        GfxState gfx = Run(
            (0x1f9, [I(0x337c), I(12), I(0x00ff00)]),            // load with key: id, 0xff00ff00, created 0
            (0x1f9, [I(0x2cad), I(42), I(0x00ff00)]), (0x1fa, [I(42)]),   // released: key survives
            (0x1f8, [I(22), I(8), I(8), I(0)]), (0x1fa, [I(22)]),        // blank surface released: created survives
            (0x249, [I(0x337a), I(13), I(-1)]),                    // mode-1 load records id -1
            (0x1f8, [I(14), I(8), I(8), I(0)]), (0x1f9, [I(0x21), I(14), I(-1)]),   // load clears created
            (0x20b, [I(30), I(0), I(0), I(4), I(4), I(255), I(0)]),             // fill marks created
            (0x258, [I(4), I(3)]));                                // both persistence bits
        gfx.SnapshotSurfaceRecords();

        Assert.Equal([0x337c, unchecked((int)0xff00ff00), 0, 0, 0], Record(gfx, 12));
        Assert.Equal([-1, unchecked((int)0xff00ff00), 0, 0, 0], Record(gfx, 42));
        Assert.Equal([-1, 0, 0, 0, 1], Record(gfx, 22));
        Assert.Equal([-1, 0, 0, 0, 0], Record(gfx, 13));
        Assert.Equal([0x21, 0, 0, 0, 0], Record(gfx, 14));
        Assert.Equal([-1, 0, 0, 0, 1], Record(gfx, 30));
        Assert.Equal([-1, 0, 1, 1, 0], Record(gfx, 4));        // no key: 0, not 0xff000000
        Assert.Equal([-1, 0, 0, 0, 0], Record(gfx, 999));
        Assert.Equal([-1, 0, 0, 0, 1], Record(gfx, 0x24));   // boot-created composite scratch
        Assert.Equal([-1, 0, 0, 0, 1], Record(gfx, 0x25));
    }

    // Persistent Skip on, then an ADV entry starts fast-forward (run-state 0x8000000).
    private static readonly (int, Operand[])[] FastForward = [(0x88, [I(1)]), (0x19c, [])];

    public static TheoryData<string, bool, int, long[], int> LegacyTransitions => new()
    {
        // Under DRAWMODE=1 the black and white fades fill scratch surface 0x20; the crossfade and the patterned reveals
        // never touch it; under fast-forward only a negative surface (black or white) makes finalize fill it.
        { "SYS4422", false, 0x21, [1, 30], 1 },
        { "SYS4422", true, 0x21, [1, 30], 0 },
        { "SYS4422", false, 0x22, [1, 30], 1 },
        { "SYS4422", true, 0x22, [1, 30], 1 },
        { "SYS4422", false, 0x25, [1, 2, 8], 0 },
        { "SYS4422", true, 0x25, [1, 2, 8], 0 },
        { "SYS4433", false, 0x24, [1, 80], 1 },
        { "SYS4433", false, 0x27, [2, 40, 64, 3], 0 },
        { "SYS4433", false, 0x28, [2, 10, 64, 0], 0 },
        { "SYS4433", true, 0x28, [2, 10, 64, 0], 0 },
    };

    [Theory]
    [MemberData(nameof(LegacyTransitions))]
    public void LegacyTransitionsMarkScratch0x20OnlyWhereNativeFillsIt(string abi, bool fastForward, int op,
                                                                       long[] operands, int created)
    {
        var ops = new List<(int, Operand[])>();
        if (fastForward) ops.AddRange(FastForward);
        ops.Add((op, Array.ConvertAll(operands, I)));
        GfxState gfx = RunOn(OpcodeTableJson.Load(Paths.OpcodesJson, abi), ops.ToArray());
        gfx.SnapshotSurfaceRecords();

        Assert.Equal(created, Record(gfx, 0x20)[4]);
    }

    [Fact]
    public void DefiningATextLayoutRecordsItsBlankSurface()
    {
        GfxState gfx = Run((0x70, [I(3), I(400), I(120), I(75), I(340)]));
        gfx.SnapshotSurfaceRecords();

        Assert.Equal([-1, 0, 0, 0, 1], Record(gfx, 3 + 0x14));
    }

    [Fact]
    public void SavesWriteThePageSnapshotNotTheLiveTable()
    {
        GfxState gfx = Run((0x1f9, [I(0x32a9), I(0xc0), I(-1)]));
        gfx.SnapshotSurfaceRecords();                 // op 0x71 at the page boundary
        gfx.CreateSurface(0xc0);                      // e.g. SAVE.BIN's thumbnail surface
        gfx.SetSurface(0xc1, 0x337a, -1, rawTextureMode: true);

        Assert.Equal([0x32a9, 0, 0, 0, 0], Record(gfx, 0xc0));
        Assert.Equal([-1, 0, 0, 0, 0], Record(gfx, 0xc1));

        gfx.SnapshotSurfaceRecords();
        Assert.Equal([-1, 0, 0, 0, 1], Record(gfx, 0xc0));
    }

    [Fact]
    public void PersistenceFlagsReachTheSnapshotImmediatelyAndScriptEntryClearsThem()
    {
        var gfx = new GfxState();
        gfx.SetSurfacePersistenceFlags(5, 3);
        Assert.Equal([-1, 0, 1, 1, 0], Record(gfx, 5));   // native writes both tables

        gfx.ClearSurfaceReloadPolicies();
        Assert.Equal([-1, 0, 0, 0, 0], Record(gfx, 5));
    }
}
