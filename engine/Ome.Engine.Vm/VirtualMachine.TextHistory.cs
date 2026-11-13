using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0x1bb: HISTORY.BIN suppresses recording its own UI
    private StepResult OpSetTextHistoryRecording(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (Read(a[0]) is 0 or 1)
        {
            bool enabled = Read(a[0]) == 1;
            _text.History.SetRecordingEnabled(enabled);
            if (enabled) _text.EndTextHistoryPresentation(Gfx);
        }
        return StepResult.Next;
    }

    // 0x1d2: (metadata type, value)
    private StepResult OpAppendTextHistoryMetadata(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.History.AppendMetadata(Read(a[1]), Read(a[0]), _text.Style); return StepResult.Next;
    }

    // 0x1d0: cumulative delta from the latest retained boundary
    private StepResult OpStepTextHistory(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (_text.History.TryStepGroup((int)Read(a[2]), out var historyEntry))
        {
            Write(a[0], historyEntry.LayoutSlot);
            Write(a[1], historyEntry.FirstRecordIndex);
        }
        else
        {
            Write(a[0], -1);
            Write(a[1], -1);
        }
        return StepResult.Next;
    }

    // 0x83: redraw direction plus selected (layout, record)
    private StepResult OpQueryCurrentTextRedraw(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Native SYS4433 exposes the mutable direct-history cursor here; its six Kamidori callers
        // discard the direction and immediately feed the pair to 0x82. The portable ADV service
        // does not drive that native-only cursor, so direction is neutral and the current entry is
        // the newest retained boundary.
        Write(a[0], 0);
        if (_text.History.TryGetCurrentRedrawEntry(out var redrawEntry))
        {
            Write(a[1], redrawEntry.LayoutSlot);
            Write(a[2], redrawEntry.FirstRecordIndex);
        }
        else
        {
            Write(a[1], -1);
            Write(a[2], -1);
        }
        return StepResult.Next;
    }

    // 0x1d1: rasterize/bind one retained group to a target layout
    private StepResult OpRenderTextHistory(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Native History draws every record in the text manager's current style.
        _text.RenderHistoryGroup(Gfx, (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), Read(a[3]), Read(a[4]));
        return StepResult.Next;
    }

    // 0x82: draw an ordinary ADV group again at its records' rectangles
    private StepResult OpRedrawRetainedTextGroup(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.RedrawGroup(Gfx, (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), Read(a[3]), Read(a[4]));
        return StepResult.Next;
    }

    // 0x1d3: operand 3 is accepted but ignored natively
    private StepResult OpFindTextHistoryValue(Instruction ins, int pc)
    {
        var a = ins.Args;
        bool found = _text.History.TryFindMetadata((int)Read(a[3]), Read(a[4]), out long value);
        Write(a[0], found ? 1 : 0);
        Write(a[1], value);
        return StepResult.Next;
    }

    // 0x1d4: operand 3 is accepted but ignored natively
    private StepResult OpFindTextHistoryPair(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.History.TryFindVoicePair((int)Read(a[3]), out long voiceId, out long voiceArgument);
        Write(a[0], voiceId);
        Write(a[1], voiceArgument);
        return StepResult.Next;
    }

    // 0x85: bound the backlog to the current ordinary ADV block
    private StepResult OpClearTextHistory(Instruction ins, int pc)
    {
        _text.History.Clear(); return StepResult.Next;
    }
}
