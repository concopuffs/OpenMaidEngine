using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Scripting.Lua;

/// <summary>
/// Milestone 6 of docs/superpowers/specs/2026-11-10-lua-scripting-design.md, the seeded half of gate 11 (§5.6): under
/// <c>rand-seed</c> each mod's <c>math.random</c> draws from a stream of its own, which a session's rebuild does not
/// restart and which neither shifts nor is shifted by op 0x60's sites. The driven half is the port scenario
/// tools/drive/scenarios/port/himegari-lua-random.steps; <c>math.randomseed</c>'s absence is in
/// <see cref="LuaScriptingTests"/>.
/// </summary>
public sealed class LuaRandomTests : IDisposable
{
    private const int T_IMM = 0, T_GINT = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        foreach (IDisposable item in _owned) item.Dispose();
    }

    private Scripting Native(ScriptMemory memory, LuaStackBuilder builder)
    {
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        var (stack, plan) = builder.Build();
        return new Scripting(memory, new ScriptingSetup
        {
            Runtime = runtime, Stack = stack, Plan = plan, Errors = LuaErrorPolicy.Continue, Limits = ModLimits.Default,
        });
    }

    private static LuaStackBuilder TwoMods() => new LuaStackBuilder()
        .Mod("mod.a", "-- file: init.lua\n", vm: false)
        .Mod("mod.b", "-- file: init.lua\n", vm: false);

    /// <summary>ROOT: G[0x100] = rand() % 30000; exit.</summary>
    private static Script Roll() => ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
    {
        (0x60, [new Operand(T_GINT, 0x100), new Operand(T_IMM, 30000)]),
        (0x2, []),
    }, []);

    private static VirtualMachine Run(Script script, Scripting native, ScriptMemory memory)
    {
        var vm = new VirtualMachine(script, Table, new HeadlessWaits(), memory: memory, scripting: native);
        vm.Run();
        return vm;
    }

    private static string Chunk(Scripting native, string mod, string code)
    {
        ConsoleRequest request = native.SubmitConsole(mod, code);
        native.ServiceRequests();
        Assert.True(request.Done);
        Assert.True(request.Result!.Succeeded, request.Result.Text);
        return request.Result.Text;
    }

    // math.random(0, 32767) covers one draw exactly, so it returns the draw itself.
    private const string Draw = "math.random(0, 32767)";

    private static int[] Stream(uint seed, string modId, int count)
    {
        uint state = ScriptMemory.DeterministicModInitialState(seed, modId);
        return Enumerable.Range(0, count).Select(_ => ScriptMemory.DeterministicRandomNext(ref state)).ToArray();
    }

    private static string Joined(IEnumerable<int> values) => string.Join(" ", values);

    [Fact]
    public void AModsStreamStartsFromTheSeedXorTheHashOfItsId()
    {
        // The vectors are an independent Python computation of FNV-1a 32 and MSVC rand.
        Assert.Equal(0xe40c292bu, ScriptMemory.DeterministicModInitialState(7, "a"));
        Assert.Equal(0x0428b554u, ScriptMemory.DeterministicModInitialState(7, "example.lua-random"));
        Assert.Equal(new[] { 25554, 16368, 12149 }, Stream(7, "example.lua-random", 3));
    }

    [Fact]
    public void UnderASeedEachModDrawsItsOwnStreamAndTheReportCountsItsDraws()
    {
        var memory = new ScriptMemory();
        Scripting native = Native(memory, TwoMods());
        Run(Roll(), native, memory);
        memory.SeedDeterministicRandom(7);

        Assert.Equal(Joined(Stream(7, "mod.a", 2)), Chunk(native, "mod.a", $"return {Draw} .. ' ' .. {Draw}"));
        Assert.Equal(Stream(7, "mod.b", 1)[0].ToString(), Chunk(native, "mod.b", $"return {Draw}"));
        Assert.Equal(Stream(7, "mod.a", 3)[2].ToString(), Chunk(native, "mod.a", $"return {Draw}"));
        // §5.6's sizes: math.random() takes four draws, a range of 2^40 three, one of 2^20 two.
        Chunk(native, "mod.b", "math.random() math.random(1, 2^40) math.random(1, 2^20)");

        DeterministicRandomSummary drawn = memory.SeedDeterministicRandom(null);
        Assert.Equal(7u, drawn.Seed);
        Assert.Equal(0, drawn.Draws);   // the sites' total counts no mod's draw
        Assert.Equal(new[] { ("mod.a", 3), ("mod.b", 10) }, drawn.ModList.Select(mod => (mod.ModId, mod.Draws)));
    }

    [Fact]
    public void TheSameSeedGivesTheSameDrawsInAPairsLoop()
    {
        const string Rolls = """
            local rolls, keys = {}, {}
            for name in pairs({ sword = 1, shield = 2, ring = 3, cape = 4, boots = 5, helm = 6, lance = 7, orb = 8 }) do
                rolls[name] = math.random(100)
                keys[#keys + 1] = name
            end
            table.sort(keys)
            local out = {}
            for _, name in ipairs(keys) do out[#out + 1] = name .. "=" .. rolls[name] end
            return table.concat(out, " ") .. " f=" .. math.random()
            """;
        string Rolled()
        {
            var memory = new ScriptMemory();
            Scripting native = Native(memory, TwoMods());
            Run(Roll(), native, memory);
            memory.SeedDeterministicRandom(11);
            return Chunk(native, "mod.a", Rolls);
        }

        string first = Rolled();
        Assert.Equal(first, Rolled());
        // Each key's draw is the stream's draw at its place in the iteration: eight draws of 1 to 100, then four more.
        var expected = Stream(11, "mod.a", 8).Select(value => value % 100 + 1).OrderBy(value => value);
        var rolled = first.Split(' ').SkipLast(1).Select(pair => int.Parse(pair.Split('=')[1])).OrderBy(value => value);
        Assert.Equal(expected, rolled);
    }

    [Fact]
    public void AModsDrawsNeitherShiftNorAreShiftedByTheSitesDraws()
    {
        Script root = Roll();
        var memory = new ScriptMemory();
        Scripting native = Native(memory, new LuaStackBuilder()
            .Mod("mod.a", "-- file: hooks.lua\nfunction before_roll(ctx) first = math.random(0, 32767) end")
            .Point(root, 0, "mod.a", "hooks.lua:before_roll"));
        memory.SeedDeterministicRandom(5);

        VirtualMachine vm = Run(root, native, memory);

        uint site = ScriptMemory.DeterministicRandomInitialState(5, root.PackedId, root.Instructions[0].Offset);
        Assert.Equal(ScriptMemory.DeterministicRandomNext(ref site) % 30000, vm.Globals[0x100]);
        Assert.Equal(Stream(5, "mod.a", 1)[0].ToString(), Chunk(native, "mod.a", "return first"));
        DeterministicRandomSummary drawn = memory.SeedDeterministicRandom(null);
        Assert.Equal(1, drawn.Draws);
        Assert.Equal(new[] { ("mod.a", 1) }, drawn.ModList.Select(mod => (mod.ModId, mod.Draws)));
    }

    [Fact]
    public void ASessionsRebuildDoesNotRestartAStreamButTheNextSeedDoes()
    {
        var memory = new ScriptMemory();
        Scripting native = Native(memory, TwoMods());
        Run(Roll(), native, memory);
        memory.SeedDeterministicRandom(3);
        int[] stream = Stream(3, "mod.a", 3);

        Assert.Equal(stream[0].ToString(), Chunk(native, "mod.a", $"return {Draw}"));
        native.LoadedDataOnly();   // a data-only load ends the session; the next boundary builds the next one
        Assert.True(native.BoundaryPending);
        Assert.Null(native.ServiceBoundary());
        Assert.Equal(stream[1].ToString(), Chunk(native, "mod.a", $"return {Draw}"));

        memory.SeedDeterministicRandom(3);
        Assert.Equal(stream[0].ToString(), Chunk(native, "mod.a", $"return {Draw}"));
    }

    [Fact]
    public void WithoutASeedAModDrawsFromTheSystemsGenerator()
    {
        string Rolled()
        {
            var memory = new ScriptMemory();
            Scripting native = Native(memory, TwoMods());
            Run(Roll(), native, memory);
            string drawn = Chunk(native, "mod.a", string.Join(" .. ' ' .. ", Enumerable.Repeat(Draw, 8)).Insert(0, "return "));
            Assert.Empty(memory.SeedDeterministicRandom(null).ModList);
            return drawn;
        }

        // Eight draws of 15 bits each: two runs alike by chance once in 2^120.
        Assert.NotEqual(Rolled(), Rolled());
    }
}
