using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepTextHistory(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "set-text-history-recording": // 0x1bb: HISTORY.BIN suppresses recording its own UI
                if (Read(a[0]) is 0 or 1)
                {
                    bool enabled = Read(a[0]) == 1;
                    TextHistory.SetRecordingEnabled(enabled);
                    if (enabled) _host.EndTextHistoryPresentation(Gfx);
                }
                return pc + 1;
            case "append-text-history-metadata": // 0x1d2: (metadata type, value)
                TextHistory.AppendMetadata(Read(a[1]), Read(a[0]), _advTextStyle); return pc + 1;
            case "step-text-history": // 0x1d0: cumulative delta from the latest retained boundary
                if (TextHistory.TryStepGroup((int)Read(a[2]), out var historyEntry))
                {
                    Write(a[0], historyEntry.LayoutSlot);
                    Write(a[1], historyEntry.FirstRecordIndex);
                }
                else
                {
                    Write(a[0], -1);
                    Write(a[1], -1);
                }
                return pc + 1;
            case "query-current-text-redraw": // 0x83: redraw direction plus selected (layout, record)
                // Native SYS4433 exposes the mutable direct-history cursor here; its six Kamidori callers
                // discard the direction and immediately feed the pair to 0x82. The portable ADV service
                // does not drive that native-only cursor, so direction is neutral and the current entry is
                // the newest retained boundary.
                Write(a[0], 0);
                if (TextHistory.TryGetCurrentRedrawEntry(out var redrawEntry))
                {
                    Write(a[1], redrawEntry.LayoutSlot);
                    Write(a[2], redrawEntry.FirstRecordIndex);
                }
                else
                {
                    Write(a[1], -1);
                    Write(a[2], -1);
                }
                return pc + 1;
            case "render-text-history": // 0x1d1: rasterize/bind one retained group to a target layout
            case "redraw-retained-text-group": // 0x82: restore an ordinary ADV group at its retained layout
            case "u0041BAE0":
            {
                int flags = (int)Read(a[2]);
                if ((flags & 4) == 0 && TextHistory.TryBuildRenderBatch(
                        (int)Read(a[0]), (int)Read(a[1]), flags, Read(a[3]), Read(a[4]), out var batch))
                {
                    batch = batch with
                    {
                        // Native History uses the text manager's current leading, not a retained-record field.
                        Style = batch.Style with { LineSpacing = _advTextStyle.LineSpacing }
                    };
                    _host.RenderTextHistory(
                        Gfx,
                        TextHistory.GetPresentationBinding(batch.LayoutSlot),
                        batch);
                }
                return pc + 1;
            }
            case "u0041BB90":
            case "find-text-history-value": // 0x1d3: operand 3 is accepted but ignored natively
            {
                bool found = TextHistory.TryFindMetadata((int)Read(a[3]), Read(a[4]), out long value);
                Write(a[0], found ? 1 : 0);
                Write(a[1], value);
                return pc + 1;
            }
            case "u0041BC00":
            case "find-text-history-pair": // 0x1d4: operand 3 is accepted but ignored natively
                TextHistory.TryFindVoicePair((int)Read(a[3]), out long voiceId, out long voiceArgument);
                Write(a[0], voiceId);
                Write(a[1], voiceArgument);
                return pc + 1;
            case "clear-text-history": // 0x85: bound the backlog to the current ordinary ADV block
                TextHistory.Clear(); return pc + 1;
            default:
                throw new InvalidOperationException($"Non-text-history opcode routed to text-history handler: {label}");
        }
    }
}
