using System.Collections.Immutable;

namespace Ome.Engine.Profiles;

public enum GameProfileSelectionSource
{
    Automatic,
    Explicit,
}

/// <summary>The immutable profile decision shared by every service created after startup discovery.</summary>
public sealed record SelectedGameProfile(
    GameProfileManifest Profile,
    GameCatalogIdentity DetectedIdentity,
    GameProfileSelectionSource Source,
    bool IdentityMatched,
    bool ProbeMode)
{
    public bool PersistenceWritesEnabled
        => Profile.Persistence.WritesEnabled && IdentityMatched && !ProbeMode;

    public bool IsReadOnly => !PersistenceWritesEnabled;

    public string? ReadOnlyReason => !IsReadOnly ? null
        : ProbeMode ? "probe mode"
        : !IdentityMatched ? "catalog/game identity mismatch"
        : Profile.Persistence.ReadOnlyReason ?? "the game's persistence contract is not validated";

    public string SourceName => Source switch
    {
        GameProfileSelectionSource.Automatic => "automatic",
        GameProfileSelectionSource.Explicit => "explicit",
        _ => throw new InvalidOperationException($"unknown profile selection source: {Source}"),
    };

    public string? MismatchDiagnostic => IdentityMatched ? null
        : $"explicit game mismatch: requested={Profile.Id}; "
          + $"expected={string.Join(" or ", Profile.CatalogIdentities.Select(value => value.Display))}; "
          + $"detected={DetectedIdentity.Display}; continuing with the requested game read-only";
}

public static class GameProfileSelection
{
    /// <summary>The option that names the game (launcher spec §5.8).</summary>
    public const string GameOptionName = "--game";
    /// <summary>The old spelling of <see cref="GameOptionName"/>: the same option, accepted without a warning.</summary>
    public const string ProfileOptionName = "--profile";
    public const string ProbeOptionName = "--probe";

    public static bool IsGameOption(string argument)
        => argument is GameOptionName or ProfileOptionName;

    /// <summary>The game id the arguments ask for: the value of the last <c>--game</c> or <c>--profile</c>, which
    /// are one option, or null when neither is given. Every reader of the option goes through this, so the host and
    /// <c>Ome.Cli</c> cannot disagree about which value wins.</summary>
    public static string? RequestedGameId(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? requested = null;
        for (int index = 0; index < arguments.Count; index++)
        {
            if (!IsGameOption(arguments[index])) continue;
            if (index + 1 >= arguments.Count
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{arguments[index]} requires a game id");
            requested = arguments[++index];
        }
        return requested;
    }

    public static SelectedGameProfile Resolve(
        IReadOnlyList<string> arguments,
        GameCatalogIdentity detectedIdentity,
        GameProfileRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(detectedIdentity);
        registry ??= GameProfileRegistry.BuiltIn;

        string? explicitId = RequestedGameId(arguments);
        bool probeMode = arguments.Contains(ProbeOptionName);

        if (explicitId != null)
        {
            GameProfileManifest profile = registry.Find(explicitId)
                ?? throw new ArgumentException(
                    $"unknown game '{explicitId}'; available games: "
                    + string.Join(", ", registry.Profiles.Select(value => value.Id)));
            bool matched = profile.Matches(detectedIdentity);
            return new SelectedGameProfile(
                profile, detectedIdentity, GameProfileSelectionSource.Explicit, matched, probeMode);
        }

        if (probeMode)
            throw new ArgumentException(
                $"{ProbeOptionName} requires {GameOptionName} <id> so the runtime knows which ABI to probe");

        ImmutableArray<GameProfileManifest> matches = registry.Profiles
            .Where(profile => profile.Matches(detectedIdentity)).ToImmutableArray();
        if (matches.Length == 1)
            return new SelectedGameProfile(
                matches[0], detectedIdentity, GameProfileSelectionSource.Automatic,
                IdentityMatched: true, ProbeMode: false);
        if (matches.Length == 0)
            throw new ArgumentException(
                $"no game matches {detectedIdentity.Display}; "
                + $"pass {GameOptionName} <id> to choose a game explicitly"
                + $" (add {ProbeOptionName} for a read-only diagnostic run)");
        throw new ArgumentException(
            $"multiple games match {detectedIdentity.Display}: "
            + string.Join(", ", matches.Select(profile => profile.Id))
            + $"; pass {GameOptionName} <id>");
    }
}
