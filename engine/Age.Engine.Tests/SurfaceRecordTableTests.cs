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

    private static GfxState Run(params (int, Operand[])[] ops)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
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
