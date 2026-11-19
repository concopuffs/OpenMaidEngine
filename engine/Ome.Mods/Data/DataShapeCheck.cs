using System.Globalization;
using System.Text;
using Ome.Engine.Modding;

namespace Ome.Mods.Data;

/// <summary>What <see cref="DataShapeCheck.Check"/> gives: the first problems of a document against its shape, each
/// with its line where the file writes the path, and how many there were; or, when <see cref="StepLimit"/> is set, that
/// the shape took more work on the document than it may, which is the shape's owner's problem.</summary>
public sealed record DataShapeResult(IReadOnlyList<DataProblem> Problems, int ProblemCount, long? StepLimit)
{
    public bool Ok => ProblemCount == 0 && StepLimit == null;
}

/// <summary>
/// Checks a data file's document against its kind's shape at the link (data patches spec §7, plan §6.2). It runs no
/// code and changes nothing. Each problem gives its path, as the reader prints one, and its reason: a key an object
/// does not take, with the nearest names it does; a key that should be a whole number in decimal, with its range; a
/// value of the wrong kind or outside its range; or a field the shape does not offer, with the shape's reason. The line
/// is found in the text the reader kept (<see cref="DataLines"/>), only for a document with problems. The check's steps
/// are bounded by the document's size, which counts each table and array as each value (plan §12.6), so a document of
/// empty tables meets a limit that grows with it.
/// </summary>
public static class DataShapeCheck
{
    /// <summary>The schemas a check may apply for each value of the document, each table and array counting one, and
    /// once more: a shape written as the generated ones applies four to six to a value, so only one built to cost time
    /// reaches the bound.</summary>
    public const int StepsPerValue = 64;
    public const int StepsBase = 100_000;
    /// <summary>The names an error lists when none is near the key.</summary>
    public const int NamesShown = 8;

    /// <summary>The marker of an array's element in a path's parts, as <see cref="DataLines"/> has it; a key never holds
    /// a control character, nor another that does not print (<see cref="DataDocumentReader"/>).</summary>
    internal const char IndexMark = TomlStructure.IndexMark;

    public static DataShapeResult Check(DataShape shape, DataReadResult read)
    {
        if (read.Document is not { } document) throw new ArgumentException("the read has no document", nameof(read));
        long limit = StepsBase + (long)StepsPerValue * document.Size.Total;
        var walk = new Walk(limit);
        walk.Value(shape.Root, document.Root);
        if (walk.Over) return new DataShapeResult([], 0, limit);
        if (walk.Count == 0) return new DataShapeResult([], 0, null);
        int?[] lines = read.Text is { } text
            ? DataLines.Find(text, walk.Kept.Select(problem => problem.Parts).ToList())
            : new int?[walk.Kept.Count];
        var problems = walk.Kept.Select((problem, k) => new DataProblem(lines[k], $"{PathText(problem.Parts)}: {problem.Reason}"))
            .ToList();
        return new DataShapeResult(problems, walk.Count, null);
    }

    /// <summary>A path's text: keys joined by dots, a key that is not bare quoted, an array's element by its index.</summary>
    internal static string PathText(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0) return "the document";
        var text = new StringBuilder();
        foreach (string part in parts)
        {
            if (part.Length > 0 && part[0] == IndexMark)
            {
                text.Append('[').Append(part, 1, part.Length - 1).Append(']');
                continue;
            }
            if (text.Length > 0) text.Append('.');
            text.Append(DataValue.KeyText(part));
        }
        return text.ToString();
    }

    private sealed class Walk(long limit)
    {
        private readonly List<string> _path = [];
        private long _steps;
        private int _silent;
        private bool _failedSilently;

        public List<(string[] Parts, string Reason)> Kept { get; } = [];
        public int Count { get; private set; }
        public bool Over { get; private set; }

        /// <summary>Counts a problem and says whether its reason is wanted, which it is only for a problem that is
        /// kept. Past the kept ones, and in oneOf's forms, where a failure only decides the form, a problem is counted
        /// and nothing is built for it: a reason costs (the nearest names compare the key with every name), and a
        /// document at the value bound can have a problem in each value.</summary>
        private bool Keeps()
        {
            if (_silent > 0)
            {
                _failedSilently = true;
                return false;
            }
            return Count++ < DataDocumentReader.ProblemsKept;
        }

        private void Keep(string reason) => Kept.Add((_path.ToArray(), reason));

        public void Value(DataShapeSchema schema, DataValue value)
        {
            if (Over) return;
            if (++_steps > limit)
            {
                Over = true;
                return;
            }
            string? description = schema.Description;
            while (schema.Target is { } target)
            {
                schema = target;
                description = target.Description ?? description;
            }
            if (schema.Constant is { } constant)
            {
                if (!constant && Keeps()) Keep("takes no value here");
                return;
            }
            if (schema.Types != DataShapeTypes.None && !Matches(schema.Types, value))
            {
                if (Keeps())
                    Keep(schema.NotOffered ? description ?? "takes no value here" : KindProblem(schema.Types, value, description));
                return;
            }
            // An enum holds strings, numbers and booleans, so a table or an array fails it (plan §12.6).
            if (schema.Enum is { } values && (Scalar(value) is not { } scalar || !values.Any(item => Same(item, scalar)))
                && Keeps())
                Keep($"takes one of {string.Join(", ", values.Take(NamesShown).Select(Literal))}"
                     + $"{(values.Count > NamesShown ? $" and {values.Count - NamesShown} more" : "")}, not {Describe(value)}");
            if (Number(value) is { } number
                && (schema.Minimum is { } low && number < low || schema.Maximum is { } high && number > high) && Keeps())
                Keep($"takes {Range(schema)}, not {Describe(value)}");
            if (schema.OneOf is { } forms)
            {
                int passed = forms.Count(form => Passes(form, value));
                if (passed == 0 && Keeps()) Keep($"matches none of the {forms.Count} forms its shape gives here");
                else if (passed > 1 && Keeps()) Keep($"matches {passed} of the {forms.Count} forms its shape gives here, and oneOf takes one");
            }
            if (value is DataTable table) Object(schema, table);
            else if (value is DataArray array && schema.Items is { } items)
            {
                int index = 0;
                foreach (DataValue item in array.Items)
                {
                    _path.Add(IndexMark + (++index).ToString(CultureInfo.InvariantCulture));
                    Value(items, item);
                    _path.RemoveAt(_path.Count - 1);
                    if (Over) return;
                }
            }
        }

        private bool Passes(DataShapeSchema schema, DataValue value)
        {
            _silent++;
            bool saved = _failedSilently;
            _failedSilently = false;
            Value(schema, value);
            bool passed = !_failedSilently;
            _failedSilently = saved;
            _silent--;
            return passed;
        }

        private void Object(DataShapeSchema schema, DataTable table)
        {
            foreach (var (key, value) in table.Entries)
            {
                _path.Add(key);
                if (schema.Properties != null && schema.Properties.TryGetValue(key, out DataShapeSchema? property))
                    Value(property, value);
                else if (schema.DecimalKeys is { } range)
                {
                    if (!InDecimal(key, range))
                    {
                        if (Keeps()) Keep(DecimalProblem(key, range));
                    }
                    else if (schema.Additional is { } additional) Value(additional, value);
                }
                else if (schema.Additional is { Constant: false })
                {
                    if (Keeps()) Keep(Unknown(schema, key));
                }
                else if (schema.Additional is { } additional) Value(additional, value);
                _path.RemoveAt(_path.Count - 1);
                if (Over) return;
            }
        }
    }

    private static bool Matches(DataShapeTypes types, DataValue value) => value switch
    {
        DataTable => (types & DataShapeTypes.Object) != 0,
        DataArray => (types & DataShapeTypes.Array) != 0,
        DataString => (types & DataShapeTypes.String) != 0,
        DataBoolean => (types & DataShapeTypes.Boolean) != 0,
        DataInteger => (types & (DataShapeTypes.Integer | DataShapeTypes.Number)) != 0,
        // JSON Schema's integer is any number with no fraction, as the applier's whole number is (5.0 is 5).
        DataFloat number => (types & DataShapeTypes.Number) != 0
                            || (types & DataShapeTypes.Integer) != 0 && Math.Floor(number.Value) == number.Value,
        _ => false,
    };

    /// <summary>A value of the wrong kind. Where the shape asks for a table or an array, the reason adds the schema's
    /// description, when it has one: what goes there, and in what words, is the owner's to say, and the engine says
    /// nothing of it.</summary>
    private static string KindProblem(DataShapeTypes types, DataValue value, string? description)
    {
        string reason = $"takes {TypesText(types)}, not {Describe(value)}";
        return description != null && (types & (DataShapeTypes.Object | DataShapeTypes.Array)) != 0
            ? $"{reason} ({description})"
            : reason;
    }

    private static string TypesText(DataShapeTypes types)
    {
        var names = new List<string>();
        if ((types & DataShapeTypes.Object) != 0) names.Add("a table");
        if ((types & DataShapeTypes.Array) != 0) names.Add("an array");
        if ((types & DataShapeTypes.String) != 0) names.Add("a string");
        if ((types & DataShapeTypes.Integer) != 0 && (types & DataShapeTypes.Number) == 0) names.Add("a whole number");
        if ((types & DataShapeTypes.Number) != 0) names.Add("a number");
        if ((types & DataShapeTypes.Boolean) != 0) names.Add("a boolean");
        return names.Count == 0 ? "nothing" : string.Join(" or ", names);
    }

    private static string Range(DataShapeSchema schema)
    {
        string what = schema.Types == DataShapeTypes.Integer ? "a whole number" : "a number";
        return (schema.Minimum, schema.Maximum) switch
        {
            ({ } low, { } high) => $"{what} from {DataShape.Number(low)} to {DataShape.Number(high)}",
            ({ } low, null) => $"{what} of at least {DataShape.Number(low)}",
            (null, { } high) => $"{what} of at most {DataShape.Number(high)}",
            _ => what,
        };
    }

    private static string Describe(DataValue value) => value switch
    {
        DataTable => "a table",
        DataArray => "an array",
        DataString => "a string",
        DataBoolean => "a boolean",
        DataInteger integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        DataFloat number => DataShape.Number(number.Value),
        _ => "a value",
    };

    private static double? Number(DataValue value) => value switch
    {
        DataInteger integer => integer.Value,
        DataFloat number => number.Value,
        _ => null,
    };

    private static object? Scalar(DataValue value) => value switch
    {
        DataString text => text.Value,
        DataBoolean flag => flag.Value,
        DataInteger integer => (double)integer.Value,
        DataFloat number => number.Value,
        _ => null,
    };

    private static bool Same(object item, object scalar) => item switch
    {
        string text => scalar is string other && text == other,
        double number => scalar is double other && number == other,
        bool flag => scalar is bool other && flag == other,
        _ => false,
    };

    private static string Literal(object item) => item switch
    {
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        double number => DataShape.Number(number),
        bool flag => flag ? "true" : "false",
        _ => item.ToString() ?? "",
    };

    /// <summary>Whether a key is a whole number in decimal (digits, no sign, no leading zero but <c>0</c> itself) inside
    /// <paramref name="range"/>.</summary>
    private static bool InDecimal(string key, (long Minimum, long Maximum) range)
        => key.Length > 0 && key.All(char.IsAsciiDigit) && (key == "0" || key[0] != '0') && key.Length <= 16
           && long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out long id)
           && id >= range.Minimum && id <= range.Maximum;

    /// <summary>Why a key that <see cref="InDecimal"/> refuses is not a whole number in decimal inside
    /// <paramref name="range"/>.</summary>
    private static string DecimalProblem(string key, (long Minimum, long Maximum) range)
    {
        string inRange = $"from {range.Minimum} to {range.Maximum}";
        bool digits = key.Length > 0 && key.All(char.IsAsciiDigit);
        if (digits && (key == "0" || key[0] != '0')) return $"a key here is a whole number {inRange}, not {key}";
        if (digits) return $"a key here is written without a leading zero: {key.TrimStart('0')}";
        if (key.Length > 2 && key[0] == '0' && key[1] is 'x' or 'X' && key[2..].All(char.IsAsciiHexDigit))
            return ulong.TryParse(key[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)
                ? $"a key here is written in decimal: {value}"
                : "a key here is written in decimal";
        if (key.Length > 1 && key[0] is '+' or '-') return "a key here has no sign";
        return $"a key here is a whole number in decimal {inRange}, not {DataValue.KeyText(key)}";
    }

    /// <summary>The error for a key an object refuses: the nearest names it offers, or the names there. A name is near
    /// when it is at most a third of the longer name's length away (and 2 at least), or when one of the two starts the
    /// other and the shorter has three characters or more (<c>max</c> for <c>max_count</c>): a name cut short, or one
    /// with a word too many, is a slip the distance alone misses. The near names rank by distance.</summary>
    private static string Unknown(DataShapeSchema schema, string key)
    {
        var names = (schema.Properties ?? new Dictionary<string, DataShapeSchema>())
            .Where(pair => !Resolved(pair.Value).NotOffered).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();
        if (names.Count == 0) return "no key is taken here";
        var near = names.Select(name => (name, distance: Distance(key, name)))
            .Where(pair => pair.distance <= Math.Max(2, Math.Max(key.Length, pair.name.Length) / 3) || Starts(key, pair.name))
            .OrderBy(pair => pair.distance).ThenBy(pair => pair.name, StringComparer.Ordinal)
            .Take(3).Select(pair => pair.name).ToList();
        if (near.Count == 1) return $"no such key; the nearest name here is {near[0]}";
        if (near.Count > 1) return $"no such key; the nearest names here are {List(near)}";
        string shown = string.Join(", ", names.Take(NamesShown));
        return names.Count > NamesShown ? $"no such key; the names here are {shown} and {names.Count - NamesShown} more"
                                        : $"no such key; the names here are {List(names)}";
    }

    /// <summary>Whether one of the two starts the other, the shorter having three characters or more.</summary>
    private static bool Starts(string key, string name)
        => Math.Min(key.Length, name.Length) >= 3
           && (name.StartsWith(key, StringComparison.Ordinal) || key.StartsWith(name, StringComparison.Ordinal));

    private static string List(IReadOnlyList<string> names)
        => names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    private static DataShapeSchema Resolved(DataShapeSchema schema)
    {
        while (schema.Target is { } target) schema = target;
        return schema;
    }

    /// <summary>The optimal string alignment distance: insertions, deletions, substitutions and swaps of two neighbours,
    /// each counting one, and case counted.</summary>
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }
}
