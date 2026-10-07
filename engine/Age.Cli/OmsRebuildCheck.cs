using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Modules;
using Age.Script.Syntax;
using Age.Script.Text;

/// <summary>
/// Open Maid Script gate 3, canonical rebuild (spec §12): every catalog script is decompiled and compiled back without
/// its sidecar, then each scene runs on the original and on the rebuilt catalog from the same baseline. The executed
/// (depth, opcode, position) sequences, frame names, emitted text, halt reasons and final session states must match.
/// At the core level instructions keep their code offsets (only temporaries, data order and header counts move). At
/// the idiomatic level linking moves library subs, so positions compare by relocation key, and a global that holds a
/// code position may differ in value when both values name the same instruction.
/// </summary>
internal static class OmsRebuildCheck
{
    /// <summary>Serves rebuilt bytes for scripts and the original bytes for every other asset.</summary>
    private sealed class RebuiltAssetStore(IAssetStore inner, Func<AssetEntry, byte[], byte[]> rebuild) : IAssetStore
    {
        private readonly Dictionary<AssetEntry, byte[]> _cache = new(ReferenceEqualityComparer.Instance);

        public Stream Open(AssetEntry entry) => new MemoryStream(ReadAll(entry), writable: false);

        public byte[] ReadAll(AssetEntry entry)
        {
            if (!entry.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)) return inner.ReadAll(entry);
            if (!_cache.TryGetValue(entry, out byte[]? bytes))
                _cache[entry] = bytes = rebuild(entry, inner.ReadAll(entry));
            return bytes;
        }
    }

    /// <summary>A capture host on simulated time: the input clock advances only with sleeps and frame yields, so
    /// timed callbacks fire at the same instruction in both runs instead of following the wall clock.</summary>
    private sealed class SimulatedClockHost : IHost
    {
        private long _now;
        public List<(int Offset, string Text)> Emitted { get; } = new();
        public long InputClockMilliseconds => _now;
        public void ShowDiagnosticMessage(DiagnosticMessage message) { }
        public void ShowText(int offset, string text) => Emitted.Add((offset, text));
        public void WaitForInput() => _now += 16;
        public void Sleep(long duration) => _now += Math.Max(1, duration);
        public void FrameYield() => _now += 16;
        public void CreateTexture(int slot, int width, int height) { }
        public void SetTexture(long resourceId, int slot) { }
        public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }
        public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    /// <summary>Records frames and executed instructions as packed longs: kind, depth, opcode and pc.</summary>
    private sealed class StepRecorder : ITraceSink
    {
        public List<long> Events { get; } = new();
        public List<string> Frames { get; } = new();
        /// <summary>The script executing at each event (frames by depth).</summary>
        public List<string> Scripts { get; } = new();
        private readonly Dictionary<int, string> _byDepth = new();
        public bool TracingSteps => true;

        public void Emit(in TraceEvent e)
        {
            switch (e.Kind)
            {
                case TraceEventKind.Step:
                    Events.Add(((long)(e.Depth & 0xff) << 40) | ((long)(e.Opcode & 0xfff) << 28) | (uint)(e.Pc & 0xfffffff));
                    Scripts.Add(_byDepth.GetValueOrDefault(e.Depth, "?"));
                    break;
                case TraceEventKind.FrameEnter:
                case TraceEventKind.FrameExit:
                    if (e.Kind == TraceEventKind.FrameEnter && e.Name != null) _byDepth[e.Depth] = e.Name;
                    Frames.Add($"{e.Kind}:{e.Name}:{e.Depth}:{e.Text}");
                    Events.Add(-Frames.Count);
                    Scripts.Add(e.Name ?? "?");
                    break;
            }
        }
    }

    public static int Run(Sys4AssetCatalog catalog, IAssetStore store, OpcodeTable table, OpcodeContracts contracts,
                          IReadOnlyList<GameModule> modules, IReadOnlyList<string> scenes,
                          IReadOnlyList<string> bootScripts, VmOptions options, int limit,
                          VmCompatibilityContext compatibility, bool idiomatic = false)
    {
        var intrinsics = new IntrinsicCatalog(table, contracts);
        OmsIdioms? idioms = idiomatic ? OmsIdioms.Build(modules, table.AbiId, intrinsics) : null;
        var relocations = new Dictionary<string, Age.Script.OmsRelocation>(StringComparer.OrdinalIgnoreCase);
        int rebuilt = 0, unchanged = 0;
        var failures = new List<string>();
        byte[] Rebuild(AssetEntry entry, byte[] original)
        {
            try
            {
                byte[] bytes;
                if (idioms != null)
                {
                    Age.Script.OmsRelocation relocation = Age.Script.OmsSourceRoundTrip.IdiomaticRebuild(original, entry.Name,
                        table, contracts, intrinsics, modules, idioms);
                    relocations[entry.Name] = relocation;
                    bytes = relocation.Rebuilt;
                }
                else
                    bytes = Age.Script.OmsSourceRoundTrip.CanonicalRebuild(original, entry.Name, table, contracts,
                        intrinsics, modules);
                if (bytes.AsSpan().SequenceEqual(original)) unchanged++; else rebuilt++;
                return bytes;
            }
            catch (Exception error) when (error is InvalidDataException or OmsSyntaxException or OmsCompileException)
            {
                failures.Add($"{entry.Name}: {error.Message}");
                return original;
            }
        }
        var originalProvider = new Sys4ScriptProvider(table, catalog, store);
        var rebuiltProvider = new Sys4ScriptProvider(table, catalog, new RebuiltAssetStore(store, Rebuild));

        // Rebuild every script up front so the totals cover the whole catalog, not just the scenes reached.
        foreach (PackedAssetEntry packed in catalog.EnumerateScripts()) rebuiltProvider.GetById(packed.PackedId);

        string Baseline(Sys4ScriptProvider provider)
        {
            var session = new GameSession();
            foreach (string name in bootScripts)
                session.RunScene(provider.RequireByName(name), table, new SimulatedClockHost(), options, provider,
                    compatibility: compatibility);
            return session.ToJson();
        }
        string originalBaseline = Baseline(originalProvider), rebuiltBaseline = Baseline(rebuiltProvider);
        var divergent = new List<string>();
        if (StateDifference(originalBaseline, rebuiltBaseline, null) is { } bootDifference)
            divergent.Add($"boot baseline: {bootDifference}");

        long steps = 0;
        int lines = 0;
        foreach (string scene in scenes)
        {
            (StepRecorder Trace, SceneResult Result, string State) Play(Sys4ScriptProvider provider, string baseline)
            {
                var session = GameSession.FromJson(baseline);
                var recorder = new StepRecorder();
                SceneResult result = session.RunScene(provider.RequireByName(scene), table, new SimulatedClockHost(), options,
                    provider, recorder, compatibility);
                return (recorder, result, session.ToJson());
            }
            var a = Play(originalProvider, originalBaseline);
            var b = Play(rebuiltProvider, rebuiltBaseline);
            steps += a.Trace.Events.Count;
            lines += a.Result.Emitted.Count;
            string? why = Compare(a.Trace, b.Trace, relocations)
                ?? (a.Result.Halt != b.Result.Halt ? $"halt {a.Result.Halt} vs {b.Result.Halt}" : null)
                // Emitted lines carry their string's data offset, which a canonical data layout moves.
                ?? (!a.Result.Emitted.Select(line => (line.Text, line.Script))
                        .SequenceEqual(b.Result.Emitted.Select(line => (line.Text, line.Script)))
                    ? "emitted text differs" : null)
                ?? StateDifference(a.State, b.State, relocations.GetValueOrDefault(scene));
            if (why != null) divergent.Add($"{scene}: {why}");
        }
        Console.WriteLine($"oms-rebuild: scripts rebuilt={rebuilt} byte-identical={unchanged} failed={failures.Count} "
                          + $"scenes={scenes.Count} equivalent={scenes.Count + (bootScripts.Count > 0 ? 1 : 0) - divergent.Count} "
                          + $"divergent={divergent.Count} steps={steps} lines={lines}");
        foreach (string failure in failures.Take(limit)) Console.WriteLine($"  rebuild failed: {failure}");
        foreach (string item in divergent.Take(limit)) Console.WriteLine($"  {item}");
        return failures.Count == 0 && divergent.Count == 0 ? 0 : 1;
    }

    private static string? Compare(StepRecorder a, StepRecorder b, Dictionary<string, Age.Script.OmsRelocation> relocations)
    {
        // A step's position compares by relocation key when its script was relocated, by pc otherwise.
        (long Step, long Position) Key(StepRecorder recorder, int k, bool rebuilt)
        {
            long value = recorder.Events[k];
            if (value < 0 || !relocations.TryGetValue(recorder.Scripts[k], out Age.Script.OmsRelocation? relocation))
                return (value, 0);
            long[] keys = rebuilt ? relocation.RebuiltKeys : relocation.OriginalKeys;
            int pc = (int)(value & 0xfffffff);
            return pc < keys.Length ? (value & ~0xfffffffL, keys[pc]) : (value, -1);
        }
        int limit = Math.Min(a.Events.Count, b.Events.Count);
        for (int k = 0; k < limit; k++)
            if (a.Events[k] != b.Events[k] && (a.Scripts[k] != b.Scripts[k] || Key(a, k, false) != Key(b, k, true)))
                return $"trace diverges at event {k} in {a.Scripts[k]}: {Describe(a, a.Events[k])} vs {Describe(b, b.Events[k])}";
        return a.Events.Count != b.Events.Count ? $"trace lengths {a.Events.Count} vs {b.Events.Count}" : null;
    }

    private sealed record StateSnapshot(Dictionary<string, long> Globals, Dictionary<string, string> Strings);

    /// <summary>Why two final session states differ, or null. A global holding a code position of the relocated scene
    /// may differ in value when both values name the same instruction.</summary>
    private static string? StateDifference(string original, string rebuilt, Age.Script.OmsRelocation? relocation)
    {
        if (original == rebuilt) return null;
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        StateSnapshot? a = System.Text.Json.JsonSerializer.Deserialize<StateSnapshot>(original, options);
        StateSnapshot? b = System.Text.Json.JsonSerializer.Deserialize<StateSnapshot>(rebuilt, options);
        if (a == null || b == null) return "final state differs";
        if (!a.Strings.OrderBy(pair => pair.Key).SequenceEqual(b.Strings.OrderBy(pair => pair.Key)))
            return "final string state differs";
        foreach (string cell in a.Globals.Keys.Union(b.Globals.Keys))
        {
            long x = a.Globals.GetValueOrDefault(cell), y = b.Globals.GetValueOrDefault(cell);
            if (x != y && relocation?.SamePosition(x, y) != true)
                return $"final state differs at global {cell}: {x} vs {y}";
        }
        return null;
    }

    private static string Describe(StepRecorder recorder, long value)
        => value < 0 ? recorder.Frames[(int)(-value - 1)]
            : $"depth {value >> 40} op 0x{(value >> 28) & 0xfff:x} pc 0x{value & 0xfffffff:x}";
}
