using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

/// <summary>A direct-scene run replays the system script's literal op-0x64 copies, so the scene reads the values the
/// natural boot's own SYSTEM4 writes rather than hand-entered seeds.</summary>
public class InlineArrayBootstrapTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
    private static Operand L(long index) => new(9, index);

    // Code: 0 copy G[0x100] <- blob@6; 5 exit. The blob (count 4, then the values) follows the code at offset 6,
    // where the native compiler places it after a jump.
    private static Script WithInlineBlob(Operand destination, params uint[] blob)
    {
        Script code = ScriptAssembler.Assemble(Table, "SYSTEM", new List<(int, Operand[])>
        {
            (0x64, new[] { destination, I(6) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        return new Script
        {
            Name = code.Name,
            Header = code.Header,
            Instructions = code.Instructions,
            IndexByOffset = code.IndexByOffset,
            Strings = code.Strings,
            BodyDwords = code.BodyDwords.Take(6).Concat(blob).ToArray(),
        };
    }

    [Fact]
    public void ReplayWritesWhatExecutingTheCopyWrites()
    {
        Script system = WithInlineBlob(G(0x100), 4, 0, 4, 5, unchecked((uint)-1));
        var replayed = new Dictionary<int, long>();
        var vm = new VirtualMachine(system, Table, new RecordingHost());

        Assert.Equal(1, InlineArrayBootstrap.Apply(system, replayed));
        vm.Run();

        Assert.Equal(new long[] { 0, 4, 5, -1 }, Enumerable.Range(0x100, 4).Select(a => replayed[a]).ToArray());
        Assert.Equal(Enumerable.Range(0x100, 4).Select(a => vm.Globals[a]), Enumerable.Range(0x100, 4).Select(a => replayed[a]));
    }

    [Fact]
    public void ReplaySkipsCopiesItCannotResolveStatically()
    {
        // A local destination is frame state, and a count past the body is malformed: neither seeds a global.
        var replayed = new Dictionary<int, long>();

        Assert.Equal(0, InlineArrayBootstrap.Apply(WithInlineBlob(L(0), 1, 7), replayed));
        Assert.Equal(0, InlineArrayBootstrap.Apply(WithInlineBlob(G(0x100), 9, 7), replayed));
        Assert.Empty(replayed);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void System4ReplayIsTheSfxChannelTableTheNaturalBootCopies()
    {
        // SYSTEM4@0x1ca copies [0,4,5,6,9] to G[0x6242c..]; SC0000@0xc31 preloads channel G[0x6242d] = 4, as
        // native's SFX trace shows. Native's Frida write capture misses this copy, which is why it once looked
        // engine-owned.
        var replayed = new Dictionary<int, long>();

        Assert.Equal(1, InlineArrayBootstrap.Apply(
            Sys4ScriptProvider.Load(Table).RequireByName("SYSTEM4.BIN"), replayed));

        Assert.Equal(new long[] { 0, 4, 5, 6, 9 }, Enumerable.Range(0x6242c, 5).Select(a => replayed[a]).ToArray());
        Assert.Equal(5, replayed.Count);
    }
}
