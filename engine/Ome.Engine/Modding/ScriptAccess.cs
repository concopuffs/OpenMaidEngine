using System.Text;
using Ome.Engine.Model;

namespace Ome.Engine.Modding;

/// <summary>A refused access (spec §8.1, §14.2): a value that does not fit its cell, an address that is no address, a
/// local outside a frame. The runtime turns it into a Lua error, which <c>pcall</c> may catch.</summary>
public sealed class ScriptAccessException(string message) : Exception(message);

public enum ModLogLevel { Info, Warn, Error }

/// <summary>A frame as <c>maid.vm.frame</c> and <c>maid.vm.frames()</c> give it: the script, the linked offset of its
/// current instruction, the original offset when the instruction is the base script's, its depth (the outermost is
/// 1), and the mods that changed the script.</summary>
public sealed record ScriptFrameInfo(string Script, int Offset, int? Site, int Depth, IReadOnlyList<string> ChangedBy);

/// <summary>The facade a runtime reaches engine state through (spec §8.1, §14.2), with one face for each mod. Its
/// checks are the engine's, so a runtime cannot loosen them.</summary>
public interface IScriptAccess
{
    /// <summary>The face of a mod of the stack.</summary>
    IModAccess Mod(string modId);

    /// <summary>The console's face (spec §13): <c>vm</c>, and no write list.</summary>
    IModAccess Console { get; }

    /// <summary><c>maid.session.restoring</c> (spec §11): true from a full load's session build until its last frame
    /// is restored.</summary>
    bool Restoring { get; }
}

/// <summary>One mod's face.</summary>
public interface IModAccess
{
    string ModId { get; }

    /// <summary>Raw access, or null when the mod did not declare <c>vm</c> (spec §6.3).</summary>
    IVmAccess? Vm { get; }

    /// <summary>One draw of 15 bits from script memory's generator, the one op 0x60 draws from (spec §5.6).</summary>
    int DrawRandom();

    /// <summary><c>maid.log</c> and <c>print</c>: a line of <c>diagnostics/lua.log</c> with the mod's id.</summary>
    void Log(ModLogLevel level, string message);

    /// <summary>The accessors over the layout of the base mod <paramref name="baseModId"/> (spec §9.3): with this mod's
    /// write list when it requires that base mod, with none when it is that base mod or the console; null when this
    /// mod may not name that base mod's cells.</summary>
    INamedAccess? Names(string baseModId);

    /// <summary>The base mod whose accessors <c>maid.names</c> holds (spec §8.4): a base mod's own, and for the console
    /// the profile's (spec §13); null for any other mod.</summary>
    string? OwnLayout { get; }

    /// <summary><c>maid.events.emit</c> (spec §8.2): raise an event this mod declares with <c>raised_by = "lua"</c>, with
    /// every declared field's value. Its subscribers run before it returns; the result is the values they left.</summary>
    IReadOnlyDictionary<string, CellValue> Emit(string eventName, IReadOnlyDictionary<string, CellValue> fields);

    /// <summary><c>maid.text_fits</c> (data patches spec §6.2): why a string cell would refuse <paramref name="text"/>,
    /// by the rule every string write applies (<see cref="CellRules.Text"/>, in the save's code page), or null when it
    /// would take it. It reads and writes nothing, so a mod can check a text before its first write.</summary>
    string? TextRefusal(string text);
}

/// <summary>
/// A base mod's accessors for one mod (spec §9.3): reads by an exported name, writes checked against the mod's write
/// list, and indices checked against the global's shape, so an accessor reaches only the named global's own cells.
/// Indices are the game's: the first row and the first column are 0. Every rule is here, in the engine.
/// </summary>
public interface INamedAccess
{
    string BaseModId { get; }

    /// <summary>The globals, by name or alias.</summary>
    GameLayout Layout { get; }

    /// <summary>The global names this face may write; null when it may write any (a base mod's own accessors, the
    /// console's).</summary>
    IReadOnlySet<string>? Writes { get; }

    /// <summary>The global a name or an alias names; a refused access for a name the base mod does not export.</summary>
    LayoutGlobal Global(string name);

    /// <summary>A row of <paramref name="global"/> (an element of a 1-D array), checked against its rows.</summary>
    int Row(LayoutGlobal global, double index);

    /// <summary>A column of a 2-D array, checked against its width.</summary>
    int Column(LayoutGlobal global, double index);

    /// <summary>The index a column's name stands for in the last dimension: a 2-D array's column, a 1-D array's
    /// element.</summary>
    int Named(LayoutGlobal global, string column);

    /// <summary>A cell: a scalar with no indices, a 1-D element with its row, a 2-D cell with both (-1 for none).</summary>
    CellValue Read(LayoutGlobal global, int row = -1, int column = -1);

    /// <summary>Write a cell, if the write list names the global; the value passes <see cref="CellRules"/>.</summary>
    void Write(LayoutGlobal global, int row, int column, CellValue value);
}

/// <summary>The index rules of the accessors (spec §9.3), in one place for every face.</summary>
public static class LayoutCells
{
    /// <summary>A row of <paramref name="global"/>: whole, from 0 to its rows; for an open array that nothing follows,
    /// any row whose cells stay below the last address.</summary>
    public static int Row(LayoutGlobal global, double index)
    {
        if (global.IsScalar) throw new ScriptAccessException($"{global.Name} is no table, so it has no rows");
        if (global.Rows is int rows)
        {
            if (!Whole(index) || index < 0 || index >= rows)
                throw new ScriptAccessException(rows == 0
                    ? $"{global.Name} has no rows"
                    : $"{global.Name}[{Show(index)}]: an index is a whole number from 0 to {rows - 1}");
            return (int)index;
        }
        long last = ((long)CellRules.MaxAddress - global.Address - (global.RowWidth - 1)) / global.RowWidth;
        if (!Whole(index) || index < 0 || index > last)
            throw new ScriptAccessException($"{global.Name}[{Show(index)}]: an index is a whole number from 0 to {last}");
        return (int)index;
    }

    /// <summary>A column of a 2-D array: whole, from 0 to its width.</summary>
    public static int Column(LayoutGlobal global, double index)
    {
        if (global.Dimensions.Count != 2) throw new ScriptAccessException($"{global.Name} has no columns");
        int width = global.RowWidth;
        if (!Whole(index) || index < 0 || index >= width)
            throw new ScriptAccessException($"{global.Name}: a column is a whole number from 0 to {width - 1}, not {Show(index)}");
        return (int)index;
    }

    public static int Named(LayoutGlobal global, string column)
        => global.Columns.TryGetValue(column, out int index)
            ? index
            : throw new ScriptAccessException(global.Columns.Count == 0
                ? $"{global.Name} names no columns, so '{column}' is not one of its fields"
                : $"{global.Name} has no column '{column}' (its columns: "
                  + string.Join(", ", global.Columns.OrderBy(pair => pair.Value).Select(pair => pair.Key)) + ")");

    /// <summary>The address of a cell, with its indices checked again: a scalar takes none, a 1-D array a row, a 2-D
    /// array a row and a column.</summary>
    public static int Address(LayoutGlobal global, int row, int column)
    {
        switch (global.Dimensions.Count)
        {
            case 0 when row < 0 && column < 0:
                return global.Address;
            case 1 when row >= 0 && column < 0:
                return global.Address + Row(global, row);
            case 2 when row >= 0 && column >= 0:
                return global.Address + Row(global, row) * global.RowWidth + Column(global, column);
            default:
                throw new ScriptAccessException(global.Dimensions.Count switch
                {
                    0 => $"{global.Name} is no table: it takes no index",
                    1 => $"{global.Name} is a table: it takes one index",
                    _ => $"{global.Name} is a table of rows: it takes a row and a column",
                });
        }
    }

    private static bool Whole(double value) => double.IsFinite(value) && value == Math.Floor(value);

    private static string Show(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary><c>maid.vm</c> (spec §8.1). Numbers come and go as Lua's: an address or an index must be a whole number
/// from 0 to 2^31-1, an integer a whole number in 32 bits.</summary>
public interface IVmAccess
{
    double ReadInteger(double address);
    void WriteInteger(double address, double value);
    double ReadFloat(double address);
    void WriteFloat(double address, double value);
    string ReadString(double address);
    void WriteString(double address, string value);
    /// <summary>A global pointer cell: the address it holds. Pointer cells are not writable in version 1.</summary>
    double ReadPointer(double index);
    double ReadStringPointer(double index);

    double ReadLocalInteger(double index);
    void WriteLocalInteger(double index, double value);
    double ReadLocalFloat(double index);
    void WriteLocalFloat(double index, double value);
    string ReadLocalString(double index);
    void WriteLocalString(double index, string value);

    /// <summary>The frame the hook fired in, or the waiting frame for a console chunk; null in an engine event.</summary>
    ScriptFrameInfo? Frame { get; }

    /// <summary>The call stack, outermost first.</summary>
    IReadOnlyList<ScriptFrameInfo> Frames();

    /// <summary>A catalog name (<c>ADDEXP</c> or <c>ADDEXP.BIN</c>) to its packed id, or null.</summary>
    double? ScriptId(string name);

    /// <summary>
    /// <c>maid.vm.call</c> (spec §10): run a script the mod's <c>[lua] calls</c> lists (<c>ADDEXP</c> or <c>ADDEXP.BIN</c>)
    /// as a nested frame, and return when it exits. A handler that the interpreter invoked at an instruction may call:
    /// a hook, a watch or an event; the console, <c>init.lua</c> and an <c>ome.session.start</c> handler may not. A refusal,
    /// and a call the script refused (a wait, a save, a load, a restore, a script error, the budget), is a
    /// <see cref="ScriptAccessException"/>; a script that ends the scene or the run does not return.
    /// </summary>
    void Call(string script);
}

/// <summary>What a cell or an operand holds, as Lua sees it: an integer or a float as a number, or a string.</summary>
public enum CellKind { Integer, Float, String }

public readonly record struct CellValue(CellKind Kind, double Number, string? Text)
{
    public static CellValue Integer(double value) => new(CellKind.Integer, value, null);
    public static CellValue Float(double value) => new(CellKind.Float, value, null);
    public static CellValue String(string value) => new(CellKind.String, 0, value);
    public override string ToString() => Kind == CellKind.String ? Text ?? "" : Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The rules every write from Lua keeps (spec §8.1), in one place for the cells, the locals and the
/// operands.</summary>
public static class CellRules
{
    public const double MaxAddress = int.MaxValue;

    /// <summary>An address or an index: whole, and from 0 to 2^31-1.</summary>
    public static int Address(double value, string what = "an address")
    {
        if (!double.IsFinite(value) || value != Math.Floor(value) || value < 0 || value > MaxAddress)
            throw new ScriptAccessException($"{what} must be a whole number from 0 to {int.MaxValue}, not {Show(value)}");
        return (int)value;
    }

    /// <summary>An integer for a 32-bit cell: whole, from -2^31 to 2^32-1, stored as its signed low 32 bits.</summary>
    public static long Integer(double value)
    {
        if (!double.IsFinite(value) || value != Math.Floor(value) || value < int.MinValue || value > uint.MaxValue)
            throw new ScriptAccessException($"an integer cell holds a whole number in 32 bits, not {Show(value)}");
        return unchecked((int)(long)value);
    }

    /// <summary>The signed low 32 bits of a cell, as a read gives them.</summary>
    public static double ReadInteger(long cell) => unchecked((int)cell);

    /// <summary>A float cell holds a 32-bit float: a write rounds to single precision.</summary>
    public static long FloatBits(double value) => BitConverter.SingleToInt32Bits((float)value);

    public static double ReadFloat(long cell) => BitConverter.Int32BitsToSingle(unchecked((int)cell));

    private static readonly object Lock = new();
    private static readonly Dictionary<int, Encoding> Strict = new();

    /// <summary>A string must be encodable in the save's code page: a numbered save would store any other character
    /// as '?'. Code page 932 is the scripts' own text form, which also keeps a raw unit a script string holds
    /// (<see cref="Sys4.Sys4TextEncoding"/>).</summary>
    public static string Text(string value, int codePage)
    {
        Encoding encoding;
        lock (Lock)
        {
            if (!Strict.TryGetValue(codePage, out encoding!))
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Strict[codePage] = encoding = codePage == 932
                    ? Sys4.Sys4TextEncoding.Strict
                    : Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
        }
        try
        {
            encoding.GetByteCount(value);
        }
        catch (EncoderFallbackException error)
        {
            throw new ScriptAccessException(
                $"a string cell holds text the save can encode (code page {codePage}); U+{(int)error.CharUnknown:X4} is not in it");
        }
        return value;
    }

    private static string Show(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The interpreter's side of <c>maid.vm</c> (spec §8.1, §14.1): frames, locals and operands. Ome.Engine defines it and
/// <c>VirtualMachine</c> implements it, as it does <c>IInterpreterInput</c>, so references still point one way.
/// Engine thread only.
/// </summary>
public interface IInterpreterAccess
{
    /// <summary>The frames on the stack, outermost first: each script's name and its current instruction's linked
    /// offset.</summary>
    IReadOnlyList<(string Script, int Offset)> Frames();

    /// <summary>Whether a frame is running, whose locals the accessors below read.</summary>
    bool HasFrame { get; }

    /// <summary>The running frame as an identity, or null when none runs: the journal of a delivery (data patches plan
    /// §12.6) puts a local back only in the frame it was written in.</summary>
    object? RunningFrame => null;

    /// <summary>The local of the running frame that a write to <paramref name="operand"/> changes, directly or through a
    /// local pointer, or null when it changes none.</summary>
    (CellKind Kind, int Index)? LocalCellOf(Operand operand) => operand.Type switch
    {
        OperandTypes.LocalInteger => (CellKind.Integer, unchecked((int)operand.Value)),
        OperandTypes.LocalFloat => (CellKind.Float, unchecked((int)operand.Value)),
        OperandTypes.LocalString => (CellKind.String, unchecked((int)operand.Value)),
        _ => null,
    };

    long LocalInteger(int index);
    void SetLocalInteger(int index, long value);
    long LocalFloat(int index);
    void SetLocalFloat(int index, long bits);
    string LocalString(int index);
    void SetLocalString(int index, string value);

    /// <summary>An operand of the current frame's instruction, read as its opcode would read it.</summary>
    CellValue ReadOperand(Operand operand);

    /// <summary>Write a cell operand as its opcode would; the value has passed <see cref="CellRules"/>.</summary>
    void WriteOperand(Operand operand, CellValue value);

    /// <summary>The packed id of a catalog script, or null.</summary>
    uint? ScriptId(string name);

    /// <summary>
    /// <c>maid.vm.call</c> (spec §10): run the catalog script <paramref name="name"/> (no <c>.BIN</c>) as a nested frame
    /// of the running one, as <c>call-script</c> runs it, for at most <paramref name="instructions"/> instructions. A
    /// wait, a save, a load, an op 0xae while a restore is pending, a script error, the budget, the frame cap or a script
    /// that is not installed refuses the call: a <see cref="ScriptAccessException"/> naming the instruction, with the
    /// statement block put back as the call found it. A frame that returns to title, stops or halts does not return:
    /// the interpreter's own unwind passes the caller and every Lua frame above it.
    /// </summary>
    void Call(string name, long instructions);

    /// <summary>Whether <paramref name="opcode"/> is the numbered save (op 0x19e in both games), before which
    /// <c>ome.save.writing</c> fires (spec §7.7). The interpreter answers from its opcode table, so the native names
    /// no opcode number.</summary>
    bool IsNumberedSave(int opcode);

    /// <summary>Whether the running instructions are a wait's callback, nested in the instruction that waits (a text
    /// wait's HISTORY or CALLBACK_SETTING.BIN): a Lua reload is not taken there (spec §13).</summary>
    bool InsideWait => false;
}
