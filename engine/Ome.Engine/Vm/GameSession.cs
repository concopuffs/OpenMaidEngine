using System.Text.Json;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Persistence;

namespace Ome.Engine.Vm;

/// <summary>
/// A persistent store carried across scenes. AGE keeps separate integer, float, string, and pointer
/// banks; running scenes in isolation with empty state is why our headless VM
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
    public Dictionary<int, long> GlobalFloats { get; } = new();
    public Dictionary<int, string> GlobalStrings { get; } = new();
    public Dictionary<int, int> GlobalPointers { get; } = new();
    public Dictionary<int, int> GlobalStringPointers { get; } = new();
    /// <summary>AGE's selected profile-wide cells plus native shared SAVE.DAT/RT.DAT lifecycle.</summary>
    public SharedProfile SharedProfile { get; }
    /// <summary>Native shared/numbered save directory service used by persistence opcodes.</summary>
    public INativeDatStore? NativeDatStore { get; }
    /// <summary>AGE's profile-lifetime sound:* settings registry, independent of SAVE.DAT.</summary>
    public AudioMixerSettings AudioMixerSettings { get; }
    /// <summary>The live retained ADV backlog shared by every VM run in this session.</summary>
    public AdvTextHistory TextHistory { get; } = new();
    /// <summary>The EngineCtx-lifetime diagnostic accumulator shared across script/scene VMs.</summary>
    public DiagnosticOutputState DiagnosticOutput { get; } = new();

    public GameSession(SharedProfile? sharedProfile = null, INativeDatStore? nativeDatStore = null,
                       AudioMixerSettings? audioMixerSettings = null)
    {
        SharedProfile = sharedProfile ?? new SharedProfile();
        NativeDatStore = nativeDatStore;
        AudioMixerSettings = audioMixerSettings ?? new AudioMixerSettings();
    }

    public void Seed(int addr, long value) => Globals[addr] = value;
    public void SeedString(int addr, string value) => GlobalStrings[addr] = value;

    /// <summary>Run one scene: seed a fresh VM from session state, execute, merge final state back.</summary>
    public SceneResult RunScene(Script script, OpcodeTable table, IHost host,
                                VmOptions? options = null, IScriptProvider? provider = null,
                                ITraceSink? sink = null,
                                VmCompatibilityContext? compatibility = null)
    {
        var vm = new VirtualMachine(
            script, table, host, options, provider, sink, TextHistory, SharedProfile, NativeDatStore,
            AudioMixerSettings, DiagnosticOutput, compatibility);
        foreach (var kv in Globals) vm.Globals[kv.Key] = kv.Value;
        foreach (var kv in GlobalFloats) vm.GlobalFloats[kv.Key] = kv.Value;
        foreach (var kv in GlobalStrings) vm.GlobalStrings[kv.Key] = kv.Value;
        foreach (var kv in GlobalPointers) vm.GlobalPointers[kv.Key] = kv.Value;
        foreach (var kv in GlobalStringPointers) vm.GlobalStringPointers[kv.Key] = kv.Value;

        vm.Run();

        // The VM started from every session cell, so its final banks are the whole state: a cell the scene removed
        // (op 0x9 clears the banks) must not come back from the session's copy.
        Replace(Globals, vm.Globals);
        Replace(GlobalFloats, vm.GlobalFloats);
        Replace(GlobalStrings, vm.GlobalStrings);
        Replace(GlobalPointers, vm.GlobalPointers);
        Replace(GlobalStringPointers, vm.GlobalStringPointers);

        return new SceneResult(vm.Emitted.ToList(), vm.HaltReason, vm.Steps);
    }

    private static void Replace<T>(Dictionary<int, T> session, Dictionary<int, T> final)
    {
        session.Clear();
        foreach (var kv in final) session[kv.Key] = kv.Value;
    }

    /// <summary>Serialize the persistent global banks (integer, float, string, pointer and string pointer) to JSON,
    /// keyed by decimal address.
    /// Lets an expensive booted state be snapshotted and reused. Live <see cref="TextHistory"/> is
    /// intentionally excluded until the unified save/profile architecture defines its lifecycle.</summary>
    public string ToJson()
    {
        var snap = new StateSnapshot(
            Keyed(Globals), Keyed(GlobalStrings), Keyed(GlobalFloats), Keyed(GlobalPointers), Keyed(GlobalStringPointers));
        return JsonSerializer.Serialize(snap,
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Rebuild a session from a <see cref="ToJson"/> snapshot.</summary>
    public static GameSession FromJson(string json)
    {
        var s = new GameSession();
        var snap = JsonSerializer.Deserialize<StateSnapshot>(json) ?? new StateSnapshot(new(), new());
        Unkeyed(snap.Globals, s.Globals);
        Unkeyed(snap.Strings, s.GlobalStrings);
        Unkeyed(snap.Floats, s.GlobalFloats);
        Unkeyed(snap.Pointers, s.GlobalPointers);
        Unkeyed(snap.StringPointers, s.GlobalStringPointers);
        return s;
    }

    private static Dictionary<string, T> Keyed<T>(Dictionary<int, T> bank)
        => bank.ToDictionary(kv => kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), kv => kv.Value);

    private static void Unkeyed<T>(Dictionary<string, T>? keyed, Dictionary<int, T> bank)
    {
        if (keyed == null) return;
        foreach (var kv in keyed)
            bank[int.Parse(kv.Key, System.Globalization.CultureInfo.InvariantCulture)] = kv.Value;
    }

    /// <summary>A snapshot from before the float and pointer banks were saved has only the first two.</summary>
    private sealed record StateSnapshot(Dictionary<string, long> Globals, Dictionary<string, string> Strings,
                                        Dictionary<string, long>? Floats = null, Dictionary<string, int>? Pointers = null,
                                        Dictionary<string, int>? StringPointers = null);
}

/// <summary>The observable result of running one scene into a <see cref="GameSession"/>.</summary>
public sealed record SceneResult(IReadOnlyList<(int Offset, string Text, string Script)> Emitted, string? Halt, long Steps);
