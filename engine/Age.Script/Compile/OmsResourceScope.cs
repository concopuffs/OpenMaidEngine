namespace Age.Script.Compile;

/// <summary>
/// The names a mod's code may reach by reference rather than by packed id (mod loader spec §4.3–§4.4, §6.2): its own
/// new scripts and assets, and those of the mods it declares in <c>[[requires]]</c>. A name resolves to the canonical
/// reference <c>mod.id:NAME</c>; the linker turns references into packed ids once selectors are allocated.
/// </summary>
public sealed class OmsResourceScope
{
    private readonly Dictionary<string, (HashSet<string> Scripts, HashSet<string> Assets)> _mods = new(StringComparer.Ordinal);

    /// <summary>The mod whose code is compiled.</summary>
    public string ModId { get; }

    public OmsResourceScope(string modId, IEnumerable<string> scripts, IEnumerable<string> assets)
    {
        ModId = modId;
        Add(modId, scripts, assets);
    }

    /// <summary>Makes a declared dependency's names reachable as <c>mod.id:NAME</c>.</summary>
    public OmsResourceScope WithDependency(string modId, IEnumerable<string> scripts, IEnumerable<string> assets)
    {
        Add(modId, scripts, assets);
        return this;
    }

    private void Add(string modId, IEnumerable<string> scripts, IEnumerable<string> assets)
        => _mods[modId] = (new HashSet<string>(scripts, StringComparer.OrdinalIgnoreCase),
                           new HashSet<string>(assets, StringComparer.OrdinalIgnoreCase));

    /// <summary>The canonical reference of a script name, or null when the name is not a mod script (it may then be a
    /// base script). A qualified name (<c>mod.id:NAME</c>) that does not resolve is an error.</summary>
    public string? Script(string name, out string? error) => Resolve(name, kind: "script", assets: false, out error);

    /// <summary>The canonical reference of an asset name; an unknown name is an error, since base assets are
    /// addressed by packed id in version 1.</summary>
    public string? Asset(string name, out string? error)
    {
        string? reference = Resolve(name, kind: "asset", assets: true, out error);
        if (reference == null && error == null)
            error = $"'{name}' is not a new asset of {ModId}; base assets are addressed by packed id";
        return reference;
    }

    private string? Resolve(string name, string kind, bool assets, out string? error)
    {
        error = null;
        int colon = name.IndexOf(':');
        string modId = colon < 0 ? ModId : name[..colon];
        string local = colon < 0 ? name : name[(colon + 1)..];
        if (!_mods.TryGetValue(modId, out var names))
        {
            error = $"'{modId}' is not {ModId} or a mod it declares in [[requires]], so '{name}' cannot be reached";
            return null;
        }
        HashSet<string> set = assets ? names.Assets : names.Scripts;
        string? match = set.FirstOrDefault(entry => string.Equals(entry, local, StringComparison.OrdinalIgnoreCase));
        if (match != null) return $"{modId}:{match}";
        if (colon >= 0) error = $"{modId} has no {kind} named '{local}'";
        return null;
    }
}
