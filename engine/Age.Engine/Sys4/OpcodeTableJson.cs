using System.Text.Json;
using Age.Engine.Model;
namespace Age.Engine.Sys4;
public static class OpcodeTableJson
{
    public static OpcodeTable Load(string path, string abiId = "AGE-catalog")
    {
        using FileStream stream = File.OpenRead(path);
        return Load(stream, abiId);
    }

    public static OpcodeTable Load(Stream stream, string abiId = "AGE-catalog")
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var doc = JsonDocument.Parse(stream);
        var observations = new Dictionary<string, OpcodeCorpusObservation>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("observations", out JsonElement observationArray))
        {
            foreach (JsonElement item in observationArray.EnumerateArray())
            {
                string profileId = item.GetProperty("profile_id").GetString() ?? "";
                observations[profileId] = new OpcodeCorpusObservation(
                    profileId,
                    item.GetProperty("catalog_revision").GetString() ?? "",
                    item.GetProperty("script_revision").GetString() ?? "",
                    item.GetProperty("engine_abi_id").GetString() ?? "",
                    item.GetProperty("artifact").GetString() ?? "",
                    item.GetProperty("script_count").GetInt32(),
                    item.GetProperty("instruction_count").GetInt64(),
                    item.GetProperty("opcodes").EnumerateArray()
                        .Select(value => Convert.ToInt32(value.GetString(), 16)).ToHashSet());
            }
        }
        var dict = new Dictionary<int, OpcodeDefinition>();
        foreach (var e in doc.RootElement.GetProperty("opcodes").EnumerateArray())
        {
            int op = Convert.ToInt32(e.GetProperty("op").GetString(), 16);
            string label = e.GetProperty("label").GetString() ?? "";
            int argc = e.GetProperty("argc").GetInt32();
            var observedBy = new HashSet<string>(StringComparer.Ordinal);
            if (e.TryGetProperty("observed_by", out JsonElement observation))
                foreach (JsonElement profile in observation.EnumerateArray())
                    if (profile.GetString() is { } profileId) observedBy.Add(profileId);
            string? semanticName = e.TryGetProperty("semantics", out var semantics)
                                   && semantics.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
            dict[op] = new OpcodeDefinition(op, label, argc, observedBy, semanticName);
        }
        return new OpcodeTable(dict, abiId, observations);
    }
}
