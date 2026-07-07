using System.Text.Json;
using System.Text.RegularExpressions;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

var table = OpcodeTableJson.Load(Paths.OpcodesJson);
// call-script execution: resolves ids -> scripts. Product paths pass this so subroutines run;
// `trace` stays provider-less on purpose (the base-ISA offset oracle).
var provider = Sys4ScriptProvider.Load(table);

// --trace [--trace-file <path>] [--trace-steps] → a TextTraceSink to console or file; else inert.
static ITraceSink BuildSink(string[] a, OpcodeTable tbl)
{
    if (!a.Contains("--trace")) return NullTraceSink.Instance;
    bool steps = a.Contains("--trace-steps");
    int fi = Array.IndexOf(a, "--trace-file");
    TextWriter w = (fi >= 0 && fi + 1 < a.Length)
        ? new StreamWriter(a[fi + 1]) { AutoFlush = true }
        : Console.Out;
    return new TextTraceSink(w, tbl, steps);
}

if (args.Length == 0) { Console.WriteLine("usage: run <file> | trace <out.json>"); return 1; }

if (args[0] == "run")
{
    var script = Sys4Loader.Load(args[1], table);
    var runHost = new CaptureHost();
    var vm = new VirtualMachine(script, table, runHost, null, provider, BuildSink(args, table));
    vm.Run();
    Console.WriteLine($"{Path.GetFileName(args[1])}: {vm.Steps} steps, {vm.Emitted.Count} show-text, {vm.CallScriptDispatches} call-scripts (halt: {vm.HaltReason})");
    foreach (var (off, text, scr) in vm.Emitted.Take(30)) Console.WriteLine($"  [{scr} 0x{off:x}] {text}");
    var sources = vm.Emitted.Select(e => e.Script).Distinct().ToList();
    Console.WriteLine($"source scripts ({sources.Count}): {string.Join(", ", sources)}");
    return 0;
}

if (args[0] == "audio")
{
    // audio <SCENE.BIN> — run the scene and dump executed play-bgm/play-voice ops in order,
    // each resolved via ResourceMap (same rule as the Godot host). Diagnostic only.
    var sceneName = args[1];
    var sceneKey = Path.GetFileNameWithoutExtension(sceneName).ToUpperInvariant();
    var res = ResourceMap.Load();
    var host = new AudioTraceHost(res, sceneKey);
    var vm = new VirtualMachine(Sys4Loader.Load(Paths.Scripts()[sceneName.ToUpperInvariant()], table), table, host);
    // optional: seed globals, e.g. `audio SC0000.BIN 0xa57=1` to set Lily's form-A flag
    foreach (var s in args.Skip(2))
    {
        var kv = s.Split('=');
        int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
        long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
        vm.Globals[k] = v;
    }
    vm.Run();
    Console.WriteLine($"{sceneName}: {host.Events.Count} audio ops (halt: {vm.HaltReason})");
    foreach (var (kind, id, resolved) in host.Events)
        Console.WriteLine($"  {kind,-10} 0x{id:x2} -> {resolved}");
    return 0;
}

if (args[0] == "gfx")
{
    // gfx [--boot] <SCENE.BIN> [0xADDR=VAL ...] — run the scene and dump executed texture ops with resolved
    // file + computed geometry. --boot first runs SYSTEM4's state-setup prefix (INITCONFIG/INIT2/INIT,
    // skipping the UI scripts LOGO/OP/TITLE) through a GameSession, so scene-assumed boot state — chiefly
    // INIT2's gfx handle array 0x62455.. — is present. Diagnostic only.
    bool boot = args.Contains("--boot");
    var sceneName = args.First(a => a.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase));
    var sceneKey = Path.GetFileNameWithoutExtension(sceneName).ToUpperInvariant();
    var res = ResourceMap.Load();
    var host = new GfxTraceHost(res, sceneKey);
    var session = new GameSession();
    foreach (var s in args.Where(a => a.Contains('=')))
    {
        var kv = s.Split('=');
        int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
        long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
        session.Seed(k, v);
    }
    if (boot)
        foreach (var b in new[] { "INITCONFIG.BIN", "INIT2.BIN", "INIT.BIN" })
        {
            var bs = session.RunScene(Sys4Loader.Load(Paths.Scripts()[b], table), table, new CaptureHost(), null, provider);
            Console.WriteLine($"[boot] {b}: {bs.Steps} steps (halt: {bs.Halt})");
        }
    var target = Sys4Loader.Load(Paths.Scripts()[sceneName.ToUpperInvariant()], table);
    // With --boot, run the target like the real engine (call-scripts on) so subroutine-driven setup runs.
    var vm = boot ? new VirtualMachine(target, table, host, new VmOptions(MaxSteps: 20_000_000), provider)
                  : new VirtualMachine(target, table, host);
    foreach (var kv in session.Globals) vm.Globals[kv.Key] = kv.Value;
    foreach (var kv in session.GlobalStrings) vm.GlobalStrings[kv.Key] = kv.Value;
    vm.Run();
    Console.WriteLine($"{sceneName}: {host.Events.Count} texture ops (halt: {vm.HaltReason})");
    foreach (var line in host.Events) Console.WriteLine("  " + line);
    var gfxObjs = vm.Gfx.Objects.OrderBy(o => o.Slot).ToList();
    Console.WriteLine($"  gfx objects: {gfxObjs.Count} -> " +
        string.Join(", ", gfxObjs.Select(o => $"0x{o.Handle:x}=slot{o.Slot}")));
    var layers = vm.Gfx.SnapshotLayers();
    Console.WriteLine($"  layers ({layers.Count}, composite order):");
    foreach (var l in layers)
        Console.WriteLine($"    h=0x{l.Handle:x} slot={l.Slot} src=({l.SrcX},{l.SrcY} {l.W}x{l.H}) dst=({l.DstX},{l.DstY})");
    return 0;
}

if (args[0] == "play")
{
    // play [--boot] <SCENE.BIN...> [0xADDR=VAL ...] — run a sequence of scenes carrying persistent global
    // state across them (optional up-front seeds). --boot first runs the data-table *INIT scripts so scenes
    // see the real skill/item/unit/etc. state. The state substrate for cross-scene flow; headless.
    // The *INIT boot set — all run clean (halt: exit) and populate the game's data tables into globals.
    string[] bootScripts = { "SKINIT.BIN", "ITINIT.BIN", "EBINIT.BIN", "CGINIT.BIN", "MPINIT.BIN",
                             "AFINIT.BIN", "CCINIT.BIN", "STINIT.BIN", "STINIT2.BIN" };
    var scripts = Paths.Scripts();
    bool boot = args.Contains("--boot");
    var userScenes = args.Skip(1).Where(a => a.ToUpperInvariant().EndsWith(".BIN")).ToList();
    if (userScenes.Count == 0) { Console.WriteLine("usage: play [--boot] <SCENE.BIN...> [0xADDR=VAL ...]"); return 1; }
    var scenes = (boot ? bootScripts.Concat(userScenes) : userScenes).ToList();
    // --state <file>: start from a saved snapshot (e.g. a pre-booted state) instead of booting fresh.
    string? StateArg(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    var loadState = StateArg("--state");
    var saveState = StateArg("--save-state");
    var session = loadState != null ? GameSession.FromJson(File.ReadAllText(loadState)) : new GameSession();
    if (loadState != null) Console.WriteLine($"[state] loaded {session.Globals.Count} globals from {loadState}");
    foreach (var s in args.Skip(1).Where(a => a.Contains('=')))
    {
        var kv = s.Split('=');
        int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
        long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
        session.Seed(k, v);
    }
    long totalLines = 0;
    foreach (var name in scenes)
    {
        var script = Sys4Loader.Load(scripts[name.ToUpperInvariant()], table);
        var r = session.RunScene(script, table, new CaptureHost(), null, provider, BuildSink(args, table));
        totalLines += r.Emitted.Count;
        Console.WriteLine($"  {name,-14} {r.Emitted.Count,4} lines, {r.Steps,7} steps (halt: {r.Halt})");
    }
    Console.WriteLine($"total: {totalLines} lines across {scenes.Count} scene(s); {session.Globals.Count} globals carried");
    if (saveState != null) { File.WriteAllText(saveState, session.ToJson()); Console.WriteLine($"[state] saved -> {saveState}"); }
    return 0;
}

if (args[0] == "sweep")
{
    // sweep [--boot] — run every SC/SP scene through GameSession (each from a fresh or booted-from-snapshot
    // baseline) and report halt distribution + line counts. Validates the VM + state substrate at scale and
    // surfaces how booted real data affects the corpus. Headless.
    var sceneRe = new Regex(@"^S[CP]\d{4}\.BIN$");
    var scripts = Paths.Scripts();
    var names = scripts.Keys.Where(n => sceneRe.IsMatch(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
    bool boot = args.Contains("--boot");
    string? baseline = null;
    if (boot)
    {
        var bootSession = new GameSession();
        foreach (var s in new[] { "SKINIT.BIN", "ITINIT.BIN", "EBINIT.BIN", "CGINIT.BIN", "MPINIT.BIN",
                                  "AFINIT.BIN", "CCINIT.BIN", "STINIT.BIN", "STINIT2.BIN" })
            bootSession.RunScene(Sys4Loader.Load(scripts[s], table), table, new CaptureHost(), null, provider);
        baseline = bootSession.ToJson();
        Console.WriteLine($"[boot] baseline = {bootSession.Globals.Count} globals; running {names.Count} scenes from it.");
    }
    // Optional seeds turn sweep into a story-state explorer: run each scene with AND without the seeds
    // (from the same baseline) and report which scenes' dialogue changes — i.e. what a story flag affects.
    var seeds = new List<(int, long)>();
    foreach (var s in args.Skip(1).Where(a => a.Contains('=')))
    {
        var kv = s.Split('=');
        int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
        long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
        seeds.Add((k, v));
    }
    GameSession Fresh() => baseline != null ? GameSession.FromJson(baseline) : new GameSession();
    int RunLines(string name, bool seeded)
    {
        var session = Fresh();
        if (seeded) foreach (var (k, v) in seeds) session.Seed(k, v);
        return session.RunScene(Sys4Loader.Load(scripts[name], table), table, new CaptureHost(), null, provider).Emitted.Count;
    }

    if (seeds.Count > 0)
    {
        var changed = new List<string>();
        foreach (var name in names)
        {
            int baseLines = RunLines(name, false), seededLines = RunLines(name, true);
            if (baseLines != seededLines) changed.Add($"{name}: {baseLines} -> {seededLines} lines ({seededLines - baseLines:+#;-#;0})");
        }
        var seedStr = string.Join(" ", seeds.Select(s => $"0x{s.Item1:x}={s.Item2}"));
        Console.WriteLine($"seed [{seedStr}]{(boot ? " (booted)" : "")}: {changed.Count}/{names.Count} scenes change dialogue");
        foreach (var c in changed) Console.WriteLine("  " + c);
        return 0;
    }

    var haltDist = new SortedDictionary<string, int>(StringComparer.Ordinal);
    long totalLines = 0; var anomalies = new List<string>();
    foreach (var name in names)
    {
        var session = Fresh();
        var r = session.RunScene(Sys4Loader.Load(scripts[name], table), table, new CaptureHost(), null, provider, BuildSink(args, table));
        var halt = r.Halt ?? "null";
        haltDist[halt] = haltDist.GetValueOrDefault(halt) + 1;
        totalLines += r.Emitted.Count;
        if (halt != "exit") anomalies.Add($"{name}: {r.Emitted.Count} lines, halt={halt}");
    }
    Console.WriteLine($"swept {names.Count} scenes{(boot ? " (booted)" : "")}: {totalLines} total lines");
    Console.WriteLine("halt distribution: " + string.Join(", ", haltDist.Select(kv => $"{kv.Key}={kv.Value}")));
    if (anomalies.Count > 0) { Console.WriteLine($"non-exit halts ({anomalies.Count}):"); foreach (var a in anomalies) Console.WriteLine("  " + a); }
    return 0;
}

if (args[0] == "trace")
{
    var scene = new Regex(@"^S[CP]\d{4}\.BIN$");
    var scripts = Paths.Scripts();
    var trace = new SortedDictionary<string, object>(StringComparer.Ordinal);
    foreach (var name in scripts.Keys.Where(n => scene.IsMatch(n)).OrderBy(n => n, StringComparer.Ordinal))
    {
        var vm = new VirtualMachine(Sys4Loader.Load(scripts[name], table), table, new CaptureHost());
        vm.Run();
        trace[name] = new { offsets = vm.Emitted.Select(e => e.Offset).ToArray(), halt = vm.HaltReason, steps = vm.Steps };
    }
    File.WriteAllText(args[1], JsonSerializer.Serialize(trace,
        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"trace: {trace.Count} scenes -> {args[1]}");
    return 0;
}
Console.WriteLine("unknown command"); return 1;

sealed class AudioTraceHost : IHost
{
    private readonly ResourceMap _res;
    private readonly string _scene;
    public List<(string Kind, long Id, string Resolved)> Events { get; } = new();
    public AudioTraceHost(ResourceMap res, string scene) { _res = res; _scene = scene; }
    public void PlayBgm(long id)                        // BGM: direct name, not the manifest
    {
        var path = _res.BgmPathById(id);
        Events.Add(("play-bgm", id, path != null ? $"DATA3 {System.IO.Path.GetFileName(path)}" : $"BGM{id:D3}.OGG <missing>"));
    }
    public void PlayVoice(long id)                      // voice: per-scene manifest
    {
        var e = _res.Resolve(_scene, id);
        Events.Add(("play-voice", id, e == null ? "<unresolved>"
            : $"{e.Archive} {e.Name}" + (ResourceMap.AudioPath(e) == null ? " [NO FILE]" : "")));
    }
    public void ShowText(int offset, string text) { }
    public void WaitForInput() { }
    public void CreateTexture(int slot, int width, int height) { }
    public void SetTexture(long resourceId, int slot) { }
    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
}

sealed class GfxTraceHost : IHost
{
    private readonly ResourceMap _res;
    private readonly string _scene;
    private readonly Dictionary<int, string?> _slotBmp = new();   // slot -> resolved BMP path (or null)
    // slot -> dims. Slot 0 is the primary/screen surface (800x600), normally created at engine boot which
    // the single-scene harness skips; seed it so the first CG's anchor math stays correct (not 0x0).
    private readonly Dictionary<int, (int W, int H)> _slotDims = new() { { 0, (800, 600) } };
    public List<string> Events { get; } = new();
    public GfxTraceHost(ResourceMap res, string scene) { _res = res; _scene = scene; }

    public (int Width, int Height) GetTextureSize(int slot)
    {
        var d = _slotDims.TryGetValue(slot, out var v) ? v : (0, 0);
        Events.Add($"get-tex-size slot={slot} -> {d.Item1}x{d.Item2}");
        return d;
    }

    public void SetTexture(long resId, int slot)
    {
        var e = _res.Resolve(_scene, resId);
        var bmp = e != null ? ResourceMap.TexturePath(e) : null;
        _slotBmp[slot] = bmp;
        _slotDims[slot] = BmpHeader.ReadDims(bmp);
        Events.Add($"set-texture slot={slot} res=0x{resId:x} -> {(e?.Name ?? "<unresolved>")}"
                   + (bmp == null ? " [NO BMP]" : ""));
    }

    public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy)
    {
        _slotBmp.TryGetValue(slot, out var bmp);
        Events.Add($"draw-texture slot={slot} src=({sx},{sy} {w}x{h}) dst=({dx},{dy}) "
                   + $"file={(bmp != null ? System.IO.Path.GetFileName(bmp) : "<none>")}");
    }

    public void CreateTexture(int slot, int width, int height)
    {
        _slotDims[slot] = (width, height);
        Events.Add($"create-texture slot={slot} {width}x{height}");
    }
    public void ShowText(int offset, string text) { }
    public void WaitForInput() { }
    public void PlayBgm(long id) { }
    public void PlayVoice(long id) { }
}
