using System.Globalization;
using System.Text;
using System.Text.Json;
using Lua;
using Ome.Engine.Modding;

namespace Ome.Scripting.Lua;

/// <summary>A state that cannot be written or read (spec §8.3, §11); the message names the place in it.</summary>
internal sealed class LuaStateException(string message) : Exception(message);

/// <summary>
/// <c>maid.state</c> as JSON (spec §11). JSON and a Lua table differ in four ways, which this form settles:
/// <list type="bullet">
/// <item>an object's keys are strings, so each key is marked: <c>"s:name"</c> for a string, <c>"i:3"</c> for an
/// integer. The keys <c>1</c> and <c>"1"</c> are then two keys;</item>
/// <item>a table is written as an array only when its keys are 1 to n with no hole (n at least 1);</item>
/// <item>a number that is not finite has no JSON form, and is refused;</item>
/// <item>keys are written in one order, integers ascending and then strings in ordinal order, so the same state gives the
/// same text.</item>
/// </list>
/// A table is read raw: a metatable is neither consulted nor saved. A table reached twice is written twice and read back
/// as two tables; a table inside itself is a cycle, and refused.
/// </summary>
internal static class LuaStateCodec
{
    private const double MaxIntegerKey = 9007199254740992;   // 2^53: every integer up to it is exact in a double

    public static string Encode(LuaValue state, ModLimits limits)
    {
        if (state.Type == LuaValueType.Nil) return "{}";
        if (state.Type != LuaValueType.Table)
            throw new LuaStateException($"maid.state is a {state.TypeToString()}; it must be a table");
        var writer = new Writer(limits);
        writer.Table(state.Read<LuaTable>());
        return writer.Text.ToString();
    }

    public static LuaTable Decode(string json, ModLimits limits)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = limits.StateDepth });
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                throw new LuaStateException("the saved state is not a table");
            return Table(document.RootElement, "maid.state");
        }
        catch (JsonException error)
        {
            throw new LuaStateException($"the saved state is not valid JSON: {error.Message}");
        }
    }

    private static LuaTable Table(JsonElement element, string path)
    {
        var table = new LuaTable();
        if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 1;
            foreach (JsonElement item in element.EnumerateArray())
            {
                table[index] = Value(item, $"{path}[{index}]");
                index++;
            }
            return table;
        }
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string name = property.Name;
            if (name.StartsWith("s:", StringComparison.Ordinal))
                table[name[2..]] = Value(property.Value, Path(path, name[2..]));
            else if (name.StartsWith("i:", StringComparison.Ordinal)
                     && long.TryParse(name.AsSpan(2), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long key)
                     && Math.Abs((double)key) <= MaxIntegerKey)
                table[(double)key] = Value(property.Value, $"{path}[{key}]");
            else throw new LuaStateException($"{path} has the key \"{name}\", which is marked neither s: nor i:");
        }
        return table;
    }

    private static LuaValue Value(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.Object or JsonValueKind.Array => Table(element, path),
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new LuaStateException($"{path} is a JSON {element.ValueKind.ToString().ToLowerInvariant()}, which no Lua value gives"),
    };

    private static string Path(string path, string key)
        => IsName(key) ? $"{path}.{key}" : $"{path}[\"{key.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"]";

    private static bool IsName(string key)
        => key.Length > 0 && (char.IsAsciiLetter(key[0]) || key[0] == '_') && key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private sealed class Writer(ModLimits limits)
    {
        public readonly StringBuilder Text = new();
        private readonly HashSet<LuaTable> _open = new(ReferenceEqualityComparer.Instance);
        private readonly List<string> _path = ["maid.state"];

        private string Where => string.Concat(_path);

        private void Check()
        {
            if (Text.Length > limits.StateCharacters)
                throw new LuaStateException(
                    $"maid.state is larger than {limits.StateCharacters:N0} characters as saved (at {Where})");
        }

        public void Table(LuaTable table)
        {
            if (_open.Count >= limits.StateDepth)
                throw new LuaStateException($"{Where} nests tables more than {limits.StateDepth} deep");
            if (!_open.Add(table)) throw new LuaStateException($"{Where} is a table that contains itself (a cycle)");
            var integers = new List<(double Key, LuaValue Value)>();
            var strings = new List<(string Key, LuaValue Value)>();
            foreach (var pair in table)
            {
                LuaValue key = pair.Key;
                if (key.Type == LuaValueType.String) strings.Add((key.Read<string>(), pair.Value));
                else if (key.Type == LuaValueType.Number && key.Read<double>() is var number
                         && double.IsFinite(number) && number == Math.Floor(number) && Math.Abs(number) <= MaxIntegerKey)
                    integers.Add((number, pair.Value));
                else
                    throw new LuaStateException($"{Where} has a key that is {Describe(key)}; a saved key is a string or an integer");
            }
            integers.Sort((a, b) => a.Key.CompareTo(b.Key));
            strings.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            bool array = strings.Count == 0 && integers.Count > 0 && integers[0].Key == 1
                         && integers[^1].Key == integers.Count;
            Text.Append(array ? '[' : '{');
            bool first = true;
            foreach (var (key, value) in integers)
            {
                if (!first) Text.Append(',');
                first = false;
                if (!array) Text.Append("\"i:").Append(((long)key).ToString(CultureInfo.InvariantCulture)).Append("\":");
                _path.Add($"[{(long)key}]");
                Value(value);
                _path.RemoveAt(_path.Count - 1);
            }
            foreach (var (key, value) in strings)
            {
                if (!first) Text.Append(',');
                first = false;
                _path.Add(IsName(key) ? "." + key : $"[\"{key}\"]");
                String("s:" + key);
                Text.Append(':');
                Value(value);
                _path.RemoveAt(_path.Count - 1);
            }
            Text.Append(array ? ']' : '}');
            _open.Remove(table);
            Check();
        }

        private void Value(LuaValue value)
        {
            switch (value.Type)
            {
                case LuaValueType.Boolean:
                    Text.Append(value.Read<bool>() ? "true" : "false");
                    break;
                case LuaValueType.Number:
                    double number = value.Read<double>();
                    if (!double.IsFinite(number))
                        throw new LuaStateException($"{Where} is {Describe(value)}, which a save cannot hold");
                    // A whole number as an integer; any other, and -0, in the shortest form that reads back exactly.
                    bool whole = number == Math.Floor(number) && Math.Abs(number) < MaxIntegerKey
                                 && !(number == 0 && double.IsNegative(number));
                    Text.Append(whole
                        ? ((long)number).ToString(CultureInfo.InvariantCulture)
                        : number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case LuaValueType.String:
                    String(value.Read<string>());
                    break;
                case LuaValueType.Table:
                    Table(value.Read<LuaTable>());
                    break;
                default:
                    throw new LuaStateException($"{Where} is {Describe(value)}; a save holds booleans, numbers, strings and tables");
            }
            Check();
        }

        private void String(string text)
        {
            Text.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    Text.Append(c).Append(text[++i]);
                    continue;
                }
                if (char.IsSurrogate(c))
                    throw new LuaStateException($"{Where} holds a string that is not valid text (a lone surrogate U+{(int)c:X4})");
                switch (c)
                {
                    case '"': Text.Append("\\\""); break;
                    case '\\': Text.Append("\\\\"); break;
                    case '\n': Text.Append("\\n"); break;
                    case '\r': Text.Append("\\r"); break;
                    case '\t': Text.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == 0x7f) Text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else Text.Append(c);
                        break;
                }
            }
            Text.Append('"');
        }

        private static string Describe(LuaValue value) => value.Type switch
        {
            LuaValueType.Number when value.Read<double>() is var n && !double.IsFinite(n)
                => double.IsNaN(n) ? "nan" : n > 0 ? "inf" : "-inf",
            LuaValueType.Number => $"the number {value.Read<double>().ToString("R", CultureInfo.InvariantCulture)}",
            LuaValueType.Boolean => $"the boolean {(value.Read<bool>() ? "true" : "false")}",
            LuaValueType.Table => "a table",
            _ => "a " + value.TypeToString(),
        };
    }
}
