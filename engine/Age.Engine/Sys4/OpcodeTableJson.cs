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
        var catalog = new Dictionary<int, OpcodeDefinition>();
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
            var evidenceRevisions = new HashSet<string>(StringComparer.Ordinal);
            if (semantics.ValueKind != JsonValueKind.Undefined
                && semantics.TryGetProperty("evidence", out JsonElement evidenceArray))
            {
                foreach (JsonElement evidence in evidenceArray.EnumerateArray())
                    if (evidence.TryGetProperty("engine_revisions", out JsonElement revisions))
                        foreach (JsonElement revision in revisions.EnumerateArray())
                            if (revision.GetString() is { } value) evidenceRevisions.Add(value);
            }
            catalog[op] = new OpcodeDefinition(
                op, label, argc, observedBy, semanticName, evidenceRevisions);
        }

        if (!doc.RootElement.TryGetProperty("abi_layers", out JsonElement layerArray))
            return new OpcodeTable(catalog, abiId, observations);

        var layers = layerArray.EnumerateArray()
            .Select(item => AbiLayerDocument.Parse(item, catalog))
            .ToDictionary(layer => layer.Id, StringComparer.Ordinal);
        if (!layers.ContainsKey(abiId))
            throw new InvalidDataException(
                $"opcode ABI layer '{abiId}' is not present; available layers: "
                + string.Join(", ", layers.Keys.Order(StringComparer.Ordinal)));

        var active = new HashSet<string>(StringComparer.Ordinal);
        var applied = new List<string>();
        Dictionary<int, OpcodeDefinition> resolved = ResolveLayer(
            abiId, layers, catalog, active, applied);
        return new OpcodeTable(resolved, abiId, observations, applied);
    }

    private static Dictionary<int, OpcodeDefinition> ResolveLayer(
        string id,
        IReadOnlyDictionary<string, AbiLayerDocument> layers,
        IReadOnlyDictionary<int, OpcodeDefinition> catalog,
        HashSet<string> active,
        List<string> applied)
    {
        if (!active.Add(id))
            throw new InvalidDataException($"opcode ABI layer composition cycle at '{id}'");
        if (!layers.TryGetValue(id, out AbiLayerDocument? layer))
            throw new InvalidDataException($"opcode ABI layer '{id}' is not present");

        var resolved = new Dictionary<int, OpcodeDefinition>();
        foreach (string parent in layer.Composes)
            foreach (var pair in ResolveLayer(parent, layers, catalog, active, applied))
                resolved[pair.Key] = pair.Value;
        active.Remove(id);

        OpcodeResolutionProvenance provenance = layer.Provenance;
        foreach (int op in layer.Members)
        {
            if (!catalog.TryGetValue(op, out OpcodeDefinition? definition))
                throw new InvalidDataException($"opcode ABI layer '{id}' references missing 0x{op:x}");
            resolved[op] = definition with { Resolution = provenance };
        }
        foreach (var pair in layer.Contracts)
            resolved[pair.Key] = pair.Value with { Resolution = provenance };
        foreach (int op in layer.Removes) resolved.Remove(op);
        if (!applied.Contains(id, StringComparer.Ordinal)) applied.Add(id);
        return resolved;
    }

    private sealed record AbiLayerDocument(
        string Id,
        IReadOnlyList<string> Composes,
        IReadOnlySet<int> Members,
        IReadOnlyDictionary<int, OpcodeDefinition> Contracts,
        IReadOnlySet<int> Removes,
        OpcodeResolutionProvenance Provenance)
    {
        public static AbiLayerDocument Parse(
            JsonElement item, IReadOnlyDictionary<int, OpcodeDefinition> catalog)
        {
            string id = item.GetProperty("id").GetString() ?? "";
            string kind = item.GetProperty("kind").GetString() ?? "";
            string[] composes = item.GetProperty("composes").EnumerateArray()
                .Select(value => value.GetString() ?? "").ToArray();
            JsonElement evidence = item.GetProperty("evidence");
            var provenance = new OpcodeResolutionProvenance(
                id,
                kind,
                composes,
                evidence.GetProperty("method").GetString() ?? "",
                evidence.GetProperty("artifact").GetString() ?? "",
                evidence.GetProperty("scope").GetString() ?? "");
            HashSet<int> members = ReadOpcodeSet(item, "members");
            HashSet<int> removes = ReadOpcodeSet(item, "removes");
            var contracts = new Dictionary<int, OpcodeDefinition>();
            if (item.TryGetProperty("contracts", out JsonElement contractArray))
            {
                foreach (JsonElement contract in contractArray.EnumerateArray())
                {
                    int op = Convert.ToInt32(contract.GetProperty("op").GetString(), 16);
                    catalog.TryGetValue(op, out OpcodeDefinition? inherited);
                    contracts[op] = new OpcodeDefinition(
                        op,
                        contract.GetProperty("label").GetString() ?? "",
                        contract.GetProperty("argc").GetInt32(),
                        inherited?.ObservedBy ?? new HashSet<string>(StringComparer.Ordinal),
                        contract.TryGetProperty("semantic_name", out JsonElement semanticName)
                            ? semanticName.GetString() : inherited?.SemanticName,
                        inherited?.EvidenceRevisions,
                        contract.TryGetProperty(
                            "handler_implementation_id", out JsonElement implementationId)
                            ? implementationId.GetString() : inherited?.HandlerImplementationId);
                }
            }
            return new AbiLayerDocument(id, composes, members, contracts, removes, provenance);
        }

        private static HashSet<int> ReadOpcodeSet(JsonElement item, string property)
            => item.TryGetProperty(property, out JsonElement values)
                ? values.EnumerateArray()
                    .Select(value => Convert.ToInt32(value.GetString(), 16)).ToHashSet()
                : [];
    }
}
