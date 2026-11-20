using System.Text;
using Ome.Engine.Modding;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

/// <summary>What an integer queue or stack op found at its id.</summary>
public enum ScriptCollectionStatus
{
    /// <summary>The op ran.</summary>
    Done,

    /// <summary>The id names none of the ten slots (the interpreter checks the id first).</summary>
    OutOfRange,
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
/// <para>An integer cell holds one form, <see cref="IntegerCell"/>: the integer bank is written only through
/// <see cref="SetInteger"/>, <see cref="SeedInteger"/> and a load, and a frame's integer locals through
/// <c>Frame.SetInteger</c>.</para>
/// <para>The banks fill the first four of a numbered save's six sections (<see cref="WriteSaveRecords"/>,
/// <see cref="ReadSaveRecords"/>); the save native decides when they are written and read (spec §5.1, rule 6).</para>
/// <para>Engine thread only (spec §5.1, rule 4); the driver's <see cref="SeedDeterministicRandom"/> comes as a
/// request (InputAndWaits.Invoke).</para>
/// </remarks>
public sealed partial class ScriptMemory
{
    /// <summary>Native owns ten queues at EngineCtx +0x55130 and ten stacks at +0x55158. Its test admits id 10, which
    /// lands in the next table; the interpreter halts there (docs/engine-re.md, "Indices outside their tables").
    /// </summary>
    public const int CollectionSlots = 10;

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5).
    private sealed class Scene
    {
        public readonly Dictionary<int, long> Integers = new();
        public readonly Dictionary<int, long> Floats = new();
        public readonly Dictionary<int, string> Strings = new();
        public readonly Dictionary<int, int> Pointers = new();
        public readonly Dictionary<int, int> StringPointers = new();
        // Native constructs the queues and the stacks, empty, on every scene reset and when the engine starts
        // (scene_context_init_reset: SYS4422 0x40b689, 4.38D 0x40c962), so a queue no script has reset is empty, not
        // missing: Arterial's VISEEK uses queue 0 without resetting it.
        public readonly Queue<int>[] Queues = CreateQueues();
        public readonly Stack<int>[] Stacks = CreateStacks();
    }

    private Scene _scene = new();
    // Lua scripting spec §7.3, §7.5: the cells the plan's watches cover, or null; a global write tests this reference.
    private WatchedCells? _watched;
    // Data patches plan §12.6: the running deliveries' journal, or null between deliveries.
    private CellJournal? _journal;

    private readonly Encoding _encoding;

    /// <param name="codePage">The code page the string ops measure bytes and characters in (VmOptions'
    /// NativeStringCodePage; 932 for both games).</param>
    /// <param name="gameRandom">The game's generator, which op 0x60 and Lua draw from when no seed is set: the graphics
    /// native's (<see cref="Graphics.GameRandom"/>), so the title's leaves draw from it too. A new one seeded from the
    /// tick count when none is given.</param>
    public ScriptMemory(int codePage = 932, Hosting.CrtRandom? gameRandom = null)
    {
        GameRandom = gameRandom ?? Hosting.CrtRandom.FromTickCount();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // Code page 932 keeps every byte of a script string (Sys4TextEncoding).
        _encoding = codePage == 932 ? Sys4.Sys4TextEncoding.Replacing : Encoding.GetEncoding(codePage);
    }

    /// <summary>The integer bank (operand type 3), each cell in its one form (<see cref="IntegerCell"/>).</summary>
    public IReadOnlyDictionary<int, long> Integers => _scene.Integers;

    /// <summary>An integer cell's one form: the signed low 32 bits of the value written. Native's cells are int32, a
    /// numbered save stores and loads them so, and Lua's writes give them (<c>CellRules.Integer</c>); so 0x80000000
    /// written from an immediate and the same bits loaded from a save are one value, -2147483648.</summary>
    public static long IntegerCell(long value) => unchecked((int)value);

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
    /// write the banks directly, which is no write. Every setter keeps the cell's value for the running deliveries'
    /// journal (data patches plan §12.6).</summary>
    public void SetInteger(int address, long value)
    {
        value = IntegerCell(value);
        if (_journal != null)
            _journal.Keep(JournalBank.Integer, address, _scene.Integers.TryGetValue(address, out long old), old, null);
        if (_watched != null && _watched.Integer(address))
            _watched.Record(new CellWrite(CellKind.Integer, address, Integer(address), value, null, null));
        _scene.Integers[address] = value;
    }

    /// <summary>A write no watch sees (Lua scripting spec §7.3): a tool's or a test's seeding, or the debugger's.
    /// </summary>
    public void SeedInteger(int address, long value) => _scene.Integers[address] = IntegerCell(value);

    public void SetFloat(int address, long value)
    {
        if (_journal != null)
            _journal.Keep(JournalBank.Float, address, _scene.Floats.TryGetValue(address, out long old), old, null);
        _scene.Floats[address] = value;
    }

    public void SetString(int address, string value)
    {
        if (_journal != null)
            _journal.Keep(JournalBank.String, address, _scene.Strings.TryGetValue(address, out string? old), 0, old);
        if (_watched != null && _watched.String(address))
            _watched.Record(new CellWrite(CellKind.String, address, 0, 0, String(address), value));
        _scene.Strings[address] = value;
    }

    /// <summary>The scripting native's watches (Lua scripting spec §7.3), or null for none.</summary>
    public void Watch(WatchedCells? watched) => _watched = watched;

    /// <summary>The running deliveries' journal (data patches plan §12.6), while one runs; null otherwise, so a write
    /// costs one test.</summary>
    internal void Journal(CellJournal? journal) => _journal = journal;

    /// <summary>A cell a failed delivery wrote gets the value its journal kept (data patches plan §12.6): through the
    /// setter for a bank a watch covers, so the watch is told of it, and a cell the bank had not held is taken out
    /// again.</summary>
    internal void PutBack(JournalBank bank, int address, bool present, long number, string? text)
    {
        switch (bank)
        {
            case JournalBank.Integer:
                SetInteger(address, number);
                if (!present) _scene.Integers.Remove(address);
                break;
            case JournalBank.String:
                SetString(address, text ?? "");
                if (!present) _scene.Strings.Remove(address);
                break;
            case JournalBank.Float:
                Restore(_scene.Floats, address, present, number);
                break;
            case JournalBank.Pointer:
                Restore(_scene.Pointers, address, present, (int)number);
                break;
            case JournalBank.StringPointer:
                Restore(_scene.StringPointers, address, present, (int)number);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(bank), bank, "a local is the interpreter's");
        }
    }

    private static void Restore<T>(Dictionary<int, T> bank, int address, bool present, T value)
    {
        if (present) bank[address] = value;
        else bank.Remove(address);
    }

    public void SetPointer(int index, int address)
    {
        if (_journal != null)
            _journal.Keep(JournalBank.Pointer, index, _scene.Pointers.TryGetValue(index, out int old), old, null);
        _scene.Pointers[index] = address;
    }

    public void SetStringPointer(int index, int address)
    {
        if (_journal != null)
            _journal.Keep(JournalBank.StringPointer, index, _scene.StringPointers.TryGetValue(index, out int old), old, null);
        _scene.StringPointers[index] = address;
    }

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
        if ((uint)id >= (uint)_scene.Queues.Length) return ScriptCollectionStatus.OutOfRange;
        _scene.Queues[id] = new Queue<int>(0x100);
        return ScriptCollectionStatus.Done;
    }

    /// <summary>Op 0x133.</summary>
    public ScriptCollectionStatus Enqueue(int id, int value)
    {
        if ((uint)id >= (uint)_scene.Queues.Length) return ScriptCollectionStatus.OutOfRange;
        _scene.Queues[id].Enqueue(value);
        return ScriptCollectionStatus.Done;
    }

    /// <summary>Op 0x134: <paramref name="taken"/> is false when the queue is empty.</summary>
    public ScriptCollectionStatus TryDequeue(int id, out bool taken, out int value)
    {
        taken = false;
        value = 0;
        if ((uint)id >= (uint)_scene.Queues.Length) return ScriptCollectionStatus.OutOfRange;
        taken = _scene.Queues[id].TryDequeue(out value);
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
    /// then constructs the queues and the stacks again, empty.
    /// </summary>
    public void ResetScene() => _scene = new();

    /// <summary>
    /// The banks' sections of a numbered save, each the dense prefix the profile's dimensions give. The integers, the
    /// strings and the integer pointers are always written. The floats are written when the build keeps them
    /// (<paramref name="keepsFloats"/>: <see cref="SaveRules.KeepsFloatSections"/>); with them native writes the float
    /// pointers, a bank the port does not have, since no script of a profiled game has a global float pointer operand
    /// (type 7), so that section is zero. The string pointers are never written. A section native steps over holds
    /// what its buffer held; the port writes zero there.
    /// </summary>
    public NativeNumberedSaveState WriteSaveRecords(NativeNumberedSaveState state, NativeSaveBankDimensions dimensions,
                                                    bool keepsFloats)
        => state with
        {
            IntegerGlobals = DenseValues(_scene.Integers, dimensions.IntegerGlobals),
            FloatGlobals = keepsFloats ? DenseValues(_scene.Floats, dimensions.FloatGlobals)
                                       : new int[dimensions.FloatGlobals],
            StringGlobals = DenseStrings(_scene.Strings, dimensions.StringGlobals),
            PointerGlobals = DensePointerValues(_scene.Pointers, dimensions.PointerGlobals),
            FloatPointers = new int[dimensions.FloatPointers],
            StringPointers = new int[dimensions.StringPointers],
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
    public void ReadSaveRecords(NativeNumberedSaveState state, bool keepsFloats)
    {
        ReplaceBank(_scene.Integers, state.IntegerGlobals.Select(value => (long)value).ToArray(), value => value != 0);
        // The floats come back only when the build keeps their section; the string pointers never do. Both banks are
        // zeroed first either way.
        ReplaceBank(_scene.Floats,
                    keepsFloats ? state.FloatGlobals.Select(value => (long)value).ToArray() : Array.Empty<long>(),
                    value => value != 0);
        ReplaceBank(_scene.Strings, state.StringGlobals, value => value.Length != 0);
        ReplaceBank(_scene.Pointers, state.PointerGlobals, value => value != 0);
        _scene.StringPointers.Clear();
    }

    private static Queue<int>[] CreateQueues()
    {
        var queues = new Queue<int>[CollectionSlots];
        for (int i = 0; i < queues.Length; i++)
            queues[i] = new Queue<int>(0x100);
        return queues;
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
