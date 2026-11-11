using System.Runtime.CompilerServices;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Mods.Localization.Import;
using Ome.Script.Abi;

namespace Ome.Mods.Localization;

/// <summary>
/// The message tables of the scripts the VM runs (localization spec §6.2): each loaded script is segmented once,
/// with the same branch targets <c>import-scripts</c> uses, the first time one of its runs is drawn. Scripts are
/// keyed by object, so a script the VM loads again is segmented again, and one it drops is not kept alive. With an
/// opcode table that lacks a text opcode, no script is segmented and every run resolves on its own.
/// </summary>
public sealed class ScriptMessageIndex : ITextMessageIndex
{
    private readonly ConditionalWeakTable<Engine.Model.Script, ScriptMessages> _tables = new();
    private readonly OpcodeContracts _contracts;
    private readonly TextOpcodes? _opcodes;

    public ScriptMessageIndex(OpcodeTable table, OpcodeContracts contracts)
    {
        _contracts = contracts;
        try
        {
            _opcodes = TextOpcodes.FromTable(table);
        }
        catch (InvalidDataException)
        {
            _opcodes = null;
        }
    }

    public ScriptMessages? MessagesOf(Engine.Model.Script script)
        => _opcodes is { } opcodes
            ? _tables.GetValue(script, loaded =>
                MessageSegmenter.Segment(loaded, opcodes, ScriptAlignment.BranchTargets(loaded, _contracts)))
            : null;
}
