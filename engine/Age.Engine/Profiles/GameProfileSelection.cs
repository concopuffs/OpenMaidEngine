using System.Collections.Immutable;

namespace Age.Engine.Profiles;

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
        : !IdentityMatched ? "catalog/profile identity mismatch"
        : Profile.Persistence.ReadOnlyReason ?? "profile persistence contract is not validated";

    public string SourceName => Source switch
    {
        GameProfileSelectionSource.Automatic => "automatic",
        GameProfileSelectionSource.Explicit => "explicit",
        _ => throw new InvalidOperationException($"unknown profile selection source: {Source}"),
    };

    public string? MismatchDiagnostic => IdentityMatched ? null
        : $"explicit profile mismatch: requested={Profile.Id}; "
          + $"expected={string.Join(" or ", Profile.CatalogIdentities.Select(value => value.Display))}; "
          + $"detected={DetectedIdentity.Display}; continuing with requested profile read-only";
}

public static class GameProfileSelection
{
    public const string ProfileOptionName = "--profile";
    public const string ProbeOptionName = "--probe";

    public static SelectedGameProfile Resolve(
        IReadOnlyList<string> arguments,
        GameCatalogIdentity detectedIdentity,
        GameProfileRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(detectedIdentity);
        registry ??= GameProfileRegistry.BuiltIn;

        string? explicitId = null;
        bool probeMode = false;
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == ProbeOptionName)
            {
                probeMode = true;
                continue;
            }
            if (arguments[index] != ProfileOptionName) continue;
            if (index + 1 >= arguments.Count
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{ProfileOptionName} requires a profile id");
            explicitId = arguments[++index];
        }

        if (explicitId != null)
        {
            GameProfileManifest profile = registry.Find(explicitId)
                ?? throw new ArgumentException(
                    $"unknown profile '{explicitId}'; available profiles: "
                    + string.Join(", ", registry.Profiles.Select(value => value.Id)));
            bool matched = profile.Matches(detectedIdentity);
            return new SelectedGameProfile(
                profile, detectedIdentity, GameProfileSelectionSource.Explicit, matched, probeMode);
        }

        if (probeMode)
            throw new ArgumentException(
                $"{ProbeOptionName} requires {ProfileOptionName} <id> so the runtime knows which ABI to probe");

        ImmutableArray<GameProfileManifest> matches = registry.Profiles
            .Where(profile => profile.Matches(detectedIdentity)).ToImmutableArray();
        if (matches.Length == 1)
            return new SelectedGameProfile(
                matches[0], detectedIdentity, GameProfileSelectionSource.Automatic,
                IdentityMatched: true, ProbeMode: false);
        if (matches.Length == 0)
            throw new ArgumentException(
                $"no profile matches {detectedIdentity.Display}; "
                + $"pass {ProfileOptionName} <id> to choose a profile explicitly"
                + $" (add {ProbeOptionName} for a read-only diagnostic run)");
        throw new ArgumentException(
            $"multiple profiles match {detectedIdentity.Display}: "
            + string.Join(", ", matches.Select(profile => profile.Id))
            + $"; pass {ProfileOptionName} <id>");
    }
}
