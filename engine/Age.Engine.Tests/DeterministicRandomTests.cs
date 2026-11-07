using System;
using System.Collections.Generic;
using System.Linq;
using Age.Engine.Frontend.Control;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

/// <summary>Op 0x60's deterministic mode (drive step rand-seed; docs/engine-re.md, "Op 0x60 randomness"). The vectors
/// are the ones tools/drive/native-agent.js computes, checked against an independent Python implementation.</summary>
public class DeterministicRandomTests
{
    private const int Imm = 0, GlobalInt = 3;

    [Fact]
    public void TheStreamIsMsvcRand()
    {
        uint state = 1;   // srand(1)
        int[] drawn = Enumerable.Range(0, 5).Select(_ => VirtualMachine.DeterministicRandomNext(ref state)).ToArray();
        Assert.Equal(new[] { 41, 18467, 6334, 26500, 19169 }, drawn);
    }

    [Fact]
    public void EachCallSiteHasItsOwnStreamFromTheSeed()
    {
        uint state = VirtualMachine.DeterministicRandomInitialState(2, 0x123, 0x24e);
        Assert.Equal(0x7e7bf277u, state);
        int[] drawn = Enumerable.Range(0, 3).Select(_ => VirtualMachine.DeterministicRandomNext(ref state)).ToArray();
        Assert.Equal(new[] { 18559, 10023, 18824 }, drawn);

        uint wide = VirtualMachine.DeterministicRandomInitialState(0xffffffff, 0x01000a3f, 0x940a);
        Assert.Equal(1265, VirtualMachine.DeterministicRandomNext(ref wide));
    }

    [Fact]
    public void ASeededRunDrawsEachSiteFromItsStreamAndReportsTheDraws()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        Script script = ScriptAssembler.Assemble(table, "RAND", new List<(int, Operand[])>
        {
            (0x60, new[] { new Operand(GlobalInt, 0x100), new Operand(Imm, 30000) }),
            (0x60, new[] { new Operand(GlobalInt, 0x101), new Operand(Imm, 7) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        Assert.Null(vm.SeedDeterministicRandom(9).Seed);
        vm.Run();
        DeterministicRandomSummary drawn = vm.SeedDeterministicRandom(null);

        int First(int offset)
        {
            uint state = VirtualMachine.DeterministicRandomInitialState(9, script.PackedId, offset);
            return VirtualMachine.DeterministicRandomNext(ref state);
        }
        Assert.Equal(First(script.Instructions[0].Offset) % 30000, vm.Globals[0x100]);
        Assert.Equal(First(script.Instructions[1].Offset) % 7, vm.Globals[0x101]);
        Assert.Equal(9u, drawn.Seed);
        Assert.Equal(2, drawn.Draws);
        Assert.Equal(new[] { (script.Instructions[0].Offset, 1), (script.Instructions[1].Offset, 1) },
                     drawn.Sites.Select(site => (site.Offset, site.Draws)));
        Assert.Equal(0, vm.SeedDeterministicRandom(null).Draws);
    }

    [Fact]
    public void ADriveReportNamesSitesAsFramesAre()
    {
        var summary = new DeterministicRandomSummary(2, 17, new[]
        {
            new DeterministicRandomSite("FIELD.BIN", 0x01000001, 0x940a, 12),
            new DeterministicRandomSite("mvrtn", 0x01000002, 0x24e, 5),
        });

        var json = DriveRandomReport.ToJson(null, summary);

        Assert.Null(json["seed"]);
        Assert.Equal(2u, (uint)json["previous_seed"]!);
        Assert.Equal(17, (int)json["draws"]!);
        Assert.Equal(12, (int)json["sites"]!["FIELD@0x940a"]!);
        Assert.Equal(5, (int)json["sites"]!["MVRTN@0x24e"]!);
    }
}
