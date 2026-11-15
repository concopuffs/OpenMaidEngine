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
    /// as '?'.</summary>
    public static string Text(string value, int codePage)
    {
        Encoding encoding;
        lock (Lock)
        {
            if (!Strict.TryGetValue(codePage, out encoding!))
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Strict[codePage] = encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback,
                                                                   DecoderFallback.ExceptionFallback);
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

    /// <summary>Whether <paramref name="opcode"/> is the numbered save (op 0x19e in both games), before which
    /// <c>ome.save.writing</c> fires (spec §7.7). The interpreter answers from its opcode table, so the native names
    /// no opcode number.</summary>
    bool IsNumberedSave(int opcode);
}
