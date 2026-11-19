using Ome.Engine.Model;
using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

// The journal of a delivery (data patches plan §12.6): a delivery that fails, by an error, its budget or anything else
// that ends its call with an error, has every cell it wrote put back, latest first, before its log line, its notice,
// its trace line or any other handler. Script memory's setters keep a global's value; every write Lua makes to a local
// goes through the interpreter's access below, which keeps the local's. A delivery that succeeds, and every handler
// that is not a delivery, is unchanged.
public sealed partial class Scripting
{
    // Empty between deliveries, so no scene holds it.
    private readonly CellJournal _journal = new();
    // A failed delivery's cells are being put back: a watch is told of each as the engine's write.
    private bool _puttingBack;

    /// <summary>The most cells the journal has held at once in this run, for the tests that measure it.</summary>
    internal int JournalPeak => _journal.Peak;

    private void OpenJournal()
    {
        if (!_journal.Open) _memory.Journal(_journal);
        _journal.Begin(_interpreter?.RunningFrame);
    }

    /// <summary>The innermost delivery failed: its cells get their values back. Returns how many.</summary>
    private int PutBackJournal()
    {
        _puttingBack = true;
        try
        {
            return _journal.PutBack(_memory, _interpreter);
        }
        finally
        {
            _puttingBack = false;
        }
    }

    private void CloseJournal()
    {
        _journal.End();
        if (!_journal.Open) _memory.Journal(null);
    }

    /// <summary>A local's value before Lua writes it, for the innermost delivery: in the frame it runs in, the only
    /// frame still running when it ends.</summary>
    private void KeepLocal(IInterpreterAccess interpreter, CellKind kind, int index)
    {
        if (!_journal.Open) return;
        object? frame = interpreter.RunningFrame;
        if (frame == null || !ReferenceEquals(frame, _journal.Frame)) return;
        switch (kind)
        {
            case CellKind.Integer:
                _journal.Keep(JournalBank.LocalInteger, index, true, interpreter.LocalInteger(index), null, frame);
                break;
            case CellKind.Float:
                _journal.Keep(JournalBank.LocalFloat, index, true, interpreter.LocalFloat(index), null, frame);
                break;
            default:
                _journal.Keep(JournalBank.LocalString, index, true, 0, interpreter.LocalString(index), frame);
                break;
        }
    }

    /// <summary>The interpreter as Lua reaches it (<c>maid.vm</c>'s locals, an event's fields, <c>ctx.args</c>): a write
    /// to a local, directly or through an operand, keeps the local's value for the journal first.</summary>
    private sealed class JournaledInterpreter(Scripting owner, IInterpreterAccess inner) : IInterpreterAccess
    {
        public IInterpreterAccess Inner => inner;

        public IReadOnlyList<(string Script, int Offset)> Frames() => inner.Frames();
        public bool HasFrame => inner.HasFrame;
        public object? RunningFrame => inner.RunningFrame;
        public (CellKind Kind, int Index)? LocalCellOf(Operand operand) => inner.LocalCellOf(operand);

        public long LocalInteger(int index) => inner.LocalInteger(index);
        public long LocalFloat(int index) => inner.LocalFloat(index);
        public string LocalString(int index) => inner.LocalString(index);

        public void SetLocalInteger(int index, long value)
        {
            owner.KeepLocal(inner, CellKind.Integer, index);
            inner.SetLocalInteger(index, value);
        }

        public void SetLocalFloat(int index, long bits)
        {
            owner.KeepLocal(inner, CellKind.Float, index);
            inner.SetLocalFloat(index, bits);
        }

        public void SetLocalString(int index, string value)
        {
            owner.KeepLocal(inner, CellKind.String, index);
            inner.SetLocalString(index, value);
        }

        public CellValue ReadOperand(Operand operand) => inner.ReadOperand(operand);

        // A global the operand names is kept by script memory's setter.
        public void WriteOperand(Operand operand, CellValue value)
        {
            if (owner._journal.Open && inner.LocalCellOf(operand) is { } local) owner.KeepLocal(inner, local.Kind, local.Index);
            inner.WriteOperand(operand, value);
        }

        public uint? ScriptId(string name) => inner.ScriptId(name);
        public void Call(string name, long instructions) => inner.Call(name, instructions);
        public bool IsNumberedSave(int opcode) => inner.IsNumberedSave(opcode);
        public bool InsideWait => inner.InsideWait;
    }
}
