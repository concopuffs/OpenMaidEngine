using System;
using System.IO;
using System.Linq;
using System.Text;
using Age.Engine.Model;

/// <summary>Formats the bounded trace retained by the Godot frontend when the VM safety cap fires.</summary>
public static class StepLimitDiagnosticFormatter
{
    public static string Format(GodotTraceSnapshot snapshot, OpcodeTable table,
                                int topSites = 8, int tailSteps = 16)
    {
        string Location(GodotTraceStepSnapshot step)
        {
            string script = Path.GetFileNameWithoutExtension(step.Script).ToUpperInvariant();
            string mnemonic = table.Label(step.Opcode);
            if (string.IsNullOrEmpty(mnemonic)) mnemonic = "unknown";
            return $"{script}@0x{step.Offset:x} op=0x{step.Opcode:x3} {mnemonic}";
        }

        var output = new StringBuilder();
        var current = new GodotTraceStepSnapshot(
            snapshot.CurrentScript, snapshot.CurrentOffset, snapshot.CurrentOpcode, snapshot.CurrentDepth);
        output.AppendLine($"[step-limit] last: {Location(current)} depth={snapshot.CurrentDepth}");
        output.AppendLine($"[step-limit] frames: {string.Join(" > ",
            snapshot.CallStack.Select(name => Path.GetFileNameWithoutExtension(name).ToUpperInvariant()))}");
        output.AppendLine($"[step-limit] hot sites in final {snapshot.RecentSteps.Count} steps:");
        foreach (var site in snapshot.RecentSteps
                     .GroupBy(step => (step.Script, step.Offset, step.Opcode))
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key.Script, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.Offset)
                     .Take(Math.Max(0, topSites)))
        {
            var sample = new GodotTraceStepSnapshot(
                site.Key.Script, site.Key.Offset, site.Key.Opcode, 0);
            output.AppendLine($"[step-limit]   {site.Count(),4}x {Location(sample)}");
        }

        int tailStart = Math.Max(0, snapshot.RecentSteps.Count - Math.Max(0, tailSteps));
        output.AppendLine($"[step-limit] final {snapshot.RecentSteps.Count - tailStart} steps:");
        for (int index = tailStart; index < snapshot.RecentSteps.Count; index++)
            output.AppendLine($"[step-limit]   {Location(snapshot.RecentSteps[index])}");
        return output.ToString().TrimEnd();
    }
}
