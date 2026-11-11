using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Script.Abi;
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
    /// <summary>A literal stored with <c>set-string</c>, drawn somewhere else.</summary>
    StoredLiteral,
}

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

    public ScriptTranslationImporter(OpcodeTable table, OpcodeContracts contracts, string language)
    {
        _table = table;
        _contracts = contracts;
        _text = TextOpcodes.FromTable(table);
        _drawString = table.ByLabel("draw-string");
        _setString = table.ByLabel("set-string");
        _concat = table.ByLabel("concat");
        Language = language;
    }

    public string Language { get; }

    public ScriptImport Import(IEnumerable<ScriptTranslationPair> pairs, ImportReport report)
    {
        report.Language = Language;
        var occurrences = new List<TextOccurrence>();
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
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
                AddIdentical(script, baseScript!, messages, occurrences, report);
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
            AddTranslated(script, baseScript!, translated, messages, alignment, occurrences, report);
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
                if (LiteralKind(instruction.Opcode) is { } kind)
                    occurrences.Add(new TextOccurrence(script, instruction.Offset, kind, literal, literal));
            }
        }
    }

    private void AddTranslated(string script, Ome.Engine.Model.Script code, Ome.Engine.Model.Script translated,
                               ScriptMessages messages, ScriptAlignment alignment, List<TextOccurrence> occurrences,
                               ImportReport report)
    {
        IReadOnlyList<Instruction> b = code.Instructions, t = translated.Instructions;
        IReadOnlyList<int> map = alignment.BaseToTranslated;
        var translatedToBase = Enumerable.Repeat(-1, t.Count).ToArray();
        for (int k = 0; k < b.Count; k++) translatedToBase[map[k]] = k;
        var covered = new bool[t.Count];

        foreach (TextMessage message in messages.Messages)
        {
            CountBase(code, message, report);
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

            var presented = new StringBuilder();
            bool damaged = false, lenient = false, insertedVariable = false, joinedBreak = false, pendingBreak = false;
            for (int k = start; k < end; k++)
            {
                Instruction instruction = t[k];
                if (instruction.Opcode == _text.EndTextLine)
                {
                    pendingBreak = presented.Length > 0;
                    continue;
                }
                if (!_text.IsRun(instruction.Opcode) || instruction.Args.Count < 2) continue;
                string piece;
                if (MessageSegmenter.TryGetLiteral(translated, instruction.Args[1], out string literal))
                {
                    piece = TextTemplate.Escape(literal);
                    if (instruction.Opcode == _text.DisplayFurigana) CountFurigana(translated, instruction, report);
                }
                else if (MessageSegmenter.IsDamagedLiteral(translated, instruction.Args[1]))
                {
                    if (!TryDecodeLenient(translated, instruction.Args[1], out string recovered))
                    {
                        damaged = true;
                        continue;
                    }
                    piece = TextTemplate.Escape(recovered);
                    lenient = true;
                }
                else if (translatedToBase[k] >= 0 && placeholders.TryGetValue(translatedToBase[k], out string? name))
                    piece = "{" + name + "}";
                else
                {
                    insertedVariable = true;
                    continue;
                }
                if (pendingBreak && piece.Trim().Length > 0)
                {
                    presented.Append(' ');
                    joinedBreak = true;
                }
                if (piece.Trim().Length > 0) pendingBreak = false;
                presented.Append(piece);
            }
            string translation = CollapseSpaces(presented.ToString());
            string key = sourceKey.ToString();
            string readableKey = literalOnly && MessageSegmenter.TryGetLiteralKey(code, message, out string plain)
                ? plain : key;
            var sample = new ReportedText(script, message.Offset, readableKey, translation);
            if (damaged)
            {
                report.Unjoined.DamagedStrings.Add(sample);
                continue;
            }
            if (insertedVariable)
            {
                report.Unjoined.InsertedVariables.Add(sample);
                continue;
            }
            // Added text after the message's last base instruction, when the next message follows with nothing but
            // line ends between: the split between the two is a guess.
            if (after < b.Count && messages.TryGetRun(after, out TextMessage next, out int runIndex) && runIndex == 0
                && next.Layout == message.Layout)
            {
                int lastMatched = map[after - 1];
                for (int k = lastMatched + 1; k < end; k++)
                    if (alignment.Inserted[k] && _text.IsRun(t[k].Opcode))
                    {
                        report.Unjoined.AmbiguousBoundary.Add(sample);
                        break;
                    }
            }
            if (translation.Length == 0 && readableKey.Trim().Length > 0) report.Unjoined.Emptied.Add(sample);
            if (joinedBreak) report.Messages.TranslationsWithJoinedBreaks++;

            if (literalOnly)
            {
                report.Messages.Literal++;
                string text = Unescape(translation);
                if (lenient)
                    report.Damage.ControlCharacters.Add(new ReportedLiteral(script, message.Offset, "message",
                        readableKey, text));
                if (text != readableKey && LostCharacters(readableKey, text))
                {
                    report.Damage.LostCharacters.Add(new ReportedLiteral(script, message.Offset, "message", readableKey,
                        text));
                    text = readableKey;
                }
                if (text == readableKey) report.Messages.Untranslated++;
                else report.Messages.Translated++;
                if (readableKey.Length > 0)
                    occurrences.Add(new TextOccurrence(script, message.Offset, OccurrenceKind.Message, readableKey,
                        text));
                else if (text.Trim().Length > 0)
                    report.Literals.EmptyFilled.Add(new ReportedLiteral(script, message.Offset, "message", "", text));
            }
            else
            {
                report.Messages.WithVariables++;
                if (lenient)
                    report.Damage.ControlCharacters.Add(new ReportedLiteral(script, message.Offset, "message", key,
                        translation));
                if (translation == key) report.Messages.Untranslated++;
                else report.Messages.Translated++;
                occurrences.Add(new TextOccurrence(script, message.Offset, OccurrenceKind.Pattern, key, translation));
            }
        }

        for (int k = 0; k < t.Count; k++)
        {
            if (!alignment.Inserted[k] || covered[k] || !_text.IsRun(t[k].Opcode)) continue;
            MessageSegmenter.TryGetLiteral(translated, t[k].Args[1], out string orphan);
            report.Unjoined.OrphanInsertions.Add(new ReportedText(script, t[k].Offset, "", orphan));
        }

        for (int k = 0; k < b.Count; k++)
        {
            if (messages.TryGetRun(k, out _, out _)) continue;
            Instruction source = b[k], target = t[map[k]];
            string label = Label(source.Opcode);
            OccurrenceKind? kind = LiteralKind(source.Opcode);
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
                if (lenient) report.Damage.ControlCharacters.Add(literal);
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
                        else report.Literals.EmptyFilled.Add(literal);
                        continue;
                    }
                    if (LostCharacters(original, changed))
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

    private OccurrenceKind? LiteralKind(int opcode)
        => opcode == _drawString ? OccurrenceKind.DrawLiteral
            : opcode == _setString ? OccurrenceKind.StoredLiteral
            : null;

    private bool IsLineEnd(Instruction instruction, Operand layout)
        => instruction.Opcode == _text.EndTextLine && instruction.Args.Count >= 1 && instruction.Args[0] == layout;

    private string Label(int opcode)
        => _table.TryGetDefinition(opcode, out OpcodeDefinition definition) ? definition.CanonicalLabel : $"0x{opcode:x}";

    /// <summary>Whether <paramref name="text"/> is <paramref name="source"/> with characters missing and nothing added,
    /// the damage the translated install's scripts carry in strings their translators left untranslated (see
    /// <see cref="DamageCounts.LostCharacters"/>). Empty text is not damage: emptying a string is the translators'
    /// choice, reported as emptied.</summary>
    internal static bool LostCharacters(string source, string text)
    {
        if (text.Length == 0 || text.Length >= source.Length) return false;
        int i = 0;
        foreach (char c in source)
            if (i < text.Length && text[i] == c) i++;
        return i == text.Length;
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

    /// <summary>Runs of ASCII spaces become one, and the ends are trimmed. Other spaces (U+3000) are text.</summary>
    internal static string CollapseSpaces(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            bool space = c is ' ' or '\t' or '\n' or '\r';
            if (space && (result.Length == 0 || result[^1] == ' ')) continue;
            result.Append(space ? ' ' : c);
        }
        if (result.Length > 0 && result[^1] == ' ') result.Length--;
        return result.ToString();
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

        foreach (var group in occurrences.Where(o => o.Kind != OccurrenceKind.Pattern)
                     .GroupBy(o => o.Key, StringComparer.Ordinal))
        {
            List<TextOccurrence> all = group.ToList();
            string key = group.Key;
            if (key.Length == 0) continue;
            report.Scope.DistinctSources++;
            if (all.Select(o => o.Translation).Distinct(StringComparer.Ordinal).Count() > 1)
                report.Scope.SourcesWithSeveralTranslations++;
            else report.Scope.SourcesWithOneTranslation++;
            // A stored literal gets only the global entry, so where the key has one, the global entry is the most
            // common translation rather than the source: an untranslated copy (often in code no player reaches) must
            // not outvote the translated ones. Messages are reproduced exactly by scoped entries whatever the global
            // choice, so for them the plain majority is the choice that needs the fewest.
            List<TextOccurrence> translatedOnes = all.Where(o => o.Translation != key).ToList();
            string chosen = all.Any(o => o.Kind == OccurrenceKind.StoredLiteral) && translatedOnes.Count > 0
                ? MostCommon(translatedOnes)
                : MostCommon(all);
            if (chosen != key && Make(all[0], chosen, literal: true) is { } globalEntry)
            {
                global.Add(globalEntry);
                report.Scope.GlobalEntries++;
            }
            foreach (var inScript in all.GroupBy(o => o.Script, StringComparer.Ordinal))
            {
                var scoped = inScript.Where(o => o.Kind != OccurrenceKind.StoredLiteral).ToList();
                foreach (TextOccurrence stored in inScript.Where(o => o.Kind == OccurrenceKind.StoredLiteral
                                                                      && o.Translation != chosen))
                {
                    var conflict = new ReportedConflict(key, chosen, stored.Script, stored.Offset, stored.Translation);
                    if (stored.Translation == key) report.Literals.UntranslatedInInstall.Add(conflict);
                    else report.Literals.UnscopableConflicts.Add(conflict);
                }
                if (scoped.Count == 0) continue;
                List<string> variants = scoped.Select(o => o.Translation).Distinct(StringComparer.Ordinal).ToList();
                if (variants.Count > 1)
                    report.Scope.TranslatedSeveralWaysInOneScript.Add(new ReportedVariants(inScript.Key, key, variants));
                string effective = chosen;
                bool scriptEntry = false;
                string inThisScript = MostCommon(scoped);
                if (inThisScript != chosen && Make(scoped[0], inThisScript, literal: true) is { } entry)
                {
                    AddScoped(inScript.Key, scoped[0].Offset, entry with { Script = inScript.Key });
                    report.Scope.ScriptEntries++;
                    effective = inThisScript;
                    scriptEntry = true;
                }
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

        foreach (var group in occurrences.Where(o => o.Kind == OccurrenceKind.Pattern)
                     .GroupBy(o => (o.Script, o.Key)))
        {
            List<TextOccurrence> all = group.ToList();
            string chosen = MostCommon(all);
            if (chosen != group.Key.Key && Make(all[0], chosen, literal: false) is { } entry)
            {
                AddScoped(group.Key.Script, all[0].Offset, entry with { Script = group.Key.Script });
                report.Scope.PatternEntries++;
            }
            foreach (TextOccurrence occurrence in all.Where(o => o.Translation != chosen))
                if (Make(occurrence, occurrence.Translation, literal: false) is { } site)
                {
                    AddScoped(occurrence.Script, occurrence.Offset,
                        site with { Script = occurrence.Script, At = occurrence.Offset });
                    report.Scope.PatternEntries++;
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
