using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lua;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;
using MoonSharp.Interpreter;

namespace Ome.Scripting.Lua.Spike;

/// <summary>One run of one file of the official Lua 5.2 test suite.</summary>
/// <param name="Mode">strict: the file as written, so the first failed assert ends it; soft: a failed assert is recorded
/// and the file goes on, so a run lists every failed assert up to the first error that is not an assert.</param>
/// <param name="Prepared">The text went through <c>LuaSource.Prepare</c>, as a mod's text does.</param>
/// <param name="Outcome">pass, assert (soft only: ran to the end with failed asserts), error (a runtime error),
/// compile (the file did not compile), crash (the process ended) or hang (killed after the time limit).</param>
public sealed record ConformanceResult(string Library, string File, string Strings, string Mode, bool Prepared,
    string Outcome, string Detail, List<string> FailedAsserts, double Seconds);

/// <summary>Runs the official Lua 5.2 test suite (https://www.lua.org/tests/), unmodified, against the spike's
/// libraries: the language conformance that the spike's criteria (spec §5.1) do not measure. The files are read as
/// Latin-1, one character per byte, as reference Lua reads them. Each run is a child process on an 8 MiB thread, the
/// engine thread's size, so a .NET stack overflow or a hang is a result and does not end the run.</summary>
public static class Conformance
{
    public static readonly string[] Files =
    [
        "constructs", "literals", "locals", "goto", "vararg", "closure", "calls", "events", "nextvar", "sort",
        "strings", "pm", "math", "bitwise", "coroutine", "errors", "big", "verybig", "attrib", "gc", "db", "files",
    ];

    // assert as the soft mode replaces it: a failed assert records the line of its caller and of the caller's caller,
    // and returns its arguments as a passing assert does.
    const string SoftAssertLuaCSharp = """
        local getinfo, record, tostring, select = debug.getinfo, __conformance_record, tostring, select
        function assert(v, ...)
          if not v then
            local a, b = getinfo(2, "l"), getinfo(3, "l")
            local message = select('#', ...) > 0 and tostring((...)) or "assertion failed!"
            record((a and a.currentline or -1) .. "/" .. (b and b.currentline or -1) .. " " .. message)
          end
          return v, ...
        end
        """;

    // Records where Lua-CSharp is, so a hang reports its line. Only a rerun of a file that hung installs it: the hook
    // would change what db.lua sees.
    const string LocateLuaCSharp = """
        local getinfo, at = debug.getinfo, __conformance_at
        debug.sethook(function() local i = getinfo(2, "Sl") if i then at(i.short_src .. ":" .. tostring(i.currentline)) end end, "", 1000)
        """;

    static volatile string? s_where;

    // The "past" mode: soft, plus what steps over a stop so the rest of a file runs. Each stop was found and recorded
    // first (plan §5); none of these hides a failed assert. The shims give what the tests take for granted and a mod
    // never has: a package table (nextvar.lua clears the globals not in package.loaded) and load with a reader
    // function (calls.lua), which Lua-CSharp 0.5.8 does not implement.
    const string PastShims = """
        package = package or {loaded = {_G = _G, string = string, table = table, math = math, bit32 = bit32,
          coroutine = coroutine, io = io, os = os, debug = debug}}
        package.loaded.package = package.loaded.package or package   -- 0.5.8's package.loaded lacks it
        do
          local rawload, concat = load, table.concat
          function load(chunk, ...)
            if type(chunk) == "function" then
              local parts = {}
              while true do
                local piece = chunk()
                if piece == nil or piece == "" then break end
                if type(piece) ~= "string" then return nil, "reader function must return a string" end
                parts[#parts + 1] = piece
              end
              chunk = concat(parts)
            end
            return rawload(chunk, ...)
          end
        end
        """;

    // A line the past mode replaces: its number, text it must hold, and its replacement (one line for one).
    static readonly Dictionary<string, (int Line, string Holds, string With)[]> PastPatches = new()
    {
        // The loop waits for the collector to clear a weak entry, which Lua-CSharp never does.
        ["closure"] = [(26, "while x[1] do", "while false do   -- harness: weak entries are never cleared")],
        // The multiple assignment raises an error; run it protected so the assert after it reports.
        ["attrib"] = [(385, "i, a[i], a, j, a[j], a[i+j] = j, i, i, b, j, i",
            "if not pcall(function() i, a[i], a, j, a[j], a[i+j] = j, i, i, b, j, i end) then assert(false, 'raised') end")],
        // string.format raises where Lua 5.2 truncates; keep each an assert.
        ["strings"] =
        [
            (151, "string.format(\"%x\", 0.3)", "assert(select(2, pcall(string.format, \"%x\", 0.3)) == \"0\")"),
            (152, "string.format(\"%02x\", 0.1)", "assert(select(2, pcall(string.format, \"%02x\", 0.1)) == \"00\")"),
        ],
    };

    static string Patch(string file, string text)
    {
        if (!PastPatches.TryGetValue(file, out var patches)) return text;
        string[] lines = text.Split('\n');
        foreach (var (line, holds, with) in patches)
        {
            if (!lines[line - 1].Contains(holds, StringComparison.Ordinal))
                throw new InvalidOperationException($"{file}.lua:{line} does not hold '{holds}'");
            lines[line - 1] = with;
        }
        return string.Join('\n', lines);
    }

    public static int RunAll(string testsDirectory, string? onlyLibrary, string? onlyFile, string strings, string resultsPath,
        int timeoutSeconds)
    {
        string self = Assembly.GetEntryAssembly()!.Location;
        var results = new List<ConformanceResult>();
        foreach (string file in Files)
        {
            if (onlyFile != null && file != onlyFile) continue;
            foreach (string library in new[] { "luacsharp", "moonsharp" })
            {
                if (onlyLibrary != null && library != onlyLibrary) continue;
                // A file that LuaSource.Prepare leaves unchanged runs once, raw: its prepared run would be the same.
                string text = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(testsDirectory, file + ".lua")));
                bool changes = LuaSource.Prepare(text) != text;
                foreach (bool prepared in library == "luacsharp" && changes ? new[] { false, true } : new[] { false })
                    foreach (string mode in new[] { "strict", "soft", "past" })
                    {
                        ConformanceResult result = RunChild(self, testsDirectory, library, file, strings, mode, prepared,
                            timeoutSeconds, locate: false);
                        if (result.Outcome == "hang" && library == "luacsharp")
                        {
                            ConformanceResult located = RunChild(self, testsDirectory, library, file, strings, mode, prepared,
                                timeoutSeconds, locate: true);
                            if (located.Outcome == "hang") result = result with { Detail = located.Detail };
                        }
                        results.Add(result);
                        Console.WriteLine($"{library,-10} {file,-11} {(prepared ? "prepared" : "raw"),-9} {mode,-7} {result.Outcome,-8} " +
                                          $"{result.FailedAsserts.Count,3} asserts  {result.Seconds,6:0.0} s  {Shorten(result.Detail, 110)}");
                    }
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultsPath))!);
        File.WriteAllText(resultsPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"results: {resultsPath}");
        return 0;
    }

    static string Shorten(string text, int length)
    {
        string line = text.ReplaceLineEndings(" | ");
        return line.Length <= length ? line : line[..length] + "…";
    }

    static ConformanceResult RunChild(string self, string testsDirectory, string library, string file, string strings,
        string mode, bool prepared, int timeoutSeconds, bool locate)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = testsDirectory,
        };
        foreach (string argument in new[] { self, "conformance-one", library, file, strings, mode, prepared ? "prepared" : "raw",
                     timeoutSeconds.ToString(), locate ? "locate" : "-" })
            start.ArgumentList.Add(argument);
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((timeoutSeconds + 30) * 1000))
        {
            process.Kill(entireProcessTree: true);
            return new ConformanceResult(library, file, strings, mode, prepared, "hang",
                $"killed after {timeoutSeconds + 30} s", [], timeoutSeconds + 30);
        }
        string? line = stdout.Result.Split('\n').LastOrDefault(l => l.StartsWith("RESULT ", StringComparison.Ordinal));
        if (line != null) return JsonSerializer.Deserialize<ConformanceResult>(line["RESULT ".Length..])!;
        string tail = string.Join(" | ", stderr.Result.Split('\n').Where(l => l.Trim().Length > 0).Take(3));
        string exit = unchecked((uint)process.ExitCode) == 0xC00000FD ? "0xC00000FD (stack overflow)" : $"0x{process.ExitCode:X8}";
        return new ConformanceResult(library, file, strings, mode, prepared, "crash", $"exit {exit}: {tail}", [],
            watch.Elapsed.TotalSeconds);
    }

    public static int RunOne(string library, string file, string strings, string mode, bool prepared, int timeoutSeconds,
        bool locate)
    {
        string text = Encoding.Latin1.GetString(File.ReadAllBytes(file + ".lua"));
        if (mode == "past") text = Patch(file, text);
        if (prepared) text = LuaSource.Prepare(text);
        var failed = new List<string>();
        string outcome = "pass", detail = "";
        var watch = Stopwatch.StartNew();
        void Body()
        {
            try
            {
                if (library == "luacsharp") RunLuaCSharp(file, text, strings, mode, failed, locate);
                else RunMoonSharp(file, text, mode, failed);
                if (failed.Count > 0) { outcome = "assert"; detail = failed[0]; }
            }
            catch (LuaCompileException error) { outcome = "compile"; detail = error.Message; }
            catch (LuaParseException error) { outcome = "compile"; detail = error.Message; }
            catch (LuaRuntimeException error)
            {
                outcome = "error";
                detail = error.Message + (error.LuaTraceback is { } trace ? "\n" + string.Join("\n", trace.ToString().Split('\n').Take(4)) : "");
            }
            catch (SyntaxErrorException error) { outcome = "compile"; detail = error.DecoratedMessage ?? error.Message; }
            catch (InterpreterException error) { outcome = "error"; detail = error.DecoratedMessage ?? error.Message; }
            catch (Exception error) { outcome = "error"; detail = $"{error.GetType().Name}: {error.Message}"; }
        }
        var thread = new Thread(Body, 8 * 1024 * 1024) { IsBackground = true };
        thread.Start();
        if (!thread.Join(timeoutSeconds * 1000))
        {
            var hung = new ConformanceResult(library, file, strings, mode, prepared, "hang",
                $"no end after {timeoutSeconds} s" + (s_where is { } where ? $", running {where}" : ""), [.. failed],
                watch.Elapsed.TotalSeconds);
            Console.WriteLine("RESULT " + JsonSerializer.Serialize(hung));
            Console.Out.Flush();
            Environment.Exit(0);
        }
        var result = new ConformanceResult(library, file, strings, mode, prepared, outcome, detail, failed,
            watch.Elapsed.TotalSeconds);
        Console.WriteLine("RESULT " + JsonSerializer.Serialize(result));
        return 0;
    }

    static void RunLuaCSharp(string file, string text, string strings, string mode, List<string> failed, bool locate)
    {
        using LuaState state = CreateLuaState(strings == "bytes");
        state.OpenStandardLibraries();
        state.Environment["_port"] = true;
        state.Environment["_soft"] = true;
        if (mode == "past") Execute(state, state.Load(PastShims, "@shims"));
        if (mode is "soft" or "past")
        {
            state.Environment["__conformance_record"] = new LuaFunction("__conformance_record", (context, _) =>
            {
                lock (failed) failed.Add(context.GetArgument<string>(0));
                return new ValueTask<int>(0);
            });
            Execute(state, state.Load(SoftAssertLuaCSharp, "@soft-assert"));
        }
        if (locate)
        {
            state.Environment["__conformance_at"] = new LuaFunction("__conformance_at", (context, _) =>
            {
                s_where = context.GetArgument<string>(0);
                return new ValueTask<int>(0);
            });
            Execute(state, state.Load(LocateLuaCSharp, "@locate"));
        }
        LuaClosure closure = strings == "bytes"
            ? state.Load(LuaByteString.ToBytes(text), "@" + file + ".lua")
            : state.Load(text, "@" + file + ".lua");
        Execute(state, closure);
    }

    /// <summary>A Lua-CSharp state. CONFORMANCE_NUMBERS=lua selects Lua 5.2's number format, an option of the library's
    /// main branch after 0.5.8 (LuaPlatform.NumberFormat), set by reflection so one build runs against either.</summary>
    internal static LuaState CreateLuaState(bool bytes)
    {
        LuaPlatform platform = bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default;
        if (Environment.GetEnvironmentVariable("CONFORMANCE_NUMBERS") == "lua")
        {
            PropertyInfo property = typeof(LuaPlatform).GetProperty("NumberFormat")
                ?? throw new InvalidOperationException("this Lua-CSharp has no LuaPlatform.NumberFormat");
            object copy = typeof(LuaPlatform).IsValueType
                ? platform
                : typeof(LuaPlatform).GetMethod("<Clone>$")!.Invoke(platform, null)!;
            property.SetValue(copy, Enum.Parse(property.PropertyType, "Lua"));
            platform = (LuaPlatform)copy;
        }
        return LuaState.Create(platform);
    }

    static void Execute(LuaState state, LuaClosure closure)
    {
        ValueTask<LuaValue[]> task = state.ExecuteAsync(closure, CancellationToken.None);
        task.AsTask().GetAwaiter().GetResult();
    }

    static void RunMoonSharp(string file, string text, string mode, List<string> failed)
    {
        var script = new Script(CoreModules.Preset_Complete);
        script.Globals["_port"] = true;
        script.Globals["_soft"] = true;
        if (mode is "soft" or "past")
        {
            script.Globals["assert"] = (Func<ScriptExecutionContext, CallbackArguments, DynValue>)((context, arguments) =>
            {
                DynValue v = arguments.Count > 0 ? arguments[0] : DynValue.Nil;
                if (!v.CastToBool())
                {
                    string where = context.CallingLocation is { } at ? at.FromLine.ToString() : "-1";
                    string message = arguments.Count > 1 ? arguments[1].ToPrintString() : "assertion failed!";
                    failed.Add(where + " " + message);
                }
                return DynValue.NewTuple(arguments.GetArray());
            });
        }
        script.DoString(text, null, file + ".lua");
    }
}
