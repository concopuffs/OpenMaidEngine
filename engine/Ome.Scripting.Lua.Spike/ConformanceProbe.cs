using System.Text;
using Lua;
using Lua.Runtime;
using Lua.Standard;
using MoonSharp.Interpreter;

namespace Ome.Scripting.Lua.Spike;

/// <summary>What the official suite leaves out (plan §5): numerals with an operator or a bracket directly after them,
/// which Lua-CSharp 0.5.8's lexer refuses, and a list of small chunks with the result Lua 5.2 gives. Each chunk runs on
/// its own, raw and through <c>LuaSource.Prepare</c>, so one failure hides nothing else.</summary>
public static class ConformanceProbe
{
    const string CompileError = "(compile error)";

    // A chunk and what Lua 5.2 returns from it, as tostring prints it.
    static readonly (string Source, string Expected)[] Fixed =
    [
        // Numerals the hex rule reads.
        ("return 0xe-2", "12"),                 // 'e' is a hex digit, not an exponent
        ("return 0x1e+1", "31"),
        ("return 1e+1", "10"),
        ("return 0x1p4", "16"),
        ("return 0x.8", "0.5"),
        ("return 0xA.8p1", "21"),
        ("return 0x10P-1", "8"),
        ("return 5.", "5"),
        ("return .5", "0.5"),
        ("return 0xffffffffffffffff", "1.844674407371e+19"),
        ("return 0x7fffffff + 1", "2147483648"),
        ("return 1..2", CompileError),          // malformed number in Lua 5.2
        ("return 0x", CompileError),
        ("return 3x", CompileError),
        // Strings and long brackets.
        ("return \"\\x41\\65\\066\\z\n     C\"", "AABC"),
        ("return #\"a\\\nb\"", "3"),
        ("return [[\nx]]", "x"),
        ("return [==[a]]b]==]", "a]]b"),
        ("return '\\'' .. \"\\\"\"", "'\""),
        ("return \"\\u{48}\"", CompileError),   // \u is Lua 5.3
        ("return '\\q'", CompileError),
        ("return 'a\nb'", CompileError),
        // Comments.
        ("return 1--[[c]]+1", "2"),
        ("return 1 --[==[ x ]==] + 1", "2"),
        ("--[ not long\nreturn 3", "3"),
        ("return 4 -- trailing", "4"),
        ("return 1;", "1"),
        ("return", "nil"),
        ("do return 5 end", "5"),
        // Precedence.
        ("return 2^3^2", "512"),
        ("return -2^2", "-4"),
        ("return 2^-1", "0.5"),
        ("return 1 .. 2", "12"),
        ("return not nil == true", "true"),
        ("return 1 < 2 == true", "true"),
        ("return \"a\" .. \"b\" == \"ab\"", "true"),
        ("return 2 * 3 % 4", "2"),
        ("return - - 2", "2"),
        ("return #\"abc\" + 1", "4"),
        ("return 1 + 2 .. 3", "33"),
        // Arithmetic.
        ("return 7 % -3", "-2"),
        ("return -7 % 3", "2"),
        ("return 5.5 % 2", "1.5"),
        ("return 3 == 3.0", "true"),
        ("return math.floor(-0.5)", "-1"),
        ("return 1/0", "inf"),
        ("return -1/0", "-inf"),
        ("return 2^53 + 1 == 2^53", "true"),
        // How numbers print: Lua 5.2 uses "%.14g".
        ("return 1/3", "0.33333333333333"),
        ("return 100", "100"),
        ("return 1e15", "1e+15"),
        ("return 1e14", "1e+14"),
        ("return 2^53", "9.007199254741e+15"),
        ("return 2^63", "9.2233720368548e+18"),
        ("return 0.1", "0.1"),
        ("return -0.0", "-0"),
        ("return 1e100", "1e+100"),
        ("return 3.0", "3"),
        ("return 2^31", "2147483648"),
        ("return \"x\" .. 0.5", "x0.5"),
        ("return \"x\" .. 10 / 2", "x5"),
        ("return string.format(\"%d\", 3.0)", "3"),
        ("return string.format(\"%5.2f\", 3.14159)", " 3.14"),
        ("return string.format(\"%g\", 1e20)", "1e+20"),
        ("return string.format(\"%x\", 255)", "ff"),
        ("return string.format(\"%5s|%-5s|\", \"a\", \"b\")", "    a|b    |"),
        ("return string.format(\"%q\", \"a\\nb\")", "\"a\\\nb\""),
        ("return string.format(\"%s\", 1/3)", "0.33333333333333"),
        ("return string.format(\"%d\", 2.5)", "2"),          // Lua 5.2 truncates; 5.3 raises
        ("return string.format(\"%5.1f%%\", 12.345)", " 12.3%"),
        ("return string.format('%q', 'a\\0b')", "\"a\\0b\""),
        ("return tostring(0.1 + 0.2)", "0.3"),
        // tonumber.
        ("return tonumber(\"0x10\")", "16"),
        ("return tonumber(\"  10  \")", "10"),
        ("return tonumber(\"1e1\")", "10"),
        ("return tonumber(\"0x\")", "nil"),
        ("return tonumber(\"10\", 16)", "16"),
        ("return tonumber(\"z\", 36)", "35"),
        ("return tonumber(\"8\", 8)", "nil"),
        ("return tonumber(\"1e\")", "nil"),
        ("return tonumber(\"\")", "nil"),
        ("return \"10\" + 1", "11"),
        ("return \"0x10\" * 1", "16"),
        // Tables and keys.
        ("local t = {} t[1.0] = 'a' return t[1]", "a"),
        ("local t = {10, 20, 30, nil} return #t", "3"),
        ("local t = {[1] = 'a', 'b'} return t[1]", "b"),
        ("local t = {f = function(self) return self.v end, v = 7} return t:f()", "7"),
        ("local t = setmetatable({}, {__index = function(_, k) return k * 2 end}) return t[21]", "42"),
        ("return select('#', nil, nil)", "2"),
        ("return select(-1, 1, 2, 3)", "3"),
        ("return table.concat({1, 2, 3}, ',')", "1,2,3"),
        ("return table.unpack({1, 2, 3}, 2)", "2"),
        // Strings.
        ("return (\"x\"):rep(3)", "xxx"),
        ("return (\"abc\"):sub(-2)", "bc"),
        ("return (\"hello world\"):find(\"o w\")", "5"),
        ("return (\"a,b,c\"):gsub(\",\", \";\")", "a;b;c"),
        ("return (\"key=val\"):match(\"(%w+)=(%w+)\")", "key"),
        ("return (\"%d\"):format(3)", "3"),
        ("return (\"abc\"):upper()", "ABC"),
        ("return (\"abc\"):byte(2)", "98"),
        ("return string.char(72, 105)", "Hi"),
        ("return (\"  trim  \"):match(\"^%s*(.-)%s*$\")", "trim"),
        ("return (\"x\"):rep(3, \",\")", "x,x,x"),
        ("return \"Z\" < \"a\"", "true"),
        // Control flow.
        ("goto a; do return 0 end ::a:: return 1", "1"),
        ("local s = 0 for i = 1, 3 do s = s + i end return s", "6"),
        ("local s = 0 for i = 3, 1, -1 do s = s * 10 + i end return s", "321"),
        ("local s = 0 for i = 1, 0 do s = 1 end return s", "0"),
        ("local s = 0 for i = 0.1, 0.35, 0.1 do s = s + 1 end return s", "3"),
        ("local n = 0 repeat local x = n n = n + 1 until x >= 2 return n", "3"),
        ("local s = '' for k, v in ipairs({'a', 'b', nil, 'd'}) do s = s .. v end return s", "ab"),
        ("local function f(...) return select('#', ...) end return f(nil, nil, nil)", "3"),
        ("local function f(...) local a, b = ... return b end return f(1, 2)", "2"),
        ("local a = {} for i = 1, 3 do a[i] = function() return i end end return a[1]() + a[3]()", "4"),
        ("local x = 1 do local x = 2 end return x", "1"),
        ("local t = {} t.x, t.y = 1 return t.y", "nil"),
        ("local a, b = 1, 2 a, b = b, a return a", "2"),
        ("local a, i = {}, 3 i, a[i] = i + 1, 20 return a[3]", "20"),
        // A table and key in a target are evaluated before any assignment (attrib.lua:385).
        ("local a = {'a', 'b'} local b, i = a, 1 i, a[i], a = 2, 'x', 1 return b[1]", "x"),
        ("return pcall(error, {code = 1})", "false"),
        ("return select(2, pcall(error, 'm', 0))", "m"),
        ("return select(2, pcall(error))", "nil"),
        ("return (select(2, pcall(function() local x = nil; return x.y end))):match('attempt to index')", "attempt to index"),
        ("return bit32.band(0xff, 0x0f)", "15"),
        ("return bit32.bnot(0)", "4294967295"),
        ("return bit32.lshift(1, 31)", "2147483648"),
        ("return bit32.extract(0xf0, 4, 4)", "15"),
        ("return bit32.arshift(0x80000000, 1)", "3221225472"),
        ("return coroutine.wrap(function(a) local b = coroutine.yield(a + 1) return b * 2 end)(1)", "2"),
        ("return math.max(3, 7, 5)", "7"),
        ("return math.fmod(7, 3)", "1"),
        ("return math.fmod(-7, 3)", "-1"),
        ("return math.huge > 1e308", "true"),
        ("return math.pi", "3.1415926535898"),
        ("return math.abs(-0.0)", "0"),
        ("return math.modf(-3.5)", "-3"),
        ("return math.ceil(3.2)", "4"),
        // What Lua 5.2 does not have.
        ("return 7 // 2", CompileError),
        ("return 1 & 3", CompileError),
        ("return 1 | 2", CompileError),
        ("return 1 << 2", CompileError),
        ("return ~1", CompileError),
        ("local a <const> = 1 return a", CompileError),
        ("return math.tointeger", "nil"),
        ("return utf8", "nil"),
        ("return string.pack", "nil"),
    ];

    static readonly string[] Numerals = ["16", "0x10", "0X10", "0xa", "0x1p4", "0x10P0", "1e1", "1.6e1", "0.5e1", ".5", "5.", "0x.8p5", "1E1"];
    static readonly string[] Operators = ["+", "-", "*", "/", "%", "^", "==", "~=", "<", "<=", ">", ">="];

    public static int Run(string? onlyLibrary)
    {
        var cases = new List<(string Source, string Expected, string Group)>();
        foreach (var (source, expected) in Fixed) cases.Add((source, expected, "fixed"));
        // A numeral and an operator with no space between them reads as the same expression spaced out.
        foreach (string numeral in Numerals)
        {
            foreach (string op in Operators)
                cases.Add(($"return {numeral}{op}2", Reference($"return ({numeral}) {op} (2)"), "numeral"));
            cases.Add(($"local t = {{[{numeral}] = 'k'}} return t[{numeral}]", "k", "numeral"));
            cases.Add(($"local t = {{{numeral};{numeral}}} return #t", "2", "numeral"));
            cases.Add(($"local x = {numeral}; return x", Reference($"return ({numeral})"), "numeral"));
            cases.Add(($"return {numeral}--c", Reference($"return ({numeral})"), "numeral"));
            cases.Add(($"return ({numeral})", Reference($"return ({numeral})"), "numeral"));
            cases.Add(($"return math.max({numeral},1)", Reference($"return math.max(({numeral}), 1)"), "numeral"));
        }

        foreach (string library in new[] { "luacsharp", "moonsharp" })
        {
            if (onlyLibrary != null && library != onlyLibrary) continue;
            foreach (bool prepared in library == "luacsharp" ? new[] { false, true } : new[] { false })
            {
                int failed = 0;
                var byGroup = new Dictionary<string, (int Pass, int Fail)>();
                foreach (var (source, expected, group) in cases)
                {
                    string actual = Evaluate(library, prepared ? LuaSource.Prepare(source) : source);
                    bool pass = actual == expected;
                    var (p, f) = byGroup.GetValueOrDefault(group);
                    byGroup[group] = pass ? (p + 1, f) : (p, f + 1);
                    if (pass) continue;
                    failed++;
                    Console.WriteLine($"{library,-10} {(prepared ? "prepared" : "raw"),-9} FAIL {Escape(source),-60} expected {Escape(expected)}, got {Escape(actual)}");
                }
                Console.WriteLine($"{library,-10} {(prepared ? "prepared" : "raw"),-9} " +
                                  string.Join(", ", byGroup.Select(g => $"{g.Key} {g.Value.Pass}/{g.Value.Pass + g.Value.Fail}")) +
                                  $"; {failed} failed");
            }
        }
        return 0;
    }

    // The expected value of a spaced-out numeral expression: the arithmetic of IEEE doubles, printed as %.14g is.
    static string Reference(string spaced) => Evaluate("luacsharp", spaced);

    static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\n", "\\n");

    internal static string Evaluate(string library, string source)
    {
        // The extra parentheses adjust the chunk's results to one value, so a chunk that returns nothing reads "nil".
        string wrapped = "return tostring(((function()\n" + source + "\nend)()))";
        string result = "";
        var thread = new Thread(() =>
        {
            try
            {
                if (library == "luacsharp")
                {
                    using LuaState state = Conformance.CreateLuaState(bytes: false);
                    state.OpenStandardLibraries();
                    LuaClosure closure;
                    try { closure = state.Load(wrapped, "probe"); }
                    catch (Exception error) when (error is LuaCompileException or LuaParseException) { result = CompileError; return; }
                    LuaValue[] values = state.ExecuteAsync(closure, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                    result = values.Length > 0 ? values[0].ToString() : "(nothing)";
                }
                else
                {
                    var script = new Script(CoreModules.Preset_Complete);
                    DynValue chunk;
                    try { chunk = script.LoadString(wrapped, null, "probe"); }
                    catch (SyntaxErrorException) { result = CompileError; return; }
                    result = script.Call(chunk).CastToString() ?? "(nothing)";
                }
            }
            catch (Exception error) { result = $"({error.GetType().Name}: {error.Message.Split('\n')[0]})"; }
        }, 8 * 1024 * 1024);
        thread.Start();
        thread.Join();
        return result;
    }
}
