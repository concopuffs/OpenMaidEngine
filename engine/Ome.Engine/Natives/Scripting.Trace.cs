using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ome.Engine.Model;
using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

// The attribution trace (spec §12, --lua-trace): one JSON line per handler call in diagnostics/lua-trace.jsonl, with
// where it ran, how long it took, how it ended, each declared field before and after, and every write it made through
// maid.vm, an accessor or ctx.args. A write is the innermost running handler's, so a base mod's helper's write is
// named for the mod that called it.
public sealed partial class Scripting
{
    public const string TraceFile = "lua-trace.jsonl";
    /// <summary>The writes one call's line lists; a call that makes more counts the rest.</summary>
    public const int TracedWritesPerCall = 1000;

    private sealed record TracedValue(CellValue? Value, string? Error);

    private sealed record TracedWrite(string Cell, string? Name, string? Through, CellValue Before, CellValue After);

    private sealed class TracedCall(HandlerId handler, HookContext context, IReadOnlyList<(string Name, TracedValue Value)> fields)
    {
        public HandlerId Handler { get; } = handler;
        public HookContext Context { get; } = context;
        public IReadOnlyList<(string Name, TracedValue Value)> Before { get; } = fields;
        public long Started { get; } = Stopwatch.GetTimestamp();
        public List<TracedWrite> Writes { get; } = new();
        public int Dropped { get; set; }
    }

    // The traced calls running, innermost last; empty between calls (and always without --lua-trace).
    private readonly List<TracedCall> _traced = new();
    private readonly Queue<string> _recentTrace = new();
    private StreamWriter? _traceWriter;
    private bool _traceUnwritable;

    /// <summary>The run's latest trace lines, oldest first, for its tests and for the driver.</summary>
    public IReadOnlyList<string> RecentTrace => _recentTrace.ToArray();

    /// <summary>Whether a write now goes into a traced call's line.</summary>
    private bool Tracing => _traced.Count > 0;

    private TracedCall? TraceBegin(HandlerId handler, HookContext context)
    {
        if (!_setup.Trace) return null;
        var call = new TracedCall(handler, context, FieldValues(context));
        _traced.Add(call);
        if (context.Kind == HookKind.Point)
        {
            string modId = _setup.Stack[handler].ModId;
            context.OperandWritten = (index, operand, before, after)
                => TraceWrite(OperandCell(index, operand, out string? name), name, modId, before, after);
        }
        return call;
    }

    /// <summary>The fields a call is given: a game event's declared fields, read now, or an engine event's.</summary>
    private static List<(string, TracedValue)> FieldValues(HookContext context)
    {
        var values = new List<(string, TracedValue)>();
        if (context.Event is { } gameEvent)
            foreach (GameEventField field in gameEvent.Fields)
            {
                try
                {
                    values.Add((field.Name, new TracedValue(context.ReadField(field), null)));
                }
                catch (ScriptAccessException error)
                {
                    values.Add((field.Name, new TracedValue(null, error.Message)));
                }
            }
        else
            foreach (var (name, value) in context.Fields) values.Add((name, new TracedValue(value, null)));
        return values;
    }

    /// <summary>A write the running call made: <paramref name="faceMod"/> is the mod whose access made it, which differs
    /// from the call's mod when a base mod's helper writes for its caller.</summary>
    private void TraceWrite(string cell, string? name, string faceMod, CellValue before, CellValue after)
    {
        TracedCall call = _traced[^1];
        if (call.Writes.Count >= TracedWritesPerCall)
        {
            call.Dropped++;
            return;
        }
        string? through = faceMod == _setup.Stack[call.Handler].ModId ? null : faceMod;
        call.Writes.Add(new TracedWrite(cell, name, through, before, after));
    }

    /// <summary>A write to a global cell, with its name in the layout when it has one.</summary>
    private void TraceGlobal(CellKind kind, int address, string faceMod, CellValue before)
        => TraceWrite(CellText(kind, address, local: false), ConsoleLayout()?.Layout.NameAt(kind, address), faceMod, before,
                      ReadCell(kind, address));

    private static string CellText(CellKind kind, int address, bool local)
        => string.Create(CultureInfo.InvariantCulture, $"{(local ? "L" : "G")}{kind switch
        {
            CellKind.Float => "F",
            CellKind.String => "S",
            _ => "",
        }}[0x{address:x}]");

    /// <summary>The cell an operand names, for a write through <c>ctx.args</c>: a direct cell, or the operand alone.</summary>
    private string OperandCell(int index, Operand operand, out string? name)
    {
        name = null;
        string arg = $"arg[{index + 1}]";
        int address = unchecked((int)operand.Value);
        switch (operand.Type)
        {
            case OperandTypes.GlobalInteger or OperandTypes.GlobalFloat or OperandTypes.GlobalString:
                CellKind kind = HookContext.CellKindOf(operand.Type);
                name = ConsoleLayout()?.Layout.NameAt(kind, address);
                return $"{arg} {CellText(kind, address, local: false)}";
            case OperandTypes.LocalInteger or OperandTypes.LocalFloat or OperandTypes.LocalString:
                return $"{arg} {CellText(HookContext.CellKindOf(operand.Type), address, local: true)}";
            default:
                return arg;
        }
    }

    private void TraceEnd(TracedCall call, ModScriptException? error)
    {
        IReadOnlyList<(string Name, TracedValue Value)> after = FieldValues(call.Context);
        double milliseconds = (Stopwatch.GetTimestamp() - call.Started) * 1000.0 / Stopwatch.Frequency;
        _traced.RemoveAt(_traced.Count - 1);
        ModRuntimeHandler handler = _setup.Stack[call.Handler];
        HookContext context = call.Context;

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("mod", handler.ModId);
            json.WriteString("handler", $"{handler.File}:{handler.Function}");
            // A site hook not at a point is named as its manifest names it: the script and the original offset.
            json.WriteString("hook", context.Name ?? (context.Kind == HookKind.Point && context.Site >= 0
                ? string.Create(CultureInfo.InvariantCulture, $"{context.Script}@0x{context.Site:x}")
                : ""));
            json.WriteString("kind", context.Kind switch
            {
                HookKind.Point => context.When == null ? "point" : "site",
                HookKind.EngineEvent => "engine-event",
                HookKind.Script => "script",
                HookKind.Watch => "watch",
                _ => "event",
            });
            if (context.Kind == HookKind.Watch)
            {
                // Spec §7.3: the cell, its values and its writer; at is the writing instruction, by the writing mod.
                json.WriteString("cell", context.Cell);
                Value(json, "old", new TracedValue(context.Old, null));
                Value(json, "new", new TracedValue(context.New, null));
                json.WriteString("writer", context.Writer.ToString().ToLowerInvariant());
                if (context.WriterMod != null) json.WriteString("by", context.WriterMod);
            }
            if (context.When != null) json.WriteString("when", context.When);
            if (context.Cause != null) json.WriteString("cause", context.Cause);
            if (context.Script != null)
                json.WriteString("at", context.Offset < 0
                    ? context.Script
                    : string.Create(CultureInfo.InvariantCulture, $"{context.Script}@0x{context.Offset:x}"));
            json.WriteNumber("ms", Math.Round(milliseconds, 3));
            json.WriteString("result", error?.Kind switch
            {
                null => "ok",
                ModScriptErrorKind.Budget => "budget",
                ModScriptErrorKind.Depth => "depth",
                _ => "error",
            });
            if (error != null) json.WriteString("error", FirstLine(error.Message));
            if (context.SkippedHere) json.WriteBoolean("skip", true);   // spec §7.1: the handler that skipped
            // Spec §12: the handlers it ran inside, innermost first.
            if (_traced.Count > 0)
            {
                json.WriteStartArray("within");
                for (int i = _traced.Count - 1; i >= 0; i--) json.WriteStringValue(_setup.Stack[_traced[i].Handler].ToString());
                json.WriteEndArray();
            }
            if (call.Before.Count > 0)
            {
                json.WriteStartArray("fields");
                for (int i = 0; i < call.Before.Count; i++)
                {
                    json.WriteStartObject();
                    json.WriteString("name", call.Before[i].Name);
                    Value(json, "before", call.Before[i].Value);
                    Value(json, "after", after[i].Value);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
            }
            if (call.Writes.Count > 0)
            {
                json.WriteStartArray("writes");
                foreach (TracedWrite write in call.Writes)
                {
                    json.WriteStartObject();
                    json.WriteString("cell", write.Cell);
                    if (write.Name != null) json.WriteString("name", write.Name);
                    if (write.Through != null) json.WriteString("through", write.Through);
                    Value(json, "before", new TracedValue(write.Before, null));
                    Value(json, "after", new TracedValue(write.After, null));
                    json.WriteEndObject();
                }
                json.WriteEndArray();
            }
            if (call.Dropped > 0) json.WriteNumber("writes_not_listed", call.Dropped);
            json.WriteEndObject();
        }
        AppendTrace(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static void Value(Utf8JsonWriter json, string property, TracedValue value)
    {
        switch (value)
        {
            case { Value: { Kind: CellKind.String } text }:
                json.WriteString(property, text.Text);
                break;
            case { Value: { Kind: CellKind.Integer } integer }:
                json.WriteNumber(property, (long)integer.Number);
                break;
            case { Value: { } number }:
                json.WriteNumber(property, number.Number);
                break;
            default:
                json.WriteStartObject(property);
                json.WriteString("error", value.Error);
                json.WriteEndObject();
                break;
        }
    }

    /// <summary>A trace line: kept in memory, and written to <c>diagnostics/lua-trace.jsonl</c>, which the run's first line
    /// starts anew.</summary>
    private void AppendTrace(string line)
    {
        _recentTrace.Enqueue(line);
        while (_recentTrace.Count > RecentLogLines) _recentTrace.Dequeue();
        if (_setup.DiagnosticsDirectory is not { } directory || _traceUnwritable) return;
        try
        {
            if (_traceWriter == null)
            {
                Directory.CreateDirectory(directory);
                var stream = new FileStream(Path.Combine(directory, TraceFile), FileMode.Create, FileAccess.Write,
                                            FileShare.ReadWrite | FileShare.Delete);
                _traceWriter = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            }
            _traceWriter.WriteLine(line);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _traceUnwritable = true;
            WriteLog(ModLogLevel.Warn, null, $"--lua-trace cannot write {TraceFile}: {error.Message}");
        }
    }

    private void CloseTrace()
    {
        _traceWriter?.Dispose();
        _traceWriter = null;
    }
}
