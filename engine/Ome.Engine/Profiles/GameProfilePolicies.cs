using System.Collections.Immutable;

namespace Ome.Engine.Profiles;

public sealed record ProfileCellSeed(int Address, long Value);

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
/// Profile-owned script lists for the CLI's headless scene diagnostics (<c>--boot</c> on <c>trace</c>, <c>gfx</c>,
/// <c>play</c>, <c>sweep</c> and <c>oms-rebuild</c>), which run a scene as the root without SYSTEM4. A natural boot
/// and a Godot direct-scene run never use them: SYSTEM4 runs these scripts itself.
/// </summary>
public sealed record DirectSceneDiagnosticPolicy
{
    public DirectSceneDiagnosticPolicy(
        IEnumerable<string>? dataBootstrapScripts = null,
        IEnumerable<string>? dataTableBootstrapScripts = null)
    {
        DataBootstrapScripts = (dataBootstrapScripts ?? [])
            .Select(value => RequireScriptName(value, nameof(dataBootstrapScripts))).ToImmutableArray();
        DataTableBootstrapScripts = (dataTableBootstrapScripts ?? [])
            .Select(value => RequireScriptName(value, nameof(dataTableBootstrapScripts))).ToImmutableArray();
    }

    /// <summary>SYSTEM4's state prefix (its boot-time <c>call-script</c>s, without the UI scripts).</summary>
    public ImmutableArray<string> DataBootstrapScripts { get; }
    /// <summary>The data-table <c>*INIT</c> scripts.</summary>
    public ImmutableArray<string> DataTableBootstrapScripts { get; }

    private static string RequireScriptName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (Path.GetFileName(value) != value
            || !value.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("diagnostic script names must be .BIN file names", parameterName);
        return value;
    }
}
