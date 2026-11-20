using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using System.Text;
namespace Ome.Engine.Vm;

/// <summary>A stable identity/snapshot of the exact script frame currently executing.</summary>
public sealed record DebugFrameSnapshot(long FrameId, string CurrentScript, IReadOnlyList<string> CallStack);

public sealed partial class VirtualMachine
{
    private const long NoJump = 0xFFFFFFFF;
    // Pushed on a frame's local call stack under a wait's callback: the callback's ret pops it and returns to the wait.
    private const int HotspotReturnMarker = int.MinValue + 2;
    private const int T_IMM = 0, T_FLOAT = 1, T_STR = 2, T_GINT = 3, T_GFLOAT = 4, T_GSTR = 5, T_GPTR = 6,
                      T_GSTRPTR = 8, T_LINT = 9, T_LFLOAT = 10, T_LSTR = 11, T_LPTR = 12,
                      T_LFLOATPTR = 13, T_LSTRPTR = 14;

    private readonly Script _s;
    private readonly OpcodeTable _t;
    private readonly Handler?[] _handlers;   // _t's opcodes bound to their handlers (VirtualMachine.Handlers.cs)
    private readonly ulong[] _floatOperands; // per opcode, bit i: _t's ABI reads or writes operand i as a float
    // The input and waits native (Ome.Engine/Natives/InputAndWaits*.cs), or a stand-in whose waits return at once.
    private readonly IInputAndWaits _waits;
    private readonly SkipAndAuto _skip;
    private readonly Graphics _graphics;
    private readonly VmOptions _o;
    // The engine build's rules (Ome.Engine.Vm/EngineBuilds.cs). The VM never reads the build's id or ABI.
    private readonly EngineRules _rules;
    private readonly ScriptMemory _memory;
    private readonly IScriptProvider? _provider;
    private readonly SavesAndSettings _saves;
    private readonly NumberedSaveRecords _saveRecords;   // what a numbered save holds besides the save native's own
    private readonly ShellServices _shell;
    private readonly TextManager _text;
    private static readonly bool _diagSetTexture = System.Environment.GetEnvironmentVariable("OME_DIAG_SETTEX") == "1";
    private ExecFrame _cur = null!;
    private int _depth;
    private readonly ITraceSink _sink;
    private readonly List<string> _activeFrameNames = new();
    private readonly List<ExecFrame> _activeExecutionFrames = new();
    private int _restoreFrameIndex = -1;   // the saved frame a full load restores next (SavesAndSettings.RestoringFrames)
    private long _debugNextFrameId;
    private int _pointerX = int.MinValue, _pointerY = int.MinValue;
    // Native run-state bit 0x80000000: an ADV wait (op 0x72's text wait, or op 0xfa's poll with no page) is polling
    // input. It is clear while that wait runs a hotspot callback, as native clears the bit on dispatch and the
    // callback's ret re-runs the wait.
    private bool _advTextWaitPolling;
    private bool _advInputPollPageless;   // the polling wait is op 0xfa's
    // The stop and return-to-title requests: the input and waits native's copy when the VM is built with it, so a
    // request ends its waits too (engine layering plan §3.3).
    private readonly StopAndReload _stopAndReload;
    public long CallScriptDispatches { get; private set; }

    /// <summary>The script memory native this VM's handlers read and write (Ome.Engine/Natives/ScriptMemory.cs).
    /// </summary>
    public ScriptMemory Memory => _memory;
    // The banks, as tools and tests read them (spec §3.4): script memory's.
    public IReadOnlyDictionary<int, long> Globals => _memory.Integers;
    /// <summary>Writes an integer global as a tool or a test seeds it: in the cell's one form, unseen by watches.</summary>
    public void SeedGlobal(int address, long value) => _memory.SeedInteger(address, value);
    public Dictionary<int, long> GlobalFloats => _memory.Floats;
    public Dictionary<int, string> GlobalStrings => _memory.Strings;
    public Dictionary<int, int> GlobalPointers => _memory.Pointers;
    public Dictionary<int, int> GlobalStringPointers => _memory.StringPointers;
    /// <summary>The retained objects and surface records: the graphics native's state.</summary>
    public GfxState Gfx => _graphics.State;
    /// <summary>The graphics native this VM's handlers call (Ome.Engine/Natives/Graphics*.cs).</summary>
    public Graphics Graphics => _graphics;
    public InputBindings InputBindings { get; } = new();
    /// <summary>The shown text: the text native's (<see cref="TextManager.Emitted"/>).</summary>
    public List<(int Offset, string Text, string Script)> Emitted => _text.Emitted;
    public string? HaltReason { get; private set; }
    public UnsupportedOpcodeDiagnostic? CompatibilityFailure { get; private set; }
    /// <summary>The native script error that ended the run (AGE's エラーが発生しました box), or null.</summary>
    public DiagnosticMessage? ScriptError { get; private set; }
    public long Steps { get; private set; }
    /// <summary>The skip and auto native this VM's handlers call (Ome.Engine/Natives/SkipAndAuto.cs).</summary>
    public SkipAndAuto SkipAndAuto => _skip;
    public bool AutoMessageEnabled => _skip.AutoMessageEnabled;
    public bool MessageSkipEnabled => _skip.MessageSkipEnabled;
    /// <summary>The save and settings native this VM's handlers call (Ome.Engine/Natives/SavesAndSettings*.cs).
    /// </summary>
    public SavesAndSettings Saves => _saves;
    /// <summary>The shell-services native this VM's handlers call (Ome.Engine/Natives/ShellServices*.cs): the
    /// system-menu fields, the menu requests and callbacks, the diagnostic text, the dialogs and the screen mode.
    /// </summary>
    public ShellServices Shell => _shell;
    /// <summary>
    /// Zero-based active-frame cutoff selected by opcode 0x1ad, or null when no surviving marker
    /// exists. A numbered-save serializer consumes this boundary in the full payload slice.
    /// </summary>
    public int? SaveResumeFrameDepth
    {
        get
        {
            int index = _scene.SaveResumeFrame == null ? -1 : _activeExecutionFrames.IndexOf(_scene.SaveResumeFrame);
            return index >= 0 ? index : null;
        }
    }
    /// <summary>True while the currently executing script frame owns a timed raw mouse/input callback
    /// loop (HISTORY/HIDEWIN/FIELD family). A registered callback in a suspended parent frame is dormant
    /// while a nested ADV scene owns the VM and must not suppress that child's ordinary page input.</summary>
    public bool IsRawInputCallbackActive
    {
        get
        {
            ExecFrame? rawInputFrame;
            rawInputFrame = _scene.RawInputFrame;
            if (rawInputFrame == null) return false;
            return ReferenceEquals(rawInputFrame, _scene.DebugActiveFrame);
        }
    }
    public string? RawInputCallbackScriptName
    {
        get { return _scene.RawInputFrame?.Script.Name; }
    }
    /// <summary>The text native this VM's handlers call (Ome.Engine/Natives/TextManager*.cs): the layouts and
    /// History, the style, the reveal, the wait indicator, the message settings and the presenter.</summary>
    public TextManager Text => _text;
    /// <summary>The layouts and the backlog: the text native's.</summary>
    public AdvTextHistory TextHistory => _text.History;

    /// <summary>The currently executing recursive script frame and stack, or null outside VM execution.</summary>
    public DebugFrameSnapshot? DebugFrame
    {
        get
        {
            return _scene.DebugActiveFrame == null
                ? null
                : new DebugFrameSnapshot(_scene.DebugActiveFrameId, _scene.DebugActiveFrame.Script.Name,
                                         _activeFrameNames.ToArray());
        }
    }

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5).
    private sealed class Scene
    {
        public ExecFrame? SaveResumeFrame;
        public ExecFrame? DebugActiveFrame;
        public long DebugActiveFrameId;
        public DebugFrameReturnRequest? DebugFrameReturnRequest;
        public HostedSceneEntry? HostedSceneEntry;   // pending until the coordinator frame enters
        public long? HostedScenePackedId;            // set once the coordinator returned, until the scene enters
        public ExecFrame? InteractiveFrame;
        public ExecFrame? RawInputFrame;
        public int MouseButtonState;
        public int MouseWheelDelta;
        public int MouseHorizontalWheelDelta;   // AGE 4.38D, ctx+0x1c38: op 0x2e5 reads and clears it
        // AGE 4.38D's stepped callbacks: how many steps op 0x104 cycles through (ctx+0x771ac, op 0x103) and the
        // actions that return to op 0x104 itself (ctx+0x771b0, op 0x105). The scene reset zeroes both.
        public int InputCallbackSteps;
        public int InputCallbackRepeatMask;
        public int HeldInputCallbackMask;
        public int QueuedInputCallbackMask;
        public bool InitialRootRun;   // the run's first root run (SYSTEM4 plays LOGO and OP); a reloaded scene's is not
        public readonly Dictionary<string, int> ValueSwitchTargets = new(StringComparer.Ordinal);
        // Opcodes 0x06/0x08 load scripts into numbered EngineCtx frame slots and invoke them later.
        // Unlike ordinary call-script frames, native non-adjacent slots survive return with locals intact.
        public readonly Dictionary<int, PreloadedScriptSlot> PreloadedScriptSlots = new();
        // The op-0x199 yield is engine state (coroutine_runstate +0x6dbc8, resume offset +0x6dbcc, owner +0x5f6bc).
        // The resume is held as an instruction index: op 0x7c only takes it in a frame of the owner's script.
        public bool CoroutineYieldEntered;      // coroutine_runstate 0x4000000
        public int? CoroutineResumePc;          // coroutine_runstate 0x2000000 with its resume offset
        public Script? CoroutineYieldOwner;
        // The yield came from an ADV text wait (adv_input_service_poll's action-5 entry), so the saved run state holds
        // the wait (0x80000000): op 0x7c returns to the parked wait rather than to an instruction.
        public bool CoroutineResumesTextWait;
        public bool CoroutineYieldActive;      // CoroutineResumePc is set; read when input is routed
        // The op-0xcc mouse callback is engine state, not frame state (op 0xcd checks the registrant).
        public int MouseCallbackTarget = -1;
        public Script? MouseCallbackOwner;
        // The op-0xd3..0xd5 timed sequence is engine state (entries +0x5f694, guard +0x5f6b8), shared by every frame.
        public readonly List<TimedCallback> TimedCallbacks = new();
        public int TimedCallbackCursor;
        public long TimedCallbackStartedAtMs;
        public int TimedCallbackAbortOffset = -1;
        public Script? TimedCallbackGuard;
        // The message a frame shows run by run (VirtualMachine.AdvText.cs).
        public PresentedMessage? PresentedMessage;
        // Native tick_presentation_enabled (ctx+0x99294; SYS4433 +0x9d73c), ops 0x21b/0x247. The reset sets it to 0.
        public bool TickPresentationEnabled;
    }

    private Scene _scene = new() { InitialRootRun = true };

    public VirtualMachine(Script s, OpcodeTable t, IInputAndWaits waits, VmOptions? o = null,
                          IScriptProvider? provider = null, ITraceSink? sink = null,
                          AdvTextHistory? textHistory = null, SavesAndSettings? saves = null,
                          ShellServices? shell = null,
                          EngineBuild? build = null, SkipAndAuto? skipAndAuto = null,
                          Audio? audio = null, EngineTick? tick = null,
                          Graphics? graphics = null, Movies? movies = null, ScriptMemory? memory = null,
                          TextManager? text = null, Scripting? scripting = null)
    {
        ArgumentNullException.ThrowIfNull(waits);
        _s = s; _t = t; _handlers = BindHandlers(t); _floatOperands = FloatOperandMasks(t); _o = o ?? new VmOptions(); _provider = provider;
        // Lua scripting spec §10: with Lua in the stack, the waits refuse while a handler's script call runs.
        _waits = scripting is { Setup.Stack.HasLua: true } ? new ModCallWaits(waits, this) : waits;
        // Composition hands over the native its time device belongs to; tests and tools that give none get one over
        // the system clock. Waits that keep the effects of fast-forward hear it: the input and waits native already
        // listens to its own skip and auto native, and Listen adds a listener once.
        _skip = skipAndAuto ?? SkipAndAuto.WithSystemClock();
        if (waits is IFastForwardListener listener) _skip.Listen(listener);
        // The audio native and the tick come from composition too; without them the VM runs with a silent one.
        _audio = audio ?? Audio.Silent(_skip);
        _tick = tick ?? new EngineTick(_audio);
        // Without a front end the graphics native has no catalog and no screen (Graphics.Headless).
        _graphics = graphics ?? Graphics.Headless();
        // Without a front end no movie resolves (Movies.Headless); composition gives the one over the decoder.
        _movies = movies ?? Movies.Headless(_graphics);
        // Without a front end the text native draws nothing (TextManager.Headless); it keeps the history it is given,
        // which a GameSession shares across its scenes. A stand-in for the waits that listens to text (a test's fake) hears it.
        if (text != null && textHistory != null && !ReferenceEquals(text.History, textHistory))
            throw new ArgumentException("A text native keeps its own history.", nameof(textHistory));
        _text = text ?? TextManager.Headless(_graphics, _skip, history: textHistory);
        if (waits is ITextListener textListener) _text.Listen(textListener);
        _rules = EngineBuilds.ForVm(build, t).Rules;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // Without composition the banks start empty in a script memory of the options' code page. The scripting native
        // reaches the banks through the same script memory (Lua scripting spec §8.1).
        if (memory != null && scripting != null && !ReferenceEquals(memory, scripting.Memory))
            throw new ArgumentException("The scripting native works over the VM's script memory.", nameof(scripting));
        _memory = memory ?? scripting?.Memory ?? new ScriptMemory(_o.NativeStringCodePage);
        // Without a scripting native the VM has no hooks, as one without an audio native is silent (spec §14.1).
        _scripting = scripting;
        scripting?.AttachInterpreter(this);
        _sink = sink ?? NullTraceSink.Instance;
        // Without composition there is no save store: a numbered save fails, and the shared profile starts empty.
        _saves = saves ?? new SavesAndSettings();
        // Without composition the shell native has no window and no dialogs, and a menu request wakes this VM's waits.
        if (shell == null)
        {
            shell = ShellServices.Headless();
            shell.WakeWaitsWith(waits.WakeInputCallbackService);
        }
        _shell = shell;
        _saveRecords = new NumberedSaveRecords(
            _memory, _audio, _graphics, _text, CaptureSavedFrames,
            releaseSurfacesOnLoad: (_o.CreateObject & 2) != 0 && (_o.AutoFreeTex & 2) != 0, scripting: _scripting);
        // The input and waits native routes the front end's input to this interpreter's hotspots and callbacks.
        waits.RouteInputTo(this);
        _stopAndReload = waits.StopAndReload ?? new StopAndReload();
    }

    /// <summary>Request a clean stop at the next opcode boundary.</summary>
    public void RequestStop() => _stopAndReload.RequestStop();

    /// <summary>Host one scene the way the F4 developer launcher does, before <see cref="Run"/>. The root starts as
    /// after a return to title (so SYSTEM4 skips LOGO and OP), the coordinator returns at its entry with the entry's
    /// writes, and the run ends when the hosted scene returns (<see cref="HostedSceneEntry"/>).</summary>
    public void HostScene(HostedSceneEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.CoordinatorScript);
        _scene.HostedSceneEntry = entry with
        {
            CoordinatorWrites = new Dictionary<int, long>(entry.CoordinatorWrites),
        };
        _scene.HostedScenePackedId = null;
        _scene.InitialRootRun = false;
    }

    /// <summary>
    /// Return to title from outside the script: native's 終了 dialog (AGERC dialog 3) answers 2 for
    /// タイトルに戻る, and the window procedure's WM_CLOSE path (FUN_00405740) then calls
    /// op_0x9_reset_scene_and_reload_root directly. The port ends every active frame at its next opcode
    /// boundary, interrupting any wait, and runs the same scene reset and resource-0 reload as op 0x9.
    /// </summary>
    public void RequestRootReload()
    {
        _stopAndReload.RequestReload();   // waits that share this copy (InputAndWaits) wake from it
        if (!ReferenceEquals(_stopAndReload, _waits.StopAndReload)) _waits.InterruptForRootReload();
    }

    /// <summary>Queue global writes and return only the identified active frame at its next opcode boundary.
    /// Writes are copied here and applied by the VM thread before another opcode executes.</summary>
    public bool TryRequestDebugFrameReturn(long frameId, IReadOnlyDictionary<int, long> globalWrites)
    {
        ArgumentNullException.ThrowIfNull(globalWrites);
        if (_scene.DebugActiveFrame == null || _scene.DebugActiveFrameId != frameId
            || _scene.DebugFrameReturnRequest != null) return false;
        _scene.DebugFrameReturnRequest = new DebugFrameReturnRequest(
            _scene.DebugActiveFrame, new Dictionary<int, long>(globalWrites));
        return true;
    }

    /// <summary>Update the native 800x600 cursor coordinate without advancing the current ADV page.</summary>
    public void UpdatePointer(int x, int y)
    {
        bool wake;
        wake = MovePointerTo(x, y);
        if (wake) _waits.WakeInputCallbackService();
    }

    /// <summary>Queue the activation callback of the armed hotspot under the pointer. True means the click was
    /// consumed.</summary>
    public bool TryActivatePointer(int x, int y)
    {
        bool consumed = false;
        // Windows delivers a WM_MOUSEMOVE before a button message at a new point. The click itself hit-tests
        // nothing: it activates the current hit, which op 0x7c may have cleared under a still pointer.
        MovePointerTo(x, y);
        if (_scene.InteractiveFrame != null)
            consumed = _scene.InteractiveFrame.Hotspots.Activate();
        if (consumed) _waits.WakeInputCallbackService();
        return consumed;
    }

    /// <summary>The WM_MOUSEMOVE path: <c>age_main_window_proc</c> hit-tests a move only at a point other than the last
    /// move's (call at 0x48690f).</summary>
    private bool MovePointerTo(int x, int y)
    {
        if (x == _pointerX && y == _pointerY) return false;
        _pointerX = x; _pointerY = y;
        return _scene.InteractiveFrame?.Hotspots.MovePointer(x, y) == true;
    }

    /// <summary>Queue the first armed hotspot callback bound by op 0x97 to any action in
    /// <paramref name="actionMask"/>. True means the logical input was consumed.</summary>
    public bool TryActivateInputActions(int actionMask)
    {
        bool consumed;
        consumed = _scene.InteractiveFrame?.Hotspots.ActivateBoundActions(actionMask) == true;
        if (consumed) _waits.WakeInputCallbackService();
        return consumed;
    }

    /// <summary>Update one native mouse-button bit (left=0x1, right=0x2 in Himegari).</summary>
    public void UpdateMouseButtonState(int bit, bool pressed) => UpdateMaskBit(ref _scene.MouseButtonState, bit, pressed);

    /// <summary>Accumulate a signed native mouse-wheel delta until op 0x10d consumes it.</summary>
    public void QueueMouseWheelDelta(int delta)
    {
        if (delta == 0) return;
        _scene.MouseWheelDelta += delta;
        _waits.WakeInputCallbackService();
    }

    /// <summary>True while an ADV text wait polls input with no hotspot callback running (native run-state
    /// 0x80000000).</summary>
    public bool IsAdvTextWaitPolling => _advTextWaitPolling;

    /// <summary>Route one native WM_MOUSEWHEEL delta as age_main_window_proc@0x486320 and the ADV services do.
    /// <list type="bullet">
    /// <item>While an ADV text wait polls, the sign raises logical action set:WheelKeyUp/Down. The wait runs a
    /// hotspot record bound to that action by op 0x97 (both games bind wheel-up's action 8 to History). Otherwise
    /// a wheel-down advances when <paramref name="advanceMessageOnWheel"/> is set
    /// (adv_input_service_poll@0x411230), and the delta is dropped. It never reaches op 0x10d.</item>
    /// <item>During a text reveal with <paramref name="advanceMessageOnWheel"/> set, the reveal service (0x406fa0)
    /// reads and clears the accumulated delta: a negative total finishes the reveal, any other total is
    /// discarded.</item>
    /// <item>In the hidden-window wait entered from a text wait, the run state still holds the wait's 0x80000000, so
    /// the window proc raises the same action, which ends the wait (the window comes back). Entered from a control
    /// callback, the run state is 0x4000000 alone and the delta accumulates.</item>
    /// <item>Otherwise the delta accumulates for op 0x10d (HISTORY scrolls with it).</item>
    /// </list>
    /// set:ReDrawTextOnKey (CALLBACK_TEXT.BIN) is not modelled: both games set REDRAWTEXTONKEY=0.</summary>
    public MouseWheelOutcome ApplyMouseWheel(int delta, bool advanceMessageOnWheel, bool textRevealing)
    {
        if (delta == 0) return MouseWheelOutcome.Dropped;
        if (_hiddenWindowWaiting && _hiddenWindowWheelTriggers)
        {
            int action = delta > 0 ? _o.WheelKeyUpAction : _o.WheelKeyDownAction;
            if (action is >= 0 and < 32 && TryLatchAdvCoroutineYieldTrigger(1 << action))
                return MouseWheelOutcome.Activated;
            return MouseWheelOutcome.Dropped;
        }
        if (_advTextWaitPolling)
        {
            int action = delta > 0 ? _o.WheelKeyUpAction : _o.WheelKeyDownAction;
            if (action is >= 0 and < 32 && TryActivateInputActions(1 << action)) return MouseWheelOutcome.Activated;
            return delta < 0 && advanceMessageOnWheel ? MouseWheelOutcome.Advance : MouseWheelOutcome.Dropped;
        }
        if (textRevealing && advanceMessageOnWheel)
        {
            int total = Take(ref _scene.MouseWheelDelta) + delta;
            return total < 0 ? MouseWheelOutcome.Advance : MouseWheelOutcome.Dropped;
        }
        QueueMouseWheelDelta(delta);
        return MouseWheelOutcome.Accumulated;
    }

    /// <summary>Route one native WM_MOUSEHWHEEL delta as AGE 4.38D's age_main_window_proc@0x49f9a0 does. While an ADV
    /// text wait polls (and in the hidden-window wait entered from one) its sign raises logical action
    /// set:HWheelKeyUp or set:HWheelKeyDown, as the vertical wheel raises its own; both are -1 unless the catalog
    /// sets them, and a negative action raises nothing. Otherwise the delta accumulates for op 0x2e5.</summary>
    public MouseWheelOutcome ApplyMouseHorizontalWheel(int delta)
    {
        if (delta == 0) return MouseWheelOutcome.Dropped;
        int action = delta > 0 ? _o.HWheelKeyUpAction : _o.HWheelKeyDownAction;
        if (_hiddenWindowWaiting && _hiddenWindowWheelTriggers)
            return action is >= 0 and < 32 && TryLatchAdvCoroutineYieldTrigger(1 << action)
                ? MouseWheelOutcome.Activated
                : MouseWheelOutcome.Dropped;
        if (_advTextWaitPolling)
            return action is >= 0 and < 32 && TryActivateInputActions(1 << action)
                ? MouseWheelOutcome.Activated
                : MouseWheelOutcome.Dropped;
        _scene.MouseHorizontalWheelDelta += delta;
        _waits.WakeInputCallbackService();
        return MouseWheelOutcome.Accumulated;
    }

    /// <summary>Update one held logical AGE action directly. Physical frontends should use the
    /// keyboard/mouse/joystick methods below so script-configured bindings remain authoritative.</summary>
    public void UpdateInputCallbackState(int index, bool pressed)
    {
        if ((uint)index >= 32) return;
        UpdateMaskBit(ref _scene.HeldInputCallbackMask, 1 << index, pressed);
        _waits.WakeInputCallbackService();
    }

    public int UpdateKeyboardVirtualKeyState(int virtualKey, bool pressed)
    {
        InputBindings.UpdateKeyboardVirtualKey(virtualKey, pressed);
        OnPhysicalSkipInputChanged();
        _waits.WakeInputCallbackService();
        return InputBindings.KeyboardAction(virtualKey);
    }

    public int UpdatePhysicalMouseButtonState(int physicalButton, bool pressed)
    {
        InputBindings.UpdateMouseButton(physicalButton, pressed);
        OnPhysicalSkipInputChanged();
        _waits.WakeInputCallbackService();
        return InputBindings.MouseAction(physicalButton);
    }

    public int UpdateJoystickButtonState(int physicalButton, bool pressed)
    {
        InputBindings.UpdateJoystickButton(physicalButton, pressed);
        OnPhysicalSkipInputChanged();
        _waits.WakeInputCallbackService();
        return InputBindings.JoystickButtonActionMask(physicalButton);
    }

    public void UpdateJoystickAxisState(int axis, double value)
    {
        InputBindings.UpdateJoystickAxis(axis, value);
        _waits.WakeInputCallbackService();
    }

    /// <summary>Queue a one-shot logical action for diagnostics/tests. The native no-input callback is
    /// table index ActionCount and is selected automatically when polling returns an empty mask.</summary>
    public void QueueInputCallback(int index)
    {
        if ((uint)index >= 32) return;
        if (index == InputBindings.ActionCount)
        {
            _waits.WakeInputCallbackService();
            return;
        }
        _scene.QueuedInputCallbackMask |= 1 << index;
        _waits.WakeInputCallbackService();
    }

    // The engine thread owns the interpreter's state (engine layering spec §5.3): the front end's input reaches it
    // routed (InputAndWaits.Events.cs), so these are plain reads and writes.
    private static void UpdateMaskBit(ref int field, int bit, bool set) => field = set ? field | bit : field & ~bit;

    private static int Take(ref int field)
    {
        int value = field;
        field = 0;
        return value;
    }

    private int CurrentReadMessageIndex()
    {
        if (_cur.ReadMessageOffset < 0) return -1;
        for (int i = 0; i < _cur.Script.ReadMessageOffsets.Count; i++)
            if (_cur.Script.ReadMessageOffsets[i] == _cur.ReadMessageOffset) return i;
        return -1;
    }

    /// <summary>The read-skip block that ops 0x6e, 0x71 and 0x72 share and the menu bar's read-skip toggle repeats
    /// (SkipAndAuto.ApplyReadTextSkip): message:ReadTextSkip and whether ReadTextDB holds the current message.</summary>
    private void ApplyReadTextSkip()
    {
        bool readTextSkip = _saves.Profile.ReadMessageSkipEnabled;
        _skip.ApplyReadTextSkip(readTextSkip, readTextSkip
            && _saves.Profile.ReadText.IsMessageRead(_cur.Script.PackedId, CurrentReadMessageIndex()));
    }

    /// <summary>The entry of op_0x72_handler, which also runs when a hotspot callback's ret re-runs the waiting op: the
    /// read-skip block, then the skip native's text-wait entry (SkipAndAuto.EnterTextWait). Last, it polls the input
    /// latch and discards it.</summary>
    private void EnterTextWait()
    {
        ApplyReadTextSkip();
        _skip.EnterTextWait(InputBindings);
        DiscardHeldTriggersUnlessFastForward();
    }

    /// <summary>A physical input changed: the skip native decides whether action 6 starts or ends fast-forward.</summary>
    private void OnPhysicalSkipInputChanged()
        => _skip.PhysicalInputChanged(InputBindings, _advTextWaitPolling, _text.IsRevealing);

    private static long Gi(IReadOnlyDictionary<int, long> d, int k) => d.TryGetValue(k, out var v) ? v : 0;
    private int GlobalPointer(int index) => _memory.IntegerPointer(index);
    private int GlobalStringPointer(int index) => _memory.StringPointer(index);
    private long ReadGlobal(int k) => _memory.Integer(k);
    private static string Gs(Dictionary<int, string> d, int k) => d.TryGetValue(k, out var v) ? v : "";
    // Ops 0x53/0x54 are native CDQ+IDIV: truncate toward zero, remainder takes the dividend's sign.
    private static long NativeDiv(long a, long b) => b == 0 ? 0 : a / b;
    private static long NativeMod(long a, long b) => b == 0 ? 0 : a % b;

    private static bool IsStr(Operand o) => o.Type is T_STR or T_GSTR or T_GSTRPTR or T_LSTR or T_LSTRPTR;
    private static bool SameOperand(Operand a, Operand b) => a.Type == b.Type && a.Value == b.Value;

    private const int CommentOpcode = 0x1a7;

    private static bool IsAdvLabeledYield(Script script, Instruction ins)
        => ins.Opcode == 0x140 && ins.Args.Count >= 4
           && ins.Args[1].Type == T_STR && ins.Args[2].Type == T_STR
           && script.GetString((int)ins.Args[1].Value) == "LABEL"
           && script.GetString((int)ins.Args[2].Value) == "J";

    /// <summary>The ADV labeled yield's terminal is the scene loop head: the nearest <c>call</c> followed by
    /// <c>exit</c> before the yield. The script's own terminal immediate (the <c>mov</c> after the yield, compared
    /// with the output) must name that instruction. A stale immediate, left by code that moved without relocating it,
    /// is reported in <paramref name="problem"/>, never echoed back as if the service had returned it. Comments
    /// (op 0x1a7, which the native handler skips) may sit anywhere in the pattern, and the immediate may name a run of
    /// them leading into the head, as a patch's notes do (Open Maid Script spec §10).</summary>
    private bool TryGetAdvYieldTerminal(int pc, Operand output, out long terminal, out string? problem)
    {
        terminal = 0;
        problem = null;
        var instructions = _cur.Script.Instructions;
        bool IsComment(int k) => instructions[k].Opcode == CommentOpcode;
        int Next(int k) { do k++; while (k < instructions.Count && IsComment(k)); return k; }
        int Previous(int k) { do k--; while (k >= 0 && IsComment(k)); return k; }
        int setAt = Next(pc), compareAt = setAt < instructions.Count ? Next(setAt) : setAt;
        if (compareAt >= instructions.Count) return false;
        var setTerminal = instructions[setAt];
        var compare = instructions[compareAt];
        if (_t.Label(setTerminal.Opcode) != "mov" || setTerminal.Args.Count < 2
            || setTerminal.Args[1].Type != T_IMM
            || _t.Label(compare.Opcode) != "eq" || compare.Args.Count < 3)
            return false;

        var terminalOperand = setTerminal.Args[0];
        bool comparesTerminalToOutput =
            (SameOperand(compare.Args[1], terminalOperand) && SameOperand(compare.Args[2], output))
            || (SameOperand(compare.Args[2], terminalOperand) && SameOperand(compare.Args[1], output));
        if (!comparesTerminalToOutput) return false;

        int head = -1;
        for (int k = Previous(pc); k > 0 && head < 0; k = Previous(k))
            if (_t.Label(instructions[k].Opcode) == "exit" && Previous(k) is int call and >= 0
                && _t.Label(instructions[call].Opcode) == "call")
                head = call;
        long immediate = Read(setTerminal.Args[1]);
        bool namesHead = head >= 0 && immediate >= 0 && immediate <= int.MaxValue
                         && _cur.Script.IndexByOffset.TryGetValue((int)immediate, out int named) && named <= head
                         && Enumerable.Range(named, head - named).All(IsComment);
        if (!namesHead)
        {
            problem = head < 0
                ? $"coroutine-yield-terminal@0x{instructions[pc].Offset:x}: no scene loop head precedes the yield"
                : $"coroutine-yield-terminal@0x{instructions[pc].Offset:x}: terminal 0x{immediate:x} is not the scene loop head 0x{instructions[head].Offset:x}";
            return false;
        }
        terminal = immediate;   // names the head, or comments that lead into it
        return true;
    }

    /// <summary>An operand's integer value, as native's integer accessor (vm_operand_fetch) reads it: a float form is
    /// truncated toward zero (<see cref="NativeTruncate"/>), and a string is parsed by <c>atol</c>
    /// (<see cref="NativeAtol"/>). The types that accessor throws on (string references, float references, and those
    /// the port has no cell for) halt the VM, as they end native's script with an error (docs/engine-re.md, "VM value,
    /// array and call semantics").</summary>
    private long Read(Operand op) => op.Type switch
    {
        T_IMM => ScriptMemory.IntegerCell(op.Value),   // an int32, as the loader keeps its bits zero-extended
        T_FLOAT => NativeTruncate(BitConverter.Int32BitsToSingle(unchecked((int)op.Value))),
        T_STR or T_GSTR or T_LSTR => NativeAtol(ReadStr(op)),
        T_GINT => ReadGlobal((int)op.Value),
        T_GFLOAT or T_LFLOAT => NativeTruncate(BitConverter.Int32BitsToSingle(unchecked((int)FloatBits(op)))),
        T_GPTR => _memory.Integer(GlobalPointer((int)op.Value)),
        T_LINT => Gi(_cur.Locals.I, (int)op.Value),
        T_LPTR => ReadIntCell(Ga(_cur.Locals.P, (int)op.Value)),
        _ => throw RejectedOperand(op, "read"),
    };

    /// <summary>Stores an integer as native's integer accessor (vm_operand_write) does: into an integer cell or what an
    /// integer reference names, and as <c>(float)value</c> into a float cell. Native throws on any other destination
    /// (an immediate, a string, a local float reference), and so does this.</summary>
    private void Write(Operand op, long val)
    {
        switch (op.Type)
        {
            case T_GINT: _memory.SetInteger((int)op.Value, val); break;
            case T_GFLOAT: _memory.SetFloat((int)op.Value, NativeFloatBits(unchecked((int)val))); break;
            case T_GPTR: _memory.SetInteger(GlobalPointer((int)op.Value), val); break;
            case T_LINT: _cur.Locals.SetInteger((int)op.Value, val); break;
            case T_LFLOAT: _cur.Locals.F[(int)op.Value] = NativeFloatBits(unchecked((int)val)); break;
            case T_LPTR: WriteIntCell(Ga(_cur.Locals.P, (int)op.Value), val); break;
            default: throw RejectedOperand(op, "write");
        }
    }

    /// <summary>An operand's value as the float accessor (vm_operand_fetch_float) reads it: a float form as is, an
    /// integer converted, and a string parsed by <c>atof</c> (<see cref="NativeAtof"/>). A float form is a float
    /// literal, a float cell, or the cell a float reference names.</summary>
    private float ReadFloat(Operand op) => op.Type switch
    {
        T_FLOAT or T_GFLOAT or T_LFLOAT or T_LFLOATPTR
            => BitConverter.Int32BitsToSingle(unchecked((int)FloatBits(op))),
        T_STR or T_GSTR or T_LSTR => (float)NativeAtof(ReadStr(op)),
        _ => unchecked((int)Read(op)),
    };

    /// <summary>An operand as the float accessor leaves it on the x87 stack, before any rounding to a float: a float
    /// form widened, and an integer exact (<c>fild</c>), where <see cref="ReadFloat"/> has already rounded it. The
    /// float arithmetic opcodes compute with it.</summary>
    private double ReadFloatExact(Operand op) => op.Type switch
    {
        // Each arm is a double: a float arm beside an int one would make the switch a float and round the integer.
        T_FLOAT or T_GFLOAT or T_LFLOAT or T_LFLOATPTR or T_STR or T_GSTR or T_LSTR => (double)ReadFloat(op),
        _ => (double)unchecked((int)Read(op)),
    };

    /// <summary>Stores a float as the float accessor (vm_operand_write_float) does: its bits into a float cell or what
    /// a float reference names, and truncated toward zero into an integer cell or what an integer reference names.
    /// </summary>
    private void WriteFloat(Operand op, float value)
    {
        switch (op.Type)
        {
            case T_GFLOAT: _memory.SetFloat((int)op.Value, FloatToBits(value)); break;
            case T_LFLOAT: _cur.Locals.F[(int)op.Value] = FloatToBits(value); break;
            case T_LFLOATPTR: WriteFloatCell(FloatPointer(op), FloatToBits(value)); break;
            case T_GINT or T_GPTR or T_LINT or T_LPTR: Write(op, NativeTruncate(value)); break;
            default: throw RejectedOperand(op, "write");
        }
    }

    /// <summary>The bits a float form holds: the immediate's, the float cell's, or those of the cell a float reference
    /// names. Formatting and Lua read them as the float they are.</summary>
    private long FloatBits(Operand op) => op.Type switch
    {
        T_FLOAT => op.Value,
        T_GFLOAT => _memory.Float((int)op.Value),
        T_LFLOAT => Gi(_cur.Locals.F, (int)op.Value),
        T_LFLOATPTR => ReadFloatCell(FloatPointer(op)),
        _ => throw new ArgumentException($"operand type {op.Type} is not a float form", nameof(op)),
    };

    /// <summary>The cell a local float reference (operand type 0xd) names. AGE 4.38D keeps the cell's address there and
    /// reads and writes through it (docs/engine-re.md, "Float cells as arrays, and float references"); one never
    /// bound is a null pointer there, so the run halts.</summary>
    private VmAddress FloatPointer(Operand op)
        => _cur.Locals.FP.TryGetValue((int)op.Value, out VmAddress address)
            ? address
            : throw new VmHaltException($"float-reference-unbound:local 0x{op.Value:x}");

    /// <summary>The bits of the float cell at <paramref name="address"/>. A float reference bound to a cell of another
    /// kind reads that cell's bytes as a float in native; the port has no such view of a cell, and halts.</summary>
    private long ReadFloatCell(VmAddress address) => address.Space switch
    {
        VmAddressSpace.LocalFloat => Gi(_cur.Locals.F, address.Address),
        VmAddressSpace.GlobalFloat => _memory.Float(address.Address),
        _ => throw new VmHaltException($"float-reference-names:{address.Space} 0x{address.Address:x}"),
    };

    private void WriteFloatCell(VmAddress address, long bits)
    {
        switch (address.Space)
        {
            case VmAddressSpace.LocalFloat: _cur.Locals.F[address.Address] = bits; break;
            case VmAddressSpace.GlobalFloat: _memory.SetFloat(address.Address, bits); break;
            default: throw new VmHaltException($"float-reference-names:{address.Space} 0x{address.Address:x}");
        }
    }

    /// <summary>Operand <paramref name="index"/> of <paramref name="ins"/> as its handler reads a number: through the
    /// float accessor where this ABI's contract says <c>read-float</c> (<see cref="OpcodeDefinition.FloatOperands"/>),
    /// else through the integer one. SYS4433's transform family reads its vectors this way, SYS4422's as integers.</summary>
    private double ReadNumber(Instruction ins, int index)
        => IsFloatOperand(ins, index) ? ReadFloat(ins.Args[index]) : Read(ins.Args[index]);

    /// <summary>Stores a number into operand <paramref name="index"/> as its handler does: through the float accessor
    /// where this ABI's contract says <c>write-float</c>, else truncated through the integer one.</summary>
    private void WriteNumber(Instruction ins, int index, double value)
    {
        if (IsFloatOperand(ins, index)) WriteFloat(ins.Args[index], (float)value);
        else Write(ins.Args[index], NativeTruncate(value));
    }

    private bool IsFloatOperand(Instruction ins, int index)
        => (uint)ins.Opcode < (uint)_floatOperands.Length && (_floatOperands[ins.Opcode] & (1UL << index)) != 0;

    private static ulong[] FloatOperandMasks(OpcodeTable table)
    {
        int size = 0;
        foreach (OpcodeDefinition definition in table.Entries)
            if (definition.FloatOperands.Count > 0) size = Math.Max(size, definition.Opcode + 1);
        var masks = new ulong[size];
        foreach (OpcodeDefinition definition in table.Entries)
            foreach (int operand in definition.FloatOperands)
                if (operand is >= 0 and < 64) masks[definition.Opcode] |= 1UL << operand;
        return masks;
    }

    /// <summary>Native's float-to-integer conversion (<c>__ftol2_sse</c>, cvttsd2si): toward zero, and the integer
    /// indefinite 0x80000000 for NaN or a value outside int32.</summary>
    internal static int NativeTruncate(double value)
        => double.IsNaN(value) || value <= int.MinValue - 1.0 || value >= int.MaxValue + 1.0
            ? int.MinValue
            : (int)value;

    private static long FloatToBits(float value) => BitConverter.SingleToInt32Bits(value);

    /// <summary>A float cell's bits for <c>(float)value</c>, as vm_operand_write stores an integer there.</summary>
    private static long NativeFloatBits(int value) => FloatToBits(value);

    /// <summary>The CRT's <c>atol</c> as AGE links it (VS2005: <c>strtol(text, NULL, 10)</c>): leading white space, a
    /// sign, then ASCII digits, clamped to int32. Anything else reads 0.</summary>
    internal static long NativeAtol(string text)
    {
        int i = SkipCWhiteSpace(text, 0);
        bool negative = false;
        if (i < text.Length && text[i] is '+' or '-') negative = text[i++] == '-';
        long magnitude = 0;
        for (; i < text.Length && text[i] is >= '0' and <= '9'; i++)
            magnitude = Math.Min(magnitude * 10 + (text[i] - '0'), (long)int.MaxValue + 1);
        return Math.Clamp(negative ? -magnitude : magnitude, int.MinValue, int.MaxValue);
    }

    /// <summary>The CRT's <c>atof</c>: leading white space, then the longest decimal number (a sign, digits with an
    /// optional point, an optional exponent). Anything else reads 0.</summary>
    internal static double NativeAtof(string text)
    {
        int start = SkipCWhiteSpace(text, 0);
        int i = start;
        if (i < text.Length && text[i] is '+' or '-') i++;
        int digits = 0;
        for (; i < text.Length && text[i] is >= '0' and <= '9'; i++) digits++;
        if (i < text.Length && text[i] == '.')
            for (i++; i < text.Length && text[i] is >= '0' and <= '9'; i++) digits++;
        if (digits == 0) return 0;
        int end = i;
        if (i < text.Length && text[i] is 'e' or 'E')
        {
            int exponent = i + 1;
            if (exponent < text.Length && text[exponent] is '+' or '-') exponent++;
            int exponentEnd = exponent;
            while (exponentEnd < text.Length && text[exponentEnd] is >= '0' and <= '9') exponentEnd++;
            if (exponentEnd > exponent) end = exponentEnd;
        }
        return double.Parse(text.AsSpan(start, end - start), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The C locale's white space (isspace): space, tab, line feed, vertical tab, form feed, carriage return.</summary>
    private static int SkipCWhiteSpace(string text, int i)
    {
        while (i < text.Length && (text[i] == ' ' || text[i] is >= '\t' and <= '\r')) i++;
        return i;
    }

    private static VmHaltException RejectedOperand(Operand op, string access)
        => new($"operand-type-rejected:{access}:type 0x{op.Type:x}");

    /// <summary>A string operand as native's read-string accessors give it (vm_operand_read_string_buffer,
    /// vm_operand_read_std_string): text as stored, and a number formatted (itoa, floats "%lf") and then
    /// widened by ascii_to_fullwidth_cp932_inplace, so 12 reads as "１２". Op 0x1b2's diagnostic accessor
    /// keeps ASCII (FormatDiagnosticOperand).</summary>
    private string ReadStr(Operand op) => op.Type switch
    {
        T_STR => _cur.Script.GetString((int)op.Value),
        T_GSTR => _memory.String((int)op.Value),
        T_GSTRPTR => _memory.String(GlobalStringPointer((int)op.Value)),
        T_LSTR => Gs(_cur.Locals.S, (int)op.Value),
        T_LSTRPTR => ReadStringCell(Ga(_cur.Locals.SP, (int)op.Value)),
        T_IMM or T_GINT or T_GPTR or T_LINT or T_LPTR => AsciiToFullwidth(
            unchecked((int)Read(op)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        // vm_operand_format_as_string formats what a float reference names; the two other string accessors throw
        // on one, which the port does not tell apart.
        T_FLOAT or T_GFLOAT or T_LFLOAT or T_LFLOATPTR => AsciiToFullwidth(
            BitConverter.Int32BitsToSingle(unchecked((int)FloatBits(op)))
                .ToString("F6", System.Globalization.CultureInfo.InvariantCulture)),
        _ => "",
    };

    /// <summary>Native ascii_to_fullwidth_cp932_inplace: digits and letters become their full-width forms,
    /// '-' becomes '－', and anything else '？' (so a float's '.' too).</summary>
    private static string AsciiToFullwidth(string text)
        => string.Concat(text.Select(c => c switch
        {
            >= '0' and <= '9' => (char)('０' + c - '0'),
            '-' => '－',
            >= 'A' and <= 'Z' => (char)('Ａ' + c - 'A'),
            >= 'a' and <= 'z' => (char)('ａ' + c - 'a'),
            _ => '？',
        }));

    private void WriteStr(Operand op, string val)
    {
        switch (op.Type)
        {
            case T_GSTR: _memory.SetString((int)op.Value, val); break;
            case T_GSTRPTR: _memory.SetString(GlobalStringPointer((int)op.Value), val); break;
            case T_LSTR: _cur.Locals.S[(int)op.Value] = val; break;
            case T_LSTRPTR: WriteStringCell(Ga(_cur.Locals.SP, (int)op.Value), val); break;
        }
    }

    private int NativeStringByteLength(string value) => _memory.ByteLength(value);

    private string FormatIntegerForSurface(ref int x, int value, int fieldWidth, int flags)
    {
        int width = Math.Max(0, fieldWidth);
        var field = new char[Math.Max(1, width)];
        bool signed = value < 0
            || (value > 0 && (flags & 0x08) != 0)
            || (value == 0 && (flags & 0x30) != 0);
        char sign = value < 0 || (value == 0 && (flags & 0x10) == 0 && (flags & 0x20) != 0) ? '-' : '+';
        int digitSlots = width - (signed ? 1 : 0);
        int first = 0;
        long magnitude = value < 0 ? -(long)value : value;

        for (int pos = digitSlots - 1; pos >= 0; pos--)
        {
            if (pos == digitSlots - 1 || magnitude != 0 || (flags & 0x01) != 0)
            {
                field[pos + (signed ? 1 : 0)] = (char)('0' + magnitude % 10);
                first = pos;
            }
            magnitude /= 10;
        }

        if (signed)
            field[first] = digitSlots < 0 ? '#' : sign;

        int length = first;
        while (length < field.Length && field[length] != '\0') length++;
        string text = new(field, first, length - first);

        // Native uses the primary LOGFONT cell height as a fixed-width advance, halved for ASCII.
        // Odd heights (and the engine's 32/33-pixel special cases) are rounded down on font rebuild.
        int fontSize = _text.Style.PrimaryFontSize > 0 ? _text.Style.PrimaryFontSize : 24;
        int cellAdvance = fontSize == 33 ? 31
            : fontSize == 32 || (fontSize & 1) != 0 ? fontSize - 1
            : fontSize;
        if ((flags & 0x04) == 0)
        {
            int divisor = (flags & 0x10000) != 0 ? 2 : 1;
            if ((flags & 0x02) != 0) divisor *= 2;
            x += (cellAdvance * first) / divisor;
        }

        if ((flags & 0x10000) == 0)
            text = AsciiToFullwidth(text);
        return text;
    }

    private static VmAddress Ga(Dictionary<int, VmAddress> d, int k)
        => d.TryGetValue(k, out var value) ? value : VmAddress.Global(0);

    private long ReadIntCell(VmAddress address) => address.Space switch
    {
        VmAddressSpace.LocalInteger => Gi(_cur.Locals.I, address.Address),
        VmAddressSpace.LocalFloat => Gi(_cur.Locals.F, address.Address),
        VmAddressSpace.GlobalFloat => _memory.Float(address.Address),
        _ => _memory.Integer(address.Address),
    };

    private void WriteIntCell(VmAddress address, long value)
    {
        switch (address.Space)
        {
            case VmAddressSpace.LocalInteger: _cur.Locals.SetInteger(address.Address, value); break;
            case VmAddressSpace.LocalFloat: _cur.Locals.F[address.Address] = value; break;
            case VmAddressSpace.GlobalFloat: _memory.SetFloat(address.Address, value); break;
            default: _memory.SetInteger(address.Address, value); break;
        }
    }

    private string ReadStringCell(VmAddress address)
        => address.Space == VmAddressSpace.LocalString
            ? Gs(_cur.Locals.S, address.Address)
            : _memory.String(address.Address);

    private void WriteStringCell(VmAddress address, string value)
    {
        if (address.Space == VmAddressSpace.LocalString) _cur.Locals.S[address.Address] = value;
        else _memory.SetString(address.Address, value);
    }

    /// <summary>An operand's cell address, as vm_operand_resolve_address gives it: a cell's own, or the one a
    /// reference holds. AGE 4.38D resolves float cells and float references too; 4.24J and 4.33G throw on them, and
    /// the port resolves them for every build (docs/engine-re.md, "Float cells as arrays, and float references").
    /// </summary>
    private VmAddress BaseAddr(Operand op) => op.Type switch
    {
        T_LINT => VmAddress.LocalInteger((int)op.Value),
        T_LFLOAT => VmAddress.LocalFloat((int)op.Value),
        T_LSTR => VmAddress.LocalString((int)op.Value),
        T_GFLOAT => VmAddress.GlobalFloat((int)op.Value),
        T_GPTR => VmAddress.Global(GlobalPointer((int)op.Value)),
        T_GSTRPTR => VmAddress.Global(GlobalStringPointer((int)op.Value)),
        T_LPTR => Ga(_cur.Locals.P, (int)op.Value),
        T_LFLOATPTR => Ga(_cur.Locals.FP, (int)op.Value),
        T_LSTRPTR => Ga(_cur.Locals.SP, (int)op.Value),
        _ => VmAddress.Global((int)op.Value),
    };

    private bool TryResolveSharedProfileCell(Operand operand, bool isString, out int address)
    {
        bool acceptedType = isString
            ? operand.Type is T_GSTR or T_GSTRPTR or T_LSTRPTR
            : operand.Type is T_GINT or T_GPTR or T_LPTR;
        VmAddress resolved = acceptedType ? BaseAddr(operand) : default;
        if (!acceptedType || resolved.Space != VmAddressSpace.Global || resolved.Address < 0)
        {
            address = 0;
            return false;
        }
        address = resolved.Address;
        return true;
    }

    private bool TryStoreAddress(Operand destination, VmAddress address)
    {
        switch (destination.Type)
        {
            case T_LPTR: _cur.Locals.P[(int)destination.Value] = address; return true;
            case T_LFLOATPTR: _cur.Locals.FP[(int)destination.Value] = address; return true;
            case T_LSTRPTR: _cur.Locals.SP[(int)destination.Value] = address; return true;
            case T_GPTR: _memory.SetPointer((int)destination.Value, address.Address); return true;
            case T_GSTRPTR: _memory.SetStringPointer((int)destination.Value, address.Address); return true;
            default: return false;
        }
    }

    private void LookupStore(Operand dst, VmAddress addr, int pc)
    {
        if (TryStoreAddress(dst, addr)) return;
        if (IsStr(dst))
        {
            WriteStr(dst, ReadStringCell(addr));
            RecordStringCopy(dst, pc, addr);
        }
        else Write(dst, ReadIntCell(addr));
    }

    private void WriteConsecutive(Operand destination, int index, long value)
    {
        int address = checked((int)destination.Value + index);
        switch (destination.Type)
        {
            case T_GINT: _memory.SetInteger(address, value); break;
            case T_GFLOAT: _memory.SetFloat(address, value); break;
            case T_LINT: _cur.Locals.SetInteger(address, value); break;
            case T_LFLOAT: _cur.Locals.F[address] = value; break;
            case T_GPTR:
                _memory.SetInteger(checked(GlobalPointer((int)destination.Value) + index), value);
                break;
            case T_LPTR: WriteIntCell(Ga(_cur.Locals.P, (int)destination.Value).Offset(index), value); break;
            case T_LFLOATPTR: WriteFloatCell(FloatPointer(destination).Offset(index), value); break;
        }
    }

    private long ReadAddressedCell(Operand operand, int offset)
    {
        return operand.Type switch
        {
            T_LINT => Gi(_cur.Locals.I, checked((int)operand.Value + offset)),
            T_LFLOAT => Gi(_cur.Locals.F, checked((int)operand.Value + offset)),
            T_GINT => ReadGlobal(checked((int)operand.Value + offset)),
            T_GFLOAT => _memory.Float(checked((int)operand.Value + offset)),
            T_LPTR => ReadIntCell(Ga(_cur.Locals.P, (int)operand.Value).Offset(offset)),
            T_LFLOATPTR => ReadFloatCell(FloatPointer(operand).Offset(offset)),
            T_GPTR => _memory.Integer(checked(GlobalPointer((int)operand.Value) + offset)),
            _ => _memory.Integer(checked((int)operand.Value + offset)),
        };
    }

    private (VmAddressSpace Space, int Address) AddressedCellIdentity(Operand operand, int offset)
        => operand.Type switch
        {
            T_LINT => (VmAddressSpace.LocalInteger, checked((int)operand.Value + offset)),
            T_LFLOAT => (VmAddressSpace.LocalFloat, checked((int)operand.Value + offset)),
            T_GFLOAT => (VmAddressSpace.GlobalFloat, checked((int)operand.Value + offset)),
            T_LPTR => PointerIdentity(Ga(_cur.Locals.P, (int)operand.Value).Offset(offset)),
            T_LFLOATPTR => PointerIdentity(Ga(_cur.Locals.FP, (int)operand.Value).Offset(offset)),
            T_GPTR => (VmAddressSpace.Global,
                       checked(GlobalPointer((int)operand.Value) + offset)),
            _ => (VmAddressSpace.Global, checked((int)operand.Value + offset)),
        };

    private static (VmAddressSpace Space, int Address) PointerIdentity(VmAddress address)
        => (address.Space, address.Address);

    private string FormatDiagnosticOperand(Operand operand)
    {
        if (IsStr(operand)) return ReadStr(operand);
        if (operand.Type is T_FLOAT or T_GFLOAT or T_LFLOAT or T_LFLOATPTR)
        {
            float value = BitConverter.Int32BitsToSingle(unchecked((int)FloatBits(operand)));
            return value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        }
        return unchecked((int)Read(operand))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private DiagnosticMessage BuildDiagnosticMessage(Instruction instruction)
        => AgeBox(new AgeBoxParts(AgeBoxMessage.Script(_shell.PendingDiagnosticText), DiagnosticLocation(instruction),
                                  AgeBoxButtons.Ok));

    /// <summary>An AGE box in AGE's wording, with its parts for a front end that presents it in another language.</summary>
    private static DiagnosticMessage AgeBox(AgeBoxParts parts)
        => new(AgeBoxText.JapaneseText(AgeBoxText.CaptionKey(parts.Buttons)),
               AgeBoxText.Compose(parts, AgeBoxText.JapaneseText), parts);

    private AgeBoxLocation DiagnosticLocation(Instruction instruction)
    {
        // Himegari's release AGE initializes both optional debug metadata tables to null and has no
        // writer for either one. The native formatter consequently emits -1 and "-" here.
        const int sourceLine = -1;
        const string commandName = "-";
        int nativeDepth = Math.Max(0, _depth - 1);
        return new AgeBoxLocation(_cur.Script.Name, instruction.Offset, sourceLine, commandName, instruction.Opcode,
                                  nativeDepth);
    }

    /// <summary>
    /// A native interpreter error (a C++ throw with flags 0x10007) raised while the current frame waits: AGE shows
    /// エラーが発生しました with the current FILE/ADDRESS/DEPTH and acts on the answer (engine_main_tick's policy
    /// at 0x412689, driven 2026-11-04). 中止 ends the run, as native's forced close ends the game. 再試行 restarts
    /// with nothing re-executed. 無視 advances the frame by its last decoded instruction, the wait, so the wait's
    /// successor is skipped by that many dwords. While a frame waits, native's frame pc is already past the waiting
    /// instruction, so the coordinate is the next instruction's.
    /// </summary>
    private ScriptErrorChoice PresentWaitScriptError(AgeBoxMessage error)
    {
        ScriptErrorChoice choice = ShowScriptError(error, Math.Min(_cur.Pc + 1, _cur.Script.Instructions.Count - 1));
        if (choice == ScriptErrorChoice.Ignore)
            _cur.IgnoredDwords += InstructionDwords(_cur.Script, _cur.Pc);
        return choice;
    }

    /// <summary>
    /// A native interpreter error raised while the instruction at <paramref name="pc"/> executes (or, for the timed
    /// callback service, while the frame sits on it). Native's frame pc is still on that instruction, so it is the
    /// coordinate. Returns the instruction index to continue at. 再試行 restarts with the pc unchanged, so the
    /// instruction runs again. 無視 (and はい, for flags 0x10005's box) adds the frame's last decoded instruction length,
    /// which for these handlers is the instruction's own (op 0xcd sets it on entry; op 0xd5 sets 0, so its error
    /// repeats). 中止 (and いいえ) ends the run.
    /// </summary>
    private int RaiseInstructionScriptError(AgeBoxMessage error, int pc, bool ignoreSkipsInstruction = true,
                                            AgeBoxButtons buttons = AgeBoxButtons.AbortRetryIgnore)
        => ShowScriptError(error, pc, buttons) switch
        {
            ScriptErrorChoice.Retry => pc,
            ScriptErrorChoice.Ignore => ignoreSkipsInstruction ? pc + 1 : pc,
            _ => throw new ScriptErrorException(),
        };

    /// <summary>A script error native throws with flags 0x10001 while the instruction at <paramref name="pc"/> runs:
    /// the fatal box (age_show_message_box mode 1, 続行不可能なエラーが発生しました), whose OK ends the program (the
    /// main loop's policy at 0x412689 exits for any answer but 2 and 4).</summary>
    private void FatalScriptError(AgeBoxMessage error, int pc)
    {
        ShowScriptError(error, pc, AgeBoxButtons.Fatal);
        throw new ScriptErrorException();
    }

    /// <summary>"Depth が不正です <running> != <owner>": a callback registered by one script is dispatched while
    /// another runs (native throws flags 0x10007 from each guard).</summary>
    private AgeBoxMessage DepthError(Script owner)
        => AgeBoxMessage.Keyed(AgeBoxText.DepthMismatch, _cur.Script.Name, owner.Name);

    /// <summary>
    /// Native callback guards compare the running frame's packed resource id with the registrant's, so two
    /// activations of one script are the same owner. Synthesized scripts carry no packed id; their names decide.
    /// </summary>
    private static bool IsSameScript(Script a, Script b)
        => ReferenceEquals(a, b)
           || (a.PackedId != 0 && b.PackedId != 0
               ? a.PackedId == b.PackedId
               : string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

    // age_show_message_box@0x409370 mode 7 (the errors the interpreter throws with flags 0x10007): body "%s%s"
    // followed by the prompt (AgeBoxText.Prompt…), MessageBoxA style 0x112 (abort/retry/ignore); mode 5 (flags
    // 0x10005): the same body with a yes/no prompt, style 0x14; mode 3 (the script loader's): retry/cancel, style 0x15;
    // mode 1 (flags 0x10001): the fatal box, OK only, style 0x10. An answer the box has no button for ends the run.
    private ScriptErrorChoice ShowScriptError(AgeBoxMessage error, int coordinateIndex,
                                              AgeBoxButtons buttons = AgeBoxButtons.AbortRetryIgnore)
    {
        string text = error.Text(AgeBoxText.JapaneseText);
        DiagnosticMessage message = AgeBox(new AgeBoxParts(error,
            DiagnosticLocation(_cur.Script.Instructions[coordinateIndex]), buttons));
        _instructionRaisedScriptError = true;   // its after hooks do not fire (Lua scripting spec §7.1)
        RefuseScriptErrorInModCall(text);
        ScriptErrorChoice choice = _waits.ShowScriptError(message);
        if (choice is ScriptErrorChoice.Retry or ScriptErrorChoice.Ignore
            && AgeBoxText.ButtonChoices(buttons).Contains(choice))
            return choice;
        ScriptError = message;
        HaltReason ??= "script-error: " + text;
        return ScriptErrorChoice.Abort;
    }

    /// <summary>An instruction's encoded length in dwords: the opcode and two per operand.</summary>
    private static int InstructionDwords(Script script, int index)
    {
        IReadOnlyList<Instruction> code = script.Instructions;
        if ((uint)(index + 1) < (uint)code.Count) return code[index + 1].Offset - code[index].Offset;
        return 1 + 2 * code[index].Args.Count;
    }

    /// <summary>Apply a pending 無視 skip once the waiting instruction completes: native resumes that many dwords
    /// past the successor. An offset inside or past the code has no port equivalent (native decodes whatever is
    /// there), so the run halts.</summary>
    private StepResult ApplyIgnoredDwords(ExecFrame frame, int pc, StepResult step)
    {
        if (!step.Continues) return step;
        int next = step.NextPc(pc);
        if (frame.IgnoredDwords == 0 || (uint)next >= (uint)frame.Script.Instructions.Count)
            return step;
        int offset = frame.Script.Instructions[next].Offset + Take(ref frame.IgnoredDwords);
        if (frame.Script.IndexByOffset.TryGetValue(offset, out int index)) return StepResult.Jump(index);
        HaltReason ??= $"script-error-ignored: {frame.Script.Name} resumed at 0x{offset:x}, outside its instructions";
        return StepResult.Halt;
    }

    private sealed class RootReloadRequestedException : Exception { }
    private sealed class NumberedRestoreRequestedException : Exception { }
    private sealed class ProcessExitRequestedException : Exception { }
    private sealed class ScriptErrorException : Exception { }

    /// <summary>Ends the run from below a frame's step loop, where returning HALT would only end a nested loop (a
    /// wait's callback, a callback script, a restore). The halting op has set <see cref="HaltReason"/>, or
    /// <paramref name="reason"/> gives it; <see cref="Run"/> records the halt once.</summary>
    private sealed class VmHaltException(string? reason = null) : Exception(reason)
    {
        public string? Reason { get; } = reason;
    }
    private sealed record DebugFrameReturnRequest(ExecFrame Frame, IReadOnlyDictionary<int, long> GlobalWrites);
    private sealed record PreloadedScriptSlot(long ScriptId, ExecFrame Frame);
    private enum FrameOutcome { Returned, DebugReturned, RootReload, ExitRequested, Halted, RanOff }

    public void Run(int entryOffset = 0)
    {
        Script root = _s;
        int rootEntry = root.IndexByOffset.TryGetValue(entryOffset, out var idx) ? idx : 0;
        FrameCause cause = FrameCause.TopScene;
        NativeSavedScriptFrame? restoredRootFrame = null;
        // Lua scripting spec §5.7: a session is built between frames, before the root script is entered at the start of
        // the run and after a return to title, and before CALLBACK_LOAD after a full load.
        SessionReason? session = SessionReason.Boot;
        while (true)
        {
            FrameOutcome outcome;
            bool restoreRequested = false;
            if (session is { } reason)
            {
                session = null;
                if (!StartScriptingSession(reason)) break;
            }
            try
            {
                ExecFrame rootFrame = restoredRootFrame == null
                    ? new ExecFrame(root, rootEntry)
                    : CreateRestoredFrame(root, restoredRootFrame);
                restoredRootFrame = null;
                outcome = RunFrame(rootFrame, cause);
            }
            catch (ScriptErrorException)
            {
                break;
            }
            catch (VmHaltException halt)
            {
                HaltReason ??= halt.Reason ?? "halt";
                break;
            }
            catch (NumberedRestoreRequestedException)
            {
                if (_saves.RestoringFrames == null) throw;
                restoreRequested = true;
                outcome = FrameOutcome.Halted;
            }
            if (restoreRequested)
            {
                // Outside the catch block, so a failure while restoring (CALLBACK_LOAD's script error, a saved script
                // that is no longer installed) ends the run as a halt like any other.
                try
                {
                    // Native also arms the yield from the engine (adv_input_dispatch_callbacks, adv_input_service_poll
                    // and engine_main_tick set the resume and its owner to the running frame), which the port does not
                    // model. The port's restored scenes take op 0x7c's cold path, so a yield saved before the load must
                    // not survive it.
                    ResetCoroutineYield();
                    if (!StartScriptingSession(SessionReason.Load)) break;
                    Script? callback = _provider?.GetByName("CALLBACK_LOAD.BIN");
                    if (callback != null)
                    {
                        int callbackEntry = callback.IndexByOffset.TryGetValue(0, out int ci) ? ci : 0;
                        FrameOutcome callbackOutcome = RunFrame(
                            new ExecFrame(callback, callbackEntry), FrameCause.SaveRestore, callback.PackedId);
                        if (callbackOutcome == FrameOutcome.ExitRequested) HaltReason ??= "exit-request";
                        if (callbackOutcome is FrameOutcome.Halted or FrameOutcome.ExitRequested) break;
                        if (callbackOutcome == FrameOutcome.RootReload)
                        {
                            _saves.EndRestore();
                            Script? reloadedRoot = ReloadRootOrNull();
                            if (reloadedRoot == null) break;
                            root = reloadedRoot;
                            rootEntry = root.IndexByOffset.TryGetValue(0, out int lri) ? lri : 0;
                            cause = FrameCause.RootReload;
                            session = SessionReason.Title;
                            continue;
                        }
                    }
                    IReadOnlyList<NativeSavedScriptFrame> savedFrames = _saves.RestoringFrames!;
                    root = ResolveSavedScript(savedFrames[0]);
                    rootEntry = 0;
                    _restoreFrameIndex = 0;
                    restoredRootFrame = savedFrames[0];
                    cause = FrameCause.SaveRestore;
                    continue;
                }
                catch (ScriptErrorException)
                {
                    break;
                }
                catch (VmHaltException halt)
                {
                    HaltReason ??= halt.Reason ?? "halt";
                    break;
                }
            }
            if (outcome == FrameOutcome.RootReload)
            {
                // Native 0x9 performs the scene reset before attempting the resource-0 load. Keep
                // that ordering even when a diagnostic provider cannot resolve the root script.
                var reloaded = ReloadRootOrNull();
                if (reloaded == null) break;
                root = reloaded;
                rootEntry = root.IndexByOffset.TryGetValue(0, out int ri) ? ri : 0;
                cause = FrameCause.RootReload;
                session = SessionReason.Title;
                continue;
            }
            if (outcome == FrameOutcome.RanOff) HaltReason ??= "pc-out-of-range";
            else if (outcome is FrameOutcome.Returned or FrameOutcome.DebugReturned) HaltReason ??= "exit";
            else if (outcome == FrameOutcome.ExitRequested) HaltReason ??= "exit-request";
            // Halted: HaltReason already set by the halting op.
            break;
        }
        _sink.Emit(TraceEvent.Halt(HaltReason ?? "unknown", Steps));
    }

    private Script? ReloadRootOrNull()
    {
        ResetSceneContextForRootReload();
        var reloaded = _provider?.GetById(0);
        if (reloaded == null) HaltReason ??= "root-reload-unresolved:0x0";
        return reloaded;
    }

    private void ResetSceneContextForRootReload()
    {
        _stopAndReload.ReloadTaken();
        // op_0x9_reset_scene_and_reload_root zeroes the banks (with the string stores' origins, the text native's) and
        // scene_context_init_reset constructs the stacks (ScriptMemory.ResetScene).
        _memory.ResetScene();
        Gfx.ResetSceneContext();
        _saves.ResetScene();   // op_0x9: save_container_playtime_reset after the hotspot resets
        // scene_context_init_reset@0x40b3b0 clears coroutine_runstate, the timed sequence and every callback owner
        // (-1). It keeps the 0xcc target, which no script dispatches before registering again; the port clears it
        // with its owner. The scene is no longer the run's first (SYSTEM4 skips LOGO/OP from now on), and a return to
        // title reloads the root the natural way: TITLE runs, and nothing ends the run at a scene's return.
        _scene = new();
        TakeHeldTriggers();   // scene_context_init_reset polls the input latch and discards it
        _audio.ResetScene();
        _tick.ResetScene();   // a root reload abandons every open block with its frames
        _shell.ResetScene();   // the system-menu fields, queued menu commands and callbacks; an open dialog is cancelled
        _text.ResetScene();    // the layouts, the reveal, the indicator and the string stores go; the style starts over
        _skip.ResetScene();   // Skip, Auto and read skip start over; run-state 0x08000000 is zeroed
        _movies.ResetScene();   // every surface's movie object goes; the surfaces stay
        _waits.ResetSceneContext();
        _scripting?.ResetScene();   // the Lua session ends with the scene; Run builds the next (Lua scripting spec §5.7)
        // The reset keeps every surface. op_0x9_reset_scene_and_reload_root then runs the release worker over all
        // 1,000 slots only when (CreateObject & 2) && (AutoFreeTex & 1): Kamidori, not Himegari (docs/engine-re.md,
        // "AutoFreeTex").
        if ((_o.CreateObject & 2) != 0 && (_o.AutoFreeTex & 1) != 0)
            _graphics.ReleaseSurfaceRange(0, 1000);
    }

    private FrameOutcome RunFrame(ExecFrame frame, FrameCause cause, long callId = 0)
    {
        ExecFrame? previousInteractiveFrame;
        ExecFrame? previousRawInputFrame;
        previousInteractiveFrame = _scene.InteractiveFrame;
        previousRawInputFrame = _scene.RawInputFrame;
        var prev = _cur; _cur = frame; _depth++;
        ExecFrame? previousDebugActiveFrame;
        long previousDebugActiveFrameId;
        previousDebugActiveFrame = _scene.DebugActiveFrame;
        previousDebugActiveFrameId = _scene.DebugActiveFrameId;
        _scene.DebugActiveFrame = frame;
        _scene.DebugActiveFrameId = ++_debugNextFrameId;
        _activeFrameNames.Add(frame.Script.Name);
        _activeExecutionFrames.Add(frame);
        bool hostContextEntered = false;
        try
        {
            _waits.EnterScriptContext(frame.Script.Name);
            hostContextEntered = true;
            frame.Hooks = _scripting?.HooksFor(frame.Script);   // Lua scripting spec §7.5
            _sink.Emit(TraceEvent.FrameEnter(frame.Script.Name, _depth, cause, callId));
            var outcome = FrameOutcome.RanOff;
            int pc = frame.Pc;
            bool hostedScene = false;
            if (cause == FrameCause.CallScript)
            {
                if (TryReturnHostedCoordinator(frame))
                {
                    _sink.Emit(TraceEvent.FrameExit(frame.Script.Name, _depth, FrameOutcome.DebugReturned.ToString()));
                    return FrameOutcome.DebugReturned;
                }
                if (_scene.HostedScenePackedId == callId)
                {
                    _scene.HostedScenePackedId = null;
                    hostedScene = true;
                }
            }
            try
            {
                // Lua scripting spec §7.2: enter before the first instruction of a frame that starts at its script's
                // entry (a restored frame does), and exit for a return only, while the frame is still the running one.
                if (frame.Hooks is { Enter.Count: > 0 } entered && pc == EntryIndex(frame.Script)
                    && _scripting!.RunFrameHooks(entered, frame.Script.Name, enter: true, cause) is { } enterHalt)
                {
                    HaltReason ??= enterHalt;
                    outcome = FrameOutcome.Halted;
                }
                else
                {
                    outcome = RunInstructions(frame, pc, waitCallback: false) switch
                    {
                        LoopEnd.Returned => FrameOutcome.Returned,
                        LoopEnd.DebugReturned => FrameOutcome.DebugReturned,
                        LoopEnd.RootReload => FrameOutcome.RootReload,
                        LoopEnd.Stopped or LoopEnd.ExitRequested => FrameOutcome.ExitRequested,
                        LoopEnd.Halted => FrameOutcome.Halted,
                        _ => FrameOutcome.RanOff,   // a hotspot's ret with no wait to return to runs the frame off too
                    };
                    if (outcome == FrameOutcome.Returned && frame.Hooks is { Exit.Count: > 0 } exited
                        && _scripting!.RunFrameHooks(exited, frame.Script.Name, enter: false, cause) is { } exitHalt)
                    {
                        HaltReason ??= exitHalt;
                        outcome = FrameOutcome.Halted;
                    }
                }
            }
            catch (RootReloadRequestedException) { outcome = FrameOutcome.RootReload; }
            catch (ProcessExitRequestedException) { outcome = FrameOutcome.ExitRequested; }
            _sink.Emit(TraceEvent.FrameExit(frame.Script.Name, _depth, outcome.ToString()));
            // A hosted scene's return ends the run, as a root scene's exit did: the scene is the run's unit.
            if (hostedScene && outcome is FrameOutcome.Returned or FrameOutcome.RanOff)
            {
                HaltReason ??= outcome == FrameOutcome.Returned ? "exit" : "pc-out-of-range";
                return FrameOutcome.Halted;
            }
            return outcome;
        }
        finally
        {
            if (ReferenceEquals(_scene.DebugFrameReturnRequest?.Frame, frame)) _scene.DebugFrameReturnRequest = null;
            if (ReferenceEquals(_scene.SaveResumeFrame, frame)) _scene.SaveResumeFrame = null;
            if (_activeFrameNames.Count > 0) _activeFrameNames.RemoveAt(_activeFrameNames.Count - 1);
            if (_activeExecutionFrames.Count > 0)
                _activeExecutionFrames.RemoveAt(_activeExecutionFrames.Count - 1);
            _scene.DebugActiveFrame = previousDebugActiveFrame;
            _scene.DebugActiveFrameId = previousDebugActiveFrameId;
            if (cause == FrameCause.CallScript)
                _scene.InteractiveFrame = previousInteractiveFrame?.Hotspots.Armed == true
                    ? previousInteractiveFrame : null;
            // A Lua script call leaves the hooked frame's hotspots as it found them (Lua scripting spec §10).
            else if (cause == FrameCause.ModCall) _scene.InteractiveFrame = previousInteractiveFrame;
            else if (ReferenceEquals(_scene.InteractiveFrame, frame))
                _scene.InteractiveFrame = null;
            if (ReferenceEquals(_scene.RawInputFrame, frame)) _scene.RawInputFrame = previousRawInputFrame;
            try
            {
                if (hostContextEntered) _waits.ExitScriptContext(frame.Script.Name);
            }
            finally
            {
                _cur = prev; _depth--;
            }
        }
    }

    /// <summary>The instruction index of a script's entry, offset 0, where every frame but a debug run's starts.</summary>
    private static int EntryIndex(Script script) => script.IndexByOffset.TryGetValue(0, out int index) ? index : 0;

    /// <summary>The coordinator of a <see cref="HostScene"/> run returns before its first instruction, as TITLE's
    /// developer menu returns its choice, and arms the end of the run at the scene's return.</summary>
    private bool TryReturnHostedCoordinator(ExecFrame frame)
    {
        if (_scene.HostedSceneEntry is not { } entry
            || !frame.Script.Name.Equals(entry.CoordinatorScript, StringComparison.OrdinalIgnoreCase))
            return false;
        _scene.HostedSceneEntry = null;
        // A tool's seeding, which no watch sees (Lua scripting spec §7.3).
        foreach (var (address, value) in entry.CoordinatorWrites)
            _memory.SeedInteger(address, value);
        _scene.HostedScenePackedId = entry.ScenePackedId;
        return true;
    }

    private bool TryConsumeDebugFrameReturn(ExecFrame frame)
    {
        if (_scene.DebugFrameReturnRequest is not { } pending
            || !ReferenceEquals(pending.Frame, frame)) return false;
        if (!ReferenceEquals(_scene.DebugFrameReturnRequest?.Frame, frame)) return false;
        // The debugger's writes, which no watch sees (Lua scripting spec §7.3).
        foreach (var (address, value) in _scene.DebugFrameReturnRequest.GlobalWrites)
            _memory.SeedInteger(address, value);
        _scene.DebugFrameReturnRequest = null;
        return true;
    }

    /// <summary>The interpreter's records of a numbered save (NumberedSaveRecords): each active frame up to op 0x1ad's
    /// boundary, as context_state_serialize@0x40d320 writes it.</summary>
    private NativeSavedScriptFrame[] CaptureSavedFrames()
    {
        ExecFrame[] active;
        int cutoff;
        active = _activeExecutionFrames.ToArray();
        cutoff = _scene.SaveResumeFrame == null ? active.Length - 1 : Array.IndexOf(active, _scene.SaveResumeFrame);
        if (cutoff < 0)
            throw new InvalidDataException("No active script frame is available for a numbered save.");

        var frames = new NativeSavedScriptFrame[cutoff + 1];
        for (int i = 0; i <= cutoff; i++)
        {
            ExecFrame frame = active[i];
            NativeSavedScriptFrame? restored = frame.RestoredSaveFrame;
            // Native looks each live return address up in T3 and stops at the first that is not a
            // T3 site (context_state_serialize@0x40d320); later entries are not saved.
            int[] liveReturns = frame.CallStack
                .Select(returnPc => (uint)returnPc < (uint)frame.Script.Instructions.Count
                    ? FindTableIndex(frame.Script.LocalCallOffsets,
                        frame.Script.Instructions[returnPc].Offset - 3)
                    : -1)
                .ToArray();
            int[] returns = restored?.ReturnIndices.ToArray()
                ?? liveReturns.TakeWhile(index => index >= 0).ToArray();
            bool stoppedAtUnmatched = restored?.StoppedAtUnmatchedReturn
                ?? returns.Length < liveReturns.Length;
            int resumeIndex = restored?.ResumeIndex ?? CurrentReadMessageIndex(frame);
            int callTargetIndex = i == cutoff
                ? -1
                : restored?.CallTargetIndex
                    ?? ((uint)frame.Pc >= (uint)frame.Script.Instructions.Count
                        ? -1
                        : FindTableIndex(
                            frame.Script.ScriptCallOffsets,
                            frame.Script.Instructions[frame.Pc].Offset));
            frames[i] = new NativeSavedScriptFrame(
                i - 1, frame.Script.PackedId, returns, resumeIndex, callTargetIndex)
            {
                StoppedAtUnmatchedReturn = stoppedAtUnmatched,
            };
        }

        return frames;
    }

    /// <summary>Ops 0x19f and 0x1a1 (SavesAndSettings.LoadNumbered). A failed load, or one of the data only,
    /// leaves no frame to restore.</summary>
    private bool TryLoadNumberedState(int slot, bool restoreHistory)
    {
        NumberedLoad outcome = _saves.LoadNumbered(slot, restoreHistory, _saveRecords);
        if (outcome == NumberedLoad.Failed || (outcome == NumberedLoad.Loaded && !restoreHistory))
            _restoreFrameIndex = -1;
        return outcome == NumberedLoad.Loaded;
    }

    private Script ResolveSavedScript(NativeSavedScriptFrame frame)
    {
        if (_s.PackedId == frame.ScriptId) return _s;
        Script? script = _provider?.GetById(frame.ScriptId);
        return script ?? throw new VmHaltException($"restore-unresolved-script:0x{frame.ScriptId:x}");
    }

    private static int FindRestoreRendezvous(Script script)
    {
        for (int i = 0; i < script.Instructions.Count; i++)
            if (script.Instructions[i].Opcode == 0xae) return i;
        throw new VmHaltException($"restore-no-rendezvous:{script.Name}");
    }

    private static ExecFrame CreateRestoredFrame(Script script, NativeSavedScriptFrame saved)
    {
        // Native creates each saved script context at its ordinary entrypoint. The script runs its
        // local-array/constants/resource prologue and rendezvouses at 0xae itself; jumping directly
        // to 0xae leaves those frame-local tables zeroed (FIELD then collapses its map zoom to 0%).
        _ = FindRestoreRendezvous(script);
        int entry = script.IndexByOffset.TryGetValue(0, out int index) ? index : 0;
        var frame = new ExecFrame(script, entry)
        {
            RestoredSaveFrame = saved,
        };
        if ((uint)saved.ResumeIndex < (uint)script.ReadMessageOffsets.Count)
            frame.ReadMessageOffset = script.ReadMessageOffsets[saved.ResumeIndex];
        foreach (int returnIndex in saved.ReturnIndices)
        {
            if ((uint)returnIndex >= (uint)script.LocalCallOffsets.Count) continue;
            int returnOffset = checked(script.LocalCallOffsets[returnIndex] + 3);
            if (script.IndexByOffset.TryGetValue(returnOffset, out int returnPc))
                frame.CallStack.Add(returnPc);
        }
        return frame;
    }

    private static int ResolveTableOffset(
        Script script, IReadOnlyList<int> table, int index, int fallback)
    {
        if ((uint)index >= (uint)table.Count) return fallback;
        return script.IndexByOffset.TryGetValue(table[index], out int pc) ? pc : fallback;
    }

    private static int CurrentReadMessageIndex(ExecFrame frame)
    {
        for (int i = 0; i < frame.Script.ReadMessageOffsets.Count; i++)
            if (frame.Script.ReadMessageOffsets[i] == frame.ReadMessageOffset) return i;
        return -1;
    }

    private static int FindTableIndex(IReadOnlyList<int> table, int offset)
    {
        for (int i = 0; i < table.Count; i++)
            if (table[i] == offset) return i;
        return -1;
    }

    private bool ServiceHotspotCallback()
    {
        // A wait that op 0x7c resumed polls what the yield's handlers held, as fresh input.
        RouteHeldTriggersAtTextWait();
        // adv_input_service_poll takes op-0x97 bound actions first, which Godot activates before it requests the
        // action-5 yield, so a pending request comes before the hover and click callbacks queued after it.
        if (Take(ref _advWaitCoroutineYieldRequested) != 0 && ServiceAdvWaitCoroutineYield())
            return true;
        // A native menu command's toggle and its CALLBACK_SETTING.BIN, which native's launch queue runs from the tick.
        if (ServiceWaitSettingCallback()) return true;
        int target;
        ExecFrame owner;
        if (_scene.InteractiveFrame == null || !_scene.InteractiveFrame.Hotspots.TryDequeue(out target)) return false;
        owner = _scene.InteractiveFrame;
        // Native keeps one hotspot table and records the script that registered it (+0x7494 of the table at
        // ctx+0x5248). Every hotspot dispatch from an ADV wait first checks that owner against the running frame
        // (FUN_004079b0): a script `run` from a scene whose menu bindings are still armed cannot take them, and AGE
        // stops with "Depth が不正です <running> != <owner>" (docs/engine-re.md).
        if (!ReferenceEquals(owner, _cur))
        {
            // The dequeued callback is gone either way: 再試行 and 無視 keep waiting without it, with the owner's
            // bindings still armed (natively, the next right click raises the error again).
            if (PresentWaitScriptError(DepthError(owner.Script)) == ScriptErrorChoice.Abort)
                throw new ScriptErrorException();
            owner.Hotspots.RearmAfterCallback(_pointerX, _pointerY);
            return false;
        }
        if (_cur.Script.IndexByOffset.TryGetValue(target, out int pc))
        {
            TakeHeldTriggers();   // adv_input_service_poll flushes the latch before it dispatches (0x411294)
            _cur.CallStack.Add(HotspotReturnMarker);
            RunWaitCallback(pc);
            // A malformed callback must not leave its sentinel in the page's ordinary local-call stack.
            int sentinel = _cur.CallStack.LastIndexOf(HotspotReturnMarker);
            if (sentinel >= 0) _cur.CallStack.RemoveAt(sentinel);
            // The callback's ret re-runs the waiting op 0x72. Its entry discards what the callback's effects held and
            // restarts fast-forward from persistent Skip or read-skip (a strip button's op 0x88 or 0x101 changed them).
            // Native's ret goes 3 dwords back, op 0x72's length; after op 0xfa's poll (1 dword) that lands inside the
            // instruction before it. Only a poll whose own frame registered the hotspots gets here, and Kamidori's
            // SHOWALLMAP, the one script with op 0xfa, registers none, so the port runs op 0xfa's entry again instead.
            if (_advInputPollPageless) RefreshAdvSkipInputState();
            else EnterTextWait();
        }
        RearmAfterWaitCallback();
        return true;
    }

    /// <summary>The action-5 entry of adv_input_service_poll@0x411230: the waiting frame enters its op-0x7b handler A
    /// with the wait saved as the resume. The handler ends at op 0x7c (after the hidden-window wait and handler B
    /// when it takes that path), which returns here to the parked wait. Native pushes no return pc.</summary>
    private bool ServiceAdvWaitCoroutineYield()
    {
        if (_cur.CoroutineYieldHandlerA == -1
            || !_cur.Script.IndexByOffset.ContainsKey(_cur.CoroutineYieldHandlerA))
            return false;
        // The handlers run with the wait's poll stopped (native clears run-state 0x80000000), so no hover or click
        // dispatches until the frame is back on the wait; RearmAfterWaitCallback resumes it.
        _scene.InteractiveFrame?.Hotspots.SuspendDispatch();
        RunWaitCallback(EnterCoroutineYieldHandlerA(_cur.Pc + 1, resumesTextWait: true));
        RearmAfterWaitCallback();
        return true;
    }

    /// <summary>Run a callback inside the instruction that waits (native AGE nests it the same way) until it returns
    /// to the wait. The frame is then back on that waiting instruction, which observers (saves, frame locations)
    /// read.</summary>
    private void RunWaitCallback(int pc)
    {
        int waitingPc = _cur.Pc;
        // Native clears run-state 0x80000000 on dispatch, so the wheel feeds op 0x10d while the callback (HISTORY)
        // runs; the callback's ret re-runs the wait, which sets it again.
        bool waitPolling = _advTextWaitPolling;
        _advTextWaitPolling = false;
        _waitCallbacks++;
        try
        {
            // The callback's ops run through the frame's loop (native runs them from its main tick). A halt anywhere
            // in it ends the run, which the waiting op would otherwise outlive; a stop returns to the wait, which ends.
            switch (RunInstructions(_cur, pc, waitCallback: true))
            {
                case LoopEnd.RootReload: throw new RootReloadRequestedException();
                case LoopEnd.ExitRequested: throw new ProcessExitRequestedException();
                case LoopEnd.Halted: throw new VmHaltException();
            }
        }
        finally
        {
            _waitCallbacks--;
            _advTextWaitPolling = waitPolling;
        }
        _cur.Pc = waitingPc;
    }

    /// <summary>How <see cref="RunInstructions"/> ended.</summary>
    private enum LoopEnd { Returned, HotspotReturned, DebugReturned, RootReload, Stopped, ExitRequested, Halted, RanOff }

    /// <summary>
    /// The one instruction loop (engine layering spec §5.4): a frame's body from <paramref name="pc"/>, or a wait's
    /// callback inside the waiting frame (<paramref name="waitCallback"/>), which ends at the callback's ret. Each
    /// instruction gets native's per-op services in one order: the stop and reload requests, the step limit, the
    /// boundary's services and requests (ServiceInstructionBoundary), the handler, a pending 無視 skip, the front end's
    /// modal hold and fast-forward's pace. A frame's loop also takes a debug frame return.
    /// </summary>
    private LoopEnd RunInstructions(ExecFrame frame, int pc, bool waitCallback)
    {
        _loopsEntered++;   // an instruction that runs this loop calls (VirtualMachine.OpcodeFlow.cs)
        // The flag belongs to the instruction this loop runs inside, if any: its own script error stays its own.
        bool outerRaisedScriptError = _instructionRaisedScriptError;
        try
        {
            return RunInstructionLoop(frame, pc, waitCallback);
        }
        finally
        {
            _instructionRaisedScriptError = outerRaisedScriptError;
        }
    }

    // Whether the running instruction raised a script error (ShowScriptError): an instruction that did has not
    // finished, so its after hooks do not fire (Lua scripting spec §7.1). Each loop keeps its outer instruction's.
    private bool _instructionRaisedScriptError;

    private LoopEnd RunInstructionLoop(ExecFrame frame, int pc, bool waitCallback)
    {
        while (pc >= 0 && pc < frame.Script.Instructions.Count)
        {
            if (_stopAndReload.StopRequested) return LoopEnd.Stopped;
            if (_stopAndReload.ReloadRequested) return LoopEnd.RootReload;
            if (Steps >= _o.MaxSteps)
            {
                HaltReason ??= "STEP-LIMIT";
                return LoopEnd.Halted;
            }
            Steps++;
            frame.Pc = pc;
            // Lua scripting spec §5.7, §11, §13: a data-only load's session, a reload outside any wait, and
            // ome.session.resumed after a full load's last frame, at the first boundary, before its services run anything.
            if (_scripting is { BoundaryPending: true } && _scripting.ServiceBoundary() is { } pendingHalt)
            {
                HaltReason ??= pendingHalt;
                return LoopEnd.Halted;
            }
            switch (ServiceInstructionBoundary())
            {
                case FrameOutcome.RootReload: return LoopEnd.RootReload;
                case FrameOutcome.ExitRequested: return LoopEnd.ExitRequested;
                case FrameOutcome.Halted: return LoopEnd.Halted;
            }
            if (_modCalls > 0) RefuseInstructionInModCall(frame.Script.Instructions[pc]);   // Lua scripting spec §10
            HookSite? hooked = null;
            long sessionMark = -1;
            bool skipped = false;
            int watchSlot = -1;
            if (_scripting != null)
            {
                // Lua scripting spec §7.1, §13: the console's chunks, then the instruction's before hooks, after the
                // boundary's services and before the opcode handler. A frame's table is null for a script with no hooks.
                _scripting.ServiceRequests();
                // Spec §7.3: with watches, the instruction's records start here, so its before hooks' writes are its own.
                if (_scripting.Watching) watchSlot = _scripting.BeginInstruction(frame.Script, pc);
                if ((hooked = frame.Hooks?.ByIndex[pc]) != null)
                {
                    if (_scripting.RunBefore(hooked, frame.Script.Instructions[pc], out skipped) is { } luaHalt)
                    {
                        HaltReason ??= luaHalt;
                        return LoopEnd.Halted;
                    }
                    if (hooked.HasAfter && !skipped) sessionMark = _scripting.SessionMark;
                }
            }
            Instruction instruction = frame.Script.Instructions[pc];
            StepResult step;
            // Spec §7.1: a before handler skipped it, so it runs nothing and writes nothing, and fires no after hook. The
            // link allows a skip only on an instruction that goes on to the next one.
            if (skipped) step = StepResult.Next;
            else
            {
                if (_sink.TracingSteps) _sink.Emit(TraceEvent.Step(pc, instruction, _depth));
                _instructionRaisedScriptError = false;
                step = ApplyIgnoredDwords(frame, pc, _checkOpcodeFlow ? CheckedStep(instruction, pc) : Step(instruction, pc));
                // Spec §7.1: the after hooks, once the handler has returned and the frame goes on, unless the instruction
                // raised a script error; before the front end's frame turn.
                if (sessionMark >= 0 && step.Continues && !_instructionRaisedScriptError
                    && _scripting!.RunAfter(hooked!, instruction, sessionMark) is { } afterHalt)
                {
                    HaltReason ??= afterHalt;
                    return LoopEnd.Halted;
                }
            }
            // Spec §7.3, §7.4: the watch records the instruction made, after its after hooks, unless it halted the run.
            if (watchSlot >= 0 && _scripting!.EndInstruction(watchSlot, step.Kind != StepKind.Halt) is { } watchHalt)
            {
                HaltReason ??= watchHalt;
                return LoopEnd.Halted;
            }
            _waits.FrameYield();
            _skip.PaceOp();
            switch (step.Kind)
            {
                case StepKind.FrameReturn: return LoopEnd.Returned;
                case StepKind.RootReload: return LoopEnd.RootReload;
                case StepKind.Halt: return LoopEnd.Halted;
            }
            if (!waitCallback && TryConsumeDebugFrameReturn(frame)) return LoopEnd.DebugReturned;
            if (step.Kind == StepKind.HotspotReturn) return LoopEnd.HotspotReturned;
            pc = step.NextPc(pc);
        }
        return LoopEnd.RanOff;
    }

    private void RearmAfterWaitCallback()
    {
        // History/Hide callbacks cancel the active registry, run a nested script, then republish the
        // parent frame's definitions. Nested RunFrame deliberately clears the disarmed interactive
        // pointer, so restore the still-running parent before rearming its rebuilt registry.
        if (_scene.InteractiveFrame == null && _cur.Hotspots.HasDefinitions) _scene.InteractiveFrame = _cur;
        _scene.InteractiveFrame?.Hotspots.RearmAfterCallback(_pointerX, _pointerY);
        _waits.InputCallbackCompleted();
    }

    /// <summary>The longest run of cells or entries one instruction may write from a script-controlled count. Native
    /// would fault or exhaust memory long before; the port halts instead of looping for minutes.</summary>
    internal const int MaxScriptRunLength = 1 << 20;

    /// <summary>Run one instruction. An operand that overflows a checked address or count halts the VM with the
    /// instruction's location rather than ending the VM thread with an exception.</summary>
    private StepResult Step(Instruction ins, int pc)
    {
        Script script = _cur.Script;
        try
        {
            return StepInstruction(ins, pc);
        }
        catch (OverflowException)
        {
            HaltReason ??= $"operand-overflow:{script.Name}@0x{ins.Offset:x}:op 0x{ins.Opcode:x}";
            return StepResult.Halt;
        }
    }

    /// <summary>Run the instruction's handler. An opcode with no handler halts with a diagnostic (or, in probe mode,
    /// is skipped).</summary>
    private StepResult StepInstruction(Instruction ins, int pc)
    {
        int op = ins.Opcode;
        if ((uint)op < (uint)_handlers.Length && _handlers[op] is { } handler) return handler(this, ins, pc);
        if (!_t.TryGetDefinition(op, out OpcodeDefinition definition))
        {
            HaltReason = UnsupportedOpcodeDiagnostic.NotInAbi(_t, op);
            return StepResult.Halt;
        }
        var diagnostic = UnsupportedOpcodeDiagnostic.For(_o.ProfileId, _t, _cur.Script, ins, definition);
        _sink.Emit(TraceEvent.UnsupportedOpcode(diagnostic, pc));
        if (_o.ProbeMode) return StepResult.Next;
        CompatibilityFailure = diagnostic;
        HaltReason = diagnostic.ToString();
        return StepResult.Halt;
    }

}
