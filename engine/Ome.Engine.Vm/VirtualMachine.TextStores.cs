using Ome.Engine.Localization;
using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    /// <summary>The instruction that stored a global string cell's text, and that text (localization spec §5.2).</summary>
    private readonly record struct StringStore(TextSite Origin, string Text);

    private readonly Dictionary<int, StringStore> _stringStores = new();
    private volatile bool _recordsStringStores;

    /// <summary>
    /// Whether every write to a global string cell records its instruction and text (localization spec §5.2), so a
    /// string drawn far from where a table stored it is still scoped to the store. On whenever the mounted stack
    /// defines a language, selected or not, so a language switch during play is exact for the boot tables; off with
    /// no language in the stack (gate 1). The records are not saved; op 0x9 zeroes them with the banks.
    /// </summary>
    public bool RecordsStringStores
    {
        get => _recordsStringStores;
        set => _recordsStringStores = value;
    }

    /// <summary>The site of the instruction at <paramref name="pc"/> in the running script.</summary>
    private TextSite SiteAt(int pc) => new(_cur.Script.Name, _cur.Script.Instructions[pc].Offset);

    /// <summary>The global string cell an operand names: a cell, or a pointer to one (operand types 5, 8 and 14).
    /// False for a literal, a number, or a frame-local string.</summary>
    private bool TryGlobalStringCell(Operand operand, out int cell)
    {
        switch (operand.Type)
        {
            case T_GSTR:
                cell = (int)operand.Value;
                return true;
            case T_GSTRPTR:
                cell = GlobalStringPointer((int)operand.Value);
                return true;
            case T_LSTRPTR:
                return TryGlobalStringCell(Ga(_cur.Locals.SP, (int)operand.Value), out cell);
            default:
                cell = 0;
                return false;
        }
    }

    private static bool TryGlobalStringCell(VmAddress address, out int cell)
    {
        cell = address.Address;
        return address.Space != VmAddressSpace.LocalString;
    }

    /// <summary>A string operand's origin (spec §5.2): the instruction that stored the global cell it names, while
    /// the cell still holds the text that instruction wrote. Null for any other operand, and for a cell whose text
    /// another write changed; the record is dropped then.</summary>
    private TextSite? OriginOf(Operand operand)
        => TryGlobalStringCell(operand, out int cell) ? OriginOfCell(cell) : null;

    private TextSite? OriginOfCell(int cell)
    {
        if (!_stringStores.TryGetValue(cell, out StringStore store)) return null;
        if (string.Equals(Gs(GlobalStrings, cell), store.Text, StringComparison.Ordinal)) return store.Origin;
        _stringStores.Remove(cell);
        return null;
    }

    /// <summary>
    /// After <c>set-string</c>, <c>mov</c>, <c>concat</c> or <c>toString</c> at <paramref name="pc"/> wrote
    /// <paramref name="destination"/>: a global cell records the instruction and the text. A copy from a global cell
    /// whose record is still valid (<paramref name="copiedFrom"/>) takes over that record, so a table row copied
    /// elsewhere keeps the literal's origin.
    /// </summary>
    private void RecordStringStore(Operand destination, int pc, Operand? copiedFrom = null)
    {
        if (!_recordsStringStores || !TryGlobalStringCell(destination, out int cell)) return;
        TextSite? inherited = copiedFrom is { } source ? OriginOf(source) : null;
        _stringStores[cell] = new StringStore(inherited ?? SiteAt(pc), Gs(GlobalStrings, cell));
    }

    /// <summary>After <c>lookup-array</c> copied a string out of a table cell at <paramref name="source"/>: the
    /// copy takes over the table cell's record, or records its own instruction.</summary>
    private void RecordStringCopy(Operand destination, int pc, VmAddress source)
    {
        if (!_recordsStringStores || !TryGlobalStringCell(destination, out int cell)) return;
        TextSite? inherited = TryGlobalStringCell(source, out int from) ? OriginOfCell(from) : null;
        _stringStores[cell] = new StringStore(inherited ?? SiteAt(pc), Gs(GlobalStrings, cell));
    }

    /// <summary>What a length opcode at <paramref name="pc"/> returns for the presented form of
    /// <paramref name="text"/>, read from <paramref name="operand"/> (localization spec §5.3), or null for the native
    /// result: no presenter, no entry, or a language or entry that measures source.</summary>
    private int? PresentedLength(Operand operand, string text, int pc, TextLengthKind kind)
        => _presenter is { } presenter ? presenter.Measure(text, SiteAt(pc), OriginOf(operand), kind) : null;

    /// <summary>Op 0x9 zeroes the string bank, so every record goes with it.</summary>
    private void ClearStringStores() => _stringStores.Clear();
}
