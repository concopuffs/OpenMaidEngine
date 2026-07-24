using Age.Engine.Model;
namespace Age.Engine.Hosting;

/// <summary>Resolves a call-script id (a raw SYS4INI file index) to a loaded <see cref="Script"/>.
/// Implemented in Sys4; injected into the VM so the Vm layer never references Sys4.</summary>
public interface IScriptProvider
{
    /// <summary>The script for this id, or null if the id maps to no known script.</summary>
    Script? GetById(long id);

    /// <summary>Selectors of currently mounted append catalogs. Opcode 0x143 scans these in
    /// ascending order and executes record zero from each catalog.</summary>
    IReadOnlyList<int> MountedAppendSelectors => Array.Empty<int>();
}
