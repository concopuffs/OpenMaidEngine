namespace Age.Engine.Model;

public enum AudioMixerCategory
{
    Master = 0,
    Music = 1,
    SoundEffect = 2,
    Voice = 3,
    Movie = 4,
}

public sealed record AudioMixerSettingsSnapshot(int[] Volumes, bool[] Routes);

/// <summary>
/// Profile-lifetime projection of AGE's sound:* settings registry. Volume -1 is the native
/// unconfigured sentinel; nonnegative values are basis points. Route zero is unused because master
/// has no independent enable switch.
/// </summary>
public sealed class AudioMixerSettings
{
    public const int CategoryCount = 5;
    public const int UnconfiguredVolume = -1;
    public const int MaximumVolume = 10_000;

    private readonly object _lock = new();
    private readonly int[] _volumes =
    [
        UnconfiguredVolume, UnconfiguredVolume, UnconfiguredVolume,
        UnconfiguredVolume, UnconfiguredVolume,
    ];
    private readonly bool[] _routes = [false, true, true, true, true];

    public event Action<AudioMixerSettingsSnapshot>? Changed;

    public bool TryGetVolume(int category, out int basisPoints)
    {
        lock (_lock)
        {
            if ((uint)category >= CategoryCount)
            {
                basisPoints = default;
                return false;
            }
            basisPoints = _volumes[category];
            return true;
        }
    }

    public bool TrySetVolume(int category, long basisPoints)
    {
        AudioMixerSettingsSnapshot snapshot;
        lock (_lock)
        {
            if ((uint)category >= CategoryCount) return false;
            _volumes[category] = checked((int)basisPoints);
            snapshot = SnapshotLocked();
        }
        Changed?.Invoke(snapshot);
        return true;
    }

    public bool TryGetRouteEnabled(int category, out bool enabled)
    {
        lock (_lock)
        {
            if (category is < (int)AudioMixerCategory.Music or >= CategoryCount)
            {
                enabled = default;
                return false;
            }
            enabled = _routes[category];
            return true;
        }
    }

    public bool TrySetRouteEnabled(int category, bool enabled, out bool changed)
    {
        AudioMixerSettingsSnapshot? snapshot = null;
        lock (_lock)
        {
            if (category is < (int)AudioMixerCategory.Music or >= CategoryCount)
            {
                changed = false;
                return false;
            }
            changed = _routes[category] != enabled;
            if (changed)
            {
                _routes[category] = enabled;
                snapshot = SnapshotLocked();
            }
        }
        if (snapshot != null) Changed?.Invoke(snapshot);
        return true;
    }

    public AudioMixerSettingsSnapshot Snapshot()
    {
        lock (_lock) return SnapshotLocked();
    }

    public void Replace(AudioMixerSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        lock (_lock)
        {
            snapshot.Volumes.CopyTo(_volumes, 0);
            snapshot.Routes.CopyTo(_routes, 0);
            _routes[(int)AudioMixerCategory.Master] = false;
        }
    }

    public static void ValidateSnapshot(AudioMixerSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Volumes.Length != CategoryCount)
            throw new InvalidDataException($"Audio mixer volume table must contain {CategoryCount} values.");
        if (snapshot.Routes.Length != CategoryCount)
            throw new InvalidDataException($"Audio mixer route table must contain {CategoryCount} values.");
        if (snapshot.Routes[(int)AudioMixerCategory.Master])
            throw new InvalidDataException("Audio mixer master route flag must be false.");
        foreach (int value in snapshot.Volumes)
            if (value is < UnconfiguredVolume or > MaximumVolume)
                throw new InvalidDataException(
                    $"Audio mixer volume {value} is outside {UnconfiguredVolume}..{MaximumVolume}.");
    }

    private AudioMixerSettingsSnapshot SnapshotLocked()
        => new(_volumes.ToArray(), _routes.ToArray());
}
