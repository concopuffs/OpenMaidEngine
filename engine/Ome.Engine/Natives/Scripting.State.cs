using System.Globalization;
using System.Text;
using System.Text.Json;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

// The mods' state and the session's life across saves and loads (docs/superpowers/specs/2026-11-10-lua-scripting-design.md
// §5.7, §7.7, §11): the scripting native's save record, which the save store keeps beside the save
// (SAVEnn.modstate.json); the session a load rebuilds; maid.session.restoring; ome.session.resumed and ome.save.writing.
public sealed partial class Scripting
{
    /// <summary>The suffix of the state file beside a numbered save: <c>SAVE03.modstate.json</c>.</summary>
    public const string StateFileSuffix = ".modstate.json";
    public const string SaveWritingEvent = "ome.save.writing";
    public const string SessionResumedEvent = "ome.session.resumed";

    // A load's state, as the save native handed it over (spec §11).
    private sealed record LoadedState(int Slot, ModState State, bool FullLoad);

    /// <summary>Whether a boundary has work for the native before its services: a session to build after a data-only
    /// load, or <c>ome.session.resumed</c> to fire. The interpreter tests it at every instruction boundary.</summary>
    public bool BoundaryPending => _scene.BuildPending || _scene.ResumedPending;

    /// <summary><c>maid.session.restoring</c> (spec §11).</summary>
    public bool Restoring => _scene.Restoring;

    /// <summary>
    /// The scripting native's save record (spec §11), for op 0x19e after <c>ome.save.writing</c> has fired: each mod's
    /// state, serialized on its own, with the save's time and play time. A mod whose state does not serialize keeps
    /// its last good block (the one it loaded, or the one it last saved) and is reported; the save is still written.
    /// A block for a mod not in the session, or off in it, is carried unchanged. Null when there is nothing to keep:
    /// no mod ships Lua and no block was loaded, so the save gets no state file.
    /// </summary>
    public string? WriteSaveRecord(int slot, NativeSystemTime timestamp, uint playSeconds)
    {
        if (_scene.Session is { } session)
        {
            ModStateCapture capture = session.CaptureState();
            foreach (var (modId, block) in capture.Blocks) _scene.Blocks[modId] = block;
            foreach (ModStateFailure failure in capture.Failures)
            {
                string kept = _scene.Blocks.ContainsKey(failure.ModId) ? "its last good state is kept" : "it has none saved";
                WriteLog(ModLogLevel.Error, failure.ModId, $"maid.state was not saved in slot {slot}, {kept}: {failure.Message}");
                Notify($"Lua: {failure.ModId}'s state was not saved: {FirstLine(failure.Message)}");
                if (_setup.Errors == LuaErrorPolicy.Halt)
                    _scene.HaltPending ??= $"lua-state-error: {failure.ModId}: {FirstLine(failure.Message)}";
            }
        }
        if (_scene.Blocks.Count == 0 && !_setup.Stack.HasLua) return null;
        return ModStateFile.Write(timestamp, playSeconds, _scene.Blocks);
    }

    /// <summary>The halt a save's state failure asked for under <c>--lua-errors halt</c>, once.</summary>
    public string? TakeHalt()
    {
        string? halt = _scene.HaltPending;
        _scene.HaltPending = null;
        return halt;
    }

    /// <summary>
    /// A numbered load has succeeded (ops 0x19f and 0x1a1): the save native hands over the texts the store read beside
    /// the save. The native keeps the state for the session the load rebuilds. A state file that does not match its
    /// save, or that cannot be read, is reported and gives empty state; so is a missing one when the save's stamp names
    /// a mod that ships Lua in this stack.
    /// </summary>
    public void ReadSaveRecord(int slot, IReadOnlyDictionary<string, string> sidecars, NativeSaveMetadata metadata,
                               bool fullLoad)
    {
        ModState state = ModState.Empty;
        string file = StateFileName(slot);
        if (!sidecars.TryGetValue(StateFileSuffix, out string? text))
        {
            IReadOnlyCollection<string>? stamped = _setup.StampedMods?.Invoke(sidecars);
            var lost = _setup.Stack.Mods.Where(mod => stamped?.Contains(mod.Id) == true).Select(mod => mod.Id).ToList();
            if (lost.Count > 0)
                ReportLoad($"save slot {slot} was made with {string.Join(", ", lost)}, which "
                           + (lost.Count == 1 ? "ships" : "ship") + $" Lua, but {file} is missing: "
                           + (lost.Count == 1 ? "its" : "their") + " state starts empty");
        }
        else if (ModStateFile.Read(text, _setup.Limits.StateDepth, out ModStateFile.Contents contents) is { } error)
            ReportLoad($"{file} cannot be read ({error}): every mod's state starts empty");
        else if (contents.Timestamp != ModStateFile.Stamp(metadata.Timestamp)
                 || contents.PlaySeconds != metadata.AccumulatedPlaySeconds)
            ReportLoad($"{file} does not belong to its save (it was written for a save at {contents.Timestamp} after "
                       + $"{contents.PlaySeconds} s of play; the save is from {ModStateFile.Stamp(metadata.Timestamp)} "
                       + $"after {metadata.AccumulatedPlaySeconds} s): every mod's state starts empty");
        else state = new ModState(contents.Blocks);
        _scene.Loaded = new LoadedState(slot, state, fullLoad);
    }

    /// <summary>Op 0x19f has replaced the banks: the session ends now, so the instruction's own <c>after</c> hooks do
    /// not fire, and the next instruction boundary builds the next one with the save's state (spec §5.7).</summary>
    public void LoadedDataOnly()
    {
        LoadedState? loaded = _scene.Loaded;
        EndSession();
        _scene.Loaded = loaded;
        _scene.BuildPending = true;
    }

    /// <summary>A full load has restored its last frame (op 0xae's jump, spec §11 step 5): <c>maid.session.restoring</c>
    /// clears, and <c>ome.session.resumed</c> fires at the next instruction boundary.</summary>
    public void RestoreEnded()
    {
        if (!_scene.Restoring) return;
        _scene.Restoring = false;
        _scene.ResumedPending = true;
    }

    /// <summary>
    /// The work of <see cref="BoundaryPending"/>, at the start of an instruction boundary, before its services: the
    /// session after a data-only load, then <c>ome.session.resumed</c>. Returns a halt reason under
    /// <c>--lua-errors halt</c>, or null.
    /// </summary>
    public string? ServiceBoundary()
    {
        if (_scene.BuildPending && StartSession(SessionReason.Load) is { } halt) return halt;
        if (_scene.ResumedPending)
        {
            _scene.ResumedPending = false;
            return RaiseEngineEvent(SessionResumedEvent,
                new KeyValuePair<string, CellValue>("slot", CellValue.Integer(_scene.Slot)));
        }
        return null;
    }

    /// <summary>The state a new session is built from (spec §5.7 step 2): its load's, or none. Every block of it is
    /// carried into the next save until the mod's own state replaces it.</summary>
    private ModState TakeState(LoadedState? loaded)
    {
        _scene.Restoring = loaded?.FullLoad == true;
        _scene.Slot = loaded?.Slot ?? -1;
        ModState state = loaded?.State ?? ModState.Empty;
        foreach (var (modId, block) in state.Blocks) _scene.Blocks[modId] = block;
        return state;
    }

    private void ReportLoad(string message)
    {
        WriteLog(ModLogLevel.Error, null, message);
        Notify($"Lua: {message}");
    }

    private static string StateFileName(int slot)
        => Path.ChangeExtension(DirectoryNativeDatStore.NumberedFileName(slot), null) + StateFileSuffix;

    /// <summary>
    /// The state file (spec §11): its format, the save's time and play time, and each mod's version and state, mods by
    /// id in ordinal order so the same state gives the same file. The state is the runtime's JSON, written as it is.
    /// </summary>
    internal static class ModStateFile
    {
        public const int Format = 1;

        public sealed record Contents(string Timestamp, uint PlaySeconds, IReadOnlyDictionary<string, ModStateBlock> Blocks);

        public static string Stamp(NativeSystemTime time)
            => string.Create(CultureInfo.InvariantCulture,
                $"{time.Year:D4}-{time.Month:D2}-{time.Day:D2}T{time.Hour:D2}:{time.Minute:D2}:{time.Second:D2}.{time.Milliseconds:D3}");

        public static string Write(NativeSystemTime timestamp, uint playSeconds, IReadOnlyDictionary<string, ModStateBlock> blocks)
        {
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteNumber("modstate", Format);
                json.WriteStartObject("save");
                json.WriteString("timestamp", Stamp(timestamp));
                json.WriteNumber("play_seconds", playSeconds);
                json.WriteEndObject();
                json.WriteStartObject("mods");
                foreach (var (modId, block) in blocks.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    json.WriteStartObject(modId);
                    json.WriteString("version", block.Version);
                    json.WritePropertyName("state");
                    json.WriteRawValue(block.Json);
                    json.WriteEndObject();
                }
                json.WriteEndObject();
                json.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
        }

        /// <summary>The file's contents, or null; else what is wrong with it.</summary>
        public static string? Read(string text, int stateDepth, out Contents contents)
        {
            contents = new Contents("", 0, new Dictionary<string, ModStateBlock>());
            try
            {
                // The file nests the state three levels down (mods, the mod, its state).
                using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = stateDepth + 4 });
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return "it is not a JSON object";
                if (!root.TryGetProperty("modstate", out JsonElement format) || format.ValueKind != JsonValueKind.Number)
                    return "it has no \"modstate\" format number";
                if (format.GetInt32() != Format) return $"its format is {format.GetRawText()}; this build reads {Format}";
                if (!root.TryGetProperty("save", out JsonElement save) || save.ValueKind != JsonValueKind.Object
                    || !save.TryGetProperty("timestamp", out JsonElement timestamp) || timestamp.ValueKind != JsonValueKind.String
                    || !save.TryGetProperty("play_seconds", out JsonElement play) || !play.TryGetUInt32(out uint playSeconds))
                    return "its \"save\" lacks the timestamp or the play time";
                if (!root.TryGetProperty("mods", out JsonElement mods) || mods.ValueKind != JsonValueKind.Object)
                    return "it has no \"mods\" object";
                var blocks = new Dictionary<string, ModStateBlock>(StringComparer.Ordinal);
                foreach (JsonProperty mod in mods.EnumerateObject())
                {
                    if (mod.Value.ValueKind != JsonValueKind.Object
                        || !mod.Value.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.String
                        || !mod.Value.TryGetProperty("state", out JsonElement state))
                        return $"the entry of {mod.Name} lacks its version or its state";
                    blocks[mod.Name] = new ModStateBlock(version.GetString()!, state.GetRawText());
                }
                contents = new Contents(timestamp.GetString()!, playSeconds, blocks);
                return null;
            }
            catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException)
            {
                return error.Message;
            }
        }
    }
}
