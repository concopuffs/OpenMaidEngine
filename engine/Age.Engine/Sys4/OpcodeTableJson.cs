using System.Text.Json;
using Age.Engine.Model;
namespace Age.Engine.Sys4;
public static class OpcodeTableJson
{
    public static OpcodeTable Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var dict = new Dictionary<int, (string, int)>();
        foreach (var e in doc.RootElement.GetProperty("opcodes").EnumerateArray())
        {
            int op = Convert.ToInt32(e.GetProperty("op").GetString(), 16);
            string label = e.GetProperty("label").GetString() ?? "";
            int argc = e.GetProperty("argc").GetInt32();
            dict[op] = (label, argc);
        }
        return new OpcodeTable(dict);
    }
}
