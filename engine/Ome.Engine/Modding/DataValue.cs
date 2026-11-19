using System.Globalization;
using System.Text;

namespace Ome.Engine.Modding;

/// <summary>
/// A data file's document as plain values (data patches spec §5.3, §8): a table, an array, a string, a boolean, an
/// integer or a float, with no TOML type in it. <c>Ome.Mods</c> reads a file into it; the Lua runtime hands it to an
/// owner's handler as a read-only view. A value is immutable once read.
/// </summary>
public abstract class DataValue
{
    private protected DataValue() { }

    /// <summary>The value as one line of inline TOML, keys in the file's order: <c>{unit = {"761" = {hp = 150}}}</c>.
    /// For tests and the console, not a format anything reads.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        Write(text);
        return text.ToString();
    }

    public override string ToString() => ToText();

    internal abstract void Write(StringBuilder text);

    /// <summary>A key as a path prints it (plan §4.2): bare when it is a bare TOML key, else a quoted one, with
    /// <c>"</c>, <c>\</c> and every control character escaped, so a printed key never holds one.</summary>
    public static string KeyText(string key)
    {
        if (key.Length > 0 && key.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'))
            return key;
        var text = new StringBuilder(key.Length + 2).Append('"');
        foreach (char c in key)
        {
            if (c is '"' or '\\') text.Append('\\').Append(c);
            else if (char.IsControl(c)) text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else text.Append(c);
        }
        return text.Append('"').ToString();
    }
}

/// <summary>A table: string keys in the order the file gives them.</summary>
public sealed class DataTable : DataValue
{
    private readonly Dictionary<string, DataValue> _byKey;

    public DataTable(IReadOnlyList<KeyValuePair<string, DataValue>> entries)
    {
        Entries = entries;
        _byKey = new Dictionary<string, DataValue>(entries.Count, StringComparer.Ordinal);
        foreach (var (key, value) in entries)
            if (!_byKey.TryAdd(key, value))
                throw new ArgumentException($"the key {KeyText(key)} is given twice", nameof(entries));
    }

    /// <summary>The entries, in the file's order.</summary>
    public IReadOnlyList<KeyValuePair<string, DataValue>> Entries { get; }

    public int Count => Entries.Count;

    /// <summary>The value at <paramref name="key"/>, or null.</summary>
    public DataValue? this[string key] => _byKey.GetValueOrDefault(key);

    internal override void Write(StringBuilder text)
    {
        text.Append('{');
        for (int k = 0; k < Entries.Count; k++)
        {
            if (k > 0) text.Append(", ");
            text.Append(KeyText(Entries[k].Key)).Append(" = ");
            Entries[k].Value.Write(text);
        }
        text.Append('}');
    }
}

/// <summary>An array; Lua sees it indexed from 1.</summary>
public sealed class DataArray(IReadOnlyList<DataValue> items) : DataValue
{
    public IReadOnlyList<DataValue> Items { get; } = items;

    internal override void Write(StringBuilder text)
    {
        text.Append('[');
        for (int k = 0; k < Items.Count; k++)
        {
            if (k > 0) text.Append(", ");
            Items[k].Write(text);
        }
        text.Append(']');
    }
}

public sealed class DataString(string value) : DataValue
{
    public string Value { get; } = value;

    internal override void Write(StringBuilder text)
    {
        text.Append('"');
        foreach (char c in Value)
        {
            if (c is '"' or '\\') text.Append('\\').Append(c);
            else if (char.IsControl(c)) text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else text.Append(c);
        }
        text.Append('"');
    }
}

public sealed class DataBoolean(bool value) : DataValue
{
    public bool Value { get; } = value;

    internal override void Write(StringBuilder text) => text.Append(Value ? "true" : "false");
}

/// <summary>An integer from -2^53 to 2^53, which a Lua number holds exactly whatever the runtime.</summary>
public sealed class DataInteger : DataValue
{
    /// <summary>The largest magnitude (2^53).</summary>
    public const long Limit = 1L << 53;

    public DataInteger(long value)
    {
        if (value is > Limit or < -Limit) throw new ArgumentOutOfRangeException(nameof(value), value, "past 2^53");
        Value = value;
    }

    public long Value { get; }

    internal override void Write(StringBuilder text) => text.Append(Value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>A finite float.</summary>
public sealed class DataFloat : DataValue
{
    public DataFloat(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "not finite");
        Value = value;
    }

    public double Value { get; }

    internal override void Write(StringBuilder text)
    {
        string number = Value.ToString("R", CultureInfo.InvariantCulture);
        text.Append(number);
        if (!number.Contains('.') && !number.Contains('E')) text.Append(".0");
    }
}
