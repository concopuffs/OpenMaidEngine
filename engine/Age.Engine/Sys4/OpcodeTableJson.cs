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
        var dict = new Dictionary<int, OpcodeDefinition>();
        foreach (var e in doc.RootElement.GetProperty("opcodes").EnumerateArray())
        {
            int op = Convert.ToInt32(e.GetProperty("op").GetString(), 16);
            string label = e.GetProperty("label").GetString() ?? "";
            int argc = e.GetProperty("argc").GetInt32();
            bool observed = e.TryGetProperty("observed_in_himegari", out var observation)
                            && observation.GetBoolean();
            string? semanticName = e.TryGetProperty("semantics", out var semantics)
                                   && semantics.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
            dict[op] = new OpcodeDefinition(op, label, argc, observed, semanticName);
        }
        return new OpcodeTable(dict, abiId);
    }
}
