namespace Ome.Mods.Manifest;

/// <summary>The forms a <c>[[hooks]]</c> entry takes (Lua scripting spec §6.3).</summary>
public enum ModHookForm
{
    /// <summary><c>event = "…"</c>: an event a base mod declares.</summary>
    Event,
    /// <summary><c>engine = "ome.…"</c>: an event the engine defines (§7.7).</summary>
    Engine,
    /// <summary><c>point = "…"</c>: a named patch point.</summary>
    Point,
    /// <summary><c>script</c>, <c>fingerprint</c>, <c>site</c>, <c>when = "before"|"after"</c>: an original offset.</summary>
    Site,
    /// <summary><c>script</c>, <c>when = "enter"|"exit"</c>: a script's entry or exit (§7.2).</summary>
    Script,
    /// <summary><c>watch = "…"</c>: writes to a global (§7.3).</summary>
    Watch,
}

/// <summary>A <c>[lua]</c> table (Lua scripting spec §6.2).</summary>
public sealed record ModLuaSettings(int Api, IReadOnlyList<string> Capabilities, IReadOnlyList<string> Writes,
                                    IReadOnlyList<string> Calls)
{
    /// <summary>The <c>maid</c> API version this engine provides.</summary>
    public const int CurrentApi = 1;
    /// <summary>Raw VM access (§8.1), the one capability of version 1.</summary>
    public const string VmCapability = "vm";
    public static readonly IReadOnlyList<string> KnownCapabilities = [VmCapability];

    public bool HasVm => Capabilities.Contains(VmCapability, StringComparer.Ordinal);
}

/// <summary>A handler: a file under <c>lua/</c> and a function declared at its top level (§6.2).</summary>
public sealed record ModHandlerRef(string File, string Function)
{
    public override string ToString() => $"{File}:{Function}";
}

/// <summary>One <c>[[hooks]]</c> entry, in manifest order (§6.3). <see cref="Target"/> is the event, engine event,
/// point, script or watched name the form keys on.</summary>
public sealed record ModHook(int Index, ModHookForm Form, string Target, ModHandlerRef Handler)
{
    public int Priority { get; init; }
    /// <summary>A site hook's script fingerprint (<c>sha256:</c>).</summary>
    public string? Fingerprint { get; init; }
    /// <summary>A site hook's original dword offset.</summary>
    public int? Site { get; init; }
    /// <summary><c>before</c> or <c>after</c> for a site, <c>enter</c> or <c>exit</c> for a script.</summary>
    public string? When { get; init; }
    /// <summary>The hook may skip the instruction (§7.1).</summary>
    public bool Skip { get; init; }

    /// <summary>How the manifest names it, for reports: <c>hooks[2] point himegari.x</c>.</summary>
    public string Describe() => $"hooks[{Index}] {Form.ToString().ToLowerInvariant()} {Target}";
}
