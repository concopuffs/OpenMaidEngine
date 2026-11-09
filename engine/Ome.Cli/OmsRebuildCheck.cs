using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Script.Abi;
using Ome.Script.Compile;
using Ome.Script.Ir;
using Ome.Script.Modules;
using Ome.Script.Syntax;
using Ome.Script.Text;

/// <summary>
/// Open Maid Script gate 3, canonical rebuild (spec §12): every catalog script is decompiled and compiled back without
/// its sidecar, then each scene runs on the original and on the rebuilt catalog from the same baseline. The executed
/// (depth, opcode, position) sequences, frame names, emitted text, halt reasons and final session states must match.
/// At the core level instructions keep their code offsets (only temporaries, data order and header counts move). At
/// the idiomatic level linking moves library subs, so positions compare by relocation key, and a global that holds a
/// code position may differ in value when both values name the same instruction. With a patch probe (gate 7) every
/// script is patched with an inert probe plan instead; the inserted steps are filtered out of its traces, and an empty
/// plan must reproduce every script byte for byte.
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
    /// timed callbacks fire at the same instruction in both runs instead of following the wall clock. The VM yields
    /// after every step; steps a patch probe inserted are <see cref="Discounted"/>, so they take no time.</summary>
    private sealed class SimulatedClockHost : IHost
    {
        private const long FrameMilliseconds = 16;
        private long _now;
        public long Discounted { get; set; }
        public List<(int Offset, string Text)> Emitted { get; } = new();
        public long InputClockMilliseconds => _now - Discounted * FrameMilliseconds;
        public void ShowDiagnosticMessage(DiagnosticMessage message) { }
        public void ShowText(int offset, string text) => Emitted.Add((offset, text));
        public void WaitForInput() => _now += FrameMilliseconds;
        public void Sleep(long duration) => _now += Math.Max(1, duration);
        public void FrameYield() => _now += FrameMilliseconds;
        public void CreateTexture(int slot, int width, int height) { }
        public void SetTexture(long resourceId, int slot) { }
        public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }
        public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    /// <summary>Records frames and executed instructions as packed longs: kind, depth, opcode and pc. A step that
    /// <paramref name="inserted"/> recognizes (script, pc) calls <paramref name="onInserted"/>.</summary>
    private sealed class StepRecorder(Func<string, int, bool>? inserted = null, Action? onInserted = null) : ITraceSink
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
                    if (inserted?.Invoke(Scripts[^1], e.Pc) == true) onInserted?.Invoke();
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
                          VmCompatibilityContext compatibility, bool idiomatic = false, int patchProbe = 0,
                          string? dumpDirectory = null)
    {
        var intrinsics = new IntrinsicCatalog(table, contracts);
        OmsIdioms? idioms = idiomatic ? OmsIdioms.Build(modules, table.AbiId, intrinsics) : null;
        var relocations = new Dictionary<string, Ome.Script.OmsRelocation>(StringComparer.OrdinalIgnoreCase);
        int rebuilt = 0, unchanged = 0, emptyIdentical = 0;
        var failures = new List<string>();
        byte[] Rebuild(AssetEntry entry, byte[] original)
        {
            try
            {
                byte[] bytes;
                if (patchProbe > 0)
                {
                    Ome.Script.OmsRelocation relocation = Ome.Script.OmsSourceRoundTrip.PatchProbe(original, entry.Name, table,
                        contracts, patchProbe, out bool identical);
                    if (identical) emptyIdentical++;
                    else failures.Add($"{entry.Name}: an empty plan does not reproduce the original");
                    relocations[entry.Name] = relocation;
                    bytes = relocation.Rebuilt;
                }
                else if (idioms != null)
                {
                    Ome.Script.OmsRelocation relocation = Ome.Script.OmsSourceRoundTrip.IdiomaticRebuild(original, entry.Name,
                        table, contracts, intrinsics, modules, idioms);
                    relocations[entry.Name] = relocation;
                    bytes = relocation.Rebuilt;
                }
                else
                    bytes = Ome.Script.OmsSourceRoundTrip.CanonicalRebuild(original, entry.Name, table, contracts,
                        intrinsics, modules);
                if (bytes.AsSpan().SequenceEqual(original)) unchanged++; else rebuilt++;
                if (dumpDirectory != null)
                {
                    Directory.CreateDirectory(dumpDirectory);
                    File.WriteAllBytes(Path.Combine(dumpDirectory, entry.Name), bytes);
                }
                return bytes;
            }
            catch (Exception error) when (error is InvalidDataException or OmsSyntaxException or OmsCompileException
                                              or Ome.Script.Patch.OmsPatchException)
            {
                failures.Add($"{entry.Name}: {error.Message}");
                return original;
            }
        }
        bool InsertedStep(string script, int pc)
            => relocations.TryGetValue(script, out Ome.Script.OmsRelocation? relocation)
               && relocation.RebuiltInserted is { } inserted && pc >= 0 && pc < inserted.Length && inserted[pc];
        var originalProvider = new Sys4ScriptProvider(table, catalog, store);
        var rebuiltProvider = new Sys4ScriptProvider(table, catalog, new RebuiltAssetStore(store, Rebuild));

        // Rebuild every script up front so the totals cover the whole catalog, not just the scenes reached.
        foreach (PackedAssetEntry packed in catalog.EnumerateScripts()) rebuiltProvider.GetById(packed.PackedId);

        string Baseline(Sys4ScriptProvider provider, bool probed)
        {
            var session = new GameSession();
            foreach (string name in bootScripts)
            {
                var host = new SimulatedClockHost();
                session.RunScene(provider.RequireByName(name), table, host, options, provider,
                    probed ? new StepRecorder(InsertedStep, () => host.Discounted++) : null, compatibility);
            }
            return session.ToJson();
        }
        string originalBaseline = Baseline(originalProvider, false), rebuiltBaseline = Baseline(rebuiltProvider, patchProbe > 0);
        var divergent = new List<string>();
        if (StateDifference(originalBaseline, rebuiltBaseline, null) is { } bootDifference)
            divergent.Add($"boot baseline: {bootDifference}");

        long steps = 0;
        int lines = 0;
        foreach (string scene in scenes)
        {
            (StepRecorder Trace, SceneResult Result, string State) Play(Sys4ScriptProvider provider, string baseline,
                                                                         VmOptions playOptions, bool probed)
            {
                var session = GameSession.FromJson(baseline);
                var host = new SimulatedClockHost();
                var recorder = probed ? new StepRecorder(InsertedStep, () => host.Discounted++) : new StepRecorder();
                SceneResult result = session.RunScene(provider.RequireByName(scene), table, host, playOptions,
                    provider, recorder, compatibility);
                return (recorder, result, session.ToJson());
            }
            var a = Play(originalProvider, originalBaseline, options, probed: false);
            // Probe notes add steps, so the patched run gets room for them under the step limit.
            var b = Play(rebuiltProvider, rebuiltBaseline,
                patchProbe > 0 ? options with { MaxSteps = options.MaxSteps * 4 } : options, probed: patchProbe > 0);
            steps += a.Trace.Events.Count;
            lines += a.Result.Emitted.Count;
            // A probed run that the original stopped at the step limit compares as far as the original got.
            bool truncated = patchProbe > 0 && a.Result.Halt == "STEP-LIMIT";
            string? why = truncated ? Compare(a.Trace, b.Trace, relocations, prefix: true)
                : Compare(a.Trace, b.Trace, relocations)
                ?? (a.Result.Halt != b.Result.Halt ? $"halt {a.Result.Halt} vs {b.Result.Halt}" : null)
                // Emitted lines carry their string's data offset, which a canonical data layout moves.
                ?? (!a.Result.Emitted.Select(line => (line.Text, line.Script))
                        .SequenceEqual(b.Result.Emitted.Select(line => (line.Text, line.Script)))
                    ? "emitted text differs" : null)
                ?? StateDifference(a.State, b.State, relocations.GetValueOrDefault(scene));
            if (why != null) divergent.Add($"{scene}: {why} (halts: {a.Result.Halt ?? "none"} / {b.Result.Halt ?? "none"})");
        }
        if (patchProbe > 0)
            Console.WriteLine($"oms-rebuild --patch-probe {patchProbe}: empty plans byte-identical={emptyIdentical}");
        Console.WriteLine($"oms-rebuild: scripts rebuilt={rebuilt} byte-identical={unchanged} failed={failures.Count} "
                          + $"scenes={scenes.Count} equivalent={scenes.Count + (bootScripts.Count > 0 ? 1 : 0) - divergent.Count} "
                          + $"divergent={divergent.Count} steps={steps} lines={lines}");
        foreach (string failure in failures.Take(limit)) Console.WriteLine($"  rebuild failed: {failure}");
        foreach (string item in divergent.Take(limit)) Console.WriteLine($"  {item}");
        return failures.Count == 0 && divergent.Count == 0 ? 0 : 1;
    }

    private static string? Compare(StepRecorder a, StepRecorder b, Dictionary<string, Ome.Script.OmsRelocation> relocations,
                                   bool prefix = false)
    {
        // Steps of instructions a patch probe inserted are not compared.
        bool Inserted(int k)
        {
            long value = b.Events[k];
            if (value < 0 || !relocations.TryGetValue(b.Scripts[k], out Ome.Script.OmsRelocation? relocation)
                || relocation.RebuiltInserted is not { } inserted) return false;
            int pc = (int)(value & 0xfffffff);
            return pc < inserted.Length && inserted[pc];
        }
        int[] kept = Enumerable.Range(0, b.Events.Count).Where(k => !Inserted(k)).ToArray();
        // A step's position compares by relocation key when its script was relocated, by pc otherwise.
        (long Step, long Position) Key(StepRecorder recorder, int k, bool rebuilt)
        {
            long value = recorder.Events[k];
            if (value < 0 || !relocations.TryGetValue(recorder.Scripts[k], out Ome.Script.OmsRelocation? relocation))
                return (value, 0);
            long[] keys = rebuilt ? relocation.RebuiltKeys : relocation.OriginalKeys;
            int pc = (int)(value & 0xfffffff);
            return pc < keys.Length ? (value & ~0xfffffffL, keys[pc]) : (value, -1);
        }
        // A run stopped at the step limit ends with the frame exits of the halt, which the longer run does not have.
        int length = a.Events.Count;
        if (prefix)
            while (length > 0 && a.Events[length - 1] < 0) length--;
        int limit = Math.Min(length, kept.Length);
        for (int k = 0; k < limit; k++)
        {
            int j = kept[k];
            if (a.Events[k] != b.Events[j] && (a.Scripts[k] != b.Scripts[j] || Key(a, k, false) != Key(b, j, true)))
            {
                string Context(StepRecorder recorder, Func<int, int> at, int from)
                    => string.Join("; ", Enumerable.Range(from, k - from + 1).Select(i => Describe(recorder, recorder.Events[at(i)])));
                int from = Math.Max(0, k - 4);
                return $"trace diverges at event {k} in {a.Scripts[k]}: {Describe(a, a.Events[k])} vs {Describe(b, b.Events[j])}"
                       + (Environment.GetEnvironmentVariable("OMS_REBUILD_CONTEXT") == "1"
                           ? $"\n    original: {Context(a, i => i, from)}\n    rebuilt:  {Context(b, i => kept[i], from)}"
                             + $"\n    rebuilt raw: {string.Join("; ", Enumerable.Range(kept[from], j - kept[from] + 1).Select(i => Describe(b, b.Events[i])))}"
                           : "");
            }
        }
        if (prefix) return kept.Length < length ? $"trace stops after {kept.Length} of {length} events" : null;
        return a.Events.Count != kept.Length ? $"trace lengths {a.Events.Count} vs {kept.Length}" : null;
    }

    private sealed record StateSnapshot(Dictionary<string, long> Globals, Dictionary<string, string> Strings);

    /// <summary>Why two final session states differ, or null. A global holding a code position of the relocated scene
    /// may differ in value when both values name the same instruction.</summary>
    private static string? StateDifference(string original, string rebuilt, Ome.Script.OmsRelocation? relocation)
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
