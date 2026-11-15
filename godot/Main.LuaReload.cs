using System.Collections.Generic;
using System.Linq;
using Godot;
using Ome.Engine.Natives;
using Ome.Mods.Launch;
using Ome.Mods.Linking;

/// <summary>--lua-reload (Lua scripting spec §13): the port watches each unpacked mod's <c>lua/</c> and hands its checked
/// files to the scripting native, which rebuilds the session at the next instruction boundary outside a wait.</summary>
public partial class Main
{
    /// <summary>How long a delivery waits for the engine thread to take it before it is dropped.</summary>
    private const int LuaReloadHandOverMs = 10_000;

    private IReadOnlyDictionary<string, string> _unpackedLua = new Dictionary<string, string>();
    private LuaReloadWatcher? _luaReload;

    private void StartLuaReload(IReadOnlyList<string> userArgs)
    {
        if (!userArgs.Contains("--lua-reload")) return;
        if (_scripting is not { Setup.Stack: { HasLua: true } stack } scripting)
        {
            GD.PushWarning("[lua] --lua-reload: no mod ships Lua, so there is nothing to watch");
            return;
        }
        _luaReload = new LuaReloadWatcher(_unpackedLua, stack, _luaRuntime.Get(),
            (modId, check) => HandOverLuaReload(scripting, modId, check));
        if (_luaReload.Watched.Count == 0)
            GD.PushWarning("[lua] --lua-reload: no mod that ships Lua is unpacked; a packed .omp is not watched");
        else
            GD.Print($"[lua] --lua-reload watches {string.Join(", ", _luaReload.Watched.Select(pair => $"{pair.Key} ({pair.Value})"))}");
    }

    // On a pool thread: a request only hands the files over (spec §13), and the native logs and notifies the outcome.
    private void HandOverLuaReload(Scripting scripting, string modId, ModLuaReloadCheck check)
    {
        if (_done) return;
        bool taken = _waits.TryInvoke(() =>
        {
            if (check.Passed) scripting.RequestReload(modId, check.Files);
            else scripting.RefuseReload(modId, string.Join("; ", check.Problems));
            return true;
        }, LuaReloadHandOverMs, out _);
        if (!taken)
            GD.PushWarning($"[lua] --lua-reload: the engine did not take {modId}'s files within {LuaReloadHandOverMs / 1000} s; "
                           + "they were dropped");
        else if (check.Passed)
            GD.Print($"[lua] {modId}: lua/ changed; the session is rebuilt at the next instruction boundary outside a wait");
        else
            GD.PushWarning($"[lua] {modId} was not reloaded: {string.Join("; ", check.Problems)}");
    }
}
