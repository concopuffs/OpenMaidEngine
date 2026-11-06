using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Age.Engine.Vm;

namespace Age.Engine.Frontend.Control;

/// <summary>Driving protocol constants (docs/superpowers/specs/2026-11-03-game-driving-design.md §4).</summary>
public static class DriveProtocol
{
    public const int Version = 1;
    public const string TokenEnvironmentVariable = "OME_DRIVE_TOKEN";
}

public sealed class DriveProtocolException(string message) : Exception(message);

/// <summary>One frame as both backends report it: the script name without extension, upper-case, and the resume
/// offset in code dwords.</summary>
public sealed record DriveFrame(string Script, int Offset)
{
    public string Location => $"{Script}@0x{Offset:x}";

    public static string NormalizeScript(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^4];
        return trimmed.ToUpperInvariant();
    }

    /// <summary>Parse "SC0000@0x8a9" (decimal offsets are accepted too).</summary>
    public static DriveFrame Parse(string text)
    {
        int at = text.LastIndexOf('@');
        if (at <= 0 || at == text.Length - 1)
            throw new DriveProtocolException($"expected SCRIPT@OFFSET (e.g. SC0000@0x8a9), got '{text}'");
        string offsetText = text[(at + 1)..].Trim();
        bool hex = offsetText.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (!int.TryParse(hex ? offsetText[2..] : offsetText,
                hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None,
                CultureInfo.InvariantCulture, out int offset) || offset < 0)
            throw new DriveProtocolException($"'{offsetText}' is not an offset in '{text}'");
        return new DriveFrame(NormalizeScript(text[..at]), offset);
    }

    /// <summary>Frames in native AGE's convention: the innermost frame at its resume offset, every caller at its
    /// call-script instruction.</summary>
    public static IReadOnlyList<DriveFrame> FromVm(IReadOnlyList<VmFrameLocation> frames)
        => frames.Select((frame, index) => new DriveFrame(
                NormalizeScript(frame.Script),
                index == frames.Count - 1 ? frame.ResumeOffset : frame.CurrentOffset))
            .ToArray();
}

/// <summary>What the game is blocked on. Null means running.</summary>
public static class DriveWaiting
{
    public const string Input = "input";           // an ADV page or hotspot wait (wait-for-input)
    public const string Menu = "menu";             // a script-polled menu: sleeping in a raw input-callback frame
    public const string Movie = "movie";           // a modal movie
    public const string Transition = "transition"; // a blocking presentation effect
    public const string Sleep = "sleep";           // a timed hold

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Input, Menu, Movie, Transition, Sleep };
}

/// <summary>The backend-neutral game state. <see cref="Busy"/> is true while text reveals or a screen transition
/// runs, which keeps <c>settle</c> waiting.</summary>
public sealed record DriveState(IReadOnlyList<DriveFrame> Frames, string? Waiting, bool Busy = false)
{
    public DriveFrame? Top => Frames.Count == 0 ? null : Frames[^1];

    public JsonObject ToJson()
    {
        var frames = new JsonArray();
        foreach (DriveFrame frame in Frames)
            frames.Add(new JsonObject
            {
                ["script"] = frame.Script,
                ["offset"] = frame.Offset,
                ["location"] = frame.Location,
            });
        return new JsonObject
        {
            ["frames"] = frames,
            ["location"] = Top?.Location,
            ["waiting"] = Waiting,
            ["busy"] = Busy,
        };
    }
}

/// <summary>A wait/expect condition; every key present must match (§4 "Conditions").</summary>
public sealed record DriveCondition(DriveFrame? At, DriveFrame? Top, string? Script, string? Waiting)
{
    public static DriveCondition Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new DriveProtocolException("a condition is an object with at, top, script and/or waiting");
        DriveFrame? at = null, top = null;
        string? script = null, waiting = null;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()!
                : throw new DriveProtocolException($"condition '{property.Name}' must be a string");
            switch (property.Name)
            {
                case "at": at = DriveFrame.Parse(value); break;
                case "top": top = DriveFrame.Parse(value); break;
                case "script": script = DriveFrame.NormalizeScript(value); break;
                case "waiting":
                    if (!DriveWaiting.All.Contains(value))
                        throw new DriveProtocolException(
                            $"waiting must be one of {string.Join(", ", DriveWaiting.All.Order())}, got '{value}'");
                    waiting = value;
                    break;
                default: throw new DriveProtocolException($"unknown condition '{property.Name}'");
            }
        }
        if (at == null && top == null && script == null && waiting == null)
            throw new DriveProtocolException("a condition needs at least one of at, top, script, waiting");
        return new DriveCondition(at, top, script, waiting);
    }

    public bool Matches(DriveState state)
        => (At == null || state.Frames.Contains(At))
           && (Top == null || state.Top == Top)
           && (Script == null || state.Frames.Any(frame => frame.Script == Script))
           && (Waiting == null || state.Waiting == Waiting);
}

/// <summary>One request line: <c>{"id": n, "op": "...", ...arguments}</c>.</summary>
public sealed record DriveRequest(long Id, string Op, JsonElement Body)
{
    public static DriveRequest Parse(string line)
    {
        JsonElement body;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            body = document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new DriveProtocolException($"invalid JSON: {error.Message}");
        }
        if (body.ValueKind != JsonValueKind.Object)
            throw new DriveProtocolException("a request is a JSON object");
        long id = body.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt64(out long parsed)
            ? parsed : 0;
        if (!body.TryGetProperty("op", out JsonElement op) || op.ValueKind != JsonValueKind.String)
            throw new DriveProtocolException("a request needs a string 'op'");
        return new DriveRequest(id, op.GetString()!, body);
    }

    public int Int(string name, int? fallback = null)
    {
        if (Body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number))
            return number;
        if (fallback is int defaultValue && !Body.TryGetProperty(name, out _)) return defaultValue;
        throw new DriveProtocolException($"'{Op}' needs an integer '{name}'");
    }

    public double Number(string name, double? fallback = null)
    {
        if (Body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
            return value.GetDouble();
        if (fallback is double defaultValue && !Body.TryGetProperty(name, out _)) return defaultValue;
        throw new DriveProtocolException($"'{Op}' needs a number '{name}'");
    }

    public string Text(string name, string? fallback = null)
    {
        if (Body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            return value.GetString()!;
        if (fallback != null && !Body.TryGetProperty(name, out _)) return fallback;
        throw new DriveProtocolException($"'{Op}' needs a string '{name}'");
    }

    public bool Bool(string name, bool fallback = false)
    {
        if (!Body.TryGetProperty(name, out JsonElement value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new DriveProtocolException($"'{Op}' needs a boolean '{name}'"),
        };
    }

    public bool Has(string name) => Body.TryGetProperty(name, out _);
    public JsonElement Element(string name)
        => Body.TryGetProperty(name, out JsonElement value)
            ? value : throw new DriveProtocolException($"'{Op}' needs '{name}'");
}

public static class DriveReply
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static string Ok(long id, JsonNode? result = null)
        => new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result ?? new JsonObject() }
            .ToJsonString(Compact);

    public static string Error(long id, string message)
        => new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = message }.ToJsonString(Compact);
}
