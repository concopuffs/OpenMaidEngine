using System.Collections.Immutable;
using Age.Engine.Model;

namespace Age.Engine.Profiles;

public sealed record ProfileCellSeed(int Address, long Value);

public sealed record ProfileSceneCellSeed(string ScriptName, int Address, long Value)
{
    public bool Matches(string scriptName)
        => ScriptName.Equals(scriptName, StringComparison.OrdinalIgnoreCase);
}

public sealed record ProfileSurfaceBootstrap(string ResourceName, int SurfaceSlot, uint Flags);

public sealed record ProfileWaitIndicatorBootstrap(
    int TextLayout,
    int X,
    int Y,
    int SurfaceSlot,
    int SourceX,
    int SourceY,
    int FrameWidth,
    int FrameHeight,
    int FrameCount,
    int FrameMilliseconds)
{
    public AdvWaitIndicatorConfig ToConfig()
        => new(TextLayout, X, Y, SurfaceSlot, SourceX, SourceY,
               FrameWidth, FrameHeight, FrameCount, FrameMilliseconds);
}

public sealed record DebugSceneLaunchPolicy
{
    public DebugSceneLaunchPolicy(
        string rootScript,
        string coordinatorScript,
        int packedScriptIdAddress,
        IEnumerable<ProfileCellSeed>? coordinatorWrites = null)
    {
        RootScript = RequireScriptName(rootScript, nameof(rootScript));
        CoordinatorScript = RequireScriptName(coordinatorScript, nameof(coordinatorScript));
        if (packedScriptIdAddress < 0)
            throw new ArgumentException("debug-scene packed-script address cannot be negative");
        PackedScriptIdAddress = packedScriptIdAddress;
        CoordinatorWrites = (coordinatorWrites ?? []).ToImmutableArray();
        if (CoordinatorWrites.Any(seed => seed.Address < 0))
            throw new ArgumentException("debug-scene coordinator addresses cannot be negative");
    }

    public string RootScript { get; }
    public string CoordinatorScript { get; }
    public int PackedScriptIdAddress { get; }
    public ImmutableArray<ProfileCellSeed> CoordinatorWrites { get; }

    private static string RequireScriptName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (Path.GetFileName(value) != value
            || !value.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("debug-scene scripts must be .BIN file names", parameterName);
        return value;
    }
}

/// <summary>Native service state proven for one game profile, outside script-visible writes.</summary>
public sealed record GameRuntimePolicy
{
    public GameRuntimePolicy(
        IEnumerable<ProfileCellSeed>? externalGlobalSeeds = null,
        int? sceneEntryCoroutineGateAddress = null)
    {
        ExternalGlobalSeeds = (externalGlobalSeeds ?? []).ToImmutableArray();
        if (ExternalGlobalSeeds.Any(seed => seed.Address < 0))
            throw new ArgumentException("external global seed addresses cannot be negative");
        if (sceneEntryCoroutineGateAddress < 0)
            throw new ArgumentException("scene-entry coroutine gate address cannot be negative");
        SceneEntryCoroutineGateAddress = sceneEntryCoroutineGateAddress;
    }

    public ImmutableArray<ProfileCellSeed> ExternalGlobalSeeds { get; }
    public int? SceneEntryCoroutineGateAddress { get; }
    public static GameRuntimePolicy Empty { get; } = new();

    public void ApplyExternalGlobals(IDictionary<int, long> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (ProfileCellSeed seed in ExternalGlobalSeeds)
            target[seed.Address] = seed.Value;
    }
}

/// <summary>
/// Profile-owned state used only when a developer starts after the natural boot script. It is never
/// applied to an ordinary profile boot and therefore cannot silently become an engine-wide rule.
/// </summary>
public sealed record DirectSceneDiagnosticPolicy
{
    public DirectSceneDiagnosticPolicy(
        string systemScript,
        IEnumerable<string>? dataBootstrapScripts = null,
        IEnumerable<string>? dataTableBootstrapScripts = null,
        IEnumerable<ProfileCellSeed>? globalSeeds = null,
        IEnumerable<ProfileSceneCellSeed>? sceneExternalGlobalSeeds = null,
        IEnumerable<ProfileSurfaceBootstrap>? inheritedSurfaces = null,
        ProfileWaitIndicatorBootstrap? waitIndicator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemScript);
        SystemScript = RequireScriptName(systemScript, nameof(systemScript));
        DataBootstrapScripts = (dataBootstrapScripts ?? [])
            .Select(value => RequireScriptName(value, nameof(dataBootstrapScripts))).ToImmutableArray();
        DataTableBootstrapScripts = (dataTableBootstrapScripts ?? [])
            .Select(value => RequireScriptName(value, nameof(dataTableBootstrapScripts))).ToImmutableArray();
        GlobalSeeds = (globalSeeds ?? []).ToImmutableArray();
        SceneExternalGlobalSeeds = (sceneExternalGlobalSeeds ?? []).ToImmutableArray();
        InheritedSurfaces = (inheritedSurfaces ?? []).ToImmutableArray();
        WaitIndicator = waitIndicator;
        if (GlobalSeeds.Any(seed => seed.Address < 0)
            || SceneExternalGlobalSeeds.Any(seed => seed.Address < 0))
            throw new ArgumentException("diagnostic seed addresses cannot be negative");
        if (SceneExternalGlobalSeeds.Any(seed =>
                Path.GetFileName(seed.ScriptName) != seed.ScriptName
                || !seed.ScriptName.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("scene diagnostic seeds require .BIN script names");
        if (InheritedSurfaces.Any(surface => string.IsNullOrWhiteSpace(surface.ResourceName)
                                                || surface.SurfaceSlot < 0))
            throw new ArgumentException("diagnostic surfaces require a resource name and nonnegative slot");
    }

    public string SystemScript { get; }
    public ImmutableArray<string> DataBootstrapScripts { get; }
    public ImmutableArray<string> DataTableBootstrapScripts { get; }
    public ImmutableArray<ProfileCellSeed> GlobalSeeds { get; }
    public ImmutableArray<ProfileSceneCellSeed> SceneExternalGlobalSeeds { get; }
    public ImmutableArray<ProfileSurfaceBootstrap> InheritedSurfaces { get; }
    public ProfileWaitIndicatorBootstrap? WaitIndicator { get; }

    private static string RequireScriptName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (Path.GetFileName(value) != value
            || !value.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("diagnostic script names must be .BIN file names", parameterName);
        return value;
    }
}
