using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Localization;
using Xunit;

/// <summary>The presenter (localization spec §4, §6.4), loading a language from a text root (§9.4), the
/// pseudo-language and the miss log (§14), and where presented text may break (§7.1). Gate 3 is the loader and
/// presenter half here; scope precedence inside one catalog is in <see cref="LocalizationTextModelTests"/>.</summary>
public class TextPresenterTests : IDisposable
{
    private const string Fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000001";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-text-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private TextLanguage Load(string tag, TextLoadReport? report = null, string? installed = Fingerprint,
                              bool patched = false)
        => TextRootLoader.Load(_root, tag,
            new TextSiteBinder(_ => installed, patched ? _ => (true, null) : null), report ?? new TextLoadReport());

    private static string? Present(TextPresenter presenter, string source, string script = "SC0100", int at = 0x10)
        => presenter.Present(source, new TextSite(script, at))?.Text;

    // ---- loading and resolution (gate 3) -------------------------------------------------------------------------

    [Fact]
    public void ALanguageFallsBackToItsParentThenToSource()
    {
        WriteFile("pt/base.toml", """
            [[text]]
            source = "はい"
            text = "Sim"

            [[text]]
            source = "いいえ"
            text = "Não"
            """);
        WriteFile("pt-br/base.toml", """
            [[text]]
            source = "はい"
            text = "Sim!"
            """);

        var presenter = new TextPresenter(Load("pt-BR"));

        Assert.Equal(new[] { "pt-br", "pt" }, presenter.Language.Chain.Select(c => c.Language));
        Assert.Equal("Sim!", Present(presenter, "はい"));
        Assert.Equal("Não", Present(presenter, "いいえ"));
        Assert.Null(Present(presenter, "黒石"));
    }

    [Fact]
    public void AScopedEntryOfTheLanguageBeatsTheFallbacksButNotItsOwnSite()
    {
        WriteFile("pt/SC0100.toml", $$"""
            script = "SC0100"
            fingerprint = "{{Fingerprint}}"

            [[text]]
            source = "はい"
            at = 0x10
            text = "Sim (site, pt)"
            """);
        WriteFile("pt-br/_global.toml", """
            [[text]]
            source = "はい"
            text = "Sim (global, pt-br)"
            """);

        var presenter = new TextPresenter(Load("pt-br"));

        // The whole chain of pt-br is tried before pt: a language's global entry wins over its fallback's site.
        Assert.Equal("Sim (global, pt-br)", Present(presenter, "はい"));
    }

    [Fact]
    public void LaterFilesWinAtEqualScopeAndTheOverrideIsReported()
    {
        WriteFile("en/a.toml", """
            [[text]]
            source = "はい"
            text = "Yes"
            """);
        WriteFile("en/b/c.toml", """
            [[text]]
            source = "はい"
            text = "Yeah"
            """);
        var report = new TextLoadReport();

        var presenter = new TextPresenter(Load("en", report));

        Assert.Equal("Yeah", Present(presenter, "はい"));
        TextCatalogOverride replaced = Assert.Single(report.Overrides);
        Assert.Equal(("en/a.toml:1", "en/b/c.toml:1"), (replaced.Replaced.Origin, replaced.Winner.Origin));
        Assert.Contains("text en: 1 entries from 2 files", report.Lines());
    }

    [Fact]
    public void AMalformedFileStopsTheLoadWithEveryProblemAndItsLine()
    {
        WriteFile("en/a.toml", """
            [[text]]
            source = "はい"
            text = "Yes"
            colour = "red"
            """);
        WriteFile("en/b.toml", "[[text]]\nsource = \"x\n");

        var error = Assert.Throws<TextCatalogException>(() => Load("en"));

        Assert.Equal(2, error.Problems.Count);
        Assert.Equal("en/a.toml:1: unknown key 'colour'", error.Problems[0]);
        Assert.StartsWith("en/b.toml:2: not valid TOML", error.Problems[1]);
    }

    [Theory]
    [InlineData("sha256:00000000000000000000000000000000000000000000000000000000000000ff", false,
        "the base script's fingerprint differs")]
    [InlineData(null, false, "the script is not installed")]
    [InlineData(Fingerprint, true, "a mod replaces the script")]
    public void SiteEntriesBindOnlyToTheirUnpatchedBaseScript(string? installed, bool patched, string reason)
    {
        WriteFile("en/SC0100.toml", $$"""
            script = "SC0100"
            fingerprint = "{{Fingerprint}}"

            [[text]]
            source = "はい"
            text = "Yes"

            [[text]]
            source = "はい"
            at = 0x10
            text = "Right."
            """);
        var report = new TextLoadReport();

        var presenter = new TextPresenter(Load("en", report, installed, patched));

        Assert.Equal("Yes", Present(presenter, "はい"));
        Assert.Equal(("en/SC0100.toml", 1, reason), Assert.Single(report.DroppedSites));
    }

    [Fact]
    public void ASiteEntryAppliesOnlyWhereTheBaseScriptRuns()
    {
        WriteFile("en/SC0100.toml", $$"""
            script = "SC0100"
            fingerprint = "{{Fingerprint}}"

            [[text]]
            source = "はい"
            text = "Yes"

            [[text]]
            source = "はい"
            at = 0x10
            text = "Right."
            """);

        var presenter = new TextPresenter(Load("en"));

        Assert.Equal("Right.", Present(presenter, "はい"));
        Assert.Equal("Yes", Present(presenter, "はい", at: 0x20));
        Assert.Equal("Yes", presenter.Present("はい", new TextSite("SC0100", 0x10, IsBaseScript: false))?.Text);
    }

    [Fact]
    public void InAPatchedScriptASiteEntryMovesWithItsInstruction()
    {
        WriteFile("en/SC0100.toml", $$"""
            script = "SC0100"
            fingerprint = "{{Fingerprint}}"

            [[text]]
            source = "はい"
            at = 0x8
            text = "Before the patch."

            [[text]]
            source = "はい"
            at = 0x20
            text = "After the patch."

            [[text]]
            source = "はい"
            at = 0x12
            text = "Replaced by the patch."
            """);
        // A patch replaced 0x10..0x14 with ten dwords: what follows moved by six.
        var map = new Ome.Mods.Linking.ScriptOffsetMap(
            [new Ome.Mods.Linking.OffsetRun(0, 0x10, 0), new Ome.Mods.Linking.OffsetRun(0x14, 0x40, 6)]);
        var report = new TextLoadReport();

        var presenter = new TextPresenter(TextRootLoader.Load(_root, "en",
            new TextSiteBinder(_ => Fingerprint, script => script == "SC0100" ? (true, map) : (false, null)), report));

        Assert.Equal("Before the patch.", Present(presenter, "はい", at: 0x8));
        Assert.Equal("After the patch.", Present(presenter, "はい", at: 0x26));
        Assert.Null(Present(presenter, "はい", at: 0x20));
        Assert.Equal(("en/SC0100.toml", 1, "a patch replaces the instruction"), Assert.Single(report.DroppedSites));
    }

    [Fact]
    public void LaterSourcesWinPerEntryAndPerLanguageKey()
    {
        WriteFile("mods/en/a.toml", """
            [[text]]
            source = "はい"
            text = "Yes"

            [[text]]
            source = "いいえ"
            text = "No"

            [[text]]
            source = "まだ"
            """);
        WriteFile("root/en/b.toml", """
            [[text]]
            source = "はい"
            text = "Yeah"

            [[text]]
            source = "いいえ"
            text = "Nope"
            obsolete = true
            """);
        WriteFile("root/languages.toml", """
            [[language]]
            tag = "en"
            line_break = "anywhere"

            [[language.font]]
            requested = ["*"]
            use = ["fonts/Body.ttf", "Georgia"]
            """);
        WriteFile("root/fonts/Body.ttf", "font");
        LanguageSettings packaged = new("en") { Name = "English", GlyphDelayScale = 0.5, LineBreak = LineBreakMode.Words };
        TextSource[] sources = [new(Path.Combine(_root, "mods"), [packaged], "mods"), new(Path.Combine(_root, "root"))];
        var report = new TextLoadReport();

        TextLanguage language = TextRootLoader.Load(sources, "en", new TextSiteBinder(_ => null), report);

        var presenter = new TextPresenter(language);
        Assert.Equal("Yeah", Present(presenter, "はい"));
        Assert.Equal("No", Present(presenter, "いいえ"));
        Assert.Null(Present(presenter, "まだ"));
        Assert.Equal(("mods:en/a.toml:1", "en/b.toml:1"),
                     (Assert.Single(report.Overrides).Replaced.Origin, report.Overrides[0].Winner.Origin));
        Assert.Equal((LineBreakMode.Anywhere, 0.5), (language.LineBreak, language.GlyphDelayScale));
        TextFontMapping fonts = Assert.Single(language.Fonts);
        Assert.Equal([Path.GetFullPath(Path.Combine(_root, "root", "fonts", "Body.ttf")), "Georgia"], fonts.Use);
        Assert.True(fonts.Applies("ＭＳ ゴシック"));
    }

    [Fact]
    public void ALanguagesFileThatNamesAMissingFontStopsTheLoad()
    {
        WriteFile("en/a.toml", "[[text]]\nsource = \"はい\"\ntext = \"Yes\"\n");
        WriteFile("languages.toml",
            "[[language]]\ntag = \"en\"\n[[language.font]]\nrequested = [\"*\"]\nuse = [\"fonts/None.ttf\"]\n");

        var error = Assert.Throws<TextCatalogException>(() => Load("en"));

        Assert.Contains("'fonts/None.ttf' is not in", Assert.Single(error.Problems));
    }

    // ---- presenter, miss log, pseudo-language ---------------------------------------------------------------------

    [Fact]
    public void MissesAreCachedButCountedEachTime()
    {
        var misses = new TextMissLog();
        var presenter = new TextPresenter(new TextLanguage("en", [TextCatalog.Build("en", [TextEntry.Literal("はい", "Yes")])]),
            misses: misses);

        Assert.Equal("Yes", Present(presenter, "はい"));
        Assert.Null(Present(presenter, "いいえ"));
        Assert.Null(Present(presenter, "いいえ"));
        Assert.Null(Present(presenter, "いいえ", at: 0x20));
        Assert.Null(Present(presenter, ""));

        var written = new StringWriter();
        misses.WriteJsonLines(written);
        Assert.Equal(new[]
        {
            """{"source":"いいえ","script":"SC0100","at":"0x10","count":2}""",
            """{"source":"いいえ","script":"SC0100","at":"0x20","count":1}""",
        }, written.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')));
    }

    [Fact]
    public void ThePseudoLanguageBracketsAndLengthensEveryStringAndLogsItAsAMiss()
    {
        var misses = new TextMissLog();
        var presenter = new TextPresenter(TextLanguage.Pseudo, [new PseudoLanguageFilter()], misses);

        Assert.Equal("[あの黒石を拾った。~~~]", Present(presenter, "あの黒石を拾った。"));
        Assert.Equal("[A~]", Present(presenter, "A"));
        Assert.Equal(2, misses.Count);
    }

    [Fact]
    public void AFilterSeesTheCatalogsResult()
    {
        var presenter = new TextPresenter(
            new TextLanguage("en", [TextCatalog.Build("en", [TextEntry.Literal("はい", "Yes")])]),
            [new PseudoLanguageFilter()]);

        Assert.Equal("[Yes~]", Present(presenter, "はい"));
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("pt-BR", true)]
    [InlineData("zh_Hant", true)]
    [InlineData("x-pseudo", true)]
    [InlineData("english", false)]
    [InlineData("e", false)]
    [InlineData("en-", false)]
    public void LanguageTagsAreCheckedAndNormalized(string tag, bool wellFormed)
    {
        Assert.Equal(wellFormed, LanguageTag.IsWellFormed(tag));
        Assert.Equal(new[] { "zh-hant", "zh" }, LanguageTag.Parents("zh-Hant-TW"));
        Assert.Empty(LanguageTag.Parents("x-pseudo"));
    }

    // ---- line breaks (spec §7.1) ----------------------------------------------------------------------------------

    private static string Marked(string text, LineBreakMode mode = LineBreakMode.Words)
    {
        bool[] allowed = PresentedLineBreaks.Opportunities(text, mode);
        return string.Concat(text.Select((c, i) => (allowed[i] ? "|" : "") + c));
    }

    [Fact]
    public void WordsBreakAfterSpacesAndHyphensButNotBeforeClosingPunctuation()
    {
        Assert.Equal("I |picked |up |a |well-|known |stone.", Marked("I picked up a well-known stone."));
        Assert.Equal("\"Yes,\" |she |said |(quietly).", Marked("\"Yes,\" she said (quietly)."));
        Assert.Equal("Wait   |- |no.", Marked("Wait   - no."));
    }

    [Fact]
    public void CjkBreaksBetweenCharactersWithKinsoku()
    {
        Assert.Equal("「あ|の|黒|石|を|拾っ|た。」|Ok", Marked("「あの黒石を拾った。」Ok"));
        Assert.Equal("ルー|ク", Marked("ルーク"));
    }

    [Fact]
    public void AnywhereBreaksBetweenLettersAndNewlinesAreNotOpportunities()
    {
        Assert.Equal("a|b|c.", Marked("abc.", LineBreakMode.Anywhere));
        Assert.Equal("one\ntwo |three", Marked("one\ntwo three"));
        Assert.Equal("𠮷|𠮷", Marked("𠮷𠮷"));
    }
}

/// <summary>The VM's two presentation sites (localization spec §5.2): the host gets both forms, and everything the VM
/// keeps stays source text.</summary>
public class PresentationSiteTests
{
    private static (RecordingHost Host, VirtualMachine Vm) Run(TextPresenter? presenter)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand I(long value) => new(0, value);
        static Operand S(int index) => new(2, index);
        var script = ScriptAssembler.Assemble(table, "SC0100",
            new List<(int, Operand[])>
            {
                (0x6e, new[] { I(0), S(0) }),
                (0x196, new[] { I(0), S(1), S(2) }),
                (0x6e, new[] { I(0), S(3) }),
                (0x204, new[] { I(13), I(1), I(1), S(4) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "はい", "黒石", "くろいし", "未訳", "いいえ" });
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host) { Presenter = presenter };
        vm.Run();
        return (host, vm);
    }

    private static TextPresenter English(TextMissLog misses) => new(new TextLanguage("en", [TextCatalog.Build("en",
        [TextEntry.Literal("はい", "Yes"), TextEntry.Literal("黒石", "the black stone"), TextEntry.Literal("いいえ", "No")])]),
        misses: misses);

    [Fact]
    public void TheHostDrawsPresentedTextAndTheVmKeepsSource()
    {
        var misses = new TextMissLog();

        var (host, vm) = Run(English(misses));

        Assert.Equal(new[] { "はい", "黒石", "未訳" }, host.LiveTextRuns.Select(run => run.Run.Text));
        Assert.Equal(new[] { "Yes", "the black stone", null },
                     host.LiveTextRuns.Select(run => run.Run.Presented?.Text));
        // Presented text carries no ruby.
        Assert.All(host.LiveTextRuns, run => Assert.Null(run.Run.Ruby));
        Assert.Equal(new[] { "はい", "黒石", "未訳" }, vm.TextHistory.Records.Select(record => record.Text));
        Assert.Equal(new[] { "はい", "黒石", "未訳" }, vm.Emitted.Select(emitted => emitted.Text));
        Assert.Equal((13, 1, 1, "No"), Assert.Single(host.SurfaceStrings));
        var written = new StringWriter();
        misses.WriteJsonLines(written);
        Assert.StartsWith("""{"source":"未訳","script":"SC0100","at":"0x""", written.ToString());
        Assert.Equal(1, misses.Count);
    }

    [Theory]
    [InlineData(0.5, 25)]
    [InlineData(2.0, 100)]
    [InlineData(0.001, 1)]
    public void PresentedRunsRevealAtTheLanguagesGlyphDelayScale(double scale, int presented)
    {
        var language = new TextLanguage("en", [TextCatalog.Build("en",
            [TextEntry.Literal("はい", "Yes"), TextEntry.Literal("黒石", "the black stone")])]) { GlyphDelayScale = scale };

        var (host, _) = Run(new TextPresenter(language));

        // The host's delay is 50 ms; a run with no entry keeps it.
        Assert.Equal(new[] { presented, presented, 50 }, host.LiveTextRuns.Select(run => run.GlyphDelayMilliseconds));
    }

    [Fact]
    public void WithNoPresenterNothingChanges()
    {
        var (host, _) = Run(null);

        Assert.All(host.LiveTextRuns, run => Assert.Null(run.Run.Presented));
        Assert.Equal("くろいし", host.LiveTextRuns[1].Run.Ruby);
        Assert.Equal((13, 1, 1, "いいえ"), Assert.Single(host.SurfaceStrings));
    }
}
