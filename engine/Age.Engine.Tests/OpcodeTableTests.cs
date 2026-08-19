using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class OpcodeTableTests
{
    [Fact]
    public void LoadsFromCallerOwnedStreamWithoutClosingIt()
    {
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """
            {"opcodes":[{"op":"0x55","label":"mov","argc":2}]}
            """));

        OpcodeTable table = OpcodeTableJson.Load(source);

        Assert.True(source.CanRead);
        Assert.Equal("mov", table.Label(0x55));
        Assert.Equal(2, table.Argc(0x55));
    }

    [Fact]
    public void LoadsCompleteAgeCatalog()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        Assert.Equal(548, t.Count); // revision-unscoped upstream framing; selected snapshots are narrower
        Assert.True(t.TryGet(0x55, out var label, out var argc));
        Assert.Equal("mov", label);
        Assert.Equal(2, argc);
        Assert.Equal("u0041BEB0", t.Label(0x90));
        Assert.Equal(7, t.Argc(0x90));
        Assert.Equal(2, t.Argc(0x19f));
        Assert.Equal(2, t.Argc(0x1be)); // Kamidori snapshot member and current probe blocker
        Assert.Equal(-1, t.Argc(0x9999)); // absent -> -1
    }

    [Fact]
    public void CatalogOnlyOpcodeDoesNotTruncateFollowingInstructions()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(t, "CROSS_GAME_STUB", new List<(int, Operand[])>
        {
            (0x1be, new[] { new Operand(0, 11), new Operand(0, 22) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());

        Assert.Equal(2, script.Instructions.Count);
        Assert.Equal(0x1be, script.Instructions[0].Opcode);
        Assert.Equal(2, script.Instructions[0].Args.Count);
        Assert.Equal(0x2, script.Instructions[1].Opcode);
    }

    [Fact]
    public void ExactRevisionSnapshotsBindSharedImplementationsWithoutSharingSemanticScope()
    {
        OpcodeTable sys4422 = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
        OpcodeTable sys4433 = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");

        Assert.Equal(248, sys4422.Count);
        Assert.Equal(269, sys4433.Count);
        Assert.False(sys4422.TryGetDefinition(0x1be, out _));
        Assert.True(sys4433.TryGetDefinition(0x1be, out _));
        Assert.Equal(["SYS4422"], sys4422.ResolvedLayers);
        Assert.Equal(["SYS4433"], sys4433.ResolvedLayers);

        Assert.True(OpcodeRuntimeCoverage.TryResolve(sys4422, 0xba, out var himegari));
        Assert.True(OpcodeRuntimeCoverage.TryResolve(sys4433, 0xba, out var kamidori));
        Assert.Equal(himegari.ImplementationId, kamidori.ImplementationId);
        Assert.Equal("SYS4422", himegari.BindingProvenance.LayerId);
        Assert.Equal("SYS4433", kamidori.BindingProvenance.LayerId);
        Assert.Equal("evidence-confirmed", himegari.SemanticEvidenceStatus);
        Assert.Equal("compatibility-reuse-unconfirmed-for-revision", kamidori.SemanticEvidenceStatus);
    }

    [Fact]
    public void AbiLayerCompositionSupportsAddReplaceAndRemoveWithEffectiveProvenance()
    {
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """
            {
              "opcodes": [
                {"op":"0x1","label":"one","argc":0},
                {"op":"0x2","label":"two","argc":1},
                {"op":"0x3","label":"three","argc":0}
              ],
              "abi_layers": [
                {
                  "id":"BASE","kind":"complete-snapshot","composes":[],
                  "evidence":{"method":"fixture","artifact":"base.bin","scope":"BASE"},
                  "members":["0x1","0x2"],"contracts":[],"removes":[]
                },
                {
                  "id":"CHILD","kind":"revision-layer","composes":["BASE"],
                  "evidence":{"method":"fixture","artifact":"child.bin","scope":"CHILD"},
                  "members":["0x3"],
                  "contracts":[{
                    "op":"0x2","label":"two-new","argc":2,
                    "handler_implementation_id":"child-handler/two"
                  }],
                  "removes":["0x1"]
                }
              ]
            }
            """));

        OpcodeTable table = OpcodeTableJson.Load(source, "CHILD");

        Assert.Equal(["BASE", "CHILD"], table.ResolvedLayers);
        Assert.Equal(2, table.Count);
        Assert.False(table.TryGetDefinition(0x1, out _));
        Assert.Equal("two-new", table.Label(0x2));
        Assert.Equal(2, table.Argc(0x2));
        Assert.Equal("CHILD", table.Entries.Single(entry => entry.Opcode == 0x2)
            .ContractProvenance.LayerId);
        Assert.Equal("CHILD", table.Entries.Single(entry => entry.Opcode == 0x3)
            .ContractProvenance.LayerId);
        Assert.True(OpcodeRuntimeCoverage.TryResolve(table, 0x2, out var handler));
        Assert.Equal("child-handler/two", handler.ImplementationId);
        Assert.Equal("CHILD", handler.BindingProvenance.LayerId);
        Assert.False(handler.IsExecutable);
    }
}
