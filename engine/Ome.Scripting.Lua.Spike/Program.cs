using Ome.Scripting.Lua.Spike;

// Usage:
//   dotnet Ome.Scripting.Lua.Spike.dll run [--lib luacsharp|moonsharp] [--case PREFIX] [--out PATH]
//   dotnet Ome.Scripting.Lua.Spike.dll one LIB CASE        (one case in this process; the runner calls it)
//   dotnet Ome.Scripting.Lua.Spike.dll list
// SPIKE_STACK_KB sets the stack of the thread the cases run on (default 1024).
Console.OutputEncoding = System.Text.Encoding.UTF8;
string command = args.Length > 0 ? args[0] : "run";
string? Option(string name) => Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
switch (command)
{
    case "one":
        return Spike.RunOne(args[1], args[2]);
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
