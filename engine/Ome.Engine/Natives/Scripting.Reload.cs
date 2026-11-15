using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

// --lua-reload (docs/superpowers/specs/2026-11-10-lua-scripting-design.md §13): the front end watches an unpacked mod's
// lua/ folder, reads and checks the files on its own thread, and hands them over through InputAndWaits.Invoke. The
// native swaps them into the stack and rebuilds the session at the next instruction boundary with no Lua on the stack
// and outside any wait, carrying every mod's maid.state across. The link does not change: the handlers, hooks, events,
// write and call lists stay as the run linked them, so a reload only swaps function bodies (spec §3, decision 2).
public sealed partial class Scripting
{
    // A reload is due at the next boundary that allows one; the mods whose files changed since the session was built.
    private bool _reloadPending;
    private readonly SortedSet<string> _reloadedMods = new(StringComparer.Ordinal);

    /// <summary>
    /// Hand a mod's new <c>lua/</c> files over (spec §13). The front end calls it through <c>InputAndWaits.Invoke</c>
    /// once the files have passed the link's check: it only swaps them into the stack and returns. The running session
    /// is rebuilt with them at the next instruction boundary that has no Lua on the stack and is not inside a wait; with
    /// no session running, the next build takes them.
    /// </summary>
    public void RequestReload(string modId, IReadOnlyList<ModSource> files)
    {
        ModRuntimeMod[] mods = _setup.Stack.Mods.ToArray();
        int index = Array.FindIndex(mods, mod => mod.Id == modId);
        if (index < 0)
        {
            RefuseReload(modId, "it is not a mod of the stack that ships Lua");
            return;
        }
        mods[index] = mods[index] with { Files = files };
        _setup = _setup with { Stack = _setup.Stack with { Mods = mods } };
        _reloadedMods.Add(modId);
        if (_scene.Session != null && !_scene.ConsoleOnly) _reloadPending = true;
        WriteLog(ModLogLevel.Info, modId, $"lua/ changed ({files.Count} file{(files.Count == 1 ? "" : "s")}): the session "
                                          + "is rebuilt at the next instruction boundary outside a wait");
    }

    /// <summary>A reload the front end's check refused (spec §13): a syntax error, a handler the files no longer
    /// declare, a file that cannot be read. The running session is kept, and the log and a notice say why.</summary>
    public void RefuseReload(string modId, string problem)
    {
        WriteLog(ModLogLevel.Error, modId, $"not reloaded, the running session is kept: {problem}");
        Notify($"Lua: {modId} was not reloaded: {FirstLine(problem)}");
    }

    /// <summary>Whether a reload is waiting for its boundary.</summary>
    public bool ReloadPending => _reloadPending;

    /// <summary>
    /// The reload itself (spec §13), at a boundary <see cref="BoundaryPending"/> allows: every mod's state is captured,
    /// a session is built from the stack's files with it, and <c>ome.session.start</c> fires with <c>reload</c>. A
    /// state that does not serialize, or a build that switches off a mod that was on, keeps the running session and
    /// reports why (spec §5.5); a mod that was already off may stay off. The scene goes on: its records, its loaded
    /// blocks and its restore are the running session's. Handlers switched off for failing get another chance.
    /// </summary>
    private string? Reload()
    {
        _reloadPending = false;
        IModSession? running = _scene.Session;
        if (running == null || _scene.ConsoleOnly) return null;
        string reloaded = string.Join(", ", _reloadedMods);
        ModStateCapture capture = running.CaptureState();
        if (capture.Failures.Count > 0)
        {
            foreach (ModStateFailure failure in capture.Failures)
                RefuseReload(failure.ModId, $"its maid.state cannot be carried into the new session: {failure.Message}");
            return null;
        }
        var blocks = new Dictionary<string, ModStateBlock>(_scene.Blocks, StringComparer.Ordinal);
        foreach (var (modId, block) in capture.Blocks) blocks[modId] = block;
        var wasOff = running.Failures.Select(failure => failure.ModId).ToHashSet(StringComparer.Ordinal);

        // Spec §7.3: the build's writes and its start handlers' go with the next instruction's records; spec §10: its
        // code cannot call a script. The build's code sees no session, as at any build.
        _looseDepth++;
        _building++;
        try
        {
            _scene.Session = null;
            IModSession session;
            _depth++;
            try
            {
                session = Runtime().BuildSession(_setup.Stack, _access, new ModState(blocks), SessionReason.Reload,
                                                 _setup.Limits);
            }
            catch
            {
                _scene.Session = running;
                throw;
            }
            finally
            {
                _depth--;
            }
            var broke = session.Failures.Where(failure => !wasOff.Contains(failure.ModId)).ToList();
            if (broke.Count > 0)
            {
                session.Dispose();
                _scene.Session = running;
                foreach (ModBuildFailure failure in broke)
                {
                    WriteLog(ModLogLevel.Error, failure.ModId, "reload failed, the running session is kept: " + failure.Message
                                                               + (failure.Traceback is { } trace ? "\n" + trace : ""));
                    Notify($"Lua: reload failed, the running session is kept: {failure.ModId}: {FirstLine(failure.Message)}");
                }
                return null;
            }
            running.Dispose();
            _scene.Session = session;
            _scene.Reason = SessionReason.Reload;
            _scene.FailuresInARow.Clear();
            _scene.SwitchedOff.Clear();
            foreach (var (modId, block) in capture.Blocks) _scene.Blocks[modId] = block;
            _reloadedMods.Clear();
            foreach (ModBuildFailure failure in session.Failures)
            {
                WriteLog(ModLogLevel.Error, failure.ModId, $"still off for this session: {failure.Message}"
                                                           + (failure.Traceback is { } trace ? "\n" + trace : ""));
                Notify($"Lua: {failure.ModId} is still off for this session: {FirstLine(failure.Message)}");
            }
            WriteLog(ModLogLevel.Info, null, $"reloaded {reloaded}: the session was rebuilt, keeping maid.state");
            Notify($"Lua: reloaded {reloaded}");
            return RaiseEngineEvent(SessionStartEvent,
                new KeyValuePair<string, CellValue>("reason", CellValue.String(SessionReason.Reload.Name())));
        }
        finally
        {
            _building--;
            _looseDepth--;
        }
    }
}
