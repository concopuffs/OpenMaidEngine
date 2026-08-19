using Age.Engine.Hosting;
using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepAdvText(string label, Instruction ins, int pc)
    {
        var a = ins.Args;
        switch (label)
        {
            case "show-text":
                RefreshAdvReadSkipState();
                foreach (var o in a)
                {
                    if (!IsStr(o)) continue;
                    int off = o.Type == T_STR ? (int)o.Value : ins.Offset;
                    _cur.EmitSeen.TryGetValue(off, out var c); c++; _cur.EmitSeen[off] = c;
                    if (c > _o.EmitCap) { HaltReason = $"LOOP:line@0x{off:x}×{c}"; return HALT; }
                    string text = ReadStr(o);
                    Emitted.Add((off, text, _cur.Script.Name));
                    int layoutSlot = a.Count > 0 ? (int)Read(a[0]) : 0;
                    string[] scriptStack;
                    lock (_debugControlLock) scriptStack = _activeFrameNames.ToArray();
                    var liveRun = new AdvLiveTextRun(
                        off, TextHistory.GetLayoutSnapshot(layoutSlot), _advTextStyle, text, scriptStack);
                    TextHistory.AppendText(layoutSlot, off, text, _advTextStyle);
                    AdvTextLayoutPresentationBinding binding =
                        TextHistory.GetPresentationBinding(liveRun.Layout.Slot);
                    AdvRetainedTextRunResult? retained = _host.ShowText(
                        Gfx, binding, liveRun, _messageGlyphDelayMilliseconds);
                    if (retained is { } result)
                        TextHistory.SetCursor(
                            result.LayoutSlot, result.CursorX, result.CursorY);
                }
                return pc + 1;
            case "define-adv-text-layout": // 0x70: configure layout and begin a logical retained group
                TextHistory.DefineLayout((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                                         (int)Read(a[3]), (int)Read(a[4]));
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
                _sharedProfile.ReadText.CommitPending();
                RefreshAdvReadSkipState();
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
                _host.DrawStringToSurface((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), ReadStr(a[3]),
                                          _advTextStyle);
                return pc + 1;
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
                TextHistory.EndLine((int)Read(a[0]), _advTextStyle); return pc + 1;
            case "set-font":
                _advTextStyle = _advTextStyle with { FontFace = ReadStr(a[0]) }; return pc + 1;
            case "comment": case "display-furigana": case "dev_ukn":
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
