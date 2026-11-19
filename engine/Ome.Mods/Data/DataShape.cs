using System.Globalization;
using System.Text.Json;

namespace Ome.Mods.Data;

/// <summary>The kinds of value a schema's <c>type</c> names (JSON Schema's, draft-07).</summary>
[Flags]
public enum DataShapeTypes
{
    None = 0,
    Null = 1,
    Boolean = 2,
    Object = 4,
    Array = 8,
    Number = 16,
    Integer = 32,
    String = 64,
}

/// <summary>One schema of a shape, checked and with its <c>$ref</c> resolved (data patches plan §6.2).</summary>
public sealed class DataShapeSchema
{
    internal DataShapeSchema(string pointer) => Pointer = pointer;

    /// <summary>Where the schema is in its file, as a JSON pointer: <c>#/definitions/by_id</c>.</summary>
    public string Pointer { get; }
    /// <summary>A boolean schema's value: <c>true</c> takes every value and <c>false</c> none. Null for an object.</summary>
    public bool? Constant { get; internal set; }
    public DataShapeTypes Types { get; internal set; }
    public IReadOnlyDictionary<string, DataShapeSchema>? Properties { get; internal set; }
    /// <summary><c>additionalProperties</c>: null when the schema has none, which takes every other key.</summary>
    public DataShapeSchema? Additional { get; internal set; }
    public DataShapeSchema? Items { get; internal set; }
    /// <summary><c>enum</c>'s values: strings, doubles and booleans.</summary>
    public IReadOnlyList<object>? Enum { get; internal set; }
    public double? Minimum { get; internal set; }
    public double? Maximum { get; internal set; }
    public IReadOnlyList<DataShapeSchema>? OneOf { get; internal set; }
    public string? Description { get; internal set; }
    /// <summary><c>x-ome-decimal-keys</c>: the range of a key that <see cref="Properties"/> does not name.</summary>
    public (long Minimum, long Maximum)? DecimalKeys { get; internal set; }
    /// <summary>The schema a <c>$ref</c> names, once resolved; the reference's own <see cref="Description"/> stays.</summary>
    public DataShapeSchema? Target { get; internal set; }
    internal string? Reference { get; set; }

    /// <summary>Whether the schema takes no value TOML can give: <c>type</c> is <c>null</c> alone. The generated shapes
    /// mark a field that is not offered so (plan §6.2), with the reason in <see cref="Description"/>.</summary>
    public bool NotOffered => Types == DataShapeTypes.Null;
}

/// <summary>What <see cref="DataShape.Read"/> gives: the shape, or the problems of its file, each as
/// <c>POINTER: REASON</c>.</summary>
public sealed record DataShapeRead(DataShape? Shape, IReadOnlyList<string> Problems);

/// <summary>
/// A kind's shape (data patches spec §7, plan §6.2): a JSON file under <c>shapes/</c>, in a subset of JSON Schema
/// (draft-07): <c>type</c>, <c>properties</c>, <c>additionalProperties</c>, <c>items</c>, <c>enum</c>, <c>minimum</c>,
/// <c>maximum</c>, <c>oneOf</c>, <c>description</c> and <c>$ref</c> into the file's <c>definitions</c>, with
/// <c>$schema</c> and <c>definitions</c> at the top, and the keyword of our own <see cref="DecimalKeysKeyword"/>. A
/// keyword outside the subset is a problem of the file, so that no check is skipped without notice. Read with
/// <see cref="JsonDocument"/>, with no reflection.
/// </summary>
public sealed class DataShape
{
    /// <summary>The largest shape, in bytes, and the deepest nesting of its JSON.</summary>
    public const long MaxBytes = 4L << 20;
    public const int MaxDepth = 64;
    /// <summary>Our keyword: an object's other keys are whole numbers in decimal from <c>minimum</c> to <c>maximum</c>.</summary>
    public const string DecimalKeysKeyword = "x-ome-decimal-keys";
    /// <summary>The prefix of a <c>$ref</c>: a definition of the same file.</summary>
    public const string DefinitionsPrefix = "#/definitions/";

    private static readonly string[] Keywords =
    [
        "type", "properties", "additionalProperties", "items", "enum", "minimum", "maximum", "oneOf", "description",
        "$ref", DecimalKeysKeyword,
    ];
    private static readonly string KeywordList = string.Join(", ", Keywords) + ", and $schema and definitions at the top";

    private DataShape(DataShapeSchema root) => Root = root;

    /// <summary>The schema a document is checked against.</summary>
    public DataShapeSchema Root { get; }

    /// <summary>Reads a shape's text; every problem of the file, each with its JSON pointer.</summary>
    public static DataShapeRead Read(string text)
    {
        var problems = new List<string>();
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxBytes)
            return new DataShapeRead(null, [$"#: a shape is at most {MaxBytes >> 20} MiB"]);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException error)
        {
            string at = error.LineNumber is { } line ? $"line {line + 1}" : "#";
            return new DataShapeRead(null, [$"{at}: not JSON: {FirstSentence(error.Message)}"]);
        }
        using (document)
        {
            var reader = new Reader(problems);
            DataShapeSchema? root = reader.Root(document.RootElement);
            if (root != null) reader.Resolve();
            return new DataShapeRead(problems.Count == 0 ? new DataShape(root!) : null, problems);
        }
    }

    private static string FirstSentence(string message)
    {
        int end = message.IndexOf(" LineNumber", StringComparison.Ordinal);
        return (end < 0 ? message : message[..end]).Trim();
    }

    private sealed class Reader(List<string> problems)
    {
        private readonly Dictionary<string, DataShapeSchema> _definitions = new(StringComparer.Ordinal);
        private readonly List<DataShapeSchema> _references = [];

        public DataShapeSchema? Root(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                problems.Add("#: a shape is a JSON object");
                return null;
            }
            if (element.TryGetProperty("$schema", out JsonElement schema) && schema.ValueKind != JsonValueKind.String)
                problems.Add("#/$schema: is a string");
            if (element.TryGetProperty("definitions", out JsonElement definitions))
            {
                if (definitions.ValueKind != JsonValueKind.Object) problems.Add("#/definitions: is an object of schemas");
                else
                    foreach (JsonProperty definition in definitions.EnumerateObject())
                    {
                        string pointer = "#/definitions/" + Escape(definition.Name);
                        if (Schema(definition.Value, pointer) is { } read) _definitions[definition.Name] = read;
                    }
            }
            return Schema(element, "#", top: true);
        }

        /// <summary>Every <c>$ref</c> to its definition; a chain of references that comes back to itself is refused.</summary>
        public void Resolve()
        {
            foreach (DataShapeSchema reference in _references)
            {
                string name = reference.Reference!;
                if (!_definitions.TryGetValue(name, out DataShapeSchema? target))
                    problems.Add($"{reference.Pointer}/$ref: names no definition ({DefinitionsPrefix}{Escape(name)})");
                else reference.Target = target;
            }
            foreach (DataShapeSchema reference in _references)
            {
                var seen = new HashSet<DataShapeSchema>();
                for (DataShapeSchema? next = reference; next?.Reference != null; next = next.Target)
                    if (!seen.Add(next))
                    {
                        problems.Add($"{reference.Pointer}/$ref: the chain of $ref comes back to itself without reaching a "
                                     + "schema of its own");
                        break;
                    }
            }
        }

        private DataShapeSchema? Schema(JsonElement element, string pointer, bool top = false)
        {
            var schema = new DataShapeSchema(pointer);
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                schema.Constant = element.ValueKind == JsonValueKind.True;
                return schema;
            }
            if (element.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{pointer}: a schema is an object or a boolean");
                return null;
            }
            int before = problems.Count;
            bool hasReference = false;
            var others = new List<string>();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string at = $"{pointer}/{Escape(property.Name)}";
                JsonElement value = property.Value;
                switch (property.Name)
                {
                    case "$schema" or "definitions" when top:
                        break;   // read by Root
                    case "$schema" or "definitions":
                        problems.Add($"{at}: {property.Name} is only at the top of a shape");
                        break;
                    case "type":
                        schema.Types = TypeOf(value, at);
                        others.Add(property.Name);
                        break;
                    case "properties":
                        if (value.ValueKind != JsonValueKind.Object)
                        {
                            problems.Add($"{at}: is an object of schemas");
                            break;
                        }
                        var properties = new Dictionary<string, DataShapeSchema>(StringComparer.Ordinal);
                        foreach (JsonProperty named in value.EnumerateObject())
                        {
                            // Plan §12.6: a name no data file's key can be, so that the shape's names stay in the bound.
                            if (named.Name.Length > DataBounds.MaxKeyLength)
                                problems.Add($"{at}/{Escape(named.Name[..32])}…: a property's name is at most "
                                             + $"{DataBounds.MaxKeyLength} characters, as a data file's key is, not "
                                             + $"{named.Name.Length}");
                            else if (Schema(named.Value, $"{at}/{Escape(named.Name)}") is { } read) properties[named.Name] = read;
                        }
                        schema.Properties = properties;
                        others.Add(property.Name);
                        break;
                    case "additionalProperties":
                        schema.Additional = Schema(value, at);
                        others.Add(property.Name);
                        break;
                    case "items":
                        if (value.ValueKind == JsonValueKind.Array) problems.Add($"{at}: is one schema here, for every element");
                        else schema.Items = Schema(value, at);
                        others.Add(property.Name);
                        break;
                    case "enum":
                        schema.Enum = EnumOf(value, at);
                        others.Add(property.Name);
                        break;
                    case "minimum" or "maximum":
                        if (value.ValueKind != JsonValueKind.Number) problems.Add($"{at}: is a number");
                        else if (property.Name == "minimum") schema.Minimum = value.GetDouble();
                        else schema.Maximum = value.GetDouble();
                        others.Add(property.Name);
                        break;
                    case "oneOf":
                        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                        {
                            problems.Add($"{at}: is a list of one schema at least");
                            break;
                        }
                        var forms = new List<DataShapeSchema>();
                        int index = 0;
                        foreach (JsonElement form in value.EnumerateArray())
                            if (Schema(form, $"{at}/{index++}") is { } read) forms.Add(read);
                        schema.OneOf = forms;
                        others.Add(property.Name);
                        break;
                    case "description":
                        if (value.ValueKind != JsonValueKind.String) problems.Add($"{at}: is a string");
                        else schema.Description = value.GetString();
                        break;
                    case "$ref":
                        hasReference = true;
                        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                        if (text == null || !text.StartsWith(DefinitionsPrefix, StringComparison.Ordinal)
                            || text.Length == DefinitionsPrefix.Length || text[DefinitionsPrefix.Length..].Contains('/'))
                            problems.Add($"{at}: names a definition of this file ({DefinitionsPrefix}NAME)");
                        else
                        {
                            schema.Reference = Unescape(text[DefinitionsPrefix.Length..]);
                            _references.Add(schema);
                        }
                        break;
                    case DecimalKeysKeyword:
                        schema.DecimalKeys = DecimalKeysOf(value, at);
                        others.Add(property.Name);
                        break;
                    default:
                        problems.Add($"{at}: the keyword {property.Name} is outside the shape subset ({KeywordList})");
                        break;
                }
            }
            if (hasReference && others.Count > 0)
                problems.Add($"{pointer}: a schema with $ref takes only a description beside it, not {string.Join(", ", others)}");
            if (schema.DecimalKeys != null && schema.Additional is { Constant: false })
                problems.Add($"{pointer}: {DecimalKeysKeyword} takes the keys additionalProperties checks, so that cannot be false");
            if (schema.Minimum is { } low && schema.Maximum is { } high && low > high)
                problems.Add($"{pointer}: minimum {Number(low)} is above maximum {Number(high)}");
            return problems.Count == before ? schema : null;
        }

        private DataShapeTypes TypeOf(JsonElement value, string at)
        {
            if (value.ValueKind == JsonValueKind.String) return TypeName(value.GetString()!, at);
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            {
                problems.Add($"{at}: is a type's name or a list of them");
                return DataShapeTypes.None;
            }
            DataShapeTypes types = DataShapeTypes.None;
            foreach (JsonElement item in value.EnumerateArray())
            {
                DataShapeTypes one = item.ValueKind == JsonValueKind.String ? TypeName(item.GetString()!, at) : DataShapeTypes.None;
                if (item.ValueKind != JsonValueKind.String) problems.Add($"{at}: is a type's name or a list of them");
                else if ((types & one) != 0) problems.Add($"{at}: names {item.GetString()} twice");
                types |= one;
            }
            return types;
        }

        private DataShapeTypes TypeName(string name, string at)
        {
            DataShapeTypes type = name switch
            {
                "null" => DataShapeTypes.Null,
                "boolean" => DataShapeTypes.Boolean,
                "object" => DataShapeTypes.Object,
                "array" => DataShapeTypes.Array,
                "number" => DataShapeTypes.Number,
                "integer" => DataShapeTypes.Integer,
                "string" => DataShapeTypes.String,
                _ => DataShapeTypes.None,
            };
            if (type == DataShapeTypes.None)
                problems.Add($"{at}: {name} is not a type (null, boolean, object, array, number, integer, string)");
            return type;
        }

        private IReadOnlyList<object>? EnumOf(JsonElement value, string at)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            {
                problems.Add($"{at}: is a list of one value at least");
                return null;
            }
            var values = new List<object>();
            foreach (JsonElement item in value.EnumerateArray())
                switch (item.ValueKind)
                {
                    case JsonValueKind.String:
                        values.Add(item.GetString()!);
                        break;
                    case JsonValueKind.Number:
                        values.Add(item.GetDouble());
                        break;
                    case JsonValueKind.True or JsonValueKind.False:
                        values.Add(item.ValueKind == JsonValueKind.True);
                        break;
                    default:
                        problems.Add($"{at}: holds strings, numbers and booleans, the values a data file's cell can take");
                        return null;
                }
            return values;
        }

        private (long, long)? DecimalKeysOf(JsonElement value, string at)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{at}: is {{ \"minimum\": A, \"maximum\": B }}, whole numbers from 0 to 2^53");
                return null;
            }
            long? low = null, high = null;
            foreach (JsonProperty bound in value.EnumerateObject())
            {
                bool whole = bound.Value.ValueKind == JsonValueKind.Number && bound.Value.TryGetInt64(out long number)
                             && number >= 0 && number <= DataValueLimit;
                long read = whole ? bound.Value.GetInt64() : 0;
                if (bound.Name is not ("minimum" or "maximum"))
                    problems.Add($"{at}/{Escape(bound.Name)}: {DecimalKeysKeyword} takes minimum and maximum only");
                else if (!whole) problems.Add($"{at}/{bound.Name}: is a whole number from 0 to 2^53");
                else if (bound.Name == "minimum") low = read;
                else high = read;
            }
            if (low == null || high == null)
            {
                if (!problems.Any(problem => problem.StartsWith(at, StringComparison.Ordinal)))
                    problems.Add($"{at}: gives minimum and maximum");
                return null;
            }
            if (low > high)
            {
                problems.Add($"{at}: minimum {low} is above maximum {high}");
                return null;
            }
            return (low.Value, high.Value);
        }
    }

    private const long DataValueLimit = 1L << 53;

    internal static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");

    private static string Unescape(string name) => name.Replace("~1", "/").Replace("~0", "~");
}
