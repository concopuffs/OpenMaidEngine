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

    /// <summary>A mod's face, or the console's (no mod): its capability, its write list and the base mods it may
    /// name.</summary>
    private sealed class ModFace(Scripting owner, string modId, ModRuntimeMod? mod, bool vm) : IModAccess
    {
        private readonly Dictionary<string, INamedAccess?> _names = new(StringComparer.Ordinal);

        public string ModId { get; } = modId;
        public IVmAccess? Vm { get; } = vm ? new VmFace(owner) : null;

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
                ? new NamedFace(owner, ModId, layout, mod.WriteList.ToHashSet(StringComparer.Ordinal))
                : null;
        }
    }

    /// <summary>A base mod's accessors for one mod (spec §9.3): the index rules of <see cref="LayoutCells"/>, the value
    /// rules of <see cref="CellRules"/>, and the mod's write list.</summary>
    private sealed class NamedFace(Scripting owner, string modId, BaseModLayout layout, IReadOnlySet<string>? writes)
        : INamedAccess
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
                throw new ScriptAccessException($"{modId} writes {global.Name}, which its [lua] writes list does not name ("
                    + (writes.Count == 0 ? "it lists none" : "writes = [" + string.Join(", ", writes.Order(StringComparer.Ordinal)) + "]")
                    + ")");
            owner.WriteCell(global.Kind, LayoutCells.Address(global, row, column), value, global.Name);
        }
    }

    private sealed class VmFace(Scripting owner) : IVmAccess
    {
        private ScriptMemory Memory => owner._memory;
        private int CodePage => owner._memory.Encoding.CodePage;

        public double ReadInteger(double address) => CellRules.ReadInteger(Memory.Integer(CellRules.Address(address)));

        public void WriteInteger(double address, double value)
            => Memory.SetInteger(CellRules.Address(address), CellRules.Integer(value));

        public double ReadFloat(double address) => CellRules.ReadFloat(Memory.Float(CellRules.Address(address)));

        public void WriteFloat(double address, double value)
            => Memory.SetFloat(CellRules.Address(address), CellRules.FloatBits(value));

        public string ReadString(double address) => Memory.String(CellRules.Address(address));

        public void WriteString(double address, string value)
            => Memory.SetString(CellRules.Address(address), CellRules.Text(value, CodePage));

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
            => Frame_().SetLocalInteger(CellRules.Address(index, "an index"), CellRules.Integer(value));

        public double ReadLocalFloat(double index)
            => CellRules.ReadFloat(Frame_().LocalFloat(CellRules.Address(index, "an index")));

        public void WriteLocalFloat(double index, double value)
            => Frame_().SetLocalFloat(CellRules.Address(index, "an index"), CellRules.FloatBits(value));

        public string ReadLocalString(double index) => Frame_().LocalString(CellRules.Address(index, "an index"));

        public void WriteLocalString(double index, string value)
            => Frame_().SetLocalString(CellRules.Address(index, "an index"), CellRules.Text(value, CodePage));

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
