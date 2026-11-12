using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Script.Abi;
using Ome.Script.Compile;
using Ome.Script.Patch;

namespace Ome.Mods.Localization.Import;

/// <summary>A base script and the translated install's script of the same name (null when it has none).</summary>
public sealed record ScriptTranslationPair(string Name, byte[] Base, byte[]? Translated);

public enum OccurrenceKind
{
    /// <summary>A message whose runs are all literals; the key is its joined text.</summary>
    Message,
    /// <summary>A message with variable runs; the key is a pattern with one placeholder per variable run.</summary>
    Pattern,
    /// <summary>A literal drawn by <c>draw-string</c>, which has a site.</summary>
    DrawLiteral,
    /// <summary>A literal <c>set-string</c> stores into a string cell, drawn somewhere else. The engine records the
    /// store (review R2 of <c>docs/localization-import-test.md</c>), so its entries are scoped by the storing
    /// instruction, as a message's are.</summary>
    StoredLiteral,
    /// <summary>A literal <c>set-string</c> stores into a frame local string. Nothing records that store, so only a
    /// global entry applies to it.</summary>
    FrameLiteral,
    /// <summary>A string a script composes with <c>concat</c>; the key is its template, a pattern. The offset is the
    /// instruction that stores or draws it.</summary>
    Composed,
}

/// <summary>A translated string that is its source with characters missing (see
/// <see cref="ScriptTranslationImporter.Shortened"/>).</summary>
public enum Shortening { None, Damage, Translation }

/// <summary>One place a source text occurs in a compared script, with the translated install's text there.</summary>
public sealed record TextOccurrence(string Script, int Offset, OccurrenceKind Kind, string Key, string Translation);

/// <summary>The catalog files of an import, keyed by file name, with the report and the occurrences
/// <c>--verify</c> checks.</summary>
public sealed record ScriptImport(
    string Language,
    IReadOnlyDictionary<string, TextCatalogFile> Files,
    ImportReport Report,
    IReadOnlyList<TextOccurrence> Occurrences);

/// <summary>
/// Builds a catalog from an install whose scripts were translated in place (localization spec §13, §18.1). Each
/// translated script is aligned with its base (<see cref="ScriptAlignment"/>); each base message then takes the
/// translated text of its span, with the hand-made line breaks joined back. A source text becomes one global entry
/// with its most common translation, and script or site entries where a script or a site differs.
/// </summary>
public sealed class ScriptTranslationImporter
{
    public const string GlobalFileName = "_global.toml";

    private static readonly HashSet<string> CompareLabels = new(StringComparer.Ordinal)
    {
        "string-equals", "string-not-equals", "add-value-switch-case", "value-switch-jump",
    };

    private readonly OpcodeTable _table;
    private readonly OpcodeContracts _contracts;
    private readonly TextOpcodes _text;
    private readonly int? _drawString, _setString, _concat;
    private readonly ComposedStringFinder _composed;

    public ScriptTranslationImporter(OpcodeTable table, OpcodeContracts contracts, string language)
    {
        _table = table;
        _contracts = contracts;
        _text = TextOpcodes.FromTable(table);
        _drawString = table.ByLabel("draw-string");
        _setString = table.ByLabel("set-string");
        _concat = table.ByLabel("concat");
        _composed = new ComposedStringFinder(table, contracts);
        Language = language;
    }

    public string Language { get; }

    public ScriptImport Import(IEnumerable<ScriptTranslationPair> pairs, ImportReport report)
    {
        report.Language = Language;
        var occurrences = new List<TextOccurrence>();
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        // Scripts are finished in their order once every translated line is measured: a script with few lines
        // takes its line width from the whole install (see LineEdges).
        var pending = new List<(string Script, Ome.Engine.Model.Script Code, ScriptMessages Messages,
            TranslatedScript? Translated)>();
        foreach (ScriptTranslationPair pair in pairs)
        {
            report.Scripts.Base++;
            string script = TextCatalog.NormalizeScriptName(pair.Name);
            if (pair.Translated == null)
            {
                report.Scripts.BaseOnly.Add(pair.Name);
                continue;
            }
            if (!TryParse(pair.Base, pair.Name, out Ome.Engine.Model.Script? baseScript))
            {
                report.Scripts.Unreadable.Add(pair.Name);
                continue;
            }
            fingerprints[script] = OmsPatchPlan.FingerprintOf(pair.Base);
            ScriptMessages messages = MessageSegmenter.Segment(baseScript!, _text,
                ScriptAlignment.BranchTargets(baseScript!, _contracts));
            if (pair.Base.AsSpan().SequenceEqual(pair.Translated))
            {
                report.Scripts.Identical++;
                pending.Add((script, baseScript!, messages, null));
                continue;
            }
            report.Scripts.Changed++;
            if (!TryParse(pair.Translated, pair.Name, out Ome.Engine.Model.Script? translated))
            {
                report.Scripts.Unreadable.Add(pair.Name + " (translated)");
                continue;
            }
            if (!ScriptAlignment.TryAlign(baseScript!, translated!, _table, _contracts, _text,
                    out ScriptAlignment alignment, out AlignmentFailure? failure))
            {
                report.Scripts.NotAligned.Add(new NotAlignedScript(pair.Name, failure!));
                continue;
            }
            report.Scripts.Aligned++;
            for (int k = 0; k < translated!.Instructions.Count; k++)
            {
                if (!alignment.Inserted[k]) continue;
                if (translated.Instructions[k].Opcode == _text.ShowText) report.Alignment.InsertedShowText++;
                else report.Alignment.InsertedEndTextLine++;
            }
            foreach (TargetDisagreement disagreement in alignment.TargetDisagreements)
                report.Alignment.TargetDisagreements.Add(new ScriptTargetDisagreement(pair.Name, disagreement));
            pending.Add((script, baseScript!, messages,
                ReadTranslated(script, baseScript!, translated, messages, alignment, report)));
        }
        LineEdges edges = LineEdges.Measure(pending.Select(p => p.Translated).OfType<TranslatedScript>());
        foreach (var (script, code, messages, translated) in pending)
        {
            if (translated == null) AddIdentical(script, code, messages, occurrences, report);
            else FinishTranslated(translated, edges, occurrences, report);
        }
        var files = BuildFiles(occurrences, fingerprints, report);
        return new ScriptImport(Language, files, report, occurrences);
    }

    private bool TryParse(byte[] bytes, string name, out Ome.Engine.Model.Script? script)
    {
        try
        {
            script = Sys4Loader.Parse(bytes, _table, name);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or Sys4OpcodeDecodeException
                                          or IndexOutOfRangeException or ArgumentException)
        {
            script = null;
            return false;
        }
    }

    // ---- occurrences -------------------------------------------------------------------------------------------

    private void AddIdentical(string script, Ome.Engine.Model.Script code, ScriptMessages messages,
                              List<TextOccurrence> occurrences, ImportReport report)
    {
        foreach (TextMessage message in messages.Messages)
        {
            CountBase(code, message, report);
            if (MessageSegmenter.TryGetLiteralKey(code, message, out string key))
            {
                report.Messages.Literal++;
                report.Messages.Untranslated++;
                if (key.Length > 0)
                    occurrences.Add(new TextOccurrence(script, message.Offset, OccurrenceKind.Message, key, key));
            }
            else
            {
                report.Messages.WithVariables++;
                report.Messages.Untranslated++;
            }
        }
        for (int k = 0; k < code.Instructions.Count; k++)
        {
            if (messages.TryGetRun(k, out _, out _)) continue;
            Instruction instruction = code.Instructions[k];
            foreach (Operand operand in instruction.Args)
            {
                if (!MessageSegmenter.TryGetLiteral(code, operand, out string literal) || literal.Length == 0) continue;
                if (LiteralKind(instruction) is { } kind)
                    occurrences.Add(new TextOccurrence(script, instruction.Offset, kind, literal, literal));
            }
        }
    }

    /// <summary>A changed script, aligned and read message by message, waiting for its line edges.</summary>
    private sealed record TranslatedScript(string Script, Ome.Engine.Model.Script Code,
                                           Ome.Engine.Model.Script Translated, ScriptMessages Messages,
                                           ScriptAlignment Alignment, int[] TranslatedToBase, bool[] Covered,
                                           IReadOnlyList<MessageSpan> Spans);

    private TranslatedScript ReadTranslated(string script, Ome.Engine.Model.Script code,
                                            Ome.Engine.Model.Script translated, ScriptMessages messages,
                                            ScriptAlignment alignment, ImportReport report)
    {
        IReadOnlyList<Instruction> b = code.Instructions, t = translated.Instructions;
        IReadOnlyList<int> map = alignment.BaseToTranslated;
        var translatedToBase = Enumerable.Repeat(-1, t.Count).ToArray();
        for (int k = 0; k < b.Count; k++) translatedToBase[map[k]] = k;
        var covered = new bool[t.Count];
        var spans = new List<MessageSpan>();
        foreach (TextMessage message in messages.Messages)
        {
            CountBase(code, message, report);
            spans.Add(ReadSpan(code, translated, message, alignment, translatedToBase, covered, report));
        }
        return new TranslatedScript(script, code, translated, messages, alignment, translatedToBase, covered, spans);
    }

    private void FinishTranslated(TranslatedScript read, LineEdges edges, List<TextOccurrence> occurrences,
                                  ImportReport report)
    {
        (string script, Ome.Engine.Model.Script code, Ome.Engine.Model.Script translated, ScriptMessages messages,
            ScriptAlignment alignment, _, bool[] covered, IReadOnlyList<MessageSpan> spans) = read;
        IReadOnlyList<Instruction> b = code.Instructions, t = translated.Instructions;
        IReadOnlyList<int> map = alignment.BaseToTranslated;
        foreach (Operand layout in spans.Where(s => s.Measured).Select(s => s.Message.Layout).Distinct())
            report.Breaks.Edges.Add(edges.Report(script, layout));

        foreach (MessageSpan span in spans)
        {
            TextMessage message = span.Message;
            string translation = span.IsTranslation
                ? Join(span, edges.Of(script, span.Message.Layout), span.Measured ? report.Breaks : null, script)
                : span.Key;
            var sample = new ReportedText(script, message.Offset, span.ReadableKey,
                span.IsTranslation ? translation : span.Joined);
            if (span.Damaged)
            {
                report.Unjoined.DamagedStrings.Add(sample);
                continue;
            }
            if (span.InsertedVariable)
            {
                report.Unjoined.InsertedVariables.Add(sample);
                continue;
            }
            // Added text after the message's last base instruction, when the next message follows with nothing but
            // line ends between: the split between the two is a guess.
            if (span.After < b.Count && messages.TryGetRun(span.After, out TextMessage next, out int runIndex)
                && runIndex == 0 && next.Layout == message.Layout)
            {
                int lastMatched = map[span.After - 1];
                for (int k = lastMatched + 1; k < span.End; k++)
                    if (alignment.Inserted[k] && _text.IsRun(t[k].Opcode))
                    {
                        report.Unjoined.AmbiguousBoundary.Add(sample);
                        break;
                    }
            }
            if (span.IsTranslation && translation.Length == 0 && span.ReadableKey.Trim().Length > 0)
                report.Unjoined.Emptied.Add(sample);

            if (span.LiteralOnly)
            {
                report.Messages.Literal++;
                string text = span.IsTranslation ? Unescape(translation) : span.ReadableKey;
                if (span.Lenient)
                    report.Damage.Undecodable.Add(new ReportedLiteral(script, message.Offset, "message",
                        span.ReadableKey, Unescape(span.Joined)));
                var shortened = new ReportedLiteral(script, message.Offset, "message", span.ReadableKey,
                    Unescape(span.Joined));
                ReviewShortened(report, shortened, span.Shortening);
                if (span.Shortening == Shortening.Damage) report.Damage.LostCharacters.Add(shortened);
                if (text == span.ReadableKey) report.Messages.Untranslated++;
                else report.Messages.Translated++;
                if (span.ReadableKey.Length > 0)
                    occurrences.Add(new TextOccurrence(script, message.Offset, OccurrenceKind.Message,
                        span.ReadableKey, text));
                else if (text.Trim().Length > 0)
                    report.Literals.EmptyFilled.Add(new ReportedLiteral(script, message.Offset, "message", "", text));
            }
            else
            {
                report.Messages.WithVariables++;
                if (span.Lenient)
                    report.Damage.Undecodable.Add(new ReportedLiteral(script, message.Offset, "message",
                        span.Key, span.Joined));
                if (translation == span.Key) report.Messages.Untranslated++;
                else report.Messages.Translated++;
                occurrences.Add(new TextOccurrence(script, message.Offset, OccurrenceKind.Pattern, span.Key,
                    translation));
            }
        }

        for (int k = 0; k < t.Count; k++)
        {
            if (!alignment.Inserted[k] || covered[k] || !_text.IsRun(t[k].Opcode)) continue;
            MessageSegmenter.TryGetLiteral(translated, t[k].Args[1], out string orphan);
            report.Unjoined.OrphanInsertions.Add(new ReportedText(script, t[k].Offset, "", orphan));
        }

        foreach (ComposedString composed in _composed.Find(code, translated, alignment, script, report.Composed))
            occurrences.Add(new TextOccurrence(script, composed.ConsumerOffset, OccurrenceKind.Composed,
                composed.Source, composed.Text));

        IReadOnlySet<int> blockStarts = ScriptAlignment.BranchTargets(code, _contracts);
        var folded = new Dictionary<int, int>();
        for (int k = 0; k < b.Count; k++)
        {
            if (messages.TryGetRun(k, out _, out _)) continue;
            Instruction source = b[k], target = t[map[k]];
            string label = Label(source.Opcode);
            OccurrenceKind? kind = LiteralKind(source);
            for (int a = 0; a < source.Args.Count; a++)
            {
                if (!MessageSegmenter.TryGetLiteral(code, source.Args[a], out string original)) continue;
                bool lenient = false;
                if (!MessageSegmenter.TryGetLiteral(translated, target.Args[a], out string changed))
                {
                    if (!MessageSegmenter.IsDamagedLiteral(translated, target.Args[a])) continue;
                    if (!TryDecodeLenient(translated, target.Args[a], out changed))
                    {
                        report.Unjoined.DamagedStrings.Add(new ReportedText(script, source.Offset, original, ""));
                        continue;
                    }
                    lenient = true;
                }
                var literal = new ReportedLiteral(script, source.Offset, label, original, changed);
                if (lenient) report.Damage.Undecodable.Add(literal);
                if (original != changed)
                {
                    if (CompareLabels.Contains(label))
                    {
                        report.Literals.FeedCompares.Add(literal);
                        continue;
                    }
                    if (original.Length == 0)
                    {
                        if (changed.Trim().Length == 0) report.Damage.EmptyWrittenAsSpace.Add(literal);
                        else if (kind == OccurrenceKind.StoredLiteral
                                 && TryFold(b, k, blockStarts, script, changed, occurrences, folded))
                            report.Literals.EmptyFolded.Add(literal);
                        else report.Literals.EmptyFilled.Add(literal);
                        continue;
                    }
                    Shortening shortening = Shortened(original, changed);
                    ReviewShortened(report, literal, shortening);
                    if (shortening == Shortening.Damage)
                    {
                        report.Damage.LostCharacters.Add(literal);
                        changed = original;
                    }
                    else
                        report.Literals.ChangedByOpcode[label] = report.Literals.ChangedByOpcode.GetValueOrDefault(label) + 1;
                }
                // An empty string is no key (spec §9.3), and an unchanged one needs no entry beyond the identity.
                if (original.Length == 0) continue;
                if (kind is { } literalKind)
                    occurrences.Add(new TextOccurrence(script, source.Offset, literalKind, original, changed));
                else if (original == changed) continue;
                else if (source.Opcode == _concat) report.Literals.FeedConcat.Add(literal);
                else report.Literals.Other.Add(literal);
            }
        }
    }

    // ---- line breaks -------------------------------------------------------------------------------------------

    /// <summary>One line of a message's translated span: the written text (braces escaped, placeholders as
    /// <c>{vN}</c>) and the drawn text without placeholders, which is what gets measured.</summary>
    private sealed class SpanLine
    {
        public StringBuilder Written { get; } = new();
        public StringBuilder Plain { get; } = new();
        public bool StartsWithPlaceholder { get; set; }
        public bool HasPlaceholder { get; set; }

        /// <summary>Half cells of the drawn text, trailing spaces aside.</summary>
        public int Width => Cells(Plain.ToString().TrimEnd());

        public bool IsBlank
        {
            get
            {
                for (int k = 0; k < Written.Length; k++)
                    if (!char.IsWhiteSpace(Written[k])) return false;
                return true;
            }
        }
    }

    /// <summary>A base message and its translated span, read line by line before its breaks are classified.</summary>
    private sealed class MessageSpan
    {
        public required TextMessage Message { get; init; }
        public int After { get; init; }
        public int End { get; init; }
        public string Key { get; init; } = "";
        public string ReadableKey { get; init; } = "";
        public bool LiteralOnly { get; init; }
        public bool Damaged { get; set; }
        public bool Lenient { get; set; }
        public bool InsertedVariable { get; set; }
        public List<SpanLine> Lines { get; } = [new()];

        /// <summary>Every piece of the span written together with nothing between: what the translation holds once
        /// its line structure is set aside.</summary>
        public string Joined { get; set; } = "";
        public Shortening Shortening { get; set; }

        /// <summary>The span holds a translation: it differs from the source in more than its line breaks, and it
        /// is not the translated install's damage.</summary>
        public bool IsTranslation { get; set; }

        /// <summary>Whether the span's lines bound its layout's width: a readable translation.</summary>
        public bool Measured => IsTranslation && !Damaged && !InsertedVariable;

        public IEnumerable<(SpanLine A, SpanLine B)> Breaks()
        {
            for (int k = 0; k + 1 < Lines.Count; k++) yield return (Lines[k], Lines[k + 1]);
        }

        /// <summary>Breaks the translation added inside the message. Every script aligns by insertions alone, so the
        /// translation's breaks are the base's authored ones plus these, though which are which is not known.</summary>
        public int AddedBreaks => Math.Max(0, Lines.Count - 1 - Message.AuthoredBreaks);
    }

    private MessageSpan ReadSpan(Ome.Engine.Model.Script code, Ome.Engine.Model.Script translated, TextMessage message,
                                 ScriptAlignment alignment, int[] translatedToBase, bool[] covered, ImportReport report)
    {
        IReadOnlyList<Instruction> b = code.Instructions, t = translated.Instructions;
        IReadOnlyList<int> map = alignment.BaseToTranslated;
        int after = message.LastInstruction + 1;
        while (after < b.Count && (IsLineEnd(b[after], message.Layout) || _text.Markers.Contains(b[after].Opcode)))
            after++;
        int start = map[message.FirstInstruction];
        int end = after < b.Count ? map[after] : t.Count;
        for (int k = start; k < end; k++) covered[k] = true;

        // The base's own placeholders: one per variable run, named in order.
        var placeholders = new Dictionary<int, string>();
        var sourceKey = new StringBuilder();
        bool literalOnly = true;
        foreach (TextRun run in message.Runs)
        {
            if (MessageSegmenter.TryGetLiteral(code, run.Text, out string part))
                sourceKey.Append(TextTemplate.Escape(part));
            else
            {
                literalOnly = false;
                string name = "v" + (placeholders.Count + 1);
                placeholders[run.InstructionIndex] = name;
                sourceKey.Append('{').Append(name).Append('}');
            }
        }
        string key = sourceKey.ToString();
        var span = new MessageSpan
        {
            Message = message,
            After = after,
            End = end,
            Key = key,
            ReadableKey = literalOnly && MessageSegmenter.TryGetLiteralKey(code, message, out string plainKey)
                ? plainKey : key,
            LiteralOnly = literalOnly,
        };

        for (int k = start; k < end; k++)
        {
            Instruction instruction = t[k];
            if (instruction.Opcode == _text.EndTextLine)
            {
                span.Lines.Add(new SpanLine());
                continue;
            }
            if (!_text.IsRun(instruction.Opcode) || instruction.Args.Count < 2) continue;
            string written, plain;
            bool placeholder = false;
            if (MessageSegmenter.TryGetLiteral(translated, instruction.Args[1], out string literal))
            {
                (written, plain) = (TextTemplate.Escape(literal), literal);
                if (instruction.Opcode == _text.DisplayFurigana) CountFurigana(translated, instruction, report);
            }
            else if (MessageSegmenter.IsDamagedLiteral(translated, instruction.Args[1]))
            {
                if (!TryDecodeLenient(translated, instruction.Args[1], out string recovered))
                {
                    span.Damaged = true;
                    continue;
                }
                (written, plain) = (TextTemplate.Escape(recovered), recovered);
                span.Lenient = true;
            }
            else if (translatedToBase[k] >= 0 && placeholders.TryGetValue(translatedToBase[k], out string? name))
            {
                (written, plain) = ("{" + name + "}", "");
                placeholder = true;
            }
            else
            {
                span.InsertedVariable = true;
                continue;
            }
            SpanLine line = span.Lines[^1];
            if (placeholder)
            {
                if (line.IsBlank) line.StartsWithPlaceholder = true;
                line.HasPlaceholder = true;
            }
            line.Written.Append(written);
            line.Plain.Append(plain);
        }

        // A break before the first text or after the last is no part of the message: the base's trailing
        // end-text-line falls in the span.
        while (span.Lines.Count > 1 && span.Lines[0].IsBlank) span.Lines.RemoveAt(0);
        while (span.Lines.Count > 1 && span.Lines[^1].IsBlank) span.Lines.RemoveAt(span.Lines.Count - 1);
        span.Joined = string.Concat(span.Lines.Select(l => l.Written.ToString()));
        span.Shortening = literalOnly && span.Joined != key
            ? Shortened(span.ReadableKey, Unescape(span.Joined))
            : Shortening.None;
        span.IsTranslation = span.Joined != key && span.Shortening != Shortening.Damage;
        return span;
    }

    /// <summary>
    /// Where the translators wrapped each script's lines (R1 of the import test's review): per layout, the width that
    /// 99 of every 100 of the script's translated lines fit. A line in a hundred may be wider, because the engine
    /// itself wraps a line at the edge of its text area and some translated lines rely on that, with spaces that push
    /// a word past the edge. A script with fewer than <see cref="MinimumLines"/> such lines takes the edge of all the
    /// install's translated lines on that layout instead: a few short lines say nothing about where the area ends.
    /// </summary>
    private sealed class LineEdges
    {
        public const int MinimumLines = 20;

        private readonly Dictionary<(string Script, Operand Layout), List<int>> _widths = new();
        private readonly Dictionary<Operand, int> _install = new();

        public static LineEdges Measure(IEnumerable<TranslatedScript> scripts)
        {
            var edges = new LineEdges();
            var pooled = new Dictionary<Operand, List<int>>();
            foreach (TranslatedScript script in scripts)
                foreach (MessageSpan span in script.Spans.Where(s => s.Measured))
                    foreach (SpanLine line in span.Lines.Where(l => !l.HasPlaceholder && !l.IsBlank))
                    {
                        Operand layout = span.Message.Layout;
                        if (!edges._widths.TryGetValue((script.Script, layout), out List<int>? widths))
                            edges._widths[(script.Script, layout)] = widths = [];
                        widths.Add(line.Width);
                        if (!pooled.TryGetValue(layout, out List<int>? all)) pooled[layout] = all = [];
                        all.Add(line.Width);
                    }
            foreach (var (layout, widths) in pooled) edges._install[layout] = Edge(widths);
            return edges;
        }

        /// <summary>The edge breaks are classified against, and the widest line, which an uncertain break is
        /// measured against.</summary>
        public (int Edge, int Widest) Of(string script, Operand layout)
        {
            List<int> widths = _widths.GetValueOrDefault((script, layout)) ?? [];
            int widest = widths.Count > 0 ? widths.Max() : 0;
            int edge = widths.Count >= MinimumLines ? Edge(widths) : _install.GetValueOrDefault(layout, widest);
            return (edge, widest);
        }

        public ReportedEdge Report(string script, Operand layout)
        {
            List<int> widths = _widths.GetValueOrDefault((script, layout)) ?? [];
            var (edge, widest) = Of(script, layout);
            return new ReportedEdge(script, FormatOperand(layout), edge,
                widths.Count >= MinimumLines ? "script" : "install", widths.Count, widest,
                widths.Count(width => width > edge));
        }

        private static int Edge(List<int> widths)
        {
            List<int> sorted = widths.Order().ToList();
            return sorted[sorted.Count - sorted.Count / 100 - 1];
        }
    }

    /// <summary>
    /// The span's lines as one entry text (R1 of the import test's review). A break is a wrap when the line before
    /// it, a space and the next line's first word would cross the script's line edge: the word did not fit, so the
    /// break only wraps, and it becomes a space. Any other break is deliberate and becomes <c>\n</c>, as is a break
    /// before a blank line or before a line that starts with a space. Spaces inside a line stay as written.
    /// A message gets at most as many wraps as the translation added breaks: the base's own breaks are structure, so
    /// when the width finds more, those that cross the edge least stay deliberate. That keeps a line that ends at
    /// the edge apart from the tag line after it (<c>ITMES</c>: a description, then <c>[ Accessory ]</c>).
    /// </summary>
    private static string Join(MessageSpan span, (int Edge, int Widest) width, BreakCounts? counts, string script)
    {
        var breaks = span.Breaks().Select(pair => (pair.A, pair.B, Cells: JoinCells(pair.A, pair.B),
            Forced: ForcedDeliberate(pair.A, pair.B))).ToList();
        List<int> fit = Enumerable.Range(0, breaks.Count)
            .Where(k => !breaks[k].Forced && breaks[k].Cells > width.Edge).ToList();
        var wraps = fit.OrderByDescending(k => breaks[k].Cells).ThenBy(k => k).Take(span.AddedBreaks).ToHashSet();

        var text = new StringBuilder(span.Lines[0].Written.ToString());
        for (int k = 0; k < breaks.Count; k++)
        {
            var (a, b, cells, _) = breaks[k];
            TrimEndSpaces(text);
            bool wrap = wraps.Contains(k);
            text.Append(wrap ? WideJoin(a, b) ? "" : " " : "\n");
            text.Append(b.Written);
            if (counts == null) continue;
            if (wrap) counts.Wraps++;
            else counts.Deliberate++;
            if (wrap && cells <= width.Widest) counts.Uncertain++;
        }
        string joined = text.ToString().Trim(' ', '\t');
        if (counts != null)
        {
            int deliberate = breaks.Count - wraps.Count;
            if (deliberate > 0) counts.MessagesWithDeliberate++;
            var sample = new ReportedText(script, span.Message.Offset, span.ReadableKey, joined);
            if (fit.Count > wraps.Count)
            {
                counts.KeptByCount += fit.Count - wraps.Count;
                counts.KeptByCountSamples.Add(sample);
            }
            if (span.Message.AuthoredBreaks > 0 && deliberate == 0) counts.AuthoredWithoutDeliberate.Add(sample);
            if (span.Message.AuthoredBreaks == 0 && deliberate > 0) counts.DeliberateWithoutAuthored.Add(sample);
            foreach (SpanLine line in span.Lines.Where(l => !l.HasPlaceholder && l.Width > width.Edge))
                counts.WiderThanEdge.Add(new ReportedText(script, span.Message.Offset, span.ReadableKey,
                    line.Written.ToString()));
        }
        return joined;
    }

    /// <summary>A break before a blank line, or before a line that starts with a space (ASCII or U+3000), is never a
    /// wrap.</summary>
    private static bool ForcedDeliberate(SpanLine a, SpanLine b)
        => a.IsBlank || b.IsBlank || b.Written[0] is ' ' or '\t' or '\u3000';

    /// <summary>Half cells of the line before a break, the join and the next line's first word.</summary>
    private static int JoinCells(SpanLine a, SpanLine b)
        => Cells(a.Plain.ToString().TrimEnd()) + (WideJoin(a, b) ? 0 : 1) + FirstWordCells(b);

    /// <summary>Two wide characters meet at the break: a wrap joins them with nothing, as Japanese is written.</summary>
    private static bool WideJoin(SpanLine a, SpanLine b)
    {
        string before = a.Plain.ToString().TrimEnd();
        return before.Length > 0 && b.Plain.Length > 0 && !b.StartsWithPlaceholder
               && IsWide(before[^1]) && IsWide(b.Plain[0]);
    }

    private static int FirstWordCells(SpanLine line)
    {
        if (line.StartsWithPlaceholder || line.Plain.Length == 0) return 0;
        string plain = line.Plain.ToString();
        if (IsWide(plain[0])) return 2;
        int end = 0;
        while (end < plain.Length && plain[end] != ' ' && !IsWide(plain[end])) end++;
        return Cells(plain[..end]);
    }

    /// <summary>Width in half cells, as the engine measures text: a character's CP932 byte count.</summary>
    internal static int Cells(string text) => Cp932.GetByteCount(text);

    private static bool IsWide(char c) => Cp932.GetByteCount(stackalloc char[] { c }) == 2;

    private static void TrimEndSpaces(StringBuilder text)
    {
        int length = text.Length;
        while (length > 0 && text[length - 1] is ' ' or '\t') length--;
        text.Length = length;
    }

    private static string FormatOperand(Operand operand) => $"{operand.Type}:0x{operand.Value:x}";
    private static void CountBase(Ome.Engine.Model.Script code, TextMessage message, ImportReport report)
    {
        report.Messages.Total++;
        if (message.Runs.All(run => !MessageSegmenter.TryGetLiteral(code, run.Text, out _)
                                    && !MessageSegmenter.IsDamagedLiteral(code, run.Text)))
            report.Messages.VariableOnly++;
        if (message.AuthoredBreaks > 0) report.Messages.WithAuthoredBreaks++;
        report.Furigana.BaseRuns += message.Runs.Count(run => run.Kind == TextRunKind.Furigana);
    }

    private static void CountFurigana(Ome.Engine.Model.Script translated, Instruction instruction, ImportReport report)
    {
        MessageSegmenter.TryGetLiteral(translated, instruction.Args[1], out string word);
        string reading = instruction.Args.Count >= 3
                         && MessageSegmenter.TryGetLiteral(translated, instruction.Args[2], out string ruby)
            ? ruby : "";
        if (word.Trim().Length == 0 && reading.Trim().Length == 0) report.Furigana.TranslatedEmptied++;
        else if (reading.Trim().Length > 0) report.Furigana.TranslatedKeptReading++;
    }

    private OccurrenceKind? LiteralKind(Instruction instruction)
        => instruction.Opcode == _drawString ? OccurrenceKind.DrawLiteral
            : instruction.Opcode != _setString || instruction.Args.Count < 2 ? null
            : instruction.Args[0].Type == LocalStringType ? OccurrenceKind.FrameLiteral
            : OccurrenceKind.StoredLiteral;

    private const int GlobalStringType = 5, LocalStringType = 0xb;

    /// <summary>
    /// Review R2: the text the translation wrote into an empty stored line goes, after <c>\n</c>, into the entry of
    /// the line stored to the cell before it (index − 1), which the game draws one row higher. That store is the
    /// nearest earlier one in the same block; an empty line folded already passes the text on to the line it went
    /// into. Returns false when there is no such store.
    /// </summary>
    private bool TryFold(IReadOnlyList<Instruction> b, int k, IReadOnlySet<int> blockStarts, string script, string text,
                         List<TextOccurrence> occurrences, Dictionary<int, int> folded)
    {
        Operand cell = b[k].Args[0];
        if (cell.Type != GlobalStringType || cell.Value <= 0) return false;
        var before = new Operand(cell.Type, cell.Value - 1);
        for (int j = k - 1; j >= 0; j--)
        {
            if (blockStarts.Contains(b[j + 1].Offset) || OmsCompiler.IsTerminator(b[j].Opcode)) return false;
            if (b[j].Opcode != _setString || b[j].Args.Count < 2 || b[j].Args[0] != before) continue;
            if (!folded.TryGetValue(j, out int into))
            {
                into = occurrences.FindLastIndex(o => o.Kind == OccurrenceKind.StoredLiteral && o.Script == script
                                                      && o.Offset == b[j].Offset);
                if (into < 0) return false;
            }
            occurrences[into] = occurrences[into] with
            {
                Translation = occurrences[into].Translation.TrimEnd(' ', '\t') + "\n" + text.TrimEnd(' ', '\t'),
            };
            folded[k] = into;
            return true;
        }
        return false;
    }

    private bool IsLineEnd(Instruction instruction, Operand layout)
        => instruction.Opcode == _text.EndTextLine && instruction.Args.Count >= 1 && instruction.Args[0] == layout;

    private string Label(int opcode)
        => _table.TryGetDefinition(opcode, out OpcodeDefinition definition) ? definition.CanonicalLabel : $"0x{opcode:x}";

    /// <summary>
    /// How <paramref name="text"/> relates to <paramref name="source"/> when it is the source with characters missing
    /// and nothing added (R5 of the import test's review). The translated install's scripts lose characters from
    /// strings their translators left untranslated (<see cref="DamageCounts.LostCharacters"/>), almost always exactly
    /// one. So it is damage when one character is missing, or when a letter or digit is left. A string that lost more
    /// and keeps only punctuation is a translation whose words moved to another run (<c>』の称号を獲得</c> becomes
    /// <c>』</c>). Empty text is no shortening: emptying a string is the translators' choice, reported as emptied.
    /// </summary>
    internal static Shortening Shortened(string source, string text)
    {
        if (text.Length == 0 || text.Length >= source.Length) return Shortening.None;
        int i = 0;
        foreach (char c in source)
            if (i < text.Length && text[i] == c) i++;
        if (i != text.Length) return Shortening.None;
        return source.Length - text.Length == 1 || text.Any(c => char.IsLetter(c) || char.IsNumber(c))
            ? Shortening.Damage
            : Shortening.Translation;
    }

    /// <summary>A string that lost more than one character goes on the review list with its class.</summary>
    private static void ReviewShortened(ImportReport report, ReportedLiteral literal, Shortening shortening)
    {
        if (shortening == Shortening.None || literal.Base.Length - literal.Translated.Length <= 1) return;
        report.Damage.LostSeveral.Add(new ReportedShortening(literal.Script, literal.Offset, literal.Opcode,
            literal.Base, literal.Translated, shortening == Shortening.Damage ? "damage" : "translation"));
        if (shortening == Shortening.Translation) report.Damage.ShortenedTranslations++;
    }

    /// <summary>A string the strict decoder rejected, read as plain CP932 up to its NUL with each control character
    /// as a space. False when the operand points outside the body or holds no NUL within 4096 dwords.</summary>
    internal static bool TryDecodeLenient(Ome.Engine.Model.Script script, Operand operand, out string text)
    {
        text = "";
        if (operand.Type != TextOpcodes.LiteralOperandType || operand.Value < 0
            || operand.Value >= script.BodyDwords.Count)
            return false;
        var bytes = new List<byte>();
        int end = (int)Math.Min(operand.Value + 4096, script.BodyDwords.Count);
        for (int j = (int)operand.Value; j < end; j++)
        {
            uint x = script.BodyDwords[j] ^ 0xFFFFFFFFu;
            for (int shift = 0; shift < 32; shift += 8)
            {
                byte value = (byte)(x >> shift);
                if (value == 0)
                {
                    char[] chars = Cp932.GetString(bytes.ToArray()).ToCharArray();
                    for (int c = 0; c < chars.Length; c++)
                        if (char.IsControl(chars[c])) chars[c] = ' ';
                    text = new string(chars);
                    return true;
                }
                bytes.Add(value);
            }
        }
        return false;
    }

    private static readonly Encoding Cp932 = CreateCp932();

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    private static string Unescape(string written) => TextTemplate.Parse(written, allowKinds: false).Literal;

    // ---- entries -----------------------------------------------------------------------------------------------

    private Dictionary<string, TextCatalogFile> BuildFiles(List<TextOccurrence> occurrences,
                                                           Dictionary<string, string> fingerprints, ImportReport report)
    {
        var global = new List<TextEntry>();
        var perScript = new Dictionary<string, List<(int Offset, int Order, TextEntry Entry)>>(StringComparer.Ordinal);
        int order = 0;

        void AddScoped(string script, int offset, TextEntry entry)
        {
            if (!perScript.TryGetValue(script, out var list)) perScript[script] = list = [];
            list.Add((offset, order++, entry));
        }

        TextEntry? Make(TextOccurrence occurrence, string text, bool literal)
        {
            bool made = literal
                ? TextEntry.TryCreate(TextTemplate.Escape(occurrence.Key), TextTemplate.Escape(text), out TextEntry entry,
                    out _)
                : TextEntry.TryCreate(occurrence.Key, text, out entry, out _);
            if (made) return entry;
            report.Unjoined.Unrepresentable.Add(new ReportedText(occurrence.Script, occurrence.Offset, occurrence.Key,
                text));
            return null;
        }

        foreach (var group in occurrences.Where(o => o.Kind is not (OccurrenceKind.Pattern or OccurrenceKind.Composed))
                     .GroupBy(o => o.Key, StringComparer.Ordinal))
        {
            List<TextOccurrence> all = group.ToList();
            string key = group.Key;
            if (key.Length == 0) continue;
            report.Scope.DistinctSources++;
            if (all.Select(o => o.Translation).Distinct(StringComparer.Ordinal).Count() > 1)
                report.Scope.SourcesWithSeveralTranslations++;
            else report.Scope.SourcesWithOneTranslation++;
            // Where the key is stored, the global entry is the most common translation rather than the source: an
            // untranslated copy (often in code no player reaches) gets no entry, so it must not outvote the
            // translated ones. Messages are reproduced exactly by scoped entries whatever the global choice, so for
            // them the plain majority is the choice that needs the fewest.
            List<TextOccurrence> translatedOnes = all.Where(o => o.Translation != key).ToList();
            string chosen = all.Any(o => o.Kind is OccurrenceKind.StoredLiteral or OccurrenceKind.FrameLiteral)
                            && translatedOnes.Count > 0
                ? MostCommon(translatedOnes)
                : MostCommon(all);
            if (chosen != key && Make(all[0], chosen, literal: true) is { } globalEntry)
            {
                global.Add(globalEntry);
                report.Scope.GlobalEntries++;
            }
            foreach (var inScript in all.GroupBy(o => o.Script, StringComparer.Ordinal))
            {
                // A stored literal is scoped by its storing instruction, as a message is (review R2), except an
                // untranslated copy, which takes what the catalog chose. A literal stored into a frame local can
                // only take the global entry.
                bool UntranslatedCopy(TextOccurrence o) => o.Kind == OccurrenceKind.StoredLiteral && o.Translation == key;
                var scoped = inScript.Where(o => o.Kind != OccurrenceKind.FrameLiteral && !UntranslatedCopy(o)).ToList();
                foreach (TextOccurrence local in inScript.Where(o => o.Kind == OccurrenceKind.FrameLiteral
                                                                     && o.Translation != chosen))
                {
                    var conflict = new ReportedConflict(key, chosen, local.Script, local.Offset, local.Translation);
                    if (local.Translation == key) report.Literals.UntranslatedInInstall.Add(conflict);
                    else report.Literals.UnscopableConflicts.Add(conflict);
                }
                string effective = chosen;
                bool scriptEntry = false;
                if (scoped.Count > 0)
                {
                    List<string> variants = scoped.Select(o => o.Translation).Distinct(StringComparer.Ordinal).ToList();
                    if (variants.Count > 1)
                        report.Scope.TranslatedSeveralWaysInOneScript.Add(
                            new ReportedVariants(inScript.Key, key, variants));
                    string inThisScript = MostCommon(scoped);
                    if (inThisScript != chosen && Make(scoped[0], inThisScript, literal: true) is { } entry)
                    {
                        AddScoped(inScript.Key, scoped[0].Offset, entry with { Script = inScript.Key });
                        report.Scope.ScriptEntries++;
                        effective = inThisScript;
                        scriptEntry = true;
                    }
                }
                if (effective != key)
                    foreach (TextOccurrence copy in inScript.Where(UntranslatedCopy))
                        report.Literals.UntranslatedInInstall.Add(
                            new ReportedConflict(key, effective, copy.Script, copy.Offset, copy.Translation));
                foreach (TextOccurrence occurrence in scoped)
                {
                    bool message = occurrence.Kind == OccurrenceKind.Message;
                    if (occurrence.Translation == effective)
                    {
                        if (!message) continue;
                        if (scriptEntry) report.Scope.MessagesAtScript++;
                        else if (effective == key) report.Scope.MessagesUntranslated++;
                        else report.Scope.MessagesAtGlobal++;
                        continue;
                    }
                    if (Make(occurrence, occurrence.Translation, literal: true) is not { } site) continue;
                    AddScoped(inScript.Key, occurrence.Offset,
                        site with { Script = inScript.Key, At = occurrence.Offset });
                    report.Scope.SiteEntries++;
                    if (message) report.Scope.MessagesAtSite++;
                }
            }
        }

        // Patterns take the script's scope: a message's own script, or the script that composes the string (R4).
        foreach (var group in occurrences.Where(o => o.Kind is OccurrenceKind.Pattern or OccurrenceKind.Composed)
                     .GroupBy(o => (o.Script, o.Key)))
        {
            List<TextOccurrence> all = group.ToList();
            string chosen = MostCommon(all);
            bool composed = all[0].Kind == OccurrenceKind.Composed;
            if (chosen != group.Key.Key && Make(all[0], chosen, literal: false) is { } entry)
            {
                AddScoped(group.Key.Script, all[0].Offset, entry with { Script = group.Key.Script });
                report.Scope.PatternEntries++;
                if (composed) report.Composed.Entries++;
            }
            foreach (TextOccurrence occurrence in all.Where(o => o.Translation != chosen))
                if (Make(occurrence, occurrence.Translation, literal: false) is { } site)
                {
                    AddScoped(occurrence.Script, occurrence.Offset,
                        site with { Script = occurrence.Script, At = occurrence.Offset });
                    report.Scope.PatternEntries++;
                    if (composed) report.Composed.Entries++;
                }
        }

        var files = new Dictionary<string, TextCatalogFile>(StringComparer.Ordinal)
        {
            [GlobalFileName] = new TextCatalogFile(null, null, global, []),
        };
        foreach (var (script, entries) in perScript.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var ordered = entries.OrderBy(item => item.Offset).ThenBy(item => item.Order).Select(item => item.Entry)
                .ToList();
            string? fingerprint = ordered.Any(entry => entry.At != null) ? fingerprints[script] : null;
            files[script + ".toml"] = new TextCatalogFile(script, fingerprint, ordered, []);
        }
        return files;
    }

    /// <summary>The most common translation, the earliest one on a tie.</summary>
    private static string MostCommon(IReadOnlyList<TextOccurrence> occurrences)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        string best = occurrences[0].Translation;
        foreach (TextOccurrence occurrence in occurrences)
        {
            int count = counts[occurrence.Translation] = counts.GetValueOrDefault(occurrence.Translation) + 1;
            if (count > counts[best]) best = occurrence.Translation;
        }
        return best;
    }
}
