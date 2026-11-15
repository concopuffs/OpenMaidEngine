using System.Globalization;
using System.Text.RegularExpressions;

namespace Ome.Engine.Modding;

// Game events (docs/superpowers/specs/2026-11-10-lua-scripting-design.md §9.2): named occurrences a mod declares under
// events/, with typed fields. The link builds them from the declarations, and the cache rebuilds them from the same
// text; the scripting native reads and writes the fields. Every rule is here, so a runtime cannot loosen it.

/// <summary>A field's type (spec §9.2). <c>bool</c> lives in an integer cell: a read is true for any nonzero value, a
/// write stores 1 or 0.</summary>
public enum EventFieldType { Int, Float, String, Bool }

public static class EventFieldTypes
{
    public static string Name(this EventFieldType type) => type switch
    {
        EventFieldType.Int => "int",
        EventFieldType.Float => "float",
        EventFieldType.String => "string",
        _ => "bool",
    };

    public static EventFieldType? Parse(string text) => text switch
    {
        "int" => EventFieldType.Int,
        "float" => EventFieldType.Float,
        "string" => EventFieldType.String,
        "bool" => EventFieldType.Bool,
        _ => null,
    };

    /// <summary>The kind of cell that holds a value of the type.</summary>
    public static CellKind Cell(this EventFieldType type) => type switch
    {
        EventFieldType.Float => CellKind.Float,
        EventFieldType.String => CellKind.String,
        _ => CellKind.Integer,
    };
}

/// <summary>
/// Where a field of an event at a point lives (spec §9.2): a local of the frame, a global by registry name or address,
/// an operand of the instruction at the point, an element of a named table indexed by other locations, or a number
/// (an index only). <see cref="Parse"/> reads the text form a declaration's <c>at</c> gives.
/// </summary>
public abstract partial record FieldLocation
{
    /// <summary>The kind of cell, or null when only the instruction tells (an operand).</summary>
    public abstract CellKind? Kind { get; }

    /// <summary>A local: <c>L[0x1]</c>, <c>LF[2]</c>, <c>LS[3]</c>.</summary>
    public sealed record Local(CellKind Cell, int Index) : FieldLocation
    {
        public override CellKind? Kind => Cell;
    }

    /// <summary>A global: by address (<c>G[0x62ccb]</c>, <c>GF[…]</c>, <c>GS[…]</c>) or by an exported name.</summary>
    public sealed record Global(CellKind Cell, int Address, string? Name) : FieldLocation
    {
        public override CellKind? Kind => Cell;
    }

    /// <summary>An operand of the instruction at the point: <c>arg[1]</c> is the first.</summary>
    public sealed record Operand(int Index) : FieldLocation
    {
        public override CellKind? Kind => null;
    }

    /// <summary>An element of a named table: <c>unit_current_levels[current_unit_id]</c>, a 2-D cell
    /// <c>t[row][column]</c>, or a named column <c>t[row].charm</c>.</summary>
    public sealed record Element(LayoutGlobal Table, FieldLocation Row, FieldLocation? Column) : FieldLocation
    {
        public override CellKind? Kind => Table.Kind;
    }

    /// <summary>A number, as an index or a column.</summary>
    public sealed record Number(int Value) : FieldLocation
    {
        public override CellKind? Kind => CellKind.Integer;
    }

    /// <summary>Reads a location. Names resolve through <paramref name="layout"/>, the base mod's; without one a
    /// location names cells by address only. A text that is no location fails with a <see cref="FormatException"/>
    /// that says why.</summary>
    public static FieldLocation Parse(string text, GameLayout? layout)
    {
        var parser = new Parser(text, layout);
        FieldLocation location = parser.Location(top: true);
        parser.End();
        return location;
    }

    private sealed partial class Parser(string text, GameLayout? layout)
    {
        private int _at;

        [GeneratedRegex(@"\G\s*([A-Za-z_][A-Za-z0-9_]*|0[xX][0-9A-Fa-f]+|[0-9]+|\[|\]|\.)", RegexOptions.CultureInvariant)]
        private static partial Regex Token();

        private string? Peek()
        {
            Match match = Token().Match(text, _at);
            return match.Success ? match.Groups[1].Value : null;
        }

        private string Next(string what)
        {
            Match match = Token().Match(text, _at);
            if (!match.Success) throw Fail($"{what} expected");
            _at = match.Index + match.Length;
            return match.Groups[1].Value;
        }

        private void Expect(string token)
        {
            if (Next($"'{token}'") != token) throw Fail($"'{token}' expected");
        }

        public void End()
        {
            if (text.AsSpan(_at).Trim().Length > 0) throw Fail("nothing more expected");
        }

        private FormatException Fail(string message) => new($"'{text}': {message} at column {_at + 1}");

        public FieldLocation Location(bool top)
        {
            string token = Next("a location");
            if (Integer(token) is int number) return new Number(number);
            switch (token)
            {
                case "L" or "LF" or "LS" when Peek() == "[":
                    return new Local(BankKind(token[1..]), Bracketed("a local's index"));
                case "G" or "GF" or "GS" when Peek() == "[":
                    return new Global(BankKind(token[1..]), Bracketed("an address"), null);
                case "arg" when Peek() == "[":
                    int operand = Bracketed("an operand's number");
                    if (operand < 1) throw Fail("operands are numbered from 1");
                    return new Operand(operand - 1);
            }
            if (!char.IsLetter(token[0]) && token[0] != '_') throw Fail($"'{token}' is no location");
            if (layout == null) throw Fail($"'{token}' is a name, and no base mod's layout names globals here");
            if (!layout.TryGet(token, out LayoutGlobal global)) throw Fail($"the layout names no global '{token}'");
            if (global.IsScalar)
                return Peek() == "[" ? throw Fail($"{global.Name} is no table") : new Global(global.Kind, global.Address, global.Name);
            if (Peek() != "[") throw Fail($"{global.Name} is a table: index it ({global.Name}[…])");
            Expect("[");
            FieldLocation row = Location(top: false);
            Expect("]");
            FieldLocation? column = null;
            if (global.Dimensions.Count == 2)
            {
                if (Peek() == "[")
                {
                    Expect("[");
                    column = Location(top: false);
                    Expect("]");
                }
                else if (Peek() == ".")
                {
                    Expect(".");
                    string name = Next("a column's name");
                    column = new Number(LayoutCells.Named(global, name));
                }
                else throw Fail($"{global.Name} is a table of rows: name a column ([…] or .name)");
            }
            else if (Peek() == ".")
                throw Fail($"{global.Name} is one row: its column names name elements, so index it by number");
            if (row is Number fixedRow) LayoutCells.Row(global, fixedRow.Value);
            if (column is Number fixedColumn) LayoutCells.Column(global, fixedColumn.Value);
            if (row.Kind is not (CellKind.Integer or null) || column?.Kind is not (CellKind.Integer or null))
                throw Fail($"an index of {global.Name} is an integer location");
            return new Element(global, row, column);
        }

        private int Bracketed(string what)
        {
            Expect("[");
            string token = Next(what);
            int value = Integer(token) ?? throw Fail($"{what} is a number, not '{token}'");
            Expect("]");
            return value;
        }

        private static int? Integer(string token)
        {
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(token.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int hex)
                       && hex >= 0 ? hex : null;
            return char.IsAsciiDigit(token[0]) && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                ? value : null;
        }

        private static CellKind BankKind(string suffix) => suffix switch
        {
            "F" => CellKind.Float,
            "S" => CellKind.String,
            _ => CellKind.Integer,
        };
    }
}

/// <summary>A field of an event (spec §9.2): its name, type and access, and for an event at a point where it lives.
/// <see cref="At"/> keeps the declaration's text, which the link cache stores.</summary>
public sealed record GameEventField(string Name, EventFieldType Type, bool Writable, string? At, FieldLocation? Location);

/// <summary>
/// An event (spec §9.2): its name, the mod that declares it, the patch point it is raised at, or none when the owner
/// raises it from Lua (<c>raised_by = "lua"</c>), and its fields.
/// </summary>
public sealed record GameEvent(string Name, string Owner, string? Point, string? Description,
                               IReadOnlyList<GameEventField> Fields)
{
    public bool RaisedByLua => Point == null;

    public GameEventField? Field(string name)
    {
        foreach (GameEventField field in Fields)
            if (field.Name == name) return field;
        return null;
    }

    /// <summary>A field's name: a Lua identifier, and not <c>event</c>, which names the event in a handler's context.</summary>
    private static readonly Regex FieldName = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// A field from a declaration's text: its type, its access (<c>r</c> or <c>rw</c>) and, for an event at a point, its
    /// location. A field that breaks a rule fails with a <see cref="FormatException"/> that says which.
    /// </summary>
    public static GameEventField BuildField(string name, string type, string access, string? at, bool atPoint,
                                            GameLayout? layout)
    {
        if (!FieldName.IsMatch(name) || name == "event")
            throw new FormatException($"field '{name}': a field's name is a Lua name other than 'event'");
        EventFieldType fieldType = EventFieldTypes.Parse(type)
                                   ?? throw new FormatException($"field {name}: type '{type}' is not int, float, string or bool");
        bool writable = access switch
        {
            "r" => false,
            "rw" => true,
            _ => throw new FormatException($"field {name}: access '{access}' is not \"r\" or \"rw\""),
        };
        if (!atPoint)
            return at == null
                ? new GameEventField(name, fieldType, writable, null, null)
                : throw new FormatException($"field {name}: an event raised from Lua has no locations; its raiser gives the values");
        if (at == null) throw new FormatException($"field {name}: an event at a point gives each field's location (at = \"…\")");
        FieldLocation location;
        try
        {
            location = FieldLocation.Parse(at, layout);
        }
        catch (ScriptAccessException error)
        {
            throw new FormatException($"field {name}: '{at}': {error.Message}");
        }
        catch (FormatException error)
        {
            throw new FormatException($"field {name}: {error.Message}");
        }
        if (location is FieldLocation.Number)
            throw new FormatException($"field {name}: '{at}' is a number, not a cell");
        if (location.Kind is { } kind && kind != fieldType.Cell())
            throw new FormatException($"field {name}: '{at}' is {Describe(kind)}, and the field's type is {fieldType.Name()}");
        return new GameEventField(name, fieldType, writable, at, location);
    }

    private static string Describe(CellKind kind) => kind switch
    {
        CellKind.Float => "a float cell",
        CellKind.String => "a string cell",
        _ => "an integer cell",
    };
}

/// <summary>An event's fields as a handler reads and writes them. The scripting native gives the cells of an event at
/// a point; <see cref="EventValues"/> holds the values of an event raised from Lua.</summary>
public interface IEventFields
{
    CellValue Read(GameEventField field);

    /// <summary>Write a field whose access allows it; the value has passed <see cref="EventFieldRules.Check"/>.</summary>
    void Write(GameEventField field, CellValue value);
}

/// <summary>The value rules of a field (spec §9.2), the same for a cell and for a value from Lua.</summary>
public static class EventFieldRules
{
    /// <summary>A value for the field's type: an int whole and in 32 bits, a bool 0 or 1, a float any number, a string
    /// text.</summary>
    public static CellValue Check(GameEventField field, CellValue value)
    {
        bool text = value.Kind == CellKind.String;
        return field.Type switch
        {
            EventFieldType.Int when !text => CellValue.Integer(CellRules.ReadInteger(CellRules.Integer(value.Number))),
            EventFieldType.Bool when !text && value.Number is 0 or 1 => CellValue.Integer(value.Number),
            EventFieldType.Float when !text => CellValue.Float(value.Number),
            EventFieldType.String when text => value,
            _ => throw new ScriptAccessException(
                $"field {field.Name} is {field.Type.Name()}; it takes {(field.Type switch
                {
                    EventFieldType.String => "a string",
                    EventFieldType.Bool => "a boolean",
                    _ => "a number",
                })}"),
        };
    }
}

/// <summary>The fields of an event raised from Lua (spec §8.2): every declared field's value, given by the raiser and
/// changed by the subscribers that may write it.</summary>
public sealed class EventValues : IEventFields
{
    private readonly Dictionary<string, CellValue> _values = new(StringComparer.Ordinal);

    /// <summary>The raiser's values: every declared field, and no other.</summary>
    public EventValues(GameEvent gameEvent, IReadOnlyDictionary<string, CellValue> given)
    {
        foreach (string name in given.Keys)
            if (gameEvent.Field(name) == null)
                throw new ScriptAccessException($"{gameEvent.Name} has no field '{name}'");
        foreach (GameEventField field in gameEvent.Fields)
            _values[field.Name] = given.TryGetValue(field.Name, out CellValue value)
                ? EventFieldRules.Check(field, value)
                : throw new ScriptAccessException($"{gameEvent.Name}: field {field.Name} is missing");
    }

    public IReadOnlyDictionary<string, CellValue> Values => _values;

    public CellValue Read(GameEventField field) => _values[field.Name];

    public void Write(GameEventField field, CellValue value) => _values[field.Name] = value;
}
