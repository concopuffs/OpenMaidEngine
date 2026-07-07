using System.Text.Json;
using System.Text.RegularExpressions;
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;

var table = OpcodeTableJson.Load(Paths.OpcodesJson);

if (args.Length == 0) { Console.WriteLine("usage: run <file> | trace <out.json>"); return 1; }

if (args[0] == "run")
{
    var script = Sys4Loader.Load(args[1], table);
    var vm = new VirtualMachine(script, table, new CaptureHost());
    vm.Run();
    Console.WriteLine($"{Path.GetFileName(args[1])}: {vm.Steps} steps, {vm.Emitted.Count} show-text (halt: {vm.HaltReason})");
    foreach (var (off, text) in vm.Emitted.Take(20)) Console.WriteLine($"  [{off:x}] {text}");
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
    public void CallScript(long id) { }
    public void OnStub(int opcode) { }
    public void WaitForInput() { }
    public void CreateTexture(int slot, int width, int height) { }
    public void SetTexture(long resourceId, int slot) { }
    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
}
