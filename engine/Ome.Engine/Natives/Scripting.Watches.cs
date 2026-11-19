using Ome.Engine.Model;
using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

// Watches (Lua scripting spec §7.3): script memory's setters record a write to a watched cell, and the interpreter has
// the records delivered once the instruction that made them has returned. Instructions nest (a wait's callback and a
// called script run inside the outer instruction's handler), so each running instruction owns the records made while
// it is the innermost one, and delivers only those: an inner one delivers its own and leaves the outer one's.
public sealed partial class Scripting
{
    /// <summary>A recorded write: the write, what made it, and how many watch deliveries deep it was made (a watch
    /// handler's write is one deeper than the record it was handling), which the nesting cap bounds.</summary>
    private readonly record struct PendingWrite(CellWrite Write, CellWriter Writer, string? Mod, Script? Script, int Index,
                                                int Generation);

    /// <summary>A running instruction: where it is, where its records start, and the session it started in.</summary>
    private readonly record struct RunningInstruction(Script Script, int Index, int First, long Session);

    private readonly PlannedWatch[] _watches;
    // The handler running, innermost (spec §12: a write is its mod's); null between calls. With a contribution, the call
    // is a delivery, and a write is its contributor's (data patches spec §5.7).
    private HandlerId? _running;
    private ContributionId? _runningContribution;
    // How many console chunks are running.
    private int _consoleDepth;
    // How many session builds and boundary events are running, whose writes go with the next instruction's records.
    private int _looseDepth;
    // The mod whose face made the latest write: the writer of a build's write, which runs no handler.
    private string? _faceMod;
    // The generation of the record whose handlers run now, or -1 outside a delivery.
    private int _delivering = -1;

    /// <summary>Whether the plan has a watch: the interpreter brackets each instruction for its records only then.</summary>
    public bool Watching => _watches.Length > 0;

    private WatchedCells? WatchedCells()
        => _watches.Length == 0
            ? null
            : new WatchedCells(_watches.Select(watch => (watch.Kind, watch.Address, watch.Count)), Recorded);

    /// <summary>
    /// An instruction starts (spec §7.3): the interpreter calls it before the instruction's before hooks, so the hooks'
    /// writes are its records too. The records made outside any instruction since the last delivery join it. Returns
    /// the slot <see cref="EndInstruction"/> takes.
    /// </summary>
    public int BeginInstruction(Script script, int index)
    {
        Scene scene = _scene;
        int slot = scene.Open.Count;
        scene.Open.Add(new RunningInstruction(script, index, scene.Pending.Count, _sessionsEnded));
        AdoptLoose(scene);
        return slot;
    }

    /// <summary>
    /// The instruction of <paramref name="slot"/> has returned and its after hooks have run (spec §7.3, §7.4): its
    /// records are delivered in the order they were made, each to the handlers of every watch covering its cell, and a
    /// handler's write joins the end. With <paramref name="deliver"/> false (the instruction halted the run) they are
    /// dropped. So are they when the session ended during the instruction: they went with it (spec §5.7). Returns a
    /// halt reason under <c>--lua-errors halt</c>.
    /// </summary>
    public string? EndInstruction(int slot, bool deliver)
    {
        Scene scene = _scene;
        if (slot >= scene.Open.Count || scene.Open[slot].Session != _sessionsEnded) return null;
        int first = scene.Open[slot].First;
        string? halt = null;
        if (deliver && scene.Session is { } session)
        {
            AdoptLoose(scene);
            for (int i = first; i < scene.Pending.Count && halt == null && ReferenceEquals(scene, _scene); i++)
                halt = Deliver(session, scene.Pending[i]);
        }
        if (scene.Pending.Count > first) scene.Pending.RemoveRange(first, scene.Pending.Count - first);
        scene.Open.RemoveRange(slot, scene.Open.Count - slot);
        return halt;
    }

    private static void AdoptLoose(Scene scene)
    {
        if (scene.Loose.Count == 0) return;
        scene.Pending.AddRange(scene.Loose);
        scene.Loose.Clear();
    }

    /// <summary>Script memory's record of a write to a watched cell: what wrote it, and whose records it joins.</summary>
    private void Recorded(CellWrite write)
    {
        Scene scene = _scene;
        // Between sessions, outside a build, a write has no handler to go to: op 0x19f's status write follows the end of
        // its session (spec §5.7).
        if (scene.Session == null && _looseDepth == 0 && _consoleDepth == 0) return;
        CellWriter writer;
        string? mod = null;
        Script? script = null;
        int index = -1;
        // Data patches plan §12.6: a failed delivery's cell put back is the engine's write, after the delivery's own.
        if (_puttingBack) writer = CellWriter.Engine;
        else if (_running != null)
        {
            writer = CellWriter.Mod;
            mod = RunningMod;
        }
        else if (_consoleDepth > 0) writer = CellWriter.Console;
        else if (_depth > 0 && _faceMod != null)
        {
            writer = CellWriter.Mod;
            mod = _faceMod;
        }
        else if (scene.Open.Count > 0)
        {
            writer = CellWriter.Script;
            (script, index) = (scene.Open[^1].Script, scene.Open[^1].Index);
        }
        else writer = CellWriter.Engine;
        int generation = _delivering >= 0 && _running != null ? _delivering + 1 : 0;
        var pending = new PendingWrite(write, writer, mod, script, index, generation);
        // Spec §7.3: the build's writes, a console chunk's and a boundary event's go with the next instruction's.
        if (_looseDepth > 0 || _consoleDepth > 0 || scene.Open.Count == 0) scene.Loose.Add(pending);
        else scene.Pending.Add(pending);
    }

    /// <summary>One record's handlers: those of every watch covering its cell, in the merged order (spec §7.4).</summary>
    private string? Deliver(IModSession session, PendingWrite pending)
    {
        CellWrite write = pending.Write;
        PlannedWatch? only = null;
        List<(PlannedHandler Handler, PlannedWatch Watch)>? merged = null;
        foreach (PlannedWatch watch in _watches)
        {
            if (!watch.Covers(write.Kind, write.Address)) continue;
            if (only == null)
            {
                only = watch;
                continue;
            }
            merged ??= only.Handlers.Select(handler => (handler, only)).ToList();
            merged.AddRange(watch.Handlers.Select(handler => (handler, watch)));
        }
        if (only == null) return null;
        merged?.Sort((a, b) => a.Handler.Priority != b.Handler.Priority
            ? a.Handler.Priority.CompareTo(b.Handler.Priority)
            : a.Handler.LoadOrder.CompareTo(b.Handler.LoadOrder));
        int count = merged?.Count ?? only.Handlers.Count;
        int outer = _delivering;
        _delivering = pending.Generation;
        try
        {
            for (int i = 0; i < count; i++)
            {
                var (handler, watch) = merged != null ? merged[i] : (only.Handlers[i], only);
                if (!IsOn(session, handler.Handler)) continue;
                HookContext context = WatchContext(watch, pending);
                // Spec §7.3, §5.5: a handler's write to a cell it watches comes back to it a delivery deeper; the cap
                // ends such a loop as a call nested too deep.
                string? halt = pending.Generation >= _setup.Limits.NestingCap
                    ? Failed(handler.Handler, new ModScriptException(ModScriptErrorKind.Depth,
                        $"watch handlers' writes nest {pending.Generation} deep, the cap is {_setup.Limits.NestingCap}"),
                        Where(context))
                    : Call(session, handler.Handler, context, frameAccess: false);
                if (halt != null) return halt;
            }
        }
        finally
        {
            _delivering = outer;
        }
        return null;
    }

    private HookContext WatchContext(PlannedWatch watch, PendingWrite pending)
    {
        CellWrite write = pending.Write;
        string? global = watch.Global;
        int row = -1, column = -1;
        if (global != null)
        {
            int offset = write.Address - watch.Address;
            if (watch.Dimensions > 0) row = offset / watch.RowWidth;
            if (watch.Dimensions == 2) column = offset % watch.RowWidth;
        }
        else if (ConsoleLayout()?.Layout.GlobalAt(write.Kind, write.Address, out row, out column) is { } named)
            global = named.Name;
        CellValue Value(long number, string? text)
            => write.Kind == CellKind.String ? CellValue.String(text ?? "") : CellValue.Integer(CellRules.ReadInteger(number));
        string? scriptName = pending.Script?.Name;
        int linked = pending.Script is { } script ? script.Instructions[pending.Index].Offset : -1;
        return HookContext.ForWatch(watch.Watch, CellText(write.Kind, write.Address, local: false), write.Address, global, row,
            column, Value(write.OldNumber, write.OldText), Value(write.NewNumber, write.NewText), pending.Writer, pending.Mod,
            scriptName, linked, scriptName == null ? -1 : _setup.Sites.Site(scriptName, linked) ?? -1);
    }
}
