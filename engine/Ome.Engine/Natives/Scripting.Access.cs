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
            _faces[modId] = face = new ModFace(owner, modId, mod.Vm);
            return face;
        }

        public IModAccess Console => _console ??= new ModFace(owner, ConsoleId, vm: true);

        public bool Restoring => owner._scene.Restoring;
    }

    private sealed class ModFace(Scripting owner, string modId, bool vm) : IModAccess
    {
        public string ModId { get; } = modId;
        public IVmAccess? Vm { get; } = vm ? new VmFace(owner) : null;

        // Spec §5.6: script memory's generator, the one op 0x60 draws from.
        public int DrawRandom() => owner._memory.DrawForMod();

        public void Log(ModLogLevel level, string message) => owner.WriteLog(level, ModId, message);
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
