using System.Text.Json;
using Age.Engine.Hosting;
using Age.Engine.Model;
namespace Age.Engine.Sys4;

/// <summary>Resolves call-script ids (raw SYS4INI file indices) to loaded scripts, using
/// build/callscript-names.json (id→name) + Paths.Scripts() (name→path). Cached per id.
/// The native resolver prefers a loose override before the archive; Paths.Scripts() already
/// shadows extracted/DATA1 with root overrides, so that behavior is preserved.</summary>
public sealed class Sys4ScriptProvider : IScriptProvider
{
    private readonly OpcodeTable _table;
    private readonly IReadOnlyDictionary<long, string> _idToName;
    private readonly Dictionary<string, string> _byName;      // NAME(UPPER) -> path
    private readonly Dictionary<long, Script?> _cache = new();

    public Sys4ScriptProvider(OpcodeTable table, IReadOnlyDictionary<long, string> idToName,
                              Dictionary<string, string> byName)
    { _table = table; _idToName = idToName; _byName = byName; }

    public static Sys4ScriptProvider Load(OpcodeTable table)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Paths.CallscriptNamesJson)) ?? new();
        var idToName = raw.ToDictionary(kv => long.Parse(kv.Key), kv => kv.Value);
        return new Sys4ScriptProvider(table, idToName, Paths.Scripts());
    }

    public Script? GetById(long id)
    {
        if (_cache.TryGetValue(id, out var cached)) return cached;
        Script? s = null;
        if (_idToName.TryGetValue(id, out var name) &&
            _byName.TryGetValue(name.ToUpperInvariant(), out var path))
            s = Sys4Loader.Load(path, _table);
        _cache[id] = s;
        return s;
    }
}
