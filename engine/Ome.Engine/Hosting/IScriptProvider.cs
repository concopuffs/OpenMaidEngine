using Ome.Engine.Model;
namespace Ome.Engine.Hosting;

/// <summary>Resolves a call-script id (a raw SYS4INI file index) to a loaded <see cref="Script"/>.
/// Implemented in Sys4; injected into the VM so the Vm layer never references Sys4.</summary>
public interface IScriptProvider
{
    /// <summary>The script for this id, or null if the id maps to no known script.</summary>
    Script? GetById(long id);

    /// <summary>Resolve a fixed engine callback filename such as CALLBACK_LOAD.BIN. Only the base catalog's names, as
    /// AGE's own lookups by name.</summary>
    Script? GetByName(string name) => null;

    /// <summary>A script by its catalog name, an append record's by its <c>$n$</c> name too: the scripts a mod calls and
    /// names (Lua scripting spec §10). The engine's own lookups use <see cref="GetByName"/>.</summary>
    Script? GetByMountedName(string name) => GetByName(name);

    /// <summary>Selectors of currently mounted append catalogs. Opcode 0x143 scans these in
    /// ascending order and executes record zero from each catalog.</summary>
    IReadOnlyList<int> MountedAppendSelectors => Array.Empty<int>();
}

/// <summary>A fixed set of scripts by call-script id, for a self-test or a tool that builds its own scripts.</summary>
public sealed class ScriptMap(IReadOnlyDictionary<long, Script> scripts) : IScriptProvider
{
    public Script? GetById(long id) => scripts.GetValueOrDefault(id);
}
