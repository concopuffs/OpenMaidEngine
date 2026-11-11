using Ome.Engine.Hosting;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Text;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    private volatile TextPresenter? _presenter;

    /// <summary>
    /// The selected language's presenter (localization spec §4, §5.2), or null to draw source text: the source
    /// language, or a language with no catalog (gate 1). The VM asks it at each presentation site and passes both
    /// forms to the host; everything the VM keeps (cells, history, saves, the page map) stays source text. The shell
    /// replaces it when the language changes; the next run uses the new one.
    /// </summary>
    public TextPresenter? Presenter
    {
        get => _presenter;
        set => _presenter = value;
    }

    /// <summary>The message whose first run drew its presented sentence (localization spec §6.2), in the frame and
    /// script that run it, with the run that comes next. Its later runs, and the <c>end-text-line</c>s between them,
    /// draw nothing.</summary>
    private sealed record PresentedMessage(ExecFrame Frame, Script Script, TextMessage Message, int NextRun)
    {
        public bool Holds(ExecFrame frame, Script script)
            => ReferenceEquals(frame, Frame) && ReferenceEquals(script, Script);
    }

    private PresentedMessage? _presentedMessage;

    /// <summary>The presented form of a string the instruction at <paramref name="ins"/> draws from
    /// <paramref name="operand"/>, with the operand's origin (§5.2). Site entries bind to the running script's own
    /// offsets: the text loader keeps them only for scripts no mod patches (§6.1).</summary>
    private PresentedText? Present(string text, Instruction ins, Operand operand, bool logMiss = true)
        => _presenter?.Present(text, new TextSite(_cur.Script.Name, ins.Offset), OriginOf(operand), logMiss);

    /// <summary>
    /// What one show-text run draws (spec §6.2). A run that starts a message of several runs resolves the whole
    /// message, its runs' texts joined: on an entry, it draws the presented sentence and the message's later runs
    /// draw nothing (<paramref name="hidden"/>); on a miss, each run resolves on its own and only the message is
    /// logged. A single run resolves with its operand's origin.
    /// </summary>
    private PresentedText? PresentRun(int pc, Instruction ins, Operand operand, string text, out bool hidden)
    {
        hidden = false;
        if (_presenter is not { } presenter)
        {
            _presentedMessage = null;
            return null;
        }
        if (_presentedMessage is { } active && active.Holds(_cur, _cur.Script)
            && active.Message.Runs[active.NextRun].InstructionIndex == pc)
        {
            _presentedMessage = active.NextRun + 1 < active.Message.Runs.Count
                ? active with { NextRun = active.NextRun + 1 }
                : null;
            hidden = true;
            return null;
        }
        _presentedMessage = null;
        if (presenter.Messages?.MessagesOf(_cur.Script) is { } messages
            && messages.TryGetRun(pc, out TextMessage message, out int run) && message.Runs.Count > 1)
        {
            if (run == 0)
            {
                // Nothing executes between a message's runs, so every operand reads now as it will when its run
                // executes.
                string key = string.Concat(message.Runs.Select(part => ReadStr(part.Text)));
                if (presenter.Present(key, new TextSite(_cur.Script.Name, ins.Offset)) is { Entry: not null } sentence)
                {
                    _presentedMessage = new PresentedMessage(_cur, _cur.Script, message, 1);
                    return sentence;
                }
            }
            return Present(text, ins, operand, logMiss: false);
        }
        return Present(text, ins, operand);
    }

    /// <summary>Whether the <c>end-text-line</c> at <paramref name="pc"/> lies inside a presented message, which
    /// draws its own line breaks (§6.2).</summary>
    private bool InsidePresentedMessage(int pc)
        => _presentedMessage is { } active && active.Holds(_cur, _cur.Script)
           && pc > active.Message.Runs[active.NextRun - 1].InstructionIndex
           && pc < active.Message.Runs[active.NextRun].InstructionIndex;

    /// <summary>One show-text run (op 0x6e's string, or op 0x196's base text with its ruby). False when the
    /// runaway-line guard halted the VM.</summary>
    private bool ShowTextRun(Instruction ins, int pc, IReadOnlyList<Operand> a, Operand o, string? ruby)
    {
        int off = o.Type == T_STR ? (int)o.Value : ins.Offset;
        string text = ReadStr(o);
        // Inline strings identify one authored line, so repeated emission is a useful
        // runaway-loop guard. Dynamic string operands identify a reusable renderer site:
        // Kamidori SC2310@0x57b7 legitimately publishes every speaker name through the
        // same local-string-pointer instruction. MaxSteps remains the guard for dynamic
        // script loops.
        if (o.Type == T_STR)
        {
            _cur.EmitSeen.TryGetValue(off, out var c); c++; _cur.EmitSeen[off] = c;
            if (c > _o.EmitCap)
            {
                HaltReason = $"LOOP:line@0x{off:x}×{c}";
                return false;
            }
        }
        Emitted.Add((off, text, _cur.Script.Name));
        int layoutSlot = a.Count > 0 ? (int)Read(a[0]) : 0;
        string[] scriptStack;
        lock (_debugControlLock) scriptStack = _activeFrameNames.ToArray();
        // Presented text carries no ruby: the catalog drops it, as the translators did (§18).
        PresentedText? presented = PresentRun(pc, ins, o, text, out bool hidden);
        var liveRun = new AdvLiveTextRun(
            off, TextHistory.GetLayoutSnapshot(layoutSlot), _advTextStyle, text, scriptStack)
        {
            Ruby = string.IsNullOrEmpty(ruby) || presented != null || hidden ? null : ruby,
            ArmsGlyphTimer = ruby == null || RubyLayoutRules.ForEngine(_compatibility.EngineAbiId).ArmsGlyphTimer,
            Presented = presented,
        };
        if (hidden)
        {
            // The message's first run drew the sentence. History still records this run's source text (§6.2).
            _host.NoteHiddenTextRun(liveRun);
            TextHistory.AppendText(layoutSlot, off, text, _advTextStyle);
            return true;
        }
        AdvTextLayoutPresentationBinding binding =
            TextHistory.GetPresentationBinding(liveRun.Layout.Slot);
        // A reveal runs (run-state 0x20000000) unless fast-forward or a finished-until-wait page publishes at once. Its
        // tick tests a held action 6 before it polls the latch.
        bool reveals = _messageGlyphDelayMilliseconds > 0 && !AdvFastForwardActive
                       && !_host.TextRevealsFinishedUntilWait;
        if (reveals) ServicePhysicalSkipAtRevealStart();
        OfferHeldTriggersToReveal();
        AdvRetainedTextRunResult? retained;
        _advTextRevealing = reveals && !AdvFastForwardActive;
        try
        {
            retained = _host.ShowText(Gfx, binding, liveRun, _messageGlyphDelayMilliseconds);
        }
        finally
        {
            _advTextRevealing = false;
        }
        // History records the drawn rows (split at wraps), so it is written after layout but
        // before the cursor moves. A presented run's rows are the presented text's, so history keeps its source text
        // as one record: the source layout pass that rebuilds native's rows is milestone 3 (§8).
        if (presented == null)
            TextHistory.AppendText(layoutSlot, off, text, _advTextStyle, rows: retained?.Rows, ruby: retained?.Ruby);
        else
            TextHistory.AppendText(layoutSlot, off, text, _advTextStyle);
        if (retained is { } result)
            TextHistory.SetCursor(
                result.LayoutSlot, result.CursorX, result.CursorY);
        return true;
    }

    private int StepAdvText(string label, Instruction ins, int pc)
    {
        var a = ins.Args;
        switch (label)
        {
            case "show-text":
                ApplyReadTextSkip();
                foreach (var o in a)
                {
                    if (!IsStr(o)) continue;
                    if (!ShowTextRun(ins, pc, a, o, ruby: null)) return HALT;
                }
                return pc + 1;
            case "display-furigana": // 0x196 (layout slot, base text, ruby)
                // op_0x196_handler@0x41f2a0 builds the base text like show-text with the ruby as the builder's third
                // argument. Unlike op 0x6e it does not re-read message:ReadTextSkip; SYS4422's handler does not re-arm
                // the glyph timer either, SYS4433's does (RubyLayoutRules.ArmsGlyphTimer).
                if (!ShowTextRun(ins, pc, a, a[1], ReadStr(a[2]))) return HALT;
                return pc + 1;
            case "define-adv-text-layout": // 0x70: configure layout and begin a logical retained group
                TextHistory.DefineLayout((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                                         (int)Read(a[3]), (int)Read(a[4]));
                // adv_text_layout_define@0x4550e0 (DrawMode 1) creates the layout's blank surface at
                // slot layout+0x14; the host allocates its pixels when text is drawn.
                Gfx.RecordBlankSurfaceCreated(TextHistory.GetLayoutSnapshot((int)Read(a[0])).Slot + 0x14);
                return pc + 1;
            case "reset-adv-text-layout": // 0x71: reset layout and begin the next logical retained group
            {
                int requestedSlot = (int)Read(a[0]);
                TextHistory.ResetLayout(requestedSlot);
                var layout = TextHistory.GetLayoutSnapshot(requestedSlot);
                AdvTextLayoutPresentationBinding binding =
                    TextHistory.GetPresentationBinding(layout.Slot);
                if (binding.FirstObjectHandle >= 0 && binding.ObjectCapacity > 0)
                    Gfx.EraseRange(binding.FirstObjectHandle, binding.ObjectCapacity);
                _host.SetAdvTextCursor(layout.Slot, layout.CursorX, layout.CursorY);
                _host.ResetRenderedAdvTextLayout(Gfx, binding);
                _cur.ReadMessageOffset = ins.Offset;
                Gfx.SnapshotSurfaceRecords();   // native page snapshot that numbered saves write
                _sharedProfile.ReadText.CommitPending();
                ApplyReadTextSkip();
                return pc + 1;
            }
            case "set-adv-text-reset-cursor": // 0x79: configure cursor restored by a later 0x71
                TextHistory.SetResetCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
                return pc + 1;
            case "set-adv-text-cursor": // 0x7a (layout slot, x, y); slot 0 means current natively
                TextHistory.SetCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
                _host.SetAdvTextCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "set-adv-text-bounds": // 0x1c1: layout-local right/bottom overflow boundaries
                TextHistory.SetBounds((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
                return pc + 1;
            case "configure-adv-wait-indicator": // 0x73: per-layout animated input-wait marker
                _host.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
                    (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                    (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
                    (int)Read(a[8]), Read(a[9])));
                return pc + 1;
            case "u00423020": // 0x2bc: grid-atlas form with separate column/frame counts
            case "configure-adv-wait-indicator-grid":
                _host.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
                    (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                    (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
                    (int)Read(a[8]), (int)Read(a[9]), Read(a[10])));
                return pc + 1;
            case "u0041B9F0":
            case "set-adv-wait-indicator-enabled": // 0x1ce: explicit marker service start/stop
                _host.SetAdvWaitIndicatorEnabled(Read(a[0]) != 0);
                return pc + 1;
            case "u0041B5C0": // 0x1b1: marker origin optionally follows the final glyph
            case "set-adv-wait-indicator-follow-last-glyph":
                _host.SetAdvWaitIndicatorFollowLastGlyph(Read(a[0]) != 0);
                return pc + 1;
            case "u00420CE0":
            case "publish-adv-text-layout": // 0x20a: publish layout and current marker frame if active
            {
                int requestedSlot = (int)Read(a[0]);
                AdvTextLayoutPresentationBinding binding =
                    TextHistory.GetPresentationBinding(requestedSlot);
                _host.PublishAdvTextLayout(Gfx, binding);
                return pc + 1;
            }
            case "draw-string": // 0x204 (surface slot, x, y, string)
            {
                int surface = (int)Read(a[0]), x = (int)Read(a[1]), y = (int)Read(a[2]);
                string text = ReadStr(a[3]);
                if (Present(text, ins, a[3]) is { } presented)
                    _host.DrawStringToSurface(surface, x, y, text, _advTextStyle, presented);
                else
                    _host.DrawStringToSurface(surface, x, y, text, _advTextStyle);
                return pc + 1;
            }
            case "u00420A60":                 // pre-reference compatibility
            case "draw-formatted-integer":   // 0x205 (surface slot, x, y, value, field width, flags)
            {
                int x = (int)Read(a[1]);
                string text = FormatIntegerForSurface(
                    ref x, unchecked((int)Read(a[3])), (int)Read(a[4]), (int)Read(a[5]));
                _host.DrawStringToSurface((int)Read(a[0]), x, (int)Read(a[2]), text, _advTextStyle);
                return pc + 1;
            }
            case "set-font-size":
                _advTextStyle = _advTextStyle with { PrimaryFontSize = (int)Read(a[0]) }; return pc + 1;
            case "set-ruby-font-size":
                _advTextStyle = _advTextStyle with { RubyFontSize = (int)Read(a[0]) }; return pc + 1;
            case "u0041B3D0":
            case "set-text-line-spacing":
                _advTextStyle = _advTextStyle with { LineSpacing = (int)Read(a[0]) }; return pc + 1;
            case "set-font-bold":
                _advTextStyle = _advTextStyle with { Bold = Read(a[0]) != 0 }; return pc + 1;
            case "set-text-color":
                _advTextStyle = _advTextStyle with { TextColor = Read(a[0]) }; return pc + 1;
            case "set-text-effect-color":
                _advTextStyle = _advTextStyle with { EffectColor = Read(a[0]) }; return pc + 1;
            case "set-text-render-mode":
                _advTextStyle = _advTextStyle with { RenderMode = (int)Read(a[0]) }; return pc + 1;
            case "u004235C0": // SYS4433 opcode 0x2db
            case "set-text-aspect-mode":
                _advTextStyle = _advTextStyle with { AspectMode = (int)Read(a[0]) }; return pc + 1;
            case "u0042BA80": // SYS4433 opcode 0x2dc
            case "get-font-family-count":
                Write(a[0], _host.FontFamilyCount); return pc + 1;
            case "u0042D880": // SYS4433 opcode 0x2dd
            case "get-font-family-name":
                WriteStr(a[0], _host.GetFontFamilyName(unchecked((int)Read(a[1])))); return pc + 1;
            case "u0042BAC0": // SYS4433 opcode 0x2de
            case "find-font-family-index":
                Write(a[0], _host.FindFontFamilyIndex(ReadStr(a[1]))); return pc + 1;
            case "set-text-effect-offset":
                _advTextStyle = _advTextStyle with
                {
                    EffectOffsetX = (int)Read(a[0]),
                    EffectOffsetY = (int)Read(a[1])
                };
                return pc + 1;
            case "set-adv-text-layout-origin": // 0x198; slot 0 selects the current layout
            case "u0041B540":
                TextHistory.SetLayoutOrigin((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
                return pc + 1;
            case "get-message-glyph-delay": // 0x7f
            case "u00414C60":
                Write(a[0], _messageGlyphDelayMilliseconds); return pc + 1;
            case "set-message-glyph-delay": // 0x1b5
            case "u0041B5F0":
                _messageGlyphDelayMilliseconds = System.Math.Max(0, (int)Read(a[0]));
                _host.SetMessageGlyphDelayMilliseconds(_messageGlyphDelayMilliseconds);
                return pc + 1;
            case "end-text-line":
            {
                // The line break moves the layout's cursor record, which the host's wait indicator can follow. Inside
                // a presented message history still records the break, and the cursor stays after the sentence.
                if (InsidePresentedMessage(pc))
                {
                    TextHistory.EndLine((int)Read(a[0]), _advTextStyle, moveCursor: false);
                    return pc + 1;
                }
                TextHistory.EndLine((int)Read(a[0]), _advTextStyle);
                var layout = TextHistory.GetLayoutSnapshot((int)Read(a[0]));
                _host.SetAdvTextCursor(layout.Slot, layout.CursorX, layout.CursorY);
                return pc + 1;
            }
            case "set-font":
                _advTextStyle = _advTextStyle with { FontFace = ReadStr(a[0]) }; return pc + 1;
            case "comment": case "dev_ukn":
                return pc + 1;
            case "u0041F3A0":
            case "register-numeric-glyph-style": // 0x13a: (style)(surface)(atlas x/y)(digit w/h)
            {
                int styleIndex = unchecked((int)Read(a[0]));
                if (!Gfx.RegisterNumericGlyphStyle(styleIndex, unchecked((int)Read(a[1])),
                        unchecked((int)Read(a[2])), unchecked((int)Read(a[3])),
                        unchecked((int)Read(a[4])), unchecked((int)Read(a[5]))))
                {
                    HaltReason ??= $"numeric-glyph-style-index-out-of-range:{styleIndex}";
                    return HALT;
                }
                return pc + 1;
            }
            case "u004234E0": // SYS4433 opcode 0x2da
            case "register-extended-numeric-glyph-style":
            {
                int styleIndex = unchecked((int)Read(a[0]));
                if (!Gfx.RegisterNumericGlyphStyle(styleIndex, unchecked((int)Read(a[1])),
                        unchecked((int)Read(a[2])), unchecked((int)Read(a[3])),
                        unchecked((int)Read(a[4])), unchecked((int)Read(a[5])),
                        unchecked((int)Read(a[6])), unchecked((int)Read(a[7]))))
                {
                    HaltReason ??= $"numeric-glyph-style-index-out-of-range:{styleIndex}";
                    return HALT;
                }
                return pc + 1;
            }
            case "u00422460":
            case "draw-decimal-glyphs": // 0x23b: retained decimal glyph draw
            {
                int styleIndex = unchecked((int)Read(a[1]));
                if (!Gfx.DrawDecimalGlyphs(Read(a[0]), styleIndex, unchecked((int)Read(a[2])),
                        unchecked((int)Read(a[3])), unchecked((int)Read(a[4])),
                        unchecked((int)Read(a[5])), unchecked((int)Read(a[6]))))
                {
                    HaltReason ??= (uint)styleIndex >= 11
                        ? $"numeric-glyph-style-index-out-of-range:{styleIndex}"
                        : $"numeric-glyph-style-unregistered:{styleIndex}";
                    return HALT;
                }
                return pc + 1;
            }
            case "set-adv-wait-indicator-handle":   // 0x212 (layout)(retained handle)
            {
                int requestedSlot = (int)Read(a[0]);
                TextHistory.SetWaitIndicatorObjectHandle(requestedSlot, Read(a[1]));
                AdvTextLayoutSnapshot layout =
                    TextHistory.GetLayoutSnapshot(requestedSlot);
                _host.BindAdvWaitIndicator(
                    TextHistory.GetPresentationBinding(layout.Slot),
                    layout);
                return pc + 1;
            }
            case "set-adv-text-object-range":       // 0x213 (layout)(first handle)(count)
            {
                TextHistory.SetTextObjectRange((int)Read(a[0]), Read(a[1]), Read(a[2]));
                return pc + 1;
            }
            default:
                throw new InvalidOperationException($"Non-ADV-text opcode routed to ADV-text handler: {label}");
        }
    }
}
