namespace Age.Engine.Vm;

/// <summary>Per-script-frame registry populated by SYS4 ops 0x90/0x94/0x97.</summary>
/// <remarks>Native keeps two indices (registry at ctx+0x5248). The current hit (+0x74b0) changes only when
/// <c>age_main_window_proc</c> hit-tests a WM_MOUSEMOVE at a new point, when the first arm after a registration
/// hit-tests the cursor, and when ops 0x90 and 0x7c clear it. The hovered record (+0xefc) follows the hit only when a
/// wait polls (<c>input_hotspot_poll_hover_callback@0x4040b0</c>): a difference dispatches leave, then enter. A click
/// activates the current hit, never the point under the button.</remarks>
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
    private int _hit = -1;
    private bool _hitTestedSinceRegister;
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
        // op_0x90_handler@0x41fc80 clears the hit; input_hotspot_register_rect_callbacks clears the arm's +0x74a4, so
        // the next arm hit-tests the cursor again.
        _hit = -1;
        _hitTestedSinceRegister = false;
    }

    public void BindKey(int x, int y, int width, int height, int logicalAction)
    {
        int right = x + width, bottom = y + height;
        var entry = _entries.FirstOrDefault(e => e.Left == x && e.Top == y
                                              && e.Right == right && e.Bottom == bottom);
        if (entry != null) entry.LogicalAction = logicalAction;
    }

    /// <summary>Arm dispatch and poll. The first arm after a registration hit-tests the cursor
    /// (<c>input_hotspot_arm_cursor_tracking@0x404230</c>); later arms keep the current hit.</summary>
    public bool Arm(int pointerX, int pointerY)
    {
        _replaceOnNextRegister = false;
        Armed = true;
        if (!_hitTestedSinceRegister)
        {
            _hitTestedSinceRegister = true;
            _hit = FindHit(pointerX, pointerY);
        }
        return Poll();
    }

    /// <summary>A pointer move to a new point (native WM_MOUSEMOVE): hit-test it, armed or not, as
    /// <c>age_main_window_proc</c> does while a callback runs. Only an armed registry polls the change.</summary>
    public bool MovePointer(int x, int y)
    {
        if (_entries.Count == 0) return false;
        _hit = FindHit(x, y);
        return Armed && Poll();
    }

    /// <summary>Op 0x7c clears the hit (<c>op_0x7c_handler@0x417cb0</c>) and keeps the hovered record, so the next
    /// poll leaves the hovered record until the pointer moves again.</summary>
    public bool InvalidateHit()
    {
        _hit = -1;
        return Armed && Poll();
    }

    private bool Poll()
    {
        if (_hit == _hovered) return false;

        if (_hovered >= 0) QueueTarget(_entries[_hovered].LeaveTarget);
        _hovered = _hit;
        if (_hovered >= 0) QueueTarget(_entries[_hovered].EnterTarget);
        return _pendingTargets.Count != 0;
    }

    /// <summary>A click activates the current hit (<c>input_hotspot_current_click_callback@0x404f70</c>). With no hit
    /// the click is not consumed and advances the page.</summary>
    public bool Activate()
    {
        if (!Armed || _hit < 0) return false;

        return ConsumeActivation(_entries[_hit].ActivateTarget);
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
        SuspendDispatch();
        QueueTarget(target);
        return true;
    }

    /// <summary>Stop dispatch, keeping the definitions and the hovered record, as an activation does: native polls
    /// nothing while a callback or coroutine handler runs. RearmAfterCallback resumes it.</summary>
    public void SuspendDispatch()
    {
        _pendingTargets.Clear();
        Armed = false;
    }

    public bool RearmAfterCallback(int pointerX, int pointerY)
        => !Armed && _entries.Count != 0 && Arm(pointerX, pointerY);

    public bool TryDequeue(out int target) => _pendingTargets.TryDequeue(out target);

    public void Reset()
    {
        _pendingTargets.Clear();
        _hovered = -1;
        _hit = -1;
        _hitTestedSinceRegister = false;
        Armed = false;
        // Native op 0x93 empties the table. The port keeps the definitions as a template that the next
        // wait-for-input re-arms. That serves direct-scene runs, which force the scene's developer-loop 0x93
        // at 0x622. Native reaches that 0x93 only through TITLE's unreachable developer menu, and leaves the
        // table empty (docs/backlog.md). On retail paths a script registration always comes first; it
        // replaces the template instead of duplicating it, which is the native result.
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
