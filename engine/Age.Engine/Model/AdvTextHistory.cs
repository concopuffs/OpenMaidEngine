namespace Age.Engine.Model;

[Flags]
public enum AdvTextHistoryRecordFlags : uint
{
    None = 0,
    GroupStart = 0x00000001,
    NavigationFiltered = 0x00000002,
    TypedMetadata = 0x20000000,
    VoicePair = 0x40000000,
}

public enum AdvTextHistoryRecordKind
{
    Text,
    Metadata,
    Voice,
}

/// <summary>The text raster state copied into each native retained text record.</summary>
public readonly record struct AdvTextStyle(
    int PrimaryFontSize,
    int RubyFontSize,
    bool Bold,
    long TextColor,
    long EffectColor,
    int RenderMode,
    int EffectOffsetX,
    int EffectOffsetY)
{
    public static AdvTextStyle Default => new(0, 0, false, 0, 0, 0, 0, 0);
}

/// <summary>A stable snapshot of the layout state associated with a retained record.</summary>
public readonly record struct AdvTextLayoutSnapshot(
    int Slot,
    int Width,
    int Height,
    int OriginX,
    int OriginY,
    int CursorX,
    int CursorY);

/// <summary>
/// One semantic counterpart of AGE's 0x48-byte retained text record. Metadata uses
/// <see cref="Value"/> plus <see cref="AuxValue"/> as value/type; voice uses them as id/argument.
/// </summary>
public sealed record AdvTextHistoryRecord(
    AdvTextHistoryRecordKind Kind,
    AdvTextHistoryRecordFlags Flags,
    AdvTextLayoutSnapshot Layout,
    AdvTextStyle Style,
    string Text,
    long Value,
    long AuxValue,
    int SourceOffset);

/// <summary>AGE's logical 8-byte history index entry.</summary>
public readonly record struct AdvTextHistoryEntry(int LayoutSlot, int FirstRecordIndex);

/// <summary>
/// Engine-owned retained ADV backlog. It deliberately has no persistence behavior: native numbered-save
/// restoration belongs to the future unified save architecture, while live HISTORY.BIN reads this model.
/// </summary>
public sealed class AdvTextHistory
{
    private sealed class LayoutState
    {
        public int Width;
        public int Height;
        public int OriginX;
        public int OriginY;
        public int CursorX;
        public int CursorY;
    }

    private readonly List<AdvTextHistoryRecord> _records = new();
    private readonly List<AdvTextHistoryEntry> _entries = new();
    private readonly Dictionary<int, LayoutState> _layouts = new();
    private readonly HashSet<int> _pendingGroupStarts = new();
    private int _navigationAnchorIndex = -1;

    public IReadOnlyList<AdvTextHistoryRecord> Records => _records;
    public IReadOnlyList<AdvTextHistoryEntry> Entries => _entries;
    public bool RecordingSuppressed { get; private set; }
    public int CurrentLayoutSlot { get; private set; }

    public void SetRecordingEnabled(bool enabled) => RecordingSuppressed = !enabled;

    public void DefineLayout(int requestedSlot, int width, int height, int originX, int originY)
    {
        int slot = SelectLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.Width = width;
        layout.Height = height;
        layout.OriginX = originX;
        layout.OriginY = originY;
        AppendBoundary(slot);
    }

    public void ResetLayout(int requestedSlot)
    {
        int slot = SelectLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.CursorX = 0;
        layout.CursorY = 0;
        AppendBoundary(slot);
    }

    public void SetCursor(int requestedSlot, int x, int y)
    {
        int slot = ResolveLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.CursorX = x;
        layout.CursorY = y;
    }

    public void AppendText(int requestedSlot, int sourceOffset, string text, AdvTextStyle style,
                           AdvTextHistoryRecordFlags flags = AdvTextHistoryRecordFlags.None)
    {
        int slot = ResolveLayout(requestedSlot);
        AppendRecord(slot, AdvTextHistoryRecordKind.Text, flags,
                     style, text, 0, 0, sourceOffset);
    }

    public void AppendMetadata(long value, long metadataType, AdvTextStyle style)
        => AppendRecord(CurrentLayoutSlot, AdvTextHistoryRecordKind.Metadata,
                        AdvTextHistoryRecordFlags.TypedMetadata, style, "", value, metadataType, -1);

    public void AppendVoice(long voiceId, long voiceArgument, AdvTextStyle style)
        => AppendRecord(CurrentLayoutSlot, AdvTextHistoryRecordKind.Voice,
                        AdvTextHistoryRecordFlags.VoicePair, style, "", voiceId, voiceArgument, -1);

    /// <summary>Clear the retained records and logical index while keeping reusable layout definitions.</summary>
    public void Clear()
    {
        _records.Clear();
        _entries.Clear();
        _pendingGroupStarts.Clear();
        _navigationAnchorIndex = -1;
    }

    /// <summary>
    /// Resolve a logical entry relative to AGE's latest-boundary navigation anchor. Repeated calls do not
    /// mutate the anchor; HISTORY.BIN supplies cumulative deltas while counting and paging backward.
    /// </summary>
    public bool TryStepGroup(int delta, out AdvTextHistoryEntry entry)
    {
        entry = new AdvTextHistoryEntry(-1, -1);
        if ((uint)_navigationAnchorIndex >= (uint)_entries.Count) return false;

        int index = _navigationAnchorIndex;
        if (delta < 0)
        {
            for (int remaining = -delta; remaining > 0; remaining--)
            {
                int firstRecord = _entries[index].FirstRecordIndex;
                do
                {
                    if (firstRecord == 0 || index < 1) return false;
                    index--;
                }
                while (IsNavigationFiltered(_entries[index])
                       || _entries[index].FirstRecordIndex == firstRecord);
            }
        }
        else
        {
            for (int remaining = delta; remaining > 0; remaining--)
            {
                int firstRecord = _entries[index].FirstRecordIndex;
                do
                {
                    index++;
                    if (index >= _entries.Count) return false;
                    // Native treats the last entry's record offset as the forward sentinel.
                    if (_entries[index].FirstRecordIndex == _entries[^1].FirstRecordIndex) return false;
                }
                while (IsNavigationFiltered(_entries[index])
                       || _entries[index].FirstRecordIndex == firstRecord);
            }
        }

        entry = _entries[index];
        return true;
    }

    public bool TryFindMetadata(int firstRecordIndex, long metadataType, out long value)
    {
        value = 0;
        bool found = false;
        foreach (var record in EnumerateGroup(firstRecordIndex))
        {
            if (!record.Flags.HasFlag(AdvTextHistoryRecordFlags.TypedMetadata)
                || record.AuxValue != metadataType) continue;
            value = record.Value;
            found = true;
        }
        return found;
    }

    public bool TryFindVoicePair(int firstRecordIndex, out long voiceId, out long voiceArgument)
    {
        voiceId = -1;
        voiceArgument = -1;
        bool found = false;
        foreach (var record in EnumerateGroup(firstRecordIndex))
        {
            if (!record.Flags.HasFlag(AdvTextHistoryRecordFlags.VoicePair)) continue;
            voiceId = record.Value;
            voiceArgument = record.AuxValue;
            found = true;
        }
        return found;
    }

    private int SelectLayout(int requestedSlot)
    {
        int slot = requestedSlot == 0 ? CurrentLayoutSlot : requestedSlot;
        CurrentLayoutSlot = slot;
        return slot;
    }

    private int ResolveLayout(int requestedSlot) => requestedSlot == 0 ? CurrentLayoutSlot : requestedSlot;

    private LayoutState GetOrCreateLayout(int slot)
    {
        if (!_layouts.TryGetValue(slot, out var layout))
        {
            layout = new LayoutState();
            _layouts.Add(slot, layout);
        }
        return layout;
    }

    private void AppendBoundary(int slot)
    {
        if (RecordingSuppressed) return;
        _entries.Add(new AdvTextHistoryEntry(slot, _records.Count));
        _pendingGroupStarts.Add(slot);
        _navigationAnchorIndex = _entries.Count - 1;
    }

    private bool IsNavigationFiltered(AdvTextHistoryEntry entry)
        => (uint)entry.FirstRecordIndex < (uint)_records.Count
           && _records[entry.FirstRecordIndex].Flags.HasFlag(AdvTextHistoryRecordFlags.NavigationFiltered);

    private IEnumerable<AdvTextHistoryRecord> EnumerateGroup(int firstRecordIndex)
    {
        if ((uint)firstRecordIndex >= (uint)_records.Count) yield break;
        for (int i = firstRecordIndex; i < _records.Count; i++)
        {
            if (i > firstRecordIndex
                && _records[i].Flags.HasFlag(AdvTextHistoryRecordFlags.GroupStart)) yield break;
            yield return _records[i];
        }
    }

    private void AppendRecord(int slot, AdvTextHistoryRecordKind kind, AdvTextHistoryRecordFlags flags,
                              AdvTextStyle style, string text, long value, long auxValue, int sourceOffset)
    {
        if (RecordingSuppressed) return;
        if (_pendingGroupStarts.Remove(slot)) flags |= AdvTextHistoryRecordFlags.GroupStart;

        var layout = GetOrCreateLayout(slot);
        var snapshot = new AdvTextLayoutSnapshot(slot, layout.Width, layout.Height,
            layout.OriginX, layout.OriginY, layout.CursorX, layout.CursorY);
        _records.Add(new AdvTextHistoryRecord(kind, flags, snapshot, style, text, value, auxValue, sourceOffset));
    }
}
