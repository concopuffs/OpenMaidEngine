using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ome.Engine.Model;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>
/// <c>Ome.Cli intake --game-root INSTALL [--compare [NAME=]INSTALL …] [--output REPORT.json]</c>: a first look at an
/// install that has no profile yet (docs/adding-a-game.md, step 1; usage: docs/tools-reference.md). It needs no
/// profile, engine build or ABI snapshot: scripts decode with the whole opcode catalog. Exit 0 when the install is of
/// a SYS generation the engine already runs, 1 when a check fails, 2 on a usage error.
/// </summary>
internal static class IntakeCommand
{
    private const int OpcodesShown = 12;

    public static int Run(string[] args)
    {
        string? root = Value(args, "--game-root");
        if (root == null)
        {
            Console.Error.WriteLine("usage: Ome.Cli intake --game-root INSTALL [--compare [NAME=]INSTALL …] [--output REPORT.json]");
            return 2;
        }

        // Compared by default: each supported game's conventional install, where this machine has it.
        var compare = GameProfileRegistry.BuiltIn.Profiles
            .Select(profile => (Name: profile.Id, Root: Paths.GameInstall(profile.Id))).ToList();
        for (int k = 0; k + 1 < args.Length; k++)
        {
            if (args[k] != "--compare") continue;
            string value = args[k + 1];
            int equals = value.IndexOf('=');
            // NAME=INSTALL; a drive letter's colon keeps a bare Windows path from reading as a name.
            compare.Add(equals > 0 && !value[..equals].Contains(':') && !value[..equals].Contains(Path.DirectorySeparatorChar)
                ? (value[..equals], value[(equals + 1)..])
                : (Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(value))), value));
        }

        InstallIntakeReport report;
        try
        {
            OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
            report = InstallIntake.Run(root, table, opcode => VirtualMachine.Implements(table, opcode),
                                       KnownOperandTypes(Paths.OpcodesJson), GameProfileRegistry.BuiltIn, compare);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {error.Message}");
            return 2;
        }

        string? fileVersion = report.Executable == null ? null
            : FileVersionInfo.GetVersionInfo(Path.Combine(report.Root, report.Executable.Name)).FileVersion;
        Print(report, fileVersion);

        if (Value(args, "--output") is { } output)
        {
            JsonObject document = JsonSerializer.SerializeToNode(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DictionaryKeyPolicy = null,
            })!.AsObject();
            document["executable_file_version"] = fileVersion;
            document["generated_by"] = "Ome.Cli intake";
            string path = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
            Console.WriteLine($"  report: {path}");
        }
        return report.SameGeneration ? 0 : 1;
    }

    private static string? Value(string[] args, string name)
    {
        int index = Array.LastIndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>The operand type tags the registry names (<c>meta.arg_types</c> of build/opcodes.json).</summary>
    private static HashSet<int> KnownOperandTypes(string opcodesJson)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(opcodesJson));
        return document.RootElement.GetProperty("meta").GetProperty("arg_types").EnumerateObject()
            .Select(type => Convert.ToInt32(type.Name, 16)).ToHashSet();
    }

    private static void Print(InstallIntakeReport report, string? fileVersion)
    {
        static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
        static string List(IEnumerable<string> items) => string.Join(", ", items) is { Length: > 0 } text ? text : "none";

        Console.WriteLine($"intake: {report.Root}");
        Console.WriteLine($"  identity: {report.CatalogRevision} / {report.Title}; profile: {report.MatchingProfile ?? "none"}");
        if (report.Executable is { } executable)
            Console.WriteLine($"  {executable.Name}: {N(executable.Size)} bytes, version {fileVersion ?? "not read"}, "
                              + $"SHA-256 {executable.Sha256}");
        if (report.CatalogProblem != null)
            Console.WriteLine($"  catalog: DOES NOT READ: {report.CatalogProblem}");
        else
        {
            Console.WriteLine($"  catalog: {N(report.Entries)} entries ({N(report.Placeholders)} placeholders) in "
                              + $"{report.Archives.Count} archives; appends: "
                              + List(report.Appends.Select(append => append.Problem == null
                                  ? $"{append.File} (selector {append.Selector}, {N(append.Entries)} entries)"
                                  : $"{append.File} DOES NOT MOUNT: {append.Problem}")));
            if (report.TrailerProblem != null)
                Console.WriteLine($"  catalog trailer: {report.TrailerProblem}: {report.UnreadTrailerHex} "
                                  + $"\"{report.UnreadTrailerText}\"");
            Console.WriteLine($"  banks: saved prefix [{string.Join(", ", report.SavedPrefix)}], "
                              + $"full [{string.Join(", ", report.BankSizes)}]");
            Console.WriteLine($"  compared with: {List(report.ComparedWith.Select(other => $"{other.Name} ({other.CatalogRevision})"))}");
            Console.WriteLine($"  settings: {report.Settings.Count} pairs; keys no compared install has: "
                              + List(report.SettingsKeysNoComparedInstallHas));
            Console.WriteLine($"  file types: {List(report.FileTypes.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {N(pair.Value)}"))}; "
                              + $"types no compared install has: {List(report.FileTypesNoComparedInstallHas)}");
        }
        Console.WriteLine($"  scripts ({report.ScriptSource}): {N(report.Scripts)} ({N(report.BaseScripts)} base, "
                          + $"{N(report.AppendScripts)} append), {N(report.Instructions)} instructions; revisions: "
                          + List(report.ScriptRevisions.Select(pair => $"{pair.Key} ×{pair.Value}")));
        foreach (IntakeScriptFailure failure in report.Failures.Take(10))
            Console.WriteLine($"    DOES NOT DECODE {failure.Script}: {failure.Error}");
        if (report.Failures.Count > 10) Console.WriteLine($"    … and {report.Failures.Count - 10} more");
        Console.WriteLine($"  opcodes: {report.OpcodeOccurrences.Count} distinct; {report.WithoutHandler.Count} without a handler "
                          + $"({N(report.WithoutHandler.Sum(opcode => opcode.Occurrences))} sites)");
        foreach (IntakeOpcode opcode in report.WithoutHandler.Take(OpcodesShown))
            Console.WriteLine($"    {opcode.Opcode} {opcode.Label} ×{N(opcode.Occurrences)} in {opcode.ScriptCount} scripts "
                              + $"({string.Join(", ", opcode.TopScripts.Take(3).Select(script => $"{script.Script} ×{N(script.Occurrences)}"))})");
        if (report.WithoutHandler.Count > OpcodesShown)
            Console.WriteLine($"    … and {report.WithoutHandler.Count - OpcodesShown} more (the report lists all)");
        foreach (IntakeProfileDelta delta in report.NotUsedByProfile)
            Console.WriteLine($"  opcodes {delta.ProfileId} ({delta.ScriptRevision}) never uses: {delta.Opcodes.Count}");
        if (report.NotUsedByProfile.Count > 0)
            Console.WriteLine($"  opcodes no supported game uses: {report.UsedByNoProfile.Count}"
                              + (report.UsedByNoProfile.Count > 0 ? $" ({string.Join(", ", report.UsedByNoProfile)})" : ""));
        foreach (IntakeCheck check in report.Checks)
            Console.WriteLine($"  [{(check.Passed ? "pass" : "FAIL")}] {check.Name}: {check.Detail}");
        Console.WriteLine($"  same SYS generation: {(report.SameGeneration ? "yes" : "NO")}");
    }
}
