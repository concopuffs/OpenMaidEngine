using System.Collections.Immutable;
using Age.Engine.Model;

namespace Age.Engine.Profiles;

public sealed record ProfileCellSeed(int Address, long Value);

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

/// <summary>
/// Profile-owned state used only when a developer starts after the natural boot script. It is never
/// applied to an ordinary profile boot and therefore cannot silently become an engine-wide rule. A natural
/// boot gets every value from the game's own scripts (SYSTEM4 and what it calls) and the zero-filled banks;
/// these entries stand in for the scripts a direct-scene run skips, so each must name a script writer.
/// </summary>
public sealed record DirectSceneDiagnosticPolicy
{
    public DirectSceneDiagnosticPolicy(
        string systemScript,
        IEnumerable<string>? dataBootstrapScripts = null,
        IEnumerable<string>? dataTableBootstrapScripts = null,
        IEnumerable<ProfileCellSeed>? globalSeeds = null,
        IEnumerable<ProfileSurfaceBootstrap>? inheritedSurfaces = null,
        ProfileWaitIndicatorBootstrap? waitIndicator = null,
        int? sceneEntryCoroutineGateAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemScript);
        SystemScript = RequireScriptName(systemScript, nameof(systemScript));
        DataBootstrapScripts = (dataBootstrapScripts ?? [])
            .Select(value => RequireScriptName(value, nameof(dataBootstrapScripts))).ToImmutableArray();
        DataTableBootstrapScripts = (dataTableBootstrapScripts ?? [])
            .Select(value => RequireScriptName(value, nameof(dataTableBootstrapScripts))).ToImmutableArray();
        GlobalSeeds = (globalSeeds ?? []).ToImmutableArray();
        InheritedSurfaces = (inheritedSurfaces ?? []).ToImmutableArray();
        WaitIndicator = waitIndicator;
        if (GlobalSeeds.Any(seed => seed.Address < 0))
            throw new ArgumentException("diagnostic seed addresses cannot be negative");
        if (sceneEntryCoroutineGateAddress < 0)
            throw new ArgumentException("scene-entry coroutine gate address cannot be negative");
        SceneEntryCoroutineGateAddress = sceneEntryCoroutineGateAddress;
        if (InheritedSurfaces.Any(surface => string.IsNullOrWhiteSpace(surface.ResourceName)
                                                || surface.SurfaceSlot < 0))
            throw new ArgumentException("diagnostic surfaces require a resource name and nonnegative slot");
    }

    public string SystemScript { get; }
    public ImmutableArray<string> DataBootstrapScripts { get; }
    public ImmutableArray<string> DataTableBootstrapScripts { get; }
    public ImmutableArray<ProfileCellSeed> GlobalSeeds { get; }
    public ImmutableArray<ProfileSurfaceBootstrap> InheritedSurfaces { get; }
    public ProfileWaitIndicatorBootstrap? WaitIndicator { get; }
    /// <summary>The ADV scene-entry gate a direct-scene run sets, standing in for TITLE's developer entry
    /// (ＡＤＶパート writes 1), so a scene run as the root takes its label-picker loop once. Retail keeps it 0
    /// and SYSTEM4 hosts every scene, so a natural boot never uses it.</summary>
    public int? SceneEntryCoroutineGateAddress { get; }

    private static string RequireScriptName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (Path.GetFileName(value) != value
            || !value.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("diagnostic script names must be .BIN file names", parameterName);
        return value;
    }
}
