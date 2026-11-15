using Ome.Engine.Modding;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

namespace Ome.Mods.Linking;

/// <summary>What a reload read from an unpacked mod's <c>lua/</c>: its files, or why they cannot replace the running
/// ones.</summary>
public sealed record ModLuaReloadCheck(IReadOnlyList<ModSource> Files, IReadOnlyList<string> Problems)
{
    public bool Passed => Problems.Count == 0;
}

/// <summary>
/// The front end's half of <c>--lua-reload</c> (docs/superpowers/specs/2026-11-10-lua-scripting-design.md §13): read an
/// unpacked mod's <c>lua/</c> files again and check them as the link does, running no mod code. The link itself does
/// not run again: the hooks, the events and the lists stay as the run linked them, so a file must still declare every
/// handler the mod's hooks name. A change to the manifest or to <c>events/</c> needs a restart.
/// </summary>
public static class ModLuaReload
{
    /// <param name="folder">The mod's folder, as the launch opened it (<c>ModLaunchResult.UnpackedLua</c>).</param>
    /// <param name="mod">The mod as the running stack has it.</param>
    /// <param name="handlers">The stack's handlers: the mod's own are checked.</param>
    /// <param name="runtime">The run's Lua runtime; its check is thread-safe.</param>
    public static ModLuaReloadCheck Read(string folder, ModRuntimeMod mod, IEnumerable<ModRuntimeHandler> handlers,
                                         IModRuntime runtime)
    {
        ModPackage package;
        try
        {
            package = ModPackage.Open(folder);
        }
        catch (Exception error) when (error is ModPackageException or ModManifestException or IOException
                                          or InvalidDataException or UnauthorizedAccessException)
        {
            return new ModLuaReloadCheck([], [$"{folder}: {error.Message}"]);
        }
        using (package)
        {
            if (package.Id != mod.Id)
                return new ModLuaReloadCheck([], [$"{folder} is now mod {package.Id}, not {mod.Id}; a changed manifest needs a restart"]);
            var problems = new List<string>();
            var files = new List<ModSource>();
            var checks = new Dictionary<string, ModSourceCheck>(StringComparer.Ordinal);
            foreach (ModLuaFile file in package.Layout.Lua)
            {
                string text;
                try
                {
                    text = package.ReadText(file.File);
                }
                catch (Exception error) when (error is ModPackageException or IOException or UnauthorizedAccessException)
                {
                    problems.Add($"lua/{file.Path}: {error.Message}");
                    continue;
                }
                var source = new ModSource(mod.Id, file.Path, text);
                ModSourceCheck check = runtime.Check(source);
                foreach (ModSourceError error in check.Errors) problems.Add($"lua/{file.Path}: {error}");
                string module = ModLuaLinker.ModuleName(file.Path);
                if (mod.Requires.Contains(module, StringComparer.Ordinal))
                    problems.Add($"lua/{file.Path} is module '{module}', which is also the id of a mod it requires, so "
                                 + $"require(\"{module}\") would name both; rename the file");
                files.Add(source);
                checks[file.Path] = check;
            }
            foreach (ModRuntimeHandler handler in handlers.Where(handler => handler.ModId == mod.Id).Distinct())
            {
                if (!checks.TryGetValue(handler.File, out ModSourceCheck? check))
                    problems.Add($"lua/{handler.File} is gone, and a hook names its {handler.Function}; changing the hooks "
                                 + "needs a restart");
                else if (check.Parsed && !check.TopLevelFunctions.Contains(handler.Function))
                    problems.Add($"lua/{handler.File} declares no top-level function {handler.Function}, which a hook names; a "
                                 + $"handler is a global function of its file (function {handler.Function}(…))");
            }
            return new ModLuaReloadCheck(files, problems);
        }
    }
}
