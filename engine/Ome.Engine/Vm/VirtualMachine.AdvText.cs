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
    /// What one show-text run draws (spec §6.2), and what was looked up for it (<paramref name="lookup"/>, which
    /// history keeps, §8). A run that starts a message of several runs resolves the whole message, its runs' texts
    /// joined: on an entry, it draws the presented sentence and the message's later runs draw nothing
    /// (<paramref name="hidden"/>); on a miss, each run resolves on its own and only the message is logged. A single run
    /// resolves with its operand's origin.
    /// </summary>
    private PresentedText? PresentRun(int pc, Instruction ins, Operand operand, string text, out bool hidden,
                                      out TextLookup? lookup)
    {
        hidden = false;
        lookup = null;
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
        var site = new TextSite(_cur.Script.Name, ins.Offset);
        TextMessage? message = null;
        int run = -1;
        if (presenter.Messages?.MessagesOf(_cur.Script) is { } messages
            && messages.TryGetRun(pc, out TextMessage found, out int index) && found.Runs.Count > 1)
            (message, run) = (found, index);
        bool inMessage = message != null;
        if (message != null && run == 0)
        {
            // Nothing executes between a message's runs, so every operand reads now as it will when its run executes.
            string key = string.Concat(message.Runs.Select(part => ReadStr(part.Text)));
            if (presenter.Present(key, site) is { Entry: not null } sentence)
            {
                _presentedMessage = new PresentedMessage(_cur, _cur.Script, message, 1);
                lookup = new TextLookup(key, site, null);
                return sentence;
            }
        }
        lookup = new TextLookup(text, site, OriginOf(operand));
        return presenter.Present(text, site, lookup.Value.Origin, logMiss: !inMessage);
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
        PresentedText? presented = PresentRun(pc, ins, o, text, out bool hidden, out TextLookup? lookup);
        // The run draws from the presented cursor; its source layout pass starts at the source cursor (§8).
        var liveRun = new AdvLiveTextRun(
            off, TextHistory.GetDrawSnapshot(layoutSlot), _advTextStyle, text, scriptStack)
        {
            Ruby = string.IsNullOrEmpty(ruby) || presented != null || hidden ? null : ruby,
            SourceRuby = string.IsNullOrEmpty(ruby) ? null : ruby,
            SourceCursor = TextHistory.SourceCursorApart(layoutSlot),
            ArmsGlyphTimer = ruby == null || _rules.Text.FuriganaArmsGlyphTimer,
            Presented = presented,
        };
        if (hidden)
        {
            // The message's first run drew the sentence. History records this run's source rows, and only the source
            // cursor moves (§6.2, §8).
            AdvTextSourceLayout? hiddenSource = _host.LayoutHiddenTextRun(
                TextHistory.GetPresentationBinding(liveRun.Layout.Slot), liveRun);
            TextHistory.AppendText(layoutSlot, off, text, _advTextStyle, rows: hiddenSource?.Rows,
                                   ruby: hiddenSource?.Ruby);
            if (hiddenSource != null) TextHistory.SetSourceCursor(layoutSlot, hiddenSource.CursorX, hiddenSource.CursorY);
            if (_presentedMessage == null) TextHistory.EndLookupSpan();   // the message's last run
            return true;
        }
        if (lookup is { } asked) TextHistory.BeginLookupSpan(asked, _presenter!.Language.Tag);
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
            retained = _host.ShowText(Gfx, binding, liveRun, RevealDelay(presented));
        }
        finally
        {
            _advTextRevealing = false;
        }
        // History records the source rows (split at wraps), so it is written after layout but before the cursors move:
        // the drawn rows when the host drew the source text where native would, else its source layout pass (§8).
        AdvTextSourceLayout? source = retained is { } drawn
            ? drawn.Source ?? new AdvTextSourceLayout(drawn.Rows, drawn.Ruby, drawn.CursorX, drawn.CursorY)
            : null;
        TextHistory.AppendText(layoutSlot, off, text, _advTextStyle, rows: source?.Rows, ruby: source?.Ruby);
        if (retained is { } result)
            TextHistory.SetRunCursors(result.LayoutSlot, source!.CursorX, source.CursorY, result.CursorX, result.CursorY);
        // Presented text that needs more rows than its window has is narrowed until it fits, or cut; either is
        // reported (§7.3).
        if (retained is { CutRows: > 0 } cut && lookup is { } cutLookup) _presenter?.ReportCut(cutLookup, cut.CutRows);
        if (retained is { NarrowedToFitPercent: > 0 } narrowed && lookup is { } narrowedLookup)
            _presenter?.ReportNarrowed(narrowedLookup, narrowed.NarrowedToFitPercent);
        // A presented message's span stays open until its last run.
        if (_presentedMessage == null) TextHistory.EndLookupSpan();
        return true;
    }

    /// <summary>The reveal's delay per glyph for a run: a presented run's is scaled by its language's
    /// <c>glyph_delay_scale</c> (localization spec §7.5), and never reaches zero, so a reveal stays a reveal.</summary>
    private int RevealDelay(PresentedText? presented)
    {
        if (presented == null || _presenter == null || _messageGlyphDelayMilliseconds <= 0) return _messageGlyphDelayMilliseconds;
        double scale = _presenter.Language.GlyphDelayScale;
        return scale == 1.0
            ? _messageGlyphDelayMilliseconds
            : System.Math.Max(1, (int)System.Math.Round(_messageGlyphDelayMilliseconds * scale));
    }

    private StepResult OpShowText(Instruction ins, int pc)
    {
        var a = ins.Args;
        ApplyReadTextSkip();
        foreach (var o in a)
        {
            if (!IsStr(o)) continue;
            if (!ShowTextRun(ins, pc, a, o, ruby: null)) return StepResult.Halt;
        }
        return StepResult.Next;
    }

    // 0x196 (layout slot, base text, ruby)
    private StepResult OpDisplayFurigana(Instruction ins, int pc)
    {
        var a = ins.Args;
        // op_0x196_handler@0x41f2a0 builds the base text like show-text with the ruby as the builder's third
        // argument. Unlike op 0x6e it does not re-read message:ReadTextSkip; whether it re-arms the glyph timer
        // is the build's rule (TextRules.FuriganaArmsGlyphTimer).
        if (!ShowTextRun(ins, pc, a, a[1], ReadStr(a[2]))) return StepResult.Halt;
        return StepResult.Next;
    }

    // 0x70: configure layout and begin a logical retained group
    private StepResult OpDefineAdvTextLayout(Instruction ins, int pc)
    {
        var a = ins.Args;
        TextHistory.DefineLayout((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                                 (int)Read(a[3]), (int)Read(a[4]));
        // adv_text_layout_define@0x4550e0 (DrawMode 1) creates the layout's blank surface at
        // slot layout+0x14; the host allocates its pixels when text is drawn.
        Gfx.RecordBlankSurfaceCreated(TextHistory.GetLayoutSnapshot((int)Read(a[0])).Slot + 0x14);
        return StepResult.Next;
    }

    // 0x71: reset layout and begin the next logical retained group
    private StepResult OpResetAdvTextLayout(Instruction ins, int pc)
    {
        var a = ins.Args;
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
        return StepResult.Next;
    }

    // 0x79: configure cursor restored by a later 0x71
    private StepResult OpSetAdvTextResetCursor(Instruction ins, int pc)
    {
        var a = ins.Args;
        TextHistory.SetResetCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
        return StepResult.Next;
    }

    // 0x7a (layout slot, x, y); slot 0 means current natively
    private StepResult OpSetAdvTextCursor(Instruction ins, int pc)
    {
        var a = ins.Args;
        TextHistory.SetCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
        _host.SetAdvTextCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return StepResult.Next;
    }

    // 0x1c1: layout-local right/bottom overflow boundaries
    private StepResult OpSetAdvTextBounds(Instruction ins, int pc)
    {
        var a = ins.Args;
        TextHistory.SetBounds((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
        return StepResult.Next;
    }

    // 0x73: per-layout animated input-wait marker
    private StepResult OpConfigureAdvWaitIndicator(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
            (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
            (int)Read(a[8]), Read(a[9])));
        return StepResult.Next;
    }

    // 0x2bc: grid-atlas form with separate column/frame counts
    private StepResult OpConfigureAdvWaitIndicatorGrid(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
            (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
            (int)Read(a[8]), (int)Read(a[9]), Read(a[10])));
        return StepResult.Next;
    }

    // 0x1ce: explicit marker service start/stop
    private StepResult OpSetAdvWaitIndicatorEnabled(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.SetAdvWaitIndicatorEnabled(Read(a[0]) != 0);
        return StepResult.Next;
    }

    // 0x1b1: marker origin optionally follows the final glyph
    private StepResult OpSetAdvWaitIndicatorFollowLastGlyph(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.SetAdvWaitIndicatorFollowLastGlyph(Read(a[0]) != 0);
        return StepResult.Next;
    }

    // 0x20a: publish layout and current marker frame if active
    private StepResult OpPublishAdvTextLayout(Instruction ins, int pc)
    {
        var a = ins.Args;
        int requestedSlot = (int)Read(a[0]);
        AdvTextLayoutPresentationBinding binding =
            TextHistory.GetPresentationBinding(requestedSlot);
        _host.PublishAdvTextLayout(Gfx, binding);
        return StepResult.Next;
    }

    // 0x204 (surface slot, x, y, string)
    private StepResult OpDrawString(Instruction ins, int pc)
    {
        var a = ins.Args;
        int surface = (int)Read(a[0]), x = (int)Read(a[1]), y = (int)Read(a[2]);
        string text = ReadStr(a[3]);
        if (Present(text, ins, a[3]) is { } presented)
            _host.DrawStringToSurface(surface, x, y, text, _advTextStyle, presented);
        else
            _host.DrawStringToSurface(surface, x, y, text, _advTextStyle);
        return StepResult.Next;
    }

    // 0x205 (surface slot, x, y, value, field width, flags)
    private StepResult OpDrawFormattedInteger(Instruction ins, int pc)
    {
        var a = ins.Args;
        int x = (int)Read(a[1]);
        string text = FormatIntegerForSurface(
            ref x, unchecked((int)Read(a[3])), (int)Read(a[4]), (int)Read(a[5]));
        _host.DrawStringToSurface((int)Read(a[0]), x, (int)Read(a[2]), text, _advTextStyle);
        return StepResult.Next;
    }

    private StepResult OpSetFontSize(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with { PrimaryFontSize = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetRubyFontSize(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with { RubyFontSize = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetTextLineSpacing(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with { LineSpacing = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetFontBold(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with { Bold = Read(a[0]) != 0 }; return StepResult.Next;
    }

    private StepResult OpSetTextColor(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with { TextColor = Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetTextEffectColor(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with { EffectColor = Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetTextRenderMode(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with { RenderMode = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetTextAspectMode(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with { AspectMode = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpGetFontFamilyCount(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _host.FontFamilyCount); return StepResult.Next;
    }

    private StepResult OpGetFontFamilyName(Instruction ins, int pc)
    {
        var a = ins.Args;
        WriteStr(a[0], _host.GetFontFamilyName(unchecked((int)Read(a[1])))); return StepResult.Next;
    }

    private StepResult OpFindFontFamilyIndex(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _host.FindFontFamilyIndex(ReadStr(a[1]))); return StepResult.Next;
    }

    private StepResult OpSetTextEffectOffset(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with
        {
            EffectOffsetX = (int)Read(a[0]),
            EffectOffsetY = (int)Read(a[1])
        };
        return StepResult.Next;
    }

    // 0x198; slot 0 selects the current layout
    private StepResult OpSetAdvTextLayoutOrigin(Instruction ins, int pc)
    {
        var a = ins.Args;
        TextHistory.SetLayoutOrigin((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
        return StepResult.Next;
    }

    // 0x7f
    private StepResult OpGetMessageGlyphDelay(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _messageGlyphDelayMilliseconds); return StepResult.Next;
    }

    // 0x1b5
    private StepResult OpSetMessageGlyphDelay(Instruction ins, int pc)
    {
        var a = ins.Args;
        _messageGlyphDelayMilliseconds = System.Math.Max(0, (int)Read(a[0]));
        _host.SetMessageGlyphDelayMilliseconds(_messageGlyphDelayMilliseconds);
        return StepResult.Next;
    }

    private StepResult OpEndTextLine(Instruction ins, int pc)
    {
        var a = ins.Args;
        // The line break moves the layout's cursors; the host's wait indicator can follow the presented one.
        // Inside a presented message only the source cursor moves: the sentence draws its own breaks (§6.2).
        bool insideMessage = InsidePresentedMessage(pc);
        TextHistory.EndLine((int)Read(a[0]), _advTextStyle, moveDrawCursor: !insideMessage);
        if (insideMessage) return StepResult.Next;
        var layout = TextHistory.GetDrawSnapshot((int)Read(a[0]));
        _host.SetAdvTextCursor(layout.Slot, layout.CursorX, layout.CursorY);
        return StepResult.Next;
    }

    private StepResult OpSetFont(Instruction ins, int pc)
    {
        var a = ins.Args;
        _advTextStyle = _advTextStyle with { FontFace = ReadStr(a[0]) }; return StepResult.Next;
    }

    private StepResult OpComment(Instruction ins, int pc)
    {
        return StepResult.Next;
    }

    // 0x13a: (style)(surface)(atlas x/y)(digit w/h)
    private StepResult OpRegisterNumericGlyphStyle(Instruction ins, int pc)
    {
        var a = ins.Args;
        int styleIndex = unchecked((int)Read(a[0]));
        if (!Gfx.RegisterNumericGlyphStyle(styleIndex, unchecked((int)Read(a[1])),
                unchecked((int)Read(a[2])), unchecked((int)Read(a[3])),
                unchecked((int)Read(a[4])), unchecked((int)Read(a[5]))))
        {
            HaltReason ??= $"numeric-glyph-style-index-out-of-range:{styleIndex}";
            return StepResult.Halt;
        }
        return StepResult.Next;
    }

    private StepResult OpRegisterExtendedNumericGlyphStyle(Instruction ins, int pc)
    {
        var a = ins.Args;
        int styleIndex = unchecked((int)Read(a[0]));
        if (!Gfx.RegisterNumericGlyphStyle(styleIndex, unchecked((int)Read(a[1])),
                unchecked((int)Read(a[2])), unchecked((int)Read(a[3])),
                unchecked((int)Read(a[4])), unchecked((int)Read(a[5])),
                unchecked((int)Read(a[6])), unchecked((int)Read(a[7]))))
        {
            HaltReason ??= $"numeric-glyph-style-index-out-of-range:{styleIndex}";
            return StepResult.Halt;
        }
        return StepResult.Next;
    }

    // 0x23b: retained decimal glyph draw
    private StepResult OpDrawDecimalGlyphs(Instruction ins, int pc)
    {
        var a = ins.Args;
        int styleIndex = unchecked((int)Read(a[1]));
        if (!Gfx.DrawDecimalGlyphs(Read(a[0]), styleIndex, unchecked((int)Read(a[2])),
                unchecked((int)Read(a[3])), unchecked((int)Read(a[4])),
                unchecked((int)Read(a[5])), unchecked((int)Read(a[6]))))
        {
            HaltReason ??= (uint)styleIndex >= 11
                ? $"numeric-glyph-style-index-out-of-range:{styleIndex}"
                : $"numeric-glyph-style-unregistered:{styleIndex}";
            return StepResult.Halt;
        }
        return StepResult.Next;
    }

    // 0x212 (layout)(retained handle)
    private StepResult OpSetAdvWaitIndicatorHandle(Instruction ins, int pc)
    {
        var a = ins.Args;
        int requestedSlot = (int)Read(a[0]);
        TextHistory.SetWaitIndicatorObjectHandle(requestedSlot, Read(a[1]));
        AdvTextLayoutSnapshot layout =
            TextHistory.GetLayoutSnapshot(requestedSlot);
        _host.BindAdvWaitIndicator(
            TextHistory.GetPresentationBinding(layout.Slot),
            layout);
        return StepResult.Next;
    }

    // 0x213 (layout)(first handle)(count)
    private StepResult OpSetAdvTextObjectRange(Instruction ins, int pc)
    {
        var a = ins.Args;
        TextHistory.SetTextObjectRange((int)Read(a[0]), Read(a[1]), Read(a[2]));
        return StepResult.Next;
    }
}
