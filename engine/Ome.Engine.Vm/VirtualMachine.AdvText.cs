using Ome.Engine.Hosting;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Text;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    /// <summary>The message whose first run drew its presented sentence (localization spec §6.2), in the frame and
    /// script that run it, with the run that comes next. Its later runs, and the <c>end-text-line</c>s between them,
    /// draw nothing. It names the interpreter's frames, so it stays here; the presenter is the text native's.</summary>
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
        => _text.Presents ? _text.Present(text, new TextSite(_cur.Script.Name, ins.Offset), OriginOf(operand), logMiss) : null;

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
        if (!_text.Presents)
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
        if (_text.TryGetMessageRun(_cur.Script, pc, out TextMessage found, out int index) && found.Runs.Count > 1)
            (message, run) = (found, index);
        bool inMessage = message != null;
        if (message != null && run == 0)
        {
            // Nothing executes between a message's runs, so every operand reads now as it will when its run executes.
            string key = string.Concat(message.Runs.Select(part => ReadStr(part.Text)));
            if (_text.Present(key, site) is { Entry: not null } sentence)
            {
                _presentedMessage = new PresentedMessage(_cur, _cur.Script, message, 1);
                lookup = new TextLookup(key, site, null);
                return sentence;
            }
        }
        lookup = new TextLookup(text, site, OriginOf(operand));
        return _text.Present(text, site, lookup.Value.Origin, logMiss: !inMessage);
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
        int layoutSlot = a.Count > 0 ? (int)Read(a[0]) : 0;
        string[] scriptStack;
        lock (_debugControlLock) scriptStack = _activeFrameNames.ToArray();
        // Presented text carries no ruby: the catalog drops it, as the translators did (§18).
        PresentedText? presented = PresentRun(pc, ins, o, text, out bool hidden, out TextLookup? lookup);
        // The run draws from the presented cursor; its source layout pass starts at the source cursor (§8).
        AdvTextHistory history = _text.History;
        var liveRun = new AdvLiveTextRun(
            off, history.GetDrawSnapshot(layoutSlot), _text.Style, text, scriptStack)
        {
            Ruby = string.IsNullOrEmpty(ruby) || presented != null || hidden ? null : ruby,
            SourceRuby = string.IsNullOrEmpty(ruby) ? null : ruby,
            SourceCursor = history.SourceCursorApart(layoutSlot),
            ArmsGlyphTimer = ruby == null || _rules.Text.FuriganaArmsGlyphTimer,
            Presented = presented,
        };
        // A presented message's lookup span stays open until its last run.
        if (hidden)
        {
            _text.ShowHiddenRun(liveRun, layoutSlot, _cur.Script.Name, endsLookupSpan: _presentedMessage == null);
            return true;
        }
        // A reveal runs (run-state 0x20000000) unless fast-forward or a finished-until-wait page publishes at once. Its
        // tick tests a held action 6 before it polls the latch.
        bool reveals = _text.MessageGlyphDelayMilliseconds > 0 && !AdvFastForwardActive
                       && !_text.TextRevealsFinishedUntilWait;
        if (reveals) _skip.ServiceRevealStart(InputBindings);
        OfferHeldTriggersToReveal();
        _text.ShowRun(Gfx, liveRun, layoutSlot, _cur.Script.Name, lookup, endsLookupSpan: _presentedMessage == null);
        if (_text.IsRevealing) _host.WaitForTextReveal(Gfx, _text);
        return true;
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
        _text.History.DefineLayout((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                                   (int)Read(a[3]), (int)Read(a[4]));
        // adv_text_layout_define@0x4550e0 (DrawMode 1) creates the layout's blank surface at
        // slot layout+0x14; the text native allocates its pixels when text is drawn.
        Gfx.RecordBlankSurfaceCreated(_text.History.GetLayoutSnapshot((int)Read(a[0])).Slot + 0x14);
        return StepResult.Next;
    }

    // 0x71: reset layout and begin the next logical retained group
    private StepResult OpResetAdvTextLayout(Instruction ins, int pc)
    {
        var a = ins.Args;
        int requestedSlot = (int)Read(a[0]);
        _text.History.ResetLayout(requestedSlot);
        var layout = _text.History.GetLayoutSnapshot(requestedSlot);
        AdvTextLayoutPresentationBinding binding =
            _text.History.GetPresentationBinding(layout.Slot);
        if (binding.FirstObjectHandle >= 0 && binding.ObjectCapacity > 0)
            Gfx.EraseRange(binding.FirstObjectHandle, binding.ObjectCapacity);
        _text.SetAdvTextCursor(layout.Slot, layout.CursorX, layout.CursorY);
        _text.ResetRenderedAdvTextLayout(Gfx, binding);
        _cur.ReadMessageOffset = ins.Offset;
        Gfx.SnapshotSurfaceRecords();   // native page snapshot that numbered saves write
        _saves.Profile.ReadText.CommitPending();
        ApplyReadTextSkip();
        return StepResult.Next;
    }

    // 0x79: configure cursor restored by a later 0x71
    private StepResult OpSetAdvTextResetCursor(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.History.SetResetCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
        return StepResult.Next;
    }

    // 0x7a (layout slot, x, y); slot 0 means current natively
    private StepResult OpSetAdvTextCursor(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.History.SetCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
        _text.SetAdvTextCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return StepResult.Next;
    }

    // 0x1c1: layout-local right/bottom overflow boundaries
    private StepResult OpSetAdvTextBounds(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.History.SetBounds((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
        return StepResult.Next;
    }

    // 0x73: per-layout animated input-wait marker
    private StepResult OpConfigureAdvWaitIndicator(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
            (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
            (int)Read(a[8]), Read(a[9])));
        return StepResult.Next;
    }

    // 0x2bc: grid-atlas form with separate column/frame counts
    private StepResult OpConfigureAdvWaitIndicatorGrid(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
            (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
            (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
            (int)Read(a[8]), (int)Read(a[9]), Read(a[10])));
        return StepResult.Next;
    }

    // 0x1ce: explicit marker service start/stop
    private StepResult OpSetAdvWaitIndicatorEnabled(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.SetAdvWaitIndicatorEnabled(Read(a[0]) != 0);
        return StepResult.Next;
    }

    // 0x1b1: marker origin optionally follows the final glyph
    private StepResult OpSetAdvWaitIndicatorFollowLastGlyph(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.SetAdvWaitIndicatorFollowLastGlyph(Read(a[0]) != 0);
        return StepResult.Next;
    }

    // 0x20a: publish layout and current marker frame if active
    private StepResult OpPublishAdvTextLayout(Instruction ins, int pc)
    {
        var a = ins.Args;
        int requestedSlot = (int)Read(a[0]);
        AdvTextLayoutPresentationBinding binding =
            _text.History.GetPresentationBinding(requestedSlot);
        _text.PublishAdvTextLayout(Gfx, binding);
        return StepResult.Next;
    }

    // 0x204 (surface slot, x, y, string)
    private StepResult OpDrawString(Instruction ins, int pc)
    {
        var a = ins.Args;
        int surface = (int)Read(a[0]), x = (int)Read(a[1]), y = (int)Read(a[2]);
        string text = ReadStr(a[3]);
        _text.DrawStringToSurface(surface, x, y, text, _text.Style, Present(text, ins, a[3]));
        return StepResult.Next;
    }

    // 0x205 (surface slot, x, y, value, field width, flags)
    private StepResult OpDrawFormattedInteger(Instruction ins, int pc)
    {
        var a = ins.Args;
        int x = (int)Read(a[1]);
        string text = FormatIntegerForSurface(
            ref x, unchecked((int)Read(a[3])), (int)Read(a[4]), (int)Read(a[5]));
        _text.DrawStringToSurface((int)Read(a[0]), x, (int)Read(a[2]), text, _text.Style);
        return StepResult.Next;
    }

    private StepResult OpSetFontSize(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with { PrimaryFontSize = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetRubyFontSize(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with { RubyFontSize = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetTextLineSpacing(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with { LineSpacing = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetFontBold(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with { Bold = Read(a[0]) != 0 }; return StepResult.Next;
    }

    private StepResult OpSetTextColor(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with { TextColor = Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetTextEffectColor(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with { EffectColor = Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetTextRenderMode(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with { RenderMode = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpSetTextAspectMode(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with { AspectMode = (int)Read(a[0]) }; return StepResult.Next;
    }

    private StepResult OpGetFontFamilyCount(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _text.FontFamilyCount); return StepResult.Next;
    }

    private StepResult OpGetFontFamilyName(Instruction ins, int pc)
    {
        var a = ins.Args;
        WriteStr(a[0], _text.GetFontFamilyName(unchecked((int)Read(a[1])))); return StepResult.Next;
    }

    private StepResult OpFindFontFamilyIndex(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _text.FindFontFamilyIndex(ReadStr(a[1]))); return StepResult.Next;
    }

    private StepResult OpSetTextEffectOffset(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with
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
        _text.History.SetLayoutOrigin((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
        return StepResult.Next;
    }

    // 0x7f
    private StepResult OpGetMessageGlyphDelay(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _text.MessageGlyphDelayMilliseconds); return StepResult.Next;
    }

    // 0x1b5
    private StepResult OpSetMessageGlyphDelay(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.SetMessageGlyphDelayMilliseconds((int)Read(a[0]));
        return StepResult.Next;
    }

    private StepResult OpEndTextLine(Instruction ins, int pc)
    {
        var a = ins.Args;
        // The line break moves the layout's cursors; the wait indicator can follow the presented one.
        // Inside a presented message only the source cursor moves: the sentence draws its own breaks (§6.2).
        bool insideMessage = InsidePresentedMessage(pc);
        _text.History.EndLine((int)Read(a[0]), _text.Style, moveDrawCursor: !insideMessage);
        if (insideMessage) return StepResult.Next;
        var layout = _text.History.GetDrawSnapshot((int)Read(a[0]));
        _text.SetAdvTextCursor(layout.Slot, layout.CursorX, layout.CursorY);
        return StepResult.Next;
    }

    private StepResult OpSetFont(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.Style = _text.Style with { FontFace = ReadStr(a[0]) }; return StepResult.Next;
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
        _text.History.SetWaitIndicatorObjectHandle(requestedSlot, Read(a[1]));
        AdvTextLayoutSnapshot layout =
            _text.History.GetLayoutSnapshot(requestedSlot);
        _text.BindAdvWaitIndicator(
            _text.History.GetPresentationBinding(layout.Slot),
            layout);
        return StepResult.Next;
    }

    // 0x213 (layout)(first handle)(count)
    private StepResult OpSetAdvTextObjectRange(Instruction ins, int pc)
    {
        var a = ins.Args;
        _text.History.SetTextObjectRange((int)Read(a[0]), Read(a[1]), Read(a[2]));
        return StepResult.Next;
    }
}
