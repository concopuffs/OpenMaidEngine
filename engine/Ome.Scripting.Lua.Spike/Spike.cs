using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Ome.Scripting.Lua.Spike;

/// <summary>An engine unwind (a return to title, a stop, a halt) thrown from inside a C# callback. Spec §5.1,
/// criterion 2: it must pass out through every Lua frame, pcall and coroutines included.</summary>
public sealed class SpikeUnwindException(string message) : Exception(message);

/// <summary>The budget stop of criterion 3: thrown when a handler runs out of instructions.</summary>
public sealed class SpikeBudgetException() : Exception("instruction budget exceeded");

/// <summary>An engine unwind carried as a cancellation: LuaCSharp's pcall, like its other catch points, may let an
/// <see cref="OperationCanceledException"/> through where it catches every other exception.</summary>
public sealed class SpikeUnwindCanceledException(string message) : OperationCanceledException(message);

/// <summary>The budget stop carried as a cancellation, thrown from the instruction hook.</summary>
public sealed class SpikeBudgetCanceledException() : OperationCanceledException("instruction budget exceeded");

/// <summary>One case's outcome. Verdict is pass, fail, partial or info.</summary>
public sealed record CaseResult(string Library, string Case, int Criterion, string Verdict, string Detail,
    Dictionary<string, double>? Metrics = null);

[AttributeUsage(AttributeTargets.Method)]
public sealed class CaseAttribute(int criterion, string name) : Attribute
{
    public int Criterion { get; } = criterion;
    public string Name { get; } = name;
}

public static class Spike
{
    /// <summary>The engine thread's stack for the cases. The port's engine thread is a thread-pool thread
    /// (godot/Main.cs, Task.Run), whose stack the host process sets; 1 MiB is the conservative reading.</summary>
    public static int StackBytes => int.TryParse(Environment.GetEnvironmentVariable("SPIKE_STACK_KB"), out int kb)
        ? kb * 1024 : 1024 * 1024;

    public static IEnumerable<(string Library, MethodInfo Method, CaseAttribute Case)> AllCases()
    {
        foreach (var (library, type) in new[] { ("luacsharp", typeof(LuaCSharpCases)), ("moonsharp", typeof(MoonSharpCases)) })
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (method.GetCustomAttribute<CaseAttribute>() is { } attribute)
                    yield return (library, method, attribute);
    }

    /// <summary>Run one case in this process, on a thread with <see cref="StackBytes"/>, and print its result.</summary>
    public static int RunOne(string library, string caseName)
    {
        var (lib, method, attribute) = AllCases().Single(c => c.Library == library && c.Case.Name == caseName);
        CaseResult? result = null;
        void Body()
        {
            try { result = (CaseResult)method.Invoke(null, null)!; }
            catch (TargetInvocationException error)
            {
                result = new CaseResult(lib, caseName, attribute.Criterion, "fail",
                    $"case threw {error.InnerException?.GetType().Name}: {error.InnerException?.Message}");
            }
        }
        if (Environment.GetEnvironmentVariable("SPIKE_THREAD") == "pool")
        {
            // A thread-pool thread, as the port's engine thread is (godot/Main.cs, Task.Run): its stack is the host
            // executable's default.
            Task.Run(Body).Wait();
        }
        else
        {
            var thread = new Thread(Body, StackBytes);
            thread.Start();
            thread.Join();
        }
        Console.WriteLine("RESULT " + JsonSerializer.Serialize(result));
        return 0;
    }

    /// <summary>Run every case, each in a child process, so a .NET stack overflow is recorded and not fatal.</summary>
    public static int RunAll(string? onlyLibrary, string? onlyCase, string resultsPath)
    {
        string self = Assembly.GetEntryAssembly()!.Location;
        var results = new List<CaseResult>();
        foreach (var (library, _, attribute) in AllCases())
        {
            if (onlyLibrary != null && library != onlyLibrary) continue;
            if (onlyCase != null && !attribute.Name.StartsWith(onlyCase, StringComparison.Ordinal)) continue;
            CaseResult result = RunChild(self, library, attribute);
            if (attribute.Name == "c11-pairs-order" && result.Verdict == "info")
            {
                // Criterion 11 compares two processes: run the case again and compare the orders.
                CaseResult second = RunChild(self, library, attribute);
                bool same = second.Detail == result.Detail;
                bool hashesDiffer = result.Metrics?["string_hash"] != second.Metrics?["string_hash"];
                result = result with
                {
                    Verdict = same ? "pass" : "fail",
                    Detail = (same ? "same order in two processes" : "the order differs between two processes")
                             + (hashesDiffer ? "; .NET string hashes differed between them" : "; .NET string hashes were equal")
                             + $"; order digest {result.Detail} / {second.Detail}",
                };
            }
            results.Add(result);
            Console.WriteLine($"{result.Verdict,-8} {library,-10} {attribute.Name,-26} {result.Detail}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);
        File.WriteAllText(resultsPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"results: {resultsPath}");
        return 0;
    }

    static CaseResult RunChild(string self, string library, CaseAttribute attribute)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(self);
        start.ArgumentList.Add("one");
        start.ArgumentList.Add(library);
        start.ArgumentList.Add(attribute.Name);
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            return new CaseResult(library, attribute.Name, attribute.Criterion, "fail", "the child process ran for 120 s and was killed");
        }
        string output = stdout.Result;
        string? line = output.Split('\n').LastOrDefault(l => l.StartsWith("RESULT ", StringComparison.Ordinal));
        if (process.ExitCode == 0 && line != null)
            return JsonSerializer.Deserialize<CaseResult>(line["RESULT ".Length..])!;
        string tail = string.Join(" | ", stderr.Result.Split('\n').Where(l => l.Trim().Length > 0).Take(3));
        string exit = unchecked((uint)process.ExitCode) == 0xC00000FD ? "0xC00000FD (stack overflow)" : $"0x{process.ExitCode:X8}";
        return new CaseResult(library, attribute.Name, attribute.Criterion, "fail", $"the process ended, exit {exit}: {tail}");
    }

    /// <summary>The address of a local in the caller's frame, to measure how much C# stack one nesting level uses.</summary>
    public static unsafe long StackMark()
    {
        int local = 0;
        return (long)&local;
    }

    /// <summary>Time <paramref name="body"/> over <paramref name="count"/> iterations; nanoseconds and bytes allocated
    /// per iteration, from the best of five rounds. The warm-up runs past tiered compilation (at least 30,000 calls
    /// and 500 ms), so every measurement times tier-1 code whatever order the cases take them in.</summary>
    public static (double Nanoseconds, double Bytes) Measure(int count, Action body)
    {
        var warm = Stopwatch.StartNew();
        for (int i = 0; i < 30_000 || warm.ElapsedMilliseconds < 500; i++) body();
        double best = double.MaxValue, bytes = 0;
        for (int round = 0; round < 5; round++)
        {
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) body();
            watch.Stop();
            double nanoseconds = watch.Elapsed.TotalMilliseconds * 1e6 / count;
            if (nanoseconds < best)
            {
                best = nanoseconds;
                bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / count;
            }
        }
        return (best, bytes);
    }

    /// <summary>Run <paramref name="body"/> on its own thread; false when it has not ended after
    /// <paramref name="milliseconds"/> (the thread is left running and the process must end).</summary>
    public static bool RunWithin(int milliseconds, Action body, out Exception? error)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { caught = e; }
        }, StackBytes) { IsBackground = true };
        thread.Start();
        bool ended = thread.Join(milliseconds);
        error = caught;
        return ended;
    }

    /// <summary>A digest of an iteration order, so two processes can compare it.</summary>
    public static string Digest(IEnumerable<string> keys)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", keys)));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>Criterion 11's table: string keys inserted, some removed and some put back, plus integer keys.</summary>
    public const string PairsOrderSource = """
        local t = {}
        for i = 1, 2000 do t["key" .. i] = i end
        for i = 1, 2000, 4 do t["key" .. i] = nil end
        for i = 1, 400, 2 do t["back" .. i] = i end
        for i = 1, 300 do t[i * 7] = i end
        local keys = {}
        for k in pairs(t) do keys[#keys + 1] = tostring(k) end
        return table.concat(keys, ",")
        """;

    /// <summary>A pattern that backtracks: many a* against a run of a's with no b. Reference Lua takes very long.</summary>
    public const string BacktrackSource = """
        local s = string.rep("a", 28)
        local p = string.rep("a*", 28) .. "b"
        return string.find(s, p)
        """;

    /// <summary>The globals that the sandbox removes (spec §5.3).</summary>
    public static readonly string[] Removed = ["io", "os", "debug", "package", "load", "loadstring", "dofile", "loadfile", "collectgarbage"];

    /// <summary>The library tables each mod gets its own copy of (spec §5.2).</summary>
    public static readonly string[] LibraryTables = ["string", "table", "math", "bit32", "coroutine"];

    /// <summary>The basic functions the sandbox keeps (spec §5.3).</summary>
    public static readonly string[] Basics = ["assert", "error", "pcall", "xpcall", "select", "type", "tostring", "tonumber",
        "pairs", "ipairs", "next", "rawget", "rawset", "rawequal", "setmetatable", "getmetatable", "unpack"];
}
