using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ome.Engine.Diagnostics;

/// <summary>
/// Writes one row of a diagnostics log (the timeline, the page map, the text-miss log) as compact JSON, the text
/// <c>JsonSerializer</c> wrote for the same dictionary, without reflection: the engine ships compiled with NativeAOT,
/// where reflection-based serialization is off. A value is null, a string, a character, a bool, a number, an enum (its
/// number, as <c>JsonSerializer</c> writes one), a dictionary with string keys, or a sequence of these. Any other value
/// is written as its <see cref="object.ToString"/> text.
/// </summary>
internal static class DiagnosticJson
{
    private static readonly JsonWriterOptions Escaped = new();
    private static readonly JsonWriterOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The row as one line of JSON. <paramref name="relaxed"/> writes non-ASCII text and HTML-sensitive
    /// characters as they are (<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>) instead of escaping them.</summary>
    public static string Row(IEnumerable<KeyValuePair<string, object?>> row, bool relaxed = false)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, relaxed ? Relaxed : Escaped))
            WriteObject(writer, row);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteObject(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, object?>> row)
    {
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object?> pair in row)
        {
            writer.WritePropertyName(pair.Key);
            WriteValue(writer, pair.Value);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case char character: writer.WriteStringValue(character.ToString()); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case Enum member:
                if (Convert.GetTypeCode(member) == TypeCode.UInt64) writer.WriteNumberValue(Convert.ToUInt64(member));
                else writer.WriteNumberValue(Convert.ToInt64(member));
                break;
            case int number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case ushort number: writer.WriteNumberValue(number); break;
            case byte number: writer.WriteNumberValue(number); break;
            case sbyte number: writer.WriteNumberValue(number); break;
            case double number: writer.WriteNumberValue(number); break;
            case float number: writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case IEnumerable<KeyValuePair<string, object?>> nested: WriteObject(writer, nested); break;
            case IEnumerable items:
                writer.WriteStartArray();
                foreach (object? item in items) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            default: writer.WriteStringValue(value.ToString()); break;
        }
    }
}
