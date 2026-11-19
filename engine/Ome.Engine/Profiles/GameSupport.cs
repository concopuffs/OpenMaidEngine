namespace Ome.Engine.Profiles;

/// <summary>How well the port runs a game (docs/adding-a-game.md §1, the play track). A profile exists from
/// <see cref="Recognized"/> on; P0, a reference install, has none.</summary>
public enum PlayLevel
{
    /// <summary>P1: the engine names the install and reads all of it. No script runs, and nothing is saved.</summary>
    Recognized = 1,
    /// <summary>P2: the game goes by its own boot route to its first ADV page, with no opcode skipped.</summary>
    Boots = 2,
    /// <summary>P3: every opcode its scripts use has a handler, its engine build's rules are declared, and it
    /// saves.</summary>
    Complete = 3,
    /// <summary>P4: it has been driven to its ending on native AGE and on the port alike.</summary>
    Verified = 4,
}

/// <summary>What a modder gets for a game (docs/adding-a-game.md §1, the modding track).</summary>
public enum ModdingLevel
{
    None = 0,
    /// <summary>M1: script mods and text packages work.</summary>
    Scripts = 1,
    /// <summary>M2: the game's data tables are decoded, documented and named.</summary>
    Data = 2,
    /// <summary>M3: its base mod ships, so Lua mods and data files work.</summary>
    BaseMod = 3,
}

/// <summary>
/// A game's two support levels, the manifest's <c>support</c> entry (<c>{ "play": "P3", "modding": "M3" }</c>). The
/// manifest is their one home: the generated games page shows them (tools/modding_games_build.py), and the tests
/// that hold a complete game to its promises read them.
/// </summary>
public sealed record GameSupport(PlayLevel Play, ModdingLevel Modding)
{
    /// <summary>P3 and M3: what "supported" means.</summary>
    public static GameSupport Supported { get; } = new(PlayLevel.Complete, ModdingLevel.BaseMod);

    /// <summary>Every opcode the game's scripts use has a handler, and it saves (P3 or above).</summary>
    public bool IsPlayable => Play >= PlayLevel.Complete;

    public string PlayId => $"P{(int)Play}";
    public string ModdingId => Modding == ModdingLevel.None ? "none" : $"M{(int)Modding}";

    public static GameSupport Parse(string? play, string? modding)
    {
        PlayLevel playLevel = play switch
        {
            "P1" => PlayLevel.Recognized,
            "P2" => PlayLevel.Boots,
            "P3" => PlayLevel.Complete,
            "P4" => PlayLevel.Verified,
            _ => throw new InvalidOperationException($"support.play is '{play}', not P1, P2, P3 or P4"),
        };
        ModdingLevel moddingLevel = modding switch
        {
            "none" => ModdingLevel.None,
            "M1" => ModdingLevel.Scripts,
            "M2" => ModdingLevel.Data,
            "M3" => ModdingLevel.BaseMod,
            _ => throw new InvalidOperationException($"support.modding is '{modding}', not none, M1, M2 or M3"),
        };
        return new GameSupport(playLevel, moddingLevel);
    }
}
