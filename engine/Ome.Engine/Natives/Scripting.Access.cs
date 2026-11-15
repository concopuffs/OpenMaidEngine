using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

// The facade of spec §8.1 and §14.2: one face for each mod, over the script memory native for the cells and the
// interpreter for frames and locals. Every check a write must pass is here, so a runtime cannot loosen it.
public sealed partial class Scripting
{
    /// <summary>The console's id in the log, and the face it runs with.</summary>
    public const string ConsoleId = "console";

    private sealed class ScriptAccess(Scripting owner) : IScriptAccess
    {
        private readonly Dictionary<string, ModFace> _faces = new(StringComparer.Ordinal);
        private ModFace? _console;

        public IModAccess Mod(string modId)
        {
            if (_faces.TryGetValue(modId, out ModFace? face)) return face;
            ModRuntimeMod mod = owner._setup.Stack.Mods.FirstOrDefault(entry => entry.Id == modId)
                                ?? throw new ArgumentException($"{modId} is not a mod of the stack", nameof(modId));
            _faces[modId] = face = new ModFace(owner, modId, mod, mod.Vm);
            return face;
        }

        public IModAccess Console => _console ??= new ModFace(owner, ConsoleId, null, vm: true);

        public bool Restoring => owner._scene.Restoring;
    }

    /// <summary>A base mod's layout as a face reads it: one the stack mounts, else for the console the profile's.</summary>
    private BaseModLayout? LayoutOf(string baseModId, bool console)
    {
        foreach (ModRuntimeMod mod in _setup.Stack.Mods)
            if (mod.Id == baseModId && mod.Layout != null) return new BaseModLayout(mod.Id, mod.Layout);
        return console && _consoleLayout.Value is { } layout && layout.ModId == baseModId ? layout : null;
    }

    /// <summary>The console's layout (spec §13): the base mod the stack mounts, else the profile's, read at first use.</summary>
    private BaseModLayout? ConsoleLayout()
    {
        foreach (ModRuntimeMod mod in _setup.Stack.Mods)
            if (mod.Layout != null) return new BaseModLayout(mod.Id, mod.Layout);
        return _consoleLayout.Value;
    }

    // The (mod, name) pairs --lua-dev has let through and logged, once each for the run (spec §9.3).
    private readonly HashSet<string> _devWrites = new(StringComparer.Ordinal);

    /// <summary>A mod's face, or the console's (no mod): its capability, its write list and the base mods it may
    /// name.</summary>
    private sealed class ModFace(Scripting owner, string modId, ModRuntimeMod? mod, bool vm) : IModAccess
    {
        private readonly Dictionary<string, INamedAccess?> _names = new(StringComparer.Ordinal);

        public string ModId { get; } = modId;
        public IVmAccess? Vm { get; } = vm ? new VmFace(owner, modId) : null;

        // Spec §5.6: script memory's generator, the one op 0x60 draws from.
        public int DrawRandom() => owner._memory.DrawForMod();

        public void Log(ModLogLevel level, string message) => owner.WriteLog(level, ModId, message);

        public string? OwnLayout => mod == null ? owner.ConsoleLayout()?.ModId : mod.Layout != null ? mod.Id : null;

        public IReadOnlyDictionary<string, CellValue> Emit(string eventName, IReadOnlyDictionary<string, CellValue> fields)
            => owner.Emit(ModId, eventName, fields);

        public INamedAccess? Names(string baseModId)
        {
            if (!_names.TryGetValue(baseModId, out INamedAccess? face)) _names[baseModId] = face = Build(baseModId);
            return face;
        }

        // Spec §9.3: a mod that requires the base mod writes what its list names; the base mod's own accessors and the
        // console's have no list (spec §8.4, §13).
        private INamedAccess? Build(string baseModId)
        {
            if (owner.LayoutOf(baseModId, console: mod == null) is not { } layout) return null;
            if (mod == null || mod.Id == baseModId) return new NamedFace(owner, ModId, layout, null);
            return mod.Requires.Contains(baseModId, StringComparer.Ordinal)
                ? new NamedFace(owner, ModId, layout, mod.WriteList.ToHashSet(StringComparer.Ordinal), mod.LuaDev)
                : null;
        }
    }

    /// <summary>A base mod's accessors for one mod (spec §9.3): the index rules of <see cref="LayoutCells"/>, the value
    /// rules of <see cref="CellRules"/>, and the mod's write list, which <paramref name="luaDev"/> turns from a refusal
    /// into a report.</summary>
    private sealed class NamedFace(Scripting owner, string modId, BaseModLayout layout, IReadOnlySet<string>? writes,
                                   bool luaDev = false) : INamedAccess
    {
        public string BaseModId => layout.ModId;
        public GameLayout Layout => layout.Layout;
        public IReadOnlySet<string>? Writes => writes;

        public LayoutGlobal Global(string name)
            => layout.Layout.TryGet(name, out LayoutGlobal global)
                ? global
                : throw new ScriptAccessException($"{layout.ModId} exports no global named '{name}'");

        public int Row(LayoutGlobal global, double index) => LayoutCells.Row(global, index);
        public int Column(LayoutGlobal global, double index) => LayoutCells.Column(global, index);
        public int Named(LayoutGlobal global, string column) => LayoutCells.Named(global, column);

        public CellValue Read(LayoutGlobal global, int row = -1, int column = -1)
            => owner.ReadCell(global.Kind, LayoutCells.Address(global, row, column));

        public void Write(LayoutGlobal global, int row, int column, CellValue value)
        {
            if (writes != null && !writes.Contains(global.Name))
            {
                string list = writes.Count == 0 ? "it lists none"
                    : "writes = [" + string.Join(", ", writes.Order(StringComparer.Ordinal)) + "]";
                if (!luaDev)
                    throw new ScriptAccessException($"{modId} writes {global.Name}, which its [lua] writes list does not name ({list})");
                // Spec §9.3: under --lua-dev the write goes through, and the log names it once.
                if (owner._devWrites.Add($"{modId}\n{global.Name}"))
                    owner.WriteLog(ModLogLevel.Warn, modId, $"writes {global.Name}, which its [lua] writes list does not name "
                                                            + $"({list}); --lua-dev lets it through: add it to the list");
            }
            int address = LayoutCells.Address(global, row, column);
            if (!owner.Tracing)
            {
                owner.WriteCell(global.Kind, address, value, global.Name);
                return;
            }
            CellValue before = owner.ReadCell(global.Kind, address);
            owner.WriteCell(global.Kind, address, value, global.Name);
            string name = global.Name + (row >= 0 ? $"[{row}]" : "") + (column >= 0 ? $"[{column}]" : "");
            owner.TraceWrite(CellText(global.Kind, address, local: false), name, modId, before, owner.ReadCell(global.Kind, address));
        }
    }

    /// <summary><c>maid.vm</c> for one mod (<paramref name="modId"/> names it in the trace).</summary>
    private sealed class VmFace(Scripting owner, string modId) : IVmAccess
    {
        private ScriptMemory Memory => owner._memory;
        private int CodePage => owner._memory.Encoding.CodePage;

        public double ReadInteger(double address) => CellRules.ReadInteger(Memory.Integer(CellRules.Address(address)));

        public void WriteInteger(double address, double value)
        {
            int cell = CellRules.Address(address);
            var integer = CellRules.Integer(value);
            CellValue? before = GlobalBefore(CellKind.Integer, cell);
            Memory.SetInteger(cell, integer);
            GlobalAfter(CellKind.Integer, cell, before);
        }

        public double ReadFloat(double address) => CellRules.ReadFloat(Memory.Float(CellRules.Address(address)));

        public void WriteFloat(double address, double value)
        {
            int cell = CellRules.Address(address);
            var bits = CellRules.FloatBits(value);
            CellValue? before = GlobalBefore(CellKind.Float, cell);
            Memory.SetFloat(cell, bits);
            GlobalAfter(CellKind.Float, cell, before);
        }

        public string ReadString(double address) => Memory.String(CellRules.Address(address));

        public void WriteString(double address, string value)
        {
            int cell = CellRules.Address(address);
            var text = CellRules.Text(value, CodePage);
            CellValue? before = GlobalBefore(CellKind.String, cell);
            Memory.SetString(cell, text);
            GlobalAfter(CellKind.String, cell, before);
        }

        // Under --lua-trace (spec §12) a write is read before and after it; otherwise the before is null and nothing is
        // read or allocated.
        private CellValue? GlobalBefore(CellKind kind, int cell) => owner.Tracing ? owner.ReadCell(kind, cell) : null;

        private void GlobalAfter(CellKind kind, int cell, CellValue? before)
        {
            if (before is { } value) owner.TraceGlobal(kind, cell, modId, value);
        }

        private CellValue? LocalBefore(IInterpreterAccess frame, CellKind kind, int index)
            => owner.Tracing ? ReadLocal(frame, kind, index) : null;

        private void LocalAfter(IInterpreterAccess frame, CellKind kind, int index, CellValue? before)
        {
            if (before is { } value)
                owner.TraceWrite(CellText(kind, index, local: true), null, modId, value, ReadLocal(frame, kind, index));
        }

        private static CellValue ReadLocal(IInterpreterAccess frame, CellKind kind, int index) => kind switch
        {
            CellKind.Integer => CellValue.Integer(CellRules.ReadInteger(frame.LocalInteger(index))),
            CellKind.Float => CellValue.Float(CellRules.ReadFloat(frame.LocalFloat(index))),
            _ => CellValue.String(frame.LocalString(index)),
        };

        public double ReadPointer(double index) => Memory.Pointers.GetValueOrDefault(CellRules.Address(index, "an index"));

        public double ReadStringPointer(double index)
            => Memory.StringPointers.GetValueOrDefault(CellRules.Address(index, "an index"));

        private IInterpreterAccess Frame_()
        {
            if (!owner._frameAccess || owner._interpreter is not { HasFrame: true } interpreter)
                throw new ScriptAccessException(
                    "locals exist only in a hook with a frame (a point hook, or a console chunk while a frame runs)");
            return interpreter;
        }

        public double ReadLocalInteger(double index)
            => CellRules.ReadInteger(Frame_().LocalInteger(CellRules.Address(index, "an index")));

        public void WriteLocalInteger(double index, double value)
        {
            int local = CellRules.Address(index, "an index");
            var integer = CellRules.Integer(value);
            IInterpreterAccess frame = Frame_();
            CellValue? before = LocalBefore(frame, CellKind.Integer, local);
            frame.SetLocalInteger(local, integer);
            LocalAfter(frame, CellKind.Integer, local, before);
        }

        public double ReadLocalFloat(double index)
            => CellRules.ReadFloat(Frame_().LocalFloat(CellRules.Address(index, "an index")));

        public void WriteLocalFloat(double index, double value)
        {
            int local = CellRules.Address(index, "an index");
            var bits = CellRules.FloatBits(value);
            IInterpreterAccess frame = Frame_();
            CellValue? before = LocalBefore(frame, CellKind.Float, local);
            frame.SetLocalFloat(local, bits);
            LocalAfter(frame, CellKind.Float, local, before);
        }

        public string ReadLocalString(double index) => Frame_().LocalString(CellRules.Address(index, "an index"));

        public void WriteLocalString(double index, string value)
        {
            int local = CellRules.Address(index, "an index");
            var text = CellRules.Text(value, CodePage);
            IInterpreterAccess frame = Frame_();
            CellValue? before = LocalBefore(frame, CellKind.String, local);
            frame.SetLocalString(local, text);
            LocalAfter(frame, CellKind.String, local, before);
        }

        public ScriptFrameInfo? Frame
        {
            get
            {
                if (!owner._frameAccess || owner._interpreter is not { HasFrame: true }) return null;
                IReadOnlyList<ScriptFrameInfo> frames = Frames();
                return frames.Count == 0 ? null : frames[^1];
            }
        }

        public IReadOnlyList<ScriptFrameInfo> Frames()
        {
            if (owner._interpreter == null) return [];
            IScriptSites sites = owner._setup.Sites;
            var frames = owner._interpreter.Frames();
            var result = new ScriptFrameInfo[frames.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                var (script, offset) = frames[i];
                result[i] = new ScriptFrameInfo(script, offset, offset < 0 ? null : sites.Site(script, offset), i + 1,
                                                sites.ChangedBy(script));
            }
            return result;
        }

        public double? ScriptId(string name) => owner._interpreter?.ScriptId(name) is uint id ? id : null;
    }
}
