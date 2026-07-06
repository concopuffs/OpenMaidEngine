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
