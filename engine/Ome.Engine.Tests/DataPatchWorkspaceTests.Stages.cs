using Ome.Engine.Hosting;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Linking;
using Ome.Mods.Packages;

/// <summary>
/// Milestone 2 of docs/superpowers/specs/2026-11-17-data-patches-design.md (plan §5.4, task 6), on the installed games:
/// a stage's part of a data file, delivered to the base mods' <c>tables.lua:apply_stage</c> at
/// <c>&lt;game&gt;.stage.loaded</c> while the installed FIELD runs headless from a booted session at a stage's start;
/// and a file with a stage part where that event is not raised. The stage parts' problems at boot are rows of
/// <see cref="FileProblems"/>.
/// </summary>
public sealed partial class DataPatchWorkspaceTests
{
    /// <summary>The game as <see cref="Game"/> gives it, or Kamidori's translated install.</summary>
    private static GateGame StageGame(string profile, bool translated)
    {
        GateGame game = Game(profile);
        if (!translated) return game;
        var (linkBase, scripts) = Kamidori(translated: true);
        return game with { Base = linkBase, Scripts = scripts };
    }

    /// <summary>The game's INIT2, then its FIELD at <paramref name="stage"/>'s start (the continue mode 0), headless,
    /// each run with a scripting native of its own from <paramref name="linked"/>, or with no mod. FIELD runs the stage's
    /// loaders, raises <c>stage.loaded</c> and places the units; it does not leave the field by itself, so it ends at
    /// the step limit.</summary>
    private GameSession AtStageStart(GateGame game, ModLinkResult? linked, int stage)
    {
        GameSession session = AtStageStart(game, linked, stage, out string? halt);
        Assert.Equal("STEP-LIMIT", halt);
        return session;
    }

    /// <param name="onFloor">Kamidori: start as if the floor had been entered already (<c>G[0x428c]</c> set, unnamed in
    /// the registry), so FIELD skips the arrival scenes it would leave for first (<c>FIELD@0x1ef</c> to <c>0x304</c>),
    /// which a booted session's story state reaches at some floors.</param>
    private GameSession AtStageStart(GateGame game, ModLinkResult? linked, int stage, out string? halt, bool onFloor = false)
    {
        GameSession session = game.Boot(linked is null ? null : Native(linked, LuaErrorPolicy.Halt));
        session.Seed(Cell(game.Profile, "current_stage_id", 0), stage);
        session.Seed(Cell(game.Profile, "field_map_continue_mode", 0), 0);
        if (onFloor) session.Seed(0x428c, 1);
        SceneResult field = session.RunScene(game.Scripts.RequireByName("FIELD.BIN"), game.Table, new HeadlessWaits(),
                                             provider: game.Scripts, build: game.Build,
                                             scripting: linked is null ? null : Native(linked, LuaErrorPolicy.Halt));
        halt = field.Halt;
        Assert.Equal(stage, session.Globals.GetValueOrDefault(Cell(game.Profile, "current_stage_id", 0)));
        return session;
    }

    /// <summary>Each cell of the stage's enemy table, by the shipped layout: every <c>stage_enemy_*</c> array.</summary>
    private static int[] StageEnemyCells(string profile)
    {
        using ModPackage baseMod = ShippedPackages.Open($"{profile}-base")!;
        return baseMod.BaseLayout!.Globals.Where(global => global.Name.StartsWith("stage_enemy_", StringComparison.Ordinal))
            .SelectMany(global => Enumerable.Range(global.Address, 30 * global.RowWidth)).ToArray();
    }

    /// <summary>The units FIELD has placed on the field. Himegari: every nonzero <c>entity_unit_definition_ids</c> cell.
    /// Kamidori: <c>battle_unit_unit_ids</c> of each enemy battle slot (10 to 49) whose state flags (<c>G[0x53c16]</c>,
    /// unnamed in the registry) have bit 0, which FIELD sets when it places a unit there (<c>FIELD@0xba1</c>); a slot it
    /// has not placed this floor may keep a unit from an earlier one.</summary>
    private static long[] PlacedEnemies(string profile, GameSession session)
    {
        if (profile == "himegari")
        {
            int entities = Cell(profile, "entity_unit_definition_ids", 0);
            return Enumerable.Range(entities, 50).Select(cell => session.Globals.GetValueOrDefault(cell))
                .Where(unit => unit != 0).ToArray();
        }
        const int BattleSlotStateFlags = 0x53c16;
        int units = Cell(profile, "battle_unit_unit_ids", 0);
        return Enumerable.Range(10, 40).Where(slot => (session.Globals.GetValueOrDefault(BattleSlotStateFlags + slot) & 1) != 0)
            .Select(slot => session.Globals.GetValueOrDefault(units + slot)).ToArray();
    }

    /// <summary>A stage and its slots, and another stage, for each game: the slot the file edits, the slot it edits at
    /// the other stage, and those slots' units as the stage's loaders write them.</summary>
    public static TheoryData<string, bool, int, int, int, long, long> Stages => new()
    {
        // Himegari's stage 1: slot 1 is the boss (unit 205), slots 2 and 3 are unit 200.
        { "himegari", false, 1, 2, 3, 200, 200 },
        // Kamidori's stage 51 (開けた涼風西湖畔): slots 2 and 3 are unit 402; on both installs.
        { "kamidori", false, 51, 2, 3, 402, 402 },
        { "kamidori", true, 51, 2, 3, 402, 402 },
    };

    [Theory]
    [Trait("Category", "Workspace")]
    [MemberData(nameof(Stages))]
    public void AStagePartIsAppliedAtTheInstalledFieldsLoadedEventAndAnotherStagesPartIsNot(
        string profile, bool translated, int stage, int slot, int otherSlot, long own, long otherOwn)
    {
        GateGame game = StageGame(profile, translated);
        int unit = Cell(profile, "stage_enemy_unit_id", slot), level = Cell(profile, "stage_enemy_min_level", slot);
        int other = Cell(profile, "stage_enemy_unit_id", otherSlot);
        GameSession plain = AtStageStart(game, null, stage);
        Assert.Equal((own, otherOwn), (plain.Globals.GetValueOrDefault(unit), plain.Globals.GetValueOrDefault(other)));

        // The file edits the stage's slot and, at the next stage, another slot.
        ModLinkResult linked = LinkedInOrder(game.Base, DataMod(profile, "test.stage", "stage.toml", $"""
            [stage.{stage}.enemy.{slot}]
            unit_id = 810
            min_level = 7

            [stage.{stage + 1}.enemy.{otherSlot}]
            unit_id = 834
            """));
        GameSession edited = AtStageStart(game, linked, stage);
        int[] cells = StageEnemyCells(profile);
        var before = cells.ToDictionary(cell => cell, cell => plain.Globals.GetValueOrDefault(cell));
        var after = cells.ToDictionary(cell => cell, cell => edited.Globals.GetValueOrDefault(cell));
        // The stage's part, and nothing of the other stage's: its slot keeps the stage's own unit.
        Assert.Equal(new Dictionary<int, long> { [unit] = 810, [level] = 7 }, Changed(before, after));
        Assert.Equal(otherOwn, edited.Globals.GetValueOrDefault(other));
        // FIELD placed the slot as the file left it. Kamidori's slot 2 of stage 51 is in a spawner's pool, which places
        // nothing at the floor's start; the example's test checks the placement on its slot 1.
        Assert.DoesNotContain(810, PlacedEnemies(profile, plain));
        if (profile == "himegari") Assert.Contains(810, PlacedEnemies(profile, edited));
        else Assert.DoesNotContain(810, PlacedEnemies(profile, edited));
    }

    [Theory]
    [Trait("Category", "Workspace")]
    [InlineData("himegari", "example.stage-enemies", 1, 2, 810L, 3, 200L, 2, 834L, 202L,
                "stage.2.enemy.3.unit: stage_enemy has no field unit")]
    [InlineData("kamidori", "example.kamidori-stage-enemies", 51, 1, 761L, 3, 402L, 52, 762L, 631L,
                "stage_enemy: stage_enemy is reached through its scope: write stage.<id>.enemy")]
    public void TheStageExamplesEditTheirSlotAndTheirMistakeTwinsAreReportedAtBoot(
        string profile, string example, int stage, int slot, long unit, int otherSlot, long otherOwn,
        int secondStage, long secondUnit, long secondOwn, string mistake)
    {
        // The example mods of oms/examples/mods/ that gate 8 drives (plan §5.3), linked and run headless here.
        GateGame game = Game(profile);
        string examples = Path.Combine(Paths.Repo, "oms", "examples", "mods");
        ModPackage Open(string id)
        {
            ModPackage mod = ModPackage.Open(Path.Combine(examples, id));
            _opened.Add(mod);
            return mod;
        }
        GameSession edited = AtStageStart(game, LinkedInOrder(game.Base, Open(example)), stage);
        Assert.Equal(unit, edited.Globals.GetValueOrDefault(Cell(profile, "stage_enemy_unit_id", slot)));
        Assert.Equal(otherOwn, edited.Globals.GetValueOrDefault(Cell(profile, "stage_enemy_unit_id", otherSlot)));
        Assert.Contains(unit, PlacedEnemies(profile, edited));

        // The example's second part, at its own stage: the other slot (3) holds the edit there, in place of the stage's
        // own unit, and FIELD places it at the stage's start (a slot whose random_selection_weight is 0).
        // In a booted session's story state Kamidori's stage 52 has scenes: an arrival scene, which the run skips by
        // starting on the floor, and one after the placement, for which FIELD leaves (G[0] 1, the mode 2) and the run ends.
        bool onFloor = profile == "kamidori";
        void Ended(GameSession session, string? halt)
        {
            if (onFloor) Assert.Equal(("exit", 1L), (halt, session.Globals.GetValueOrDefault(0)));
            else Assert.Equal("STEP-LIMIT", halt);
        }
        GameSession plainSecond = AtStageStart(game, null, secondStage, out string? plainHalt, onFloor);
        Ended(plainSecond, plainHalt);
        Assert.Equal(secondOwn, plainSecond.Globals.GetValueOrDefault(Cell(profile, "stage_enemy_unit_id", otherSlot)));
        Assert.Equal(0, plainSecond.Globals.GetValueOrDefault(Cell(profile, "stage_enemy_random_selection_weight", otherSlot)));
        GameSession second = AtStageStart(game, LinkedInOrder(game.Base, Open(example)), secondStage, out string? halt, onFloor);
        Ended(second, halt);
        Assert.Equal(secondUnit, second.Globals.GetValueOrDefault(Cell(profile, "stage_enemy_unit_id", otherSlot)));
        Assert.Contains(secondUnit, PlacedEnemies(profile, second));
        Assert.DoesNotContain(secondUnit, PlacedEnemies(profile, plainSecond));

        ModLinkResult twin = LinkedInOrder(game.Base, Open($"{example}-mistake"));
        Scripting native = Native(twin, LuaErrorPolicy.Continue);
        game.Boot(native);
        string logged = Assert.Single(Logged(native, $"{example}-mistake"));
        Assert.Contains($"{example}-mistake data/stages.toml: 1 problem, so nothing of it is applied: {mistake}\n", logged,
                        StringComparison.Ordinal);
        // The port's status line shows this notice: the file once, then the problem's path.
        Assert.Equal($"Lua: {example}-mistake data/stages.toml failed: 1 problem, so nothing of it is applied: {mistake}",
                     native.LatestNotice);
    }

    /// <summary><paramref name="game"/>'s link base with another FIELD revision, whose bytes no point names, so the
    /// stage events are not raised in this install.</summary>
    private static ModLinkBase WithoutStageEvents(GateGame game)
    {
        Func<string, byte[]?> read = game.Base.ReadScript;
        return game.Base with
        {
            ReadScript = name =>
            {
                if (read(name) is not { } bytes || !name.Equals("FIELD.BIN", StringComparison.OrdinalIgnoreCase)) return read(name);
                byte[] other = (byte[])bytes.Clone();
                other[^1] ^= 1;
                return other;
            },
        };
    }

    [Theory]
    [Trait("Category", "Workspace")]
    [InlineData("himegari")]
    [InlineData("kamidori")]
    public void AFileWithAStagePartIsRefusedWholeWhereStageLoadedIsNotRaisedAndABootFileIsApplied(string profile)
    {
        GateGame game = Game(profile);
        GameSession plain = game.Boot(null);
        ModLinkResult linked = LinkedInOrder(WithoutStageEvents(game),
            DataMod(profile, "test.both", "both.toml", $"[unit.{game.Unit}]\nstats.max_hp = 150\n\n[stage.1.enemy.2]\nunit_id = 810\n"),
            DataMod(profile, "test.boot", "boot.toml", $"[unit.{game.Unit}]\nexperience_reward = 999\n"));
        string report = linked.Report.Render();
        Assert.Contains($"kind {profile}.tables ({profile}-base), at {profile}.data.layered; not raised in this install: "
                        + $"{profile}.stage.loaded", report, StringComparison.Ordinal);

        Scripting native = Native(linked, LuaErrorPolicy.Continue);
        GameSession booted = game.Boot(native);
        // The boot file's value alone: nothing of the file with a stage part.
        Assert.Equal(new Dictionary<int, long> { [Cell(profile, "unit_experience_reward", game.Unit)] = 999 },
                     Changed(plain.Globals, booted.Globals));
        string logged = Assert.Single(Logged(native, "test.both"));
        Assert.StartsWith($"data/both.toml ({profile}-base tables.lua:apply_boot) failed at {profile}.data.layered at "
                          + $"{game.Layered}: test.both data/both.toml: 1 problem, so nothing of it is applied: "
                          + $"stage.1.enemy: stage_enemy is applied at {profile}.stage.loaded, which this install does "
                          + "not raise\nstack traceback:", logged);
    }
}
