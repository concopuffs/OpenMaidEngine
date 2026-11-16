using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Script.Abi;

/// <summary>Launcher spec §5.9 (launcher plan §5.7): the link for inspection writes nothing and says why the resolved
/// order differs from the player's.</summary>
public sealed class ModInspectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-inspect-" + Guid.NewGuid().ToString("N"));

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);

    private static ModLinkBase SyntheticBase() => new()
    {
        Profile = "himegari",
        CatalogFingerprint = "sha256:1111111111111111111111111111111111111111111111111111111111111111",
        ReadScript = _ => null,
        Table = Table,
        Contracts = Contracts,
        Intrinsics = new IntrinsicCatalog(Table, Contracts),
        InstallSelectors = new HashSet<int> { 1 },
        CatalogTitle = "TEST",
        ToolchainStamp = "test",
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static void Package(string parent, string id, string script, string extra = "")
    {
        string folder = Path.Combine(parent, id);
        Directory.CreateDirectory(Path.Combine(folder, "scripts"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 1\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n[scripts]\nnames = [\"{script}\"]\n"
            + "[game]\nprofile = \"himegari\"\ntarget = \"native\"\n" + extra);
        File.WriteAllText(Path.Combine(folder, "scripts", script + ".oms"), $"oms 1\nscript \"{script}\"\n\n    exit\n");
    }

    [Fact]
    public void InspectionLinksWithoutWritingAndSaysWhyAPackageMoved()
    {
        string profile = Path.Combine(_root, "profile");
        string mods = Path.Combine(profile, ModLaunch.ModsFolder);
        Package(mods, "alpha", "ALPHA", "[[requires]]\nid = \"beta\"\nversion = \">=1.0\"\n");
        Package(mods, "beta", "BETA");
        Package(mods, "gamma", "GAMMA");
        Package(mods, "off", "OFF");
        File.WriteAllText(Path.Combine(mods, ModLaunch.LoadOrderFile), "alpha\nbeta\ngamma\n-off\n");
        string[] before = Directory.GetFileSystemEntries(profile, "*", SearchOption.AllDirectories).Order().ToArray();

        using (ModInspection inspection = ModInspection.Inspect(SyntheticBase(), profile, [], skipFailed: false))
        {
            Assert.True(inspection.Link!.Succeeded, inspection.Link.Report.Render());
            Assert.Equal(["beta", "alpha", "gamma"], inspection.Order);
            Assert.Equal(["alpha", "beta", "gamma"], inspection.UserOrder);
            OrderMove move = Assert.Single(inspection.Moves);
            Assert.Equal("alpha", move.Id);
            Assert.Equal(["beta"], move.Overtaken);
            OrderEdge edge = Assert.Single(move.Edges);
            Assert.Equal(new OrderEdge("beta", "alpha", OrderEdgeKind.Requires, "alpha"), edge);
            Assert.Equal("alpha requires beta", edge.ToString());
            Assert.Contains(inspection.Discovery.Lines, line => line.Mods.SequenceEqual(["off"]) && line.Message.Contains("disabled"));
            // The link handed out selectors in memory only.
            Assert.NotEmpty(inspection.Link.Catalogs);
        }
        // No cache, no selectors.toml, no diagnostics.
        Assert.Equal(before, Directory.GetFileSystemEntries(profile, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Fact]
    public void AMovesEdgesFollowTheChainAndAnUnrankedPackageIsNeverOvertaken()
    {
        // As the resolver orders them: B is ready first; P waits for B; the unranked base sorts after every ranked
        // package that is ready; A waits for P and the base.
        OrderEdge bp = new("B", "P", OrderEdgeKind.Requires, "P");
        OrderEdge pa = new("P", "A", OrderEdgeKind.After, "A");
        OrderEdge baseA = new("base", "A", OrderEdgeKind.Requires, "A");
        IReadOnlyList<OrderMove> moves = StackResolver.Moves(["B", "P", "base", "A"], ["P", "A", "B"], [bp, pa, baseA]);
        Assert.Equal(["P", "A"], moves.Select(move => move.Id));
        Assert.Equal(["B"], moves[0].Overtaken);
        Assert.Equal([bp], moves[0].Edges);
        Assert.Equal(["B"], moves[1].Overtaken);
        Assert.Equal([bp, pa, baseA], moves[1].Edges);

        // The player's own order, kept: nothing moved, whatever the edges.
        Assert.Empty(StackResolver.Moves(["B", "P", "A"], ["B", "P", "A"], [bp, pa]));
        Assert.Equal("P asks to load before A", new OrderEdge("P", "A", OrderEdgeKind.Before, "P").ToString());
    }
}
