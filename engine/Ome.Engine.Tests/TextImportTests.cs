using System;
using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Localization;
using Ome.Mods.Localization.Import;
using Ome.Script.Abi;
using Ome.Script.Patch;
using Xunit;

/// <summary>The message segmenter (localization spec §6.2) and <c>omp text import-scripts</c> (§18.1), on scripts
/// assembled for the test: a base script, and a "translated" one with the shape of the Kamidori fan translation's
/// scripts (text replaced in place, hand-made line breaks added as show-text + end-text-line pairs).</summary>
public class TextImportTests
{
    private const int ShowText = 0x6e, EndTextLine = 0x6f, WaitForInput = 0x72, Jmp = 0x8c, Call = 0x8f, Ret = 0x5,
        Jcc = 0xa0, SetString = 0x192, Concat = 0x193, StringEquals = 0x194, DisplayFurigana = 0x196, Comment = 0x1a7,
        ToString = 0x1c8, Exit = 0x2;
    private const uint NoTarget = 0xFFFFFFFF;

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);
    private static readonly TextOpcodes Text = TextOpcodes.FromTable(Table);

    /// <summary>A script under construction. <see cref="Here"/> is the next instruction's dword offset, so a jump can
    /// be patched to point at a later instruction.</summary>
    private sealed class Asm
    {
        private readonly List<(int Op, Operand[] Args)> _code = [];
        private readonly List<string> _strings = [];

        public int Here { get; private set; }

        public Operand S(string text)
        {
            _strings.Add(text);
            return new Operand(2, _strings.Count - 1);
        }

        public int Op(int opcode, params Operand[] args)
        {
            _code.Add((opcode, args));
            Here += 1 + 2 * args.Length;
            return _code.Count - 1;
        }

        public int Say(params string[] lines)
        {
            int at = Here;
            for (int k = 0; k < lines.Length; k++)
            {
                if (k > 0) Op(EndTextLine, I(0));
                Op(ShowText, I(0), S(lines[k]));
            }
            Op(WaitForInput, I(0));
            Op(EndTextLine, I(0));
            return at;
        }

        public void Target(int instruction, int argument, int offset)
            => _code[instruction] = (_code[instruction].Op,
                _code[instruction].Args.Select((arg, k) => k == argument ? I(offset) : arg).ToArray());

        public byte[] Bytes() => ScriptAssembler.AssembleBytes(_code, _strings);

        public Script Script(string name = "TEST") => Sys4Loader.Parse(Bytes(), Table, name);
    }

    private static Operand I(long value) => new(0, value);
    private static Operand GlobalString(long index) => new(5, index);
    private static Operand GlobalInt(long index) => new(3, index);
    private static Operand LocalString(long index) => new(0xb, index);

    private static ScriptMessages Segment(Script script)
        => MessageSegmenter.Segment(script, Text, ScriptAlignment.BranchTargets(script, Contracts));

    private static string Key(Script script, TextMessage message)
        => MessageSegmenter.TryGetLiteralKey(script, message, out string key) ? key : "(variable)";

    // ---- segmenter ---------------------------------------------------------------------------------------------

    [Fact]
    public void FuriganaAndLineBreaksJoinIntoOneMessage()
    {
        var asm = new Asm();
        asm.Op(ShowText, I(0), asm.S("あの"));
        asm.Op(DisplayFurigana, I(0), asm.S("黒石"), asm.S("くろいし"));
        asm.Op(EndTextLine, I(0));
        asm.Op(Comment, asm.S("marker"));
        asm.Op(ShowText, I(0), asm.S("を拾った。"));
        asm.Op(WaitForInput, I(0));
        asm.Op(EndTextLine, I(0));
        asm.Op(Exit);
        Script script = asm.Script();

        TextMessage message = Assert.Single(Segment(script).Messages);
        Assert.Equal("あの黒石を拾った。", Key(script, message));
        Assert.Equal(new[] { TextRunKind.Text, TextRunKind.Furigana, TextRunKind.Text },
            message.Runs.Select(run => run.Kind));
        Assert.Equal(1, message.AuthoredBreaks);
        Assert.Equal((0, 4), (message.FirstInstruction, message.LastInstruction));
    }

    [Fact]
    public void LayoutChangeWaitAndBranchTargetEndAMessage()
    {
        var asm = new Asm();
        int jump = asm.Op(Jmp, I(0));
        asm.Op(ShowText, I(0), asm.S("一"));
        asm.Op(ShowText, I(1), asm.S("二"));
        asm.Op(ShowText, I(1), asm.S("三"));
        int target = asm.Here;
        asm.Op(ShowText, I(1), asm.S("四"));
        asm.Op(WaitForInput, I(0));
        asm.Op(ShowText, I(1), asm.S("五"));
        asm.Op(Exit);
        asm.Target(jump, 0, target);
        Script script = asm.Script();

        Assert.Equal(new[] { "一", "二三", "四", "五" }, Segment(script).Messages.Select(m => Key(script, m)));
    }

    [Fact]
    public void VariableRunHasNoLiteralKey()
    {
        var asm = new Asm();
        asm.Op(ShowText, I(0), GlobalString(0x24ab));
        asm.Op(ShowText, I(0), asm.S("を手に入れた"));
        asm.Op(Exit);
        Script script = asm.Script();

        TextMessage message = Assert.Single(Segment(script).Messages);
        Assert.Equal("(variable)", Key(script, message));
        Assert.True(Segment(script).TryGetRun(1, out TextMessage found, out int run));
        Assert.Equal((message.Index, 1), (found.Index, run));
    }

    // ---- import -------------------------------------------------------------------------------------------------

    private static (ScriptImport Import, ImportReport Report) Import(params (string Name, Asm Base, Asm? Translated)[] pairs)
    {
        var report = new ImportReport();
        ScriptImport import = new ScriptTranslationImporter(Table, Contracts, "en").Import(
            pairs.Select(p => new ScriptTranslationPair(p.Name, p.Base.Bytes(), p.Translated?.Bytes())), report);
        return (import, report);
    }

    private static TextCatalog Catalog(ScriptImport import)
    {
        // Round-trip through the file format, as --verify does.
        var entries = new List<TextEntry>();
        foreach (var (name, file) in import.Files.OrderBy(f => f.Key == ScriptTranslationImporter.GlobalFileName ? 0 : 1)
                     .ThenBy(f => f.Key, StringComparer.Ordinal))
            entries.AddRange(TextCatalogFileReader.Parse(TextCatalogFileWriter.Write(file), name).Entries);
        return TextCatalog.Build("en", entries);
    }

    private static string Present(TextCatalog catalog, string source, string? script = null, int? at = null)
        => catalog.TryResolveExact(source, script, at, out TextEntry entry) ? entry.TextTemplate.Literal : source;

    [Fact]
    public void HandBrokenLinesJoinBackIntoTheirMessage()
    {
        Asm Build(bool translated)
        {
            var asm = new Asm();
            int jump = asm.Op(Jmp, I(0));
            if (translated) asm.Say("Hello, and welcome to", "the village.");
            else asm.Say("こんにちは。");
            int target = asm.Here;
            asm.Say(translated ? "Yes" : "はい");
            asm.Op(Exit);
            asm.Target(jump, 0, target);
            return asm;
        }

        var (import, report) = Import(("SC0001.BIN", Build(false), Build(true)));

        Assert.Equal((1, 1, 0), (report.Scripts.Changed, report.Scripts.Aligned, report.Scripts.NotAligned.Count));
        Assert.Equal((1, 1), (report.Alignment.InsertedShowText, report.Alignment.InsertedEndTextLine));
        Assert.Equal(0, report.Alignment.TargetDisagreements.Count);
        Assert.Equal((2, 2, 1), (report.Messages.Total, report.Messages.Translated, report.Breaks.Wraps));
        TextCatalog catalog = Catalog(import);
        Assert.Equal("Hello, and welcome to the village.", Present(catalog, "こんにちは。"));
        Assert.Equal("Yes", Present(catalog, "はい"));
        Assert.Equal(new[] { ScriptTranslationImporter.GlobalFileName }, import.Files.Keys);
        Assert.Equal(0, ImportVerifier.Verify(catalog, import.Occurrences).Mismatches.Count);
    }

    /// <summary>A translated line of exactly 40 half cells: the line edge of the scripts below.</summary>
    private const string EdgeLine = "The quick brown fox jumped over the dogs";

    private static void SayFillers(Asm asm, bool translated, int count)
    {
        for (int k = 0; k < count; k++) asm.Say(translated ? EdgeLine : "あ" + k);
    }

    [Fact]
    public void BreaksAreWrapsOrDeliberateByTheScriptsLineEdge()
    {
        const string full = "Every word of this line fills the box up";
        Assert.Equal((40, 40), (EdgeLine.Length, full.Length));
        Asm Build(bool translated)
        {
            var asm = new Asm();
            SayFillers(asm, translated, 20);
            if (translated)
            {
                asm.Say("We walked along the river and up the", "hill.");
                asm.Say("Yes.", "Fine.");
                asm.Say(full, "[ Accessory ]");
                asm.Say("Flies over pits", " [ Skill ]      CP: 3");
            }
            else
            {
                asm.Say("川沿いを歩いて丘を登った。");
                asm.Say("はい。");
                asm.Say("【装飾】", "説明");
                asm.Say("【移動】", "説明二");
            }
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("SC0008", Build(false), Build(true)));

        TextCatalog catalog = Catalog(import);
        // "hill." would cross the edge, so the break only wraps; "Fine." would have fit, so the break is meant.
        Assert.Equal("We walked along the river and up the hill.", Present(catalog, "川沿いを歩いて丘を登った。"));
        Assert.Equal("Yes.\nFine.", Present(catalog, "はい。"));
        // The line ends at the edge, but the base breaks there itself and the translation added no break.
        Assert.Equal(full + "\n[ Accessory ]", Present(catalog, "【装飾】説明"));
        // A line that starts with a space is never a wrap, and spaces inside a line stay.
        Assert.Equal("Flies over pits\n [ Skill ]      CP: 3", Present(catalog, "【移動】説明二"));
        ReportedEdge edge = Assert.Single(report.Breaks.Edges);
        Assert.Equal((40, "script"), (edge.Edge, edge.From));
        Assert.Equal((1, 3, 1), (report.Breaks.Wraps, report.Breaks.Deliberate, report.Breaks.KeptByCount));
        Assert.Equal(0, ImportVerifier.Verify(catalog, import.Occurrences).Mismatches.Count);
    }

    [Fact]
    public void AScriptWithFewLinesTakesTheInstallsLineEdge()
    {
        Asm Long(bool translated)
        {
            var asm = new Asm();
            SayFillers(asm, translated, 20);
            asm.Op(Exit);
            return asm;
        }

        Asm Short(bool translated)
        {
            var asm = new Asm();
            if (translated) asm.Say("I see.", "Okay.");
            else asm.Say("なるほど。");
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("SC0009", Long(false), Long(true)), ("SC0010", Short(false), Short(true)));

        // Measured by its own two lines, "Okay." would not fit after "I see." and the break would be a wrap.
        Assert.Equal("I see.\nOkay.", Present(Catalog(import), "なるほど。"));
        ReportedEdge edge = report.Breaks.Edges.Single(e => e.Script == "SC0010");
        Assert.Equal((40, "install", 2), (edge.Edge, edge.From, edge.Lines));
    }

    [Fact]
    public void AShortenedStringIsDamageOnlyWhileALetterIsLeft()
    {
        Asm Build(params string[] values)
        {
            var asm = new Asm();
            for (int k = 0; k < values.Length; k++) asm.Op(SetString, GlobalString(k + 1), asm.S(values[k]));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("EBINIT", Build("週送り。", "精霊／ヘタレ種", "スキル『"),
            Build("週り。", "精", "『")));

        TextCatalog catalog = Catalog(import);
        Assert.Equal("週送り。", Present(catalog, "週送り。"));
        Assert.Equal("精霊／ヘタレ種", Present(catalog, "精霊／ヘタレ種"));
        // Only punctuation is left: the words moved to another string, so this is a translation.
        Assert.Equal("『", Present(catalog, "スキル『"));
        Assert.Equal(2, report.Damage.LostCharacters.Count);
        Assert.Equal(new[] { ("精霊／ヘタレ種", "damage"), ("スキル『", "translation") },
            report.Damage.LostSeveral.Samples.Select(s => (s.Base, s.Class)));
        Assert.Equal(1, report.Damage.ShortenedTranslations);
    }

    [Fact]
    public void ASourceTranslatedTwoWaysInOneScriptGetsASiteEntry()
    {
        Asm Build(params string[] lines)
        {
            var asm = new Asm();
            foreach (string line in lines) asm.Say(line);
            asm.Op(Exit);
            return asm;
        }

        Asm baseScript = Build("はい", "はい", "はい");
        var (import, report) = Import(("SC0002", baseScript, Build("Yes", "Yeah", "Yes")),
            ("SC0003", Build("はい"), Build("Sure")));

        Assert.Equal(1, report.Scope.TranslatedSeveralWaysInOneScript.Count);
        Assert.Equal((1, 1, 1), (report.Scope.GlobalEntries, report.Scope.ScriptEntries, report.Scope.SiteEntries));
        TextCatalogFile sc0002 = import.Files["SC0002.toml"];
        Assert.Equal(OmsPatchPlan.FingerprintOf(baseScript.Bytes()), sc0002.Fingerprint);
        Assert.Null(import.Files["SC0003.toml"].Fingerprint);

        TextCatalog catalog = Catalog(import);
        int second = Segment(baseScript.Script()).Messages[1].Offset;
        Assert.Equal("Yeah", Present(catalog, "はい", "SC0002", second));
        Assert.Equal("Yes", Present(catalog, "はい", "SC0002", 0));
        Assert.Equal("Sure", Present(catalog, "はい", "SC0003", 0));
        Assert.Equal(0, ImportVerifier.Verify(catalog, import.Occurrences).Mismatches.Count);
    }

    [Fact]
    public void MessagesTakeThePlainMajorityAsTheirGlobalEntry()
    {
        Asm Build(params string[] lines)
        {
            var asm = new Asm();
            foreach (string line in lines) asm.Say(line);
            asm.Op(Exit);
            return asm;
        }

        // One copy the translators emptied must not become the global entry for every untranslated copy.
        Asm baseScript = Build("施設訪問。", "施設訪問。", "施設訪問。");
        var (import, report) = Import(("SC0007", baseScript, Build("施設訪問。", "", "施設訪問。")));

        Assert.Equal((0, 0, 1), (report.Scope.GlobalEntries, report.Scope.ScriptEntries, report.Scope.SiteEntries));
        TextCatalog catalog = Catalog(import);
        int second = Segment(baseScript.Script()).Messages[1].Offset;
        Assert.Equal("", Present(catalog, "施設訪問。", "SC0007", second));
        Assert.Equal("施設訪問。", Present(catalog, "施設訪問。", "SC0007", 0));
        Assert.Equal(0, ImportVerifier.Verify(catalog, import.Occurrences).Mismatches.Count);
    }

    [Fact]
    public void LiteralsThatAreNotTranslationsKeepTheirSource()
    {
        Asm Build(string damaged, string emptyAsSpace, string emptyFilled, string compared, string stored)
        {
            var asm = new Asm();
            asm.Op(SetString, GlobalString(1), asm.S(damaged));
            asm.Op(SetString, GlobalString(2), asm.S(emptyAsSpace));
            asm.Op(SetString, GlobalString(3), asm.S(emptyFilled));
            asm.Op(StringEquals, new Operand(1, 0), GlobalString(3), asm.S(compared));
            asm.Op(SetString, GlobalString(4), asm.S(stored));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("STINIT", Build("週送り。", "", "", "", "剣"),
            Build("週り。", " ", "to stop exploring any time.", " ", "Sword")));

        Assert.Equal("週り。", Assert.Single(report.Damage.LostCharacters.Samples).Translated);
        Assert.Equal(1, report.Damage.EmptyWrittenAsSpace.Count);
        Assert.Equal("to stop exploring any time.", Assert.Single(report.Literals.EmptyFilled.Samples).Translated);
        Assert.Equal("string-equals", Assert.Single(report.Literals.FeedCompares).Opcode);
        Assert.Equal(1, report.Literals.ChangedByOpcode["set-string"]);
        Assert.Equal(0, report.Unjoined.Unrepresentable.Count);

        TextCatalog catalog = Catalog(import);
        Assert.Equal("Sword", Present(catalog, "剣"));
        Assert.Equal("週送り。", Present(catalog, "週送り。"));
        Assert.Single(catalog.Entries);
    }

    [Fact]
    public void StoredLiteralsPreferATranslationOverAnUntranslatedCopy()
    {
        Asm Build(params string[] values)
        {
            var asm = new Asm();
            foreach (string value in values) asm.Op(SetString, GlobalString(1), asm.S(value));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(
            ("SC0004", Build("シーン回想終了", "シーン回想終了"), Build("シーン回想終了", "シン回想終了")),
            ("SP0001", Build("シーン回想終了"), Build("Return to Gallery")));

        TextCatalog catalog = Catalog(import);
        // The untranslated copies get no entry of their own, so SC0004 presents the translation too.
        Assert.Equal("Return to Gallery", Present(catalog, "シーン回想終了", "SC0004", 0));
        Assert.Equal(2, report.Literals.UntranslatedInInstall.Count);
        VerifyCounts verify = ImportVerifier.Verify(catalog, import.Occurrences);
        Assert.Equal((0, 2), (verify.Mismatches.Count, verify.StoredLiteralsTranslatedAnyway.Count));
    }

    [Fact]
    public void AStoredLiteralIsScopedByTheInstructionThatStoresIt()
    {
        Asm Build(string local, params string[] values)
        {
            var asm = new Asm();
            foreach (string value in values) asm.Op(SetString, GlobalString(0x6ba5), asm.S(value));
            asm.Op(SetString, LocalString(0), asm.S(local));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("QUINIT",
            Build("の調査", "の調査", "の調査", "の調査"), Build("Survey", "Forest survey", "Lake survey", "Forest survey")));

        TextCatalog catalog = Catalog(import);
        Assert.Equal("Forest survey", Present(catalog, "の調査"));
        // A set-string with two operands is 5 dwords long; the second store has a site entry.
        Assert.Equal("Lake survey", Present(catalog, "の調査", "QUINIT", 5));
        Assert.Equal("Forest survey", Present(catalog, "の調査", "QUINIT", 10));
        // Nothing records a store into a frame local, so that one can only take the global entry.
        ReportedConflict conflict = Assert.Single(report.Literals.UnscopableConflicts.Samples);
        Assert.Equal(("QUINIT", 15, "Survey"), (conflict.Script, conflict.Offset, conflict.Translated));
        VerifyCounts verify = ImportVerifier.Verify(catalog, import.Occurrences);
        Assert.Equal((0, 1), (verify.Mismatches.Count, verify.StoredLiteralConflicts.Count));
    }

    [Fact]
    public void AFilledEmptyLineFoldsIntoTheLineStoredToTheCellBefore()
    {
        static Asm Build(params string[] lines)
        {
            var asm = new Asm();
            for (int k = 0; k < 4; k++) asm.Op(SetString, GlobalString(0x24ab + k), asm.S(lines[k]));
            int jump = asm.Op(Jmp, I(0));
            asm.Target(jump, 0, asm.Here);
            // A block starts here, so nothing before it is the line before.
            asm.Op(SetString, GlobalString(0x24af), asm.S(lines[4]));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("STINIT", Build("一行目", "二行目", "", "", ""),
            Build("Line one", "Line two ", "three", "four", "five")));

        Assert.Equal("Line two\nthree\nfour", Present(Catalog(import), "二行目"));
        Assert.Equal((2, 1), (report.Literals.EmptyFolded.Count, report.Literals.EmptyFilled.Count));
        Assert.Equal(23, report.Literals.EmptyFilled.Samples.Single().Offset);
        Assert.Equal(0, ImportVerifier.Verify(Catalog(import), import.Occurrences).Mismatches.Count);
    }

    [Fact]
    public void VariableRunsBecomeNumberedPlaceholders()
    {
        static Asm Build(string tail)
        {
            var asm = new Asm();
            asm.Op(ShowText, I(0), GlobalString(7));
            asm.Op(ShowText, I(0), asm.S(tail));
            asm.Op(WaitForInput, I(0));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("SC0005", Build("を手に入れた"), Build(" was obtained.")));

        Assert.Equal((1, 0), (report.Messages.WithVariables, report.Messages.VariableOnly));
        TextEntry pattern = Assert.Single(Catalog(import).Patterns);
        Assert.Equal(("{v1}を手に入れた", "{v1} was obtained.", "SC0005"), (pattern.Source, pattern.Text, pattern.Script));
        Assert.Equal(0, ImportVerifier.Verify(Catalog(import), import.Occurrences).Mismatches.Count);
    }

    [Fact]
    public void ScriptWithChangedCodeDoesNotAlign()
    {
        Asm Build(int value)
        {
            var asm = new Asm();
            asm.Say("はい");
            asm.Op(SetString, GlobalString(value), asm.S("x"));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("SC0006", Build(1), Build(2)));

        NotAlignedScript failure = Assert.Single(report.Scripts.NotAligned);
        Assert.Equal(("set-string", "operand 1 differs"), (failure.Failure.BaseOpcode, failure.Failure.Reason));
        Assert.Empty(import.Occurrences);
    }

    // ---- composed strings (R4) ----------------------------------------------------------------------------------

    [Fact]
    public void AComposedStringBecomesAPatternOfTheComposingScript()
    {
        static Asm Build(string head, string tail)
        {
            var asm = new Asm();
            asm.Op(Concat, LocalString(0), GlobalString(3), asm.S(head));
            asm.Op(Concat, LocalString(0), LocalString(0), GlobalString(4));
            asm.Op(Concat, LocalString(0), LocalString(0), asm.S(tail));
            asm.Op(SetString, GlobalString(9), LocalString(0));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("SHOWCC", Build("が『", "』の称号を獲得"), Build(" has acquired the title 『", "』")));

        TextCatalog catalog = Catalog(import);
        TextEntry pattern = Assert.Single(catalog.Patterns);
        Assert.Equal(("{v1}が『{v2}』の称号を獲得", "{v1} has acquired the title 『{v2}』", "SHOWCC", (int?)null),
            (pattern.Source, pattern.Text, pattern.Script, pattern.At));
        Assert.Equal((1, 1, 1), (report.Composed.Stores, report.Composed.Templates, report.Composed.Entries));
        VerifyCounts verify = ImportVerifier.Verify(catalog, import.Occurrences);
        Assert.Equal((1, 0), (verify.CheckedComposed, verify.Mismatches.Count));

        // A pattern of the store's own site wins over the script's, so it shadows the composed string: verify fills
        // the source with sample values and finds that the catalog presents something else.
        TextOccurrence store = import.Occurrences.Single(o => o.Kind == OccurrenceKind.Composed);
        Assert.True(TextEntry.TryCreate("{v1}の称号を獲得", "Title: {v1}", out TextEntry shadow, out _));
        TextCatalog shadowed = TextCatalog.Build("en",
            catalog.Entries.Append(shadow with { Script = "SHOWCC", At = store.Offset }));
        ReportedMismatch mismatch = Assert.Single(ImportVerifier.Verify(shadowed, import.Occurrences).Mismatches.Samples);
        Assert.Equal("Composed by sample", mismatch.Kind);
    }

    [Fact]
    public void EachPathToAStoreGivesItsOwnTemplate()
    {
        static Asm Build(string minus, string plus)
        {
            var asm = new Asm();
            asm.Op(ToString, LocalString(0), GlobalInt(5));
            int branch = asm.Op(Jcc, GlobalInt(6), I(NoTarget), I(0));
            asm.Op(Concat, LocalString(0), asm.S(minus), LocalString(0));
            int skip = asm.Op(Jmp, I(0));
            asm.Target(branch, 2, asm.Here);
            asm.Op(Concat, LocalString(0), asm.S(plus), LocalString(0));
            asm.Target(skip, 0, asm.Here);
            asm.Op(SetString, GlobalString(9), LocalString(0));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("SHOWCC", Build("基本出撃コスト－", "基本出撃コスト＋"),
            Build("Command Cost -", "Command Cost +")));

        TextCatalog catalog = Catalog(import);
        Assert.Equal(new[]
            {
                ("基本出撃コスト＋{v1:num}", "Command Cost +{v1}"), ("基本出撃コスト－{v1:num}", "Command Cost -{v1}"),
            },
            catalog.Patterns.Select(p => (p.Source, p.Text)).OrderBy(p => p.Source, StringComparer.Ordinal));
        Assert.All(catalog.Patterns, p => Assert.Equal(("SHOWCC", (int?)null), (p.Script, p.At)));
        Assert.Equal((1, 2, 2), (report.Composed.Stores, report.Composed.Templates, report.Composed.Entries));
        VerifyCounts verify = ImportVerifier.Verify(catalog, import.Occurrences);
        Assert.Equal((2, 0), (verify.CheckedComposed, verify.Mismatches.Count));
    }

    [Fact]
    public void AWalkGivesUpAtALoopOrTooManyPathsAndReportsTheStore()
    {
        static Asm Loop(string start, string more)
        {
            var asm = new Asm();
            asm.Op(SetString, LocalString(0), asm.S(start));
            int top = asm.Here;
            asm.Op(Concat, LocalString(0), LocalString(0), asm.S(more));
            asm.Op(Jcc, GlobalInt(6), I(NoTarget), I(top));
            asm.Op(SetString, GlobalString(9), LocalString(0));
            asm.Op(Exit);
            return asm;
        }

        // Four optional parts: sixteen paths, more than a walk follows.
        static Asm Paths(string part)
        {
            var asm = new Asm();
            asm.Op(ToString, LocalString(0), GlobalInt(5));
            for (int k = 0; k < 4; k++)
            {
                int branch = asm.Op(Jcc, GlobalInt(6 + k), I(NoTarget), I(0));
                asm.Op(Concat, LocalString(0), LocalString(0), asm.S(part));
                asm.Target(branch, 2, asm.Here);
            }
            asm.Op(SetString, GlobalString(9), LocalString(0));
            asm.Op(Exit);
            return asm;
        }

        var (import, report) = Import(("FIELD", Loop("星", "★"), Loop("Star", "*")),
            ("SALE", Paths("個"), Paths(" pcs")));

        Assert.Equal(new[] { ("FIELD", "loop"), ("SALE", "paths") },
            report.Composed.Skipped.Samples.Select(s => (s.Script, s.Reason)));
        // The path that leaves the loop at once still gives its template; with literals only, it is an exact entry.
        TextCatalog catalog = Catalog(import);
        Assert.Equal("Star*", Present(catalog, "星★", "FIELD"));
        Assert.Equal(0, ImportVerifier.Verify(catalog, import.Occurrences).Mismatches.Count);
    }
}
