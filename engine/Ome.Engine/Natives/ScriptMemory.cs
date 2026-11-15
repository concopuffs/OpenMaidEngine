using System.Text;
using Ome.Engine.Modding;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

/// <summary>What an integer queue or stack op found at its id.</summary>
public enum ScriptCollectionStatus
{
    /// <summary>The op ran.</summary>
    Done,

    /// <summary>The id names none of the eleven slots; the op halts the run.</summary>
    OutOfRange,

    /// <summary>The queue was never reset (op 0x132); the op halts the run.</summary>
    Uninitialized,
}

/// <summary>
/// Script memory (docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.2; plan §3.2, addition 3; milestone 9):
/// the five global banks a script addresses (integers, floats, strings, and the integer and string pointer cells that
/// name other cells), the byte encoding a string op measures in, ops 0x132 to 0x139's integer queues and stacks, and
/// op 0x60's deterministic draws (<c>ScriptMemory.Random.cs</c>). It has no devices.
/// </summary>
/// <remarks>
/// <para>The banks are sparse: a cell never written reads 0 or "". The interpreter decodes operands (locals are a
/// frame's, not script memory's) and reads and writes the global cells here; tools and tests reach the banks
/// through <see cref="Integers"/> and its siblings, which the VM's public face forwards.</para>
/// <para>The banks are five sections of a numbered save (<see cref="WriteSaveRecords"/>, <see cref="ReadSaveRecords"/>);
/// the save native decides when they are written and read (spec §5.1, rule 6).</para>
/// <para>Engine thread only (spec §5.1, rule 4); the driver's <see cref="SeedDeterministicRandom"/> comes as a
/// request (InputAndWaits.Invoke).</para>
/// </remarks>
public sealed partial class ScriptMemory
{
    /// <summary>Native owns ten queues at EngineCtx +0x55130 and ten stacks at +0x55158; its admitted id 10 aliases
    /// the next table, which the port keeps as an eleventh slot of each.</summary>
    private const int CollectionSlots = 11;

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5).
    private sealed class Scene
    {
        public readonly Dictionary<int, long> Integers = new();
        public readonly Dictionary<int, long> Floats = new();
        public readonly Dictionary<int, string> Strings = new();
        public readonly Dictionary<int, int> Pointers = new();
        public readonly Dictionary<int, int> StringPointers = new();
        // Native constructs the stacks on every scene reset.
        public readonly Stack<int>[] Stacks = CreateStacks();
    }

    private Scene _scene = new();
    // Lua scripting spec §7.3, §7.5: the cells the plan's watches cover, or null; a global write tests this reference.
    private WatchedCells? _watched;

    private readonly Encoding _encoding;
    // Op 0x132 creates a queue; ATSEEK/MVSEEK use queue 0. A queue lives for the run (a scene reset keeps it).
    private readonly Queue<int>?[] _queues = new Queue<int>?[CollectionSlots];

    /// <param name="codePage">The code page the string ops measure bytes and characters in (VmOptions'
    /// NativeStringCodePage; 932 for both games).</param>
    public ScriptMemory(int codePage = 932)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encoding = Encoding.GetEncoding(codePage);
    }

    /// <summary>The integer bank (operand type 3).</summary>
    public Dictionary<int, long> Integers => _scene.Integers;

    /// <summary>The float bank (operand type 4), each cell a float's bits.</summary>
    public Dictionary<int, long> Floats => _scene.Floats;

    /// <summary>The string bank (operand type 5).</summary>
    public Dictionary<int, string> Strings => _scene.Strings;

    /// <summary>The integer pointer cells (operand type 6): the integer address each names.</summary>
    public Dictionary<int, int> Pointers => _scene.Pointers;

    /// <summary>The string pointer cells (operand type 8): the string address each names.</summary>
    public Dictionary<int, int> StringPointers => _scene.StringPointers;

    /// <summary>The encoding the string ops measure in.</summary>
    public Encoding Encoding => _encoding;

    public long Integer(int address) => _scene.Integers.TryGetValue(address, out long value) ? value : 0;

    public long Float(int address) => _scene.Floats.TryGetValue(address, out long value) ? value : 0;

    public string String(int address) => _scene.Strings.TryGetValue(address, out string? value) ? value : "";

    /// <summary>The integer and string setters are every write a script, a native or Lua makes to a global, so they
    /// record a write to a watched cell (Lua scripting spec §7.3). A load's banks, a scene reset's and a tool's seeding
    /// write the banks directly, which is no write.</summary>
    public void SetInteger(int address, long value)
    {
        if (_watched != null && _watched.Integer(address))
            _watched.Record(new CellWrite(CellKind.Integer, address, Integer(address), value, null, null));
        _scene.Integers[address] = value;
    }

    public void SetFloat(int address, long value) => _scene.Floats[address] = value;

    public void SetString(int address, string value)
    {
        if (_watched != null && _watched.String(address))
            _watched.Record(new CellWrite(CellKind.String, address, 0, 0, String(address), value));
        _scene.Strings[address] = value;
    }

    /// <summary>The scripting native's watches (Lua scripting spec §7.3), or null for none.</summary>
    public void Watch(WatchedCells? watched) => _watched = watched;

    public void SetPointer(int index, int address) => _scene.Pointers[index] = address;

    public void SetStringPointer(int index, int address) => _scene.StringPointers[index] = address;

    /// <summary>The integer address pointer cell <paramref name="index"/> names. A cell never set as a pointer reads
    /// the integer cell of that index, as native's banks overlap.</summary>
    public int IntegerPointer(int index)
        => _scene.Pointers.TryGetValue(index, out int value) ? value : unchecked((int)Integer(index));

    /// <summary>The string address string pointer cell <paramref name="index"/> names (as
    /// <see cref="IntegerPointer"/>).</summary>
    public int StringPointer(int index)
        => _scene.StringPointers.TryGetValue(index, out int value) ? value : unchecked((int)Integer(index));

    /// <summary>A string's length in the engine's bytes, up to its first NUL.</summary>
    public int ByteLength(string value)
    {
        int nul = value.IndexOf('\0');
        return _encoding.GetByteCount(nul < 0 ? value : value[..nul]);
    }

    /// <summary>Op 0x132: an empty queue at <paramref name="id"/>.</summary>
    public ScriptCollectionStatus ResetQueue(int id)
    {
        if ((uint)id >= (uint)_queues.Length) return ScriptCollectionStatus.OutOfRange;
        _queues[id] = new Queue<int>(0x100);
        return ScriptCollectionStatus.Done;
    }

    /// <summary>Op 0x133.</summary>
    public ScriptCollectionStatus Enqueue(int id, int value)
    {
        if ((uint)id >= (uint)_queues.Length) return ScriptCollectionStatus.OutOfRange;
        if (_queues[id] is not { } queue) return ScriptCollectionStatus.Uninitialized;
        queue.Enqueue(value);
        return ScriptCollectionStatus.Done;
    }

    /// <summary>Op 0x134: <paramref name="taken"/> is false when the queue is empty.</summary>
    public ScriptCollectionStatus TryDequeue(int id, out bool taken, out int value)
    {
        taken = false;
        value = 0;
        if ((uint)id >= (uint)_queues.Length) return ScriptCollectionStatus.OutOfRange;
        if (_queues[id] is not { } queue) return ScriptCollectionStatus.Uninitialized;
        taken = queue.TryDequeue(out value);
        return ScriptCollectionStatus.Done;
    }

    /// <summary>Op 0x137: an empty stack at <paramref name="id"/>.</summary>
    public ScriptCollectionStatus ResetStack(int id)
    {
        if ((uint)id >= (uint)_scene.Stacks.Length) return ScriptCollectionStatus.OutOfRange;
        _scene.Stacks[id] = new Stack<int>(0x100);
        return ScriptCollectionStatus.Done;
    }

    /// <summary>Op 0x138.</summary>
    public ScriptCollectionStatus Push(int id, int value)
    {
        if ((uint)id >= (uint)_scene.Stacks.Length) return ScriptCollectionStatus.OutOfRange;
        _scene.Stacks[id].Push(value);
        return ScriptCollectionStatus.Done;
    }

    /// <summary>Op 0x139: <paramref name="taken"/> is false when the stack is empty.</summary>
    public ScriptCollectionStatus TryPop(int id, out bool taken, out int value)
    {
        taken = false;
        value = 0;
        if ((uint)id >= (uint)_scene.Stacks.Length) return ScriptCollectionStatus.OutOfRange;
        taken = _scene.Stacks[id].TryPop(out value);
        return ScriptCollectionStatus.Done;
    }

    /// <summary>
    /// op_0x9_reset_scene_and_reload_root, after disposing the frames and before scene_context_init_reset: every
    /// integer global becomes the encoded zero, and the float, string, integer-pointer and string-pointer banks are
    /// zeroed (both bank sets under set:EnableMemFlip). Resource 0 re-authors what it needs from the configuration and
    /// the shared profile, so state a scene left behind (Kamidori's field flag 0x428c) is gone. scene_context_init_reset
    /// then constructs the stacks again; the queues stay.
    /// </summary>
    public void ResetScene() => _scene = new();

    /// <summary>The five banks' sections of a numbered save, each the dense prefix the profile's dimensions give.
    /// </summary>
    public NativeNumberedSaveState WriteSaveRecords(NativeNumberedSaveState state, NativeSaveBankDimensions dimensions)
        => state with
        {
            IntegerGlobals = DenseValues(_scene.Integers, dimensions.IntegerGlobals),
            FloatGlobals = DenseValues(_scene.Floats, dimensions.FloatGlobals),
            StringGlobals = DenseStrings(_scene.Strings, dimensions.StringGlobals),
            PointerGlobals = DensePointerValues(_scene.Pointers, dimensions.PointerGlobals),
            PointerStrings = DensePointerValues(_scene.StringPointers, dimensions.PointerStrings),
        };

    /// <summary>
    /// Layout 3 of save_data_deserialize_and_begin_restore@0x40fd10 zeroes each global bank in full, to its live size
    /// (ctx+0x53c7c..0x53c90; Himegari 1419442 integers and 18180 strings), and only then copies the saved prefixes
    /// (402459 integers, 789 strings). The cells past a prefix are therefore zero after any load until a restored frame
    /// re-authors them: every saved script re-runs its prologue from its entry to op 0xae, and SYSTEM4's (LOADCONFIG or
    /// INITCONFIG, the 0x1ca table copy, INIT2 and its *INIT data tables) precedes its 0xae. Script flags past the prefix
    /// such as G[0x62428], which SC0000 sets around its MENU.BIN call and a load from MENU never clears, read zero again
    /// (native-read 2026-11-05; docs/engine-re.md, "Native menu bar").
    /// </summary>
    public void ReadSaveRecords(NativeNumberedSaveState state)
    {
        ReplaceBank(_scene.Integers, state.IntegerGlobals.Select(value => (long)value).ToArray(), value => value != 0);
        ReplaceBank(_scene.Floats, state.FloatGlobals.Select(value => (long)value).ToArray(), value => value != 0);
        ReplaceBank(_scene.Strings, state.StringGlobals, value => value.Length != 0);
        ReplaceBank(_scene.Pointers, state.PointerGlobals, value => value != 0);
        ReplaceBank(_scene.StringPointers, state.PointerStrings, value => value != 0);
    }

    private static Stack<int>[] CreateStacks()
    {
        var stacks = new Stack<int>[CollectionSlots];
        for (int i = 0; i < stacks.Length; i++)
            stacks[i] = new Stack<int>(0x100);
        return stacks;
    }

    private static void ReplaceBank<T>(Dictionary<int, T> bank, IReadOnlyList<T> values, Func<T, bool> retain)
    {
        bank.Clear();
        for (int i = 0; i < values.Count; i++)
            if (retain(values[i])) bank[i] = values[i];
    }

    private static int[] DenseValues(IReadOnlyDictionary<int, long> source, int fixedCount)
    {
        var result = new int[fixedCount];
        foreach ((int index, long value) in source)
            if ((uint)index < (uint)result.Length) result[index] = unchecked((int)value);
        return result;
    }

    private static int[] DensePointerValues(IReadOnlyDictionary<int, int> source, int fixedCount)
    {
        var result = new int[fixedCount];
        foreach ((int index, int value) in source)
            if ((uint)index < (uint)result.Length) result[index] = value;
        return result;
    }

    private static string[] DenseStrings(IReadOnlyDictionary<int, string> source, int fixedCount)
    {
        var result = Enumerable.Repeat(string.Empty, fixedCount).ToArray();
        foreach ((int index, string value) in source)
            if ((uint)index < (uint)result.Length) result[index] = value;
        return result;
    }
}
