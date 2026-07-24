using System.Text.Json;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Persistence;

namespace Age.Engine.Vm;

/// <summary>
/// A persistent global store carried across scenes. Every scene the game runs shares one flat global
/// bank (the engine's model); running scenes in isolation with empty state is why our headless VM
/// diverges from the real game (the bg/sprite geometry drift, the state-gated EMPTY scenes, Lily's
/// form-gated voices are all state divergence — see docs/phase-a-slice-plan.md A2b-Geometry).
///
/// <para>Seam rule: references only <c>Model</c> + <c>Hosting</c> (never <c>Sys4</c>). The
/// <see cref="VirtualMachine"/> is unchanged — its globals are pre-seeded before <c>Run</c> and merged
/// back after, so trace/selftest parity is untouched. Local frames are per-call and correctly do NOT
/// persist (they live in the VM, not here).</para>
/// </summary>
public sealed class GameSession
{
    public Dictionary<int, long> Globals { get; } = new();
    public Dictionary<int, string> GlobalStrings { get; } = new();
    /// <summary>AGE's selected profile-wide cells plus native shared SAVE.DAT/RT.DAT lifecycle.</summary>
    public SharedProfile SharedProfile { get; }
    /// <summary>The live retained ADV backlog shared by every VM run in this session.</summary>
    public AdvTextHistory TextHistory { get; } = new();

    public GameSession(SharedProfile? sharedProfile = null)
        => SharedProfile = sharedProfile ?? new SharedProfile();

    public void Seed(int addr, long value) => Globals[addr] = value;
    public void SeedString(int addr, string value) => GlobalStrings[addr] = value;

    /// <summary>Run one scene: seed a fresh VM from session state, execute, merge final state back.</summary>
    public SceneResult RunScene(Script script, OpcodeTable table, IHost host,
                                VmOptions? options = null, IScriptProvider? provider = null,
                                ITraceSink? sink = null)
    {
        var vm = new VirtualMachine(
            script, table, host, options, provider, sink, TextHistory, SharedProfile);
        foreach (var kv in Globals) vm.Globals[kv.Key] = kv.Value;
        foreach (var kv in GlobalStrings) vm.GlobalStrings[kv.Key] = kv.Value;

        vm.Run();

        // Globals are one flat space; last write wins — the engine's single global bank.
        foreach (var kv in vm.Globals) Globals[kv.Key] = kv.Value;
        foreach (var kv in vm.GlobalStrings) GlobalStrings[kv.Key] = kv.Value;

        return new SceneResult(vm.Emitted.ToList(), vm.HaltReason, vm.Steps);
    }

    /// <summary>Serialize the persistent global banks to JSON, keyed by decimal address.
    /// Lets an expensive booted state be snapshotted and reused. Live <see cref="TextHistory"/> is
    /// intentionally excluded until the unified save/profile architecture defines its lifecycle.</summary>
    public string ToJson()
    {
        var snap = new StateSnapshot(
            Globals.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            GlobalStrings.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
        return JsonSerializer.Serialize(snap,
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Rebuild a session from a <see cref="ToJson"/> snapshot.</summary>
    public static GameSession FromJson(string json)
    {
        var s = new GameSession();
        var snap = JsonSerializer.Deserialize<StateSnapshot>(json) ?? new StateSnapshot(new(), new());
        foreach (var kv in snap.Globals) s.Globals[int.Parse(kv.Key)] = kv.Value;
        foreach (var kv in snap.Strings) s.GlobalStrings[int.Parse(kv.Key)] = kv.Value;
        return s;
    }

    private sealed record StateSnapshot(Dictionary<string, long> Globals, Dictionary<string, string> Strings);
}

/// <summary>The observable result of running one scene into a <see cref="GameSession"/>.</summary>
public sealed record SceneResult(IReadOnlyList<(int Offset, string Text, string Script)> Emitted, string? Halt, long Steps);
