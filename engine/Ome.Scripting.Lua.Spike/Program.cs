using Ome.Scripting.Lua.Spike;

// Usage:
//   dotnet Ome.Scripting.Lua.Spike.dll run [--lib luacsharp|moonsharp] [--case PREFIX] [--out PATH]
//   dotnet Ome.Scripting.Lua.Spike.dll one LIB CASE        (one case in this process; the runner calls it)
//   dotnet Ome.Scripting.Lua.Spike.dll list
//   dotnet Ome.Scripting.Lua.Spike.dll conformance [--tests DIR] [--lib L] [--file NAME] [--strings utf16|bytes] [--timeout S] [--out PATH]
// SPIKE_STACK_KB sets the stack of the thread the cases run on (default 1024).
// CONFORMANCE_CULTURE (de-DE, fr-FR, …) sets the culture of every thread, to show whether number text depends on it.
Console.OutputEncoding = System.Text.Encoding.UTF8;
if (Environment.GetEnvironmentVariable("CONFORMANCE_CULTURE") is { Length: > 0 } cultureName)
{
    var culture = new System.Globalization.CultureInfo(cultureName);
    System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
    System.Globalization.CultureInfo.CurrentCulture = culture;
}
string command = args.Length > 0 ? args[0] : "run";
string? Option(string name) => Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
switch (command)
{
    case "one":
        return Spike.RunOne(args[1], args[2]);
    case "conformance":
        // The official Lua 5.2 test suite, unmodified, against both libraries (plan §5).
        return Conformance.RunAll(Path.GetFullPath(Option("--tests") ?? "build/lua-conformance/official/lua-5.2.2-tests"),
            Option("--lib"), Option("--file"), Option("--strings") ?? "utf16",
            Option("--out") ?? Path.GetFullPath("build/lua-conformance/results.json"),
            int.Parse(Option("--timeout") ?? "60"));
    case "conformance-probe":
        // What the suite leaves out: numerals before operators, and chunks with Lua 5.2's results (plan §5).
        return ConformanceProbe.Run(Option("--lib"));
    case "conformance-eval":
        // One chunk on both libraries, each line of the chunk's results as tostring prints it.
        foreach (string library in new[] { "luacsharp", "moonsharp" })
            Console.WriteLine($"{library,-10} {ConformanceProbe.Evaluate(library, args[1])}");
        return 0;
    case "conformance-one":
        return Conformance.RunOne(args[1], args[2], args[3], args[4], args[5] == "prepared", int.Parse(args[6]),
            args[7] == "locate");
    case "exceptions":
        // What LuaCSharp throws and how its exceptions are built (for criteria 2 and 3).
        foreach (Type type in typeof(Lua.LuaState).Assembly.GetTypes().Where(t => typeof(Exception).IsAssignableFrom(t)))
        {
            Console.WriteLine($"{type.FullName} : {type.BaseType?.FullName} public={type.IsPublic}");
            foreach (var ctor in type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                Console.WriteLine($"  ctor({string.Join(", ", ctor.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))}) public={ctor.IsPublic}");
        }
        return 0;
    case "limits":
        // Any limit LuaCSharp's public API offers beyond the hook and the token (§15: string length, state size).
        foreach (Type type in typeof(Lua.LuaState).Assembly.GetExportedTypes())
            foreach (var member in type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
                if (System.Text.RegularExpressions.Regex.IsMatch(member.Name, "Max|Limit|Memory|Alloc|Hook|Depth|Budget|Quota", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    Console.WriteLine($"{type.FullName}.{member.Name} ({member.MemberType})");
        return 0;
    case "list":
        foreach (var (library, _, attribute) in Spike.AllCases()) Console.WriteLine($"{library} {attribute.Name} (criterion {attribute.Criterion})");
        return 0;
    default:
        string output = Option("--out") ?? Path.Combine(AppContext.BaseDirectory, "lua-spike-results.json");
        return Spike.RunAll(Option("--lib"), Option("--case"), output);
}
