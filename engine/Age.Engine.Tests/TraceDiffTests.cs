using System.Text.Json;
using System.Text.RegularExpressions;
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class TraceDiffTests
{
    private static readonly Regex Scene = new(@"^S[CP]\d{4}\.BIN$");

    [Fact]
    public void CsTraceMatchesVm0PerScene()
    {
        string refPath = Path.Combine(Paths.Build, "vm0-trace.json");
        Assert.True(File.Exists(refPath),
            "prerequisite: run `py -3.11 -X utf8 tools/vm0.py --trace build/vm0-trace.json`");

        using var doc = JsonDocument.Parse(File.ReadAllText(refPath));
        var expected = doc.RootElement;
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var scripts = Paths.Scripts();

        var mismatches = new List<string>();
        foreach (var name in scripts.Keys.Where(n => Scene.IsMatch(n)).OrderBy(n => n, StringComparer.Ordinal))
        {
            var script = Sys4Loader.Load(scripts[name], table);
            var vm = new VirtualMachine(script, table, new CaptureHost());
            vm.Run();
            var offsets = vm.Emitted.Select(e => e.Offset).ToArray();

            if (!expected.TryGetProperty(name, out var exp)) { mismatches.Add($"{name}: absent in vm0 trace"); continue; }
            var expOffsets = exp.GetProperty("offsets").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            string expHalt = exp.GetProperty("halt").GetString() ?? "";
            long expSteps = exp.GetProperty("steps").GetInt64();

            if (!offsets.SequenceEqual(expOffsets))
                mismatches.Add($"{name}: offsets differ (cs {offsets.Length} vs vm0 {expOffsets.Length}; first diff at {FirstDiff(offsets, expOffsets)})");
            else if (vm.HaltReason != expHalt) mismatches.Add($"{name}: halt cs='{vm.HaltReason}' vs vm0='{expHalt}'");
            else if (vm.Steps != expSteps) mismatches.Add($"{name}: steps cs={vm.Steps} vs vm0={expSteps}");
        }
        Assert.True(mismatches.Count == 0, "scene mismatches:\n" + string.Join("\n", mismatches.Take(20)));
    }

    private static int FirstDiff(int[] a, int[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
        return n;
    }
}
