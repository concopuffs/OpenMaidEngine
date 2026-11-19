using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

/// <summary>The bank a <see cref="CellJournal"/> entry puts a value back into: one of script memory's five, or a local
/// of the frame the delivery runs in.</summary>
internal enum JournalBank : byte { Integer, Float, String, Pointer, StringPointer, LocalInteger, LocalFloat, LocalString }

/// <summary>
/// What the running deliveries wrote (data patches plan §12.6): for each cell, the value it held before a delivery's
/// first write to it, so that a delivery that fails is put back, latest write first, before anything else runs.
/// </summary>
/// <remarks>
/// <para>Script memory's setters keep a global cell's entry while a delivery runs, so a write made any way is kept: an
/// accessor, <c>maid.vm</c>, an event's field, a handler the delivery's call ran, a script it called. The scripting
/// native keeps a local's, for the frame the delivery runs in only: a frame a call pushed is gone when the call ends.</para>
/// <para>Deliveries nest. Each opens a span; a cell has one entry in a span, its first write's, so the journal holds
/// an entry for each cell a span wrote, never one for each write. A span that ends well leaves its entries to the span
/// around it, and the last one to end clears the journal. Between deliveries it is empty, and script memory holds
/// none. It runs no Lua, so it costs a delivery no instructions.</para>
/// <para>Engine thread only.</para>
/// </remarks>
internal sealed class CellJournal
{
    private readonly record struct Entry(JournalBank Bank, int Address, bool Present, long Number, string? Text, object? Frame);

    private sealed class Span(int first, object? frame)
    {
        public int First { get; } = first;
        public object? Frame { get; } = frame;
        // The cells the span has an entry for, made at its first write.
        public HashSet<long>? Cells;
    }

    private readonly List<Entry> _entries = new();
    private readonly List<Span> _spans = new();
    private bool _puttingBack;

    /// <summary>Whether a delivery is running.</summary>
    public bool Open => _spans.Count > 0;

    /// <summary>The frame the innermost delivery runs in, or null when it runs in none.</summary>
    public object? Frame => _spans.Count > 0 ? _spans[^1].Frame : null;

    /// <summary>The most entries the journal has held at once, for the tests that measure it.</summary>
    public int Peak { get; private set; }

    /// <summary>A delivery starts, in <paramref name="frame"/> (null when no frame runs).</summary>
    public void Begin(object? frame) => _spans.Add(new Span(_entries.Count, frame));

    /// <summary>A cell's value before a write: kept at the innermost span's first write to it. <paramref name="present"/>
    /// is false for a cell its bank has never held, which reads 0 or "" and is taken out again.</summary>
    public void Keep(JournalBank bank, int address, bool present, long number, string? text, object? frame = null)
    {
        if (_puttingBack || _spans.Count == 0) return;
        Span span = _spans[^1];
        span.Cells ??= new HashSet<long>();
        if (!span.Cells.Add(((long)bank << 32) | (uint)address)) return;
        _entries.Add(new Entry(bank, address, present, number, text, frame));
        if (_entries.Count > Peak) Peak = _entries.Count;
    }

    /// <summary>
    /// The innermost delivery failed: every cell it wrote, and every cell the deliveries it ran wrote, gets its value
    /// back, latest entry first. A global goes back through script memory's setter, so a watch is told of it; a local
    /// goes back when its frame is the running one. Returns the cells put back.
    /// </summary>
    public int PutBack(ScriptMemory memory, IInterpreterAccess? interpreter)
    {
        Span span = _spans[^1];
        int count = 0;
        _puttingBack = true;
        try
        {
            object? running = interpreter?.RunningFrame;
            for (int i = _entries.Count - 1; i >= span.First; i--)
            {
                Entry entry = _entries[i];
                switch (entry.Bank)
                {
                    case JournalBank.LocalInteger or JournalBank.LocalFloat or JournalBank.LocalString:
                        if (interpreter == null || running == null || !ReferenceEquals(entry.Frame, running)) continue;
                        if (entry.Bank == JournalBank.LocalInteger) interpreter.SetLocalInteger(entry.Address, entry.Number);
                        else if (entry.Bank == JournalBank.LocalFloat) interpreter.SetLocalFloat(entry.Address, entry.Number);
                        else interpreter.SetLocalString(entry.Address, entry.Text ?? "");
                        break;
                    default:
                        memory.PutBack(entry.Bank, entry.Address, entry.Present, entry.Number, entry.Text);
                        break;
                }
                count++;
            }
        }
        finally
        {
            _puttingBack = false;
            _entries.RemoveRange(span.First, _entries.Count - span.First);
            span.Cells = null;
        }
        return count;
    }

    /// <summary>The innermost delivery ends: its entries are the enclosing span's now, and with the last span the
    /// journal empties and gives its storage back.</summary>
    public void End()
    {
        _spans.RemoveAt(_spans.Count - 1);
        if (_spans.Count > 0) return;
        _entries.Clear();
        _entries.TrimExcess();
    }
}
