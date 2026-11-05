namespace Age.Engine.Vm;

/// <summary>Per-script-frame registry populated by SYS4 ops 0x90/0x94/0x97.</summary>
internal sealed class HotspotRegistry
{
    private sealed class Entry
    {
        public int Left, Top, Right, Bottom;
        public int EnterTarget, LeaveTarget, ActivateTarget;
        public int? LogicalAction;

        public bool Contains(int x, int y)
            => x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    private readonly List<Entry> _entries = new();
    private readonly Queue<int> _pendingTargets = new();
    private int _hovered = -1;
    private bool _replaceOnNextRegister;
    public bool Armed { get; private set; }
    public bool HasDefinitions => _entries.Count != 0;

    public void Register(int x, int y, int width, int height,
                         int enterTarget, int leaveTarget, int activateTarget)
    {
        if (_replaceOnNextRegister)
        {
            _entries.Clear();
            _replaceOnNextRegister = false;
        }
        _entries.Add(new Entry
        {
            Left = x, Top = y, Right = x + width, Bottom = y + height,
            EnterTarget = enterTarget, LeaveTarget = leaveTarget,
            ActivateTarget = activateTarget,
        });
    }

    public void BindKey(int x, int y, int width, int height, int logicalAction)
    {
        int right = x + width, bottom = y + height;
        var entry = _entries.FirstOrDefault(e => e.Left == x && e.Top == y
                                              && e.Right == right && e.Bottom == bottom);
        if (entry != null) entry.LogicalAction = logicalAction;
    }

    public bool Arm(int pointerX, int pointerY)
    {
        _replaceOnNextRegister = false;
        Armed = true;
        return UpdatePointer(pointerX, pointerY);
    }

    public bool UpdatePointer(int x, int y)
    {
        if (!Armed) return false;
        int hit = FindHit(x, y);
        if (hit == _hovered) return false;

        if (_hovered >= 0) QueueTarget(_entries[_hovered].LeaveTarget);
        _hovered = hit;
        if (_hovered >= 0) QueueTarget(_entries[_hovered].EnterTarget);
        return _pendingTargets.Count != 0;
    }

    public bool Activate(int x, int y)
    {
        if (!Armed) return false;
        int hit = FindHit(x, y);
        if (hit < 0) return false;

        return ConsumeActivation(_entries[hit].ActivateTarget);
    }

    /// <summary>Activate the first armed record whose op-0x97 logical action is present in the
    /// current input mask. Native scans records in registration order before pointer activation.</summary>
    public bool ActivateBoundActions(int actionMask)
    {
        if (!Armed || actionMask == 0) return false;
        for (int i = 0; i < _entries.Count; i++)
        {
            int? action = _entries[i].LogicalAction;
            if (action is >= 0 and < 32 && (actionMask & (1 << action.Value)) != 0)
                return ConsumeActivation(_entries[i].ActivateTarget);
        }
        return false;
    }

    private bool ConsumeActivation(int target)
    {
        // At the ADV text wait native keeps the whole registration across an activation. The click
        // callback comes from input_hotspot_current_click_callback@0x404f70, which resets nothing, and the
        // dispatcher pushes the waiting op so the callback's ret re-runs the wait (traced 2026-11-04: count
        // and hovered record unchanged, no re-registration). So the definitions and the hovered record stay:
        // the clicked control's enter fires again only after the pointer has left it, while the neighbour's
        // enter fires on a direct move. Disarming only stops dispatch while the callback runs, as native
        // polls nothing then; RearmAfterCallback resumes it. (The registry-resetting
        // input_hotspot_take_click_callback@0x404330 serves the modal ops 0x91/0x92/0x95, which the port
        // does not implement.)
        _pendingTargets.Clear();
        Armed = false;
        QueueTarget(target);
        return true;
    }

    public bool RearmAfterCallback(int pointerX, int pointerY)
        => !Armed && _entries.Count != 0 && Arm(pointerX, pointerY);

    public bool TryDequeue(out int target) => _pendingTargets.TryDequeue(out target);

    public void Reset()
    {
        _pendingTargets.Clear();
        _hovered = -1;
        Armed = false;
        // Native op 0x93 empties the table. The port keeps the definitions as a template that the next
        // wait-for-input re-arms, for direct-scene runs that force SC0000's 0x93 at 0x622 (native New Game
        // never runs it; docs/backlog.md). A script registration first replaces the template instead of
        // duplicating it, which is the native result.
        _replaceOnNextRegister = _entries.Count != 0;
    }

    private int FindHit(int x, int y)
    {
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].Contains(x, y)) return i;
        return -1;
    }

    private void QueueTarget(int target)
    {
        if (target >= 0 && (uint)target != uint.MaxValue) _pendingTargets.Enqueue(target);
    }
}
