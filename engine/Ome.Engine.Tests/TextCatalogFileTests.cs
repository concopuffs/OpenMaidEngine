using System.Linq;
using Ome.Engine.Localization;
using Ome.Mods.Localization;
using Xunit;

/// <summary>Catalog files (localization spec §9.3): what the reader accepts and refuses, and the writer's round
/// trip.</summary>
public class TextCatalogFileTests
{
    private const string Fingerprint = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ReadsScopesAndPresentationKeys()
    {
        TextCatalogFile file = TextCatalogFileReader.Parse($$"""
            script = "SC0100.BIN"
            fingerprint = "{{Fingerprint}}"

            [[text]]
            source = "はい"
            text = "Yes"

            [[text]]
            script = ""
            source = "いいえ"
            text = "No"
            align = "center"
            fit = "shrink"
            measure = "source"
            cells = 12
            row_gap = 5
            note = "a choice"

            [[text]]
            source = "{who}が来た"
            at = 0x2a43
            text = "{who} came"
            """, "sc0100.toml");

        Assert.Equal("SC0100", file.Script);
        Assert.Equal(Fingerprint, file.Fingerprint);
        Assert.Equal(3, file.Entries.Count);
        TextEntry yes = file.Entries[0], no = file.Entries[1], site = file.Entries[2];
        Assert.Equal(("SC0100", (int?)null, "sc0100.toml:4"), (yes.Script, yes.At, yes.Origin));
        Assert.Null(no.Script);
        Assert.Equal((TextAlign.Center, TextFit.Shrink, TextMeasure.Source, 12, 5, "a choice"),
            (no.Align, no.Fit, no.Measure!.Value, no.Cells!.Value, no.RowGap!.Value, no.Note));
        Assert.Equal(("SC0100", 0x2a43), (site.Script, site.At!.Value));
        Assert.True(site.IsPattern);
    }

    [Fact]
    public void ReportsEveryProblemWithItsLine()
    {
        var error = Assert.Throws<TextCatalogException>(() => TextCatalogFileReader.Parse("""
            [[text]]
            source = "a"
            text = "b"
            colour = "red"

            [[text]]
            source = "c"
            obsolete = "yes"

            [[text]]
            source = "{x}c"
            text = "{x}d"

            [[text]]
            script = "SC0100"
            source = "e"
            text = "f"
            at = 16

            [[text]]
            source = "g"
            text = "h"
            align = "middle"

            [[text]]
            source = "i"
            text = "j"
            row_gap = -1
            """, "bad.toml"));

        Assert.Equal(new[]
        {
            "bad.toml:1: unknown key 'colour'",
            "bad.toml:6: obsolete must be true or false",
            "bad.toml:10: a pattern that starts or ends with a placeholder needs a script scope",
            "bad.toml:14: an entry with 'at' needs the file's 'fingerprint' of the base script",
            "bad.toml:20: align must be \"start\", \"center\" or \"end\", not \"middle\"",
            "bad.toml:25: row_gap must be 0 to 256",
        }, error.Problems);
    }

    [Fact]
    public void RefusesUnknownTopLevelKeysAndWrongTypes()
    {
        var error = Assert.Throws<TextCatalogException>(() => TextCatalogFileReader.Parse("""
            langauge = "en"
            fingerprint = "md5:00"

            [[text]]
            source = 3
            text = "x"
            """, "top.toml"));

        Assert.Contains("top.toml: fingerprint must be 'sha256:' and 64 lowercase hex digits", error.Problems);
        Assert.Contains("top.toml:4: source must be a string", error.Problems);
        Assert.Contains("top.toml: unknown key 'langauge'", error.Problems);
    }

    [Fact]
    public void ReportsTomlSyntaxErrorsWithTheirLine()
    {
        var error = Assert.Throws<TextCatalogException>(() =>
            TextCatalogFileReader.Parse("[[text]]\nsource = \"a\ntext = \"b\"\n", "broken.toml"));
        Assert.StartsWith("broken.toml:2: not valid TOML", Assert.Single(error.Problems));
    }

    [Fact]
    public void ReadsEngineEntriesAndChecksTheirKeys()
    {
        TextCatalogFile file = TextCatalogFileReader.Parse("""
            [[engine]]
            key = "shell.menu.quit"
            text = "Quit"
            """, "engine.toml");
        Assert.Equal(new EngineTextEntry("shell.menu.quit", "Quit", "engine.toml:1"), Assert.Single(file.EngineEntries));

        var error = Assert.Throws<TextCatalogException>(() => TextCatalogFileReader.Parse("""
            [[engine]]
            key = "Shell Menu"
            text = "Quit"
            """, "engine.toml"));
        Assert.Contains("must be lowercase words", Assert.Single(error.Problems));
    }

    [Fact]
    public void WriterOutputReadsBackToTheSameEntries()
    {
        TextEntry[] entries =
        [
            TextEntry.Literal("「はい」", "\"Yes,\" she said.\n\tThen a backslash: \\ and a bell \u0007"),
            TextEntry.Literal("{literal braces}", "{kept}") with { Script = null },
            TextEntry.Create("{v1}を手に入れた", "Got {v1}") with { Script = "SC0100" },
            TextEntry.Literal("はい", "Yep") with
            {
                Script = "SC0100", At = 0x2a43, Align = TextAlign.End, Fit = TextFit.Shrink,
                Measure = TextMeasure.Presented, Cells = 7, RowGap = 3, Note = "two breaks",
            },
        ];
        var file = new TextCatalogFile("SC0100", Fingerprint, entries, [new EngineTextEntry("a.b", "c")]);

        string written = TextCatalogFileWriter.Write(file, ["generated for a test"]);
        TextCatalogFile read = TextCatalogFileReader.Parse(written, "round.toml");

        Assert.StartsWith("# generated for a test\n", written.Replace("\r\n", "\n"));
        Assert.Equal("SC0100", read.Script);
        Assert.Equal(Fingerprint, read.Fingerprint);
        Assert.Equal(entries.Select(Shape), read.Entries.Select(Shape));
        Assert.Equal("c", Assert.Single(read.EngineEntries).Text);
    }

    [Fact]
    public void TemplatesAndObsoleteEntriesStayInTheFileButAreNotApplied()
    {
        TextCatalogFile file = TextCatalogFileReader.Parse("""
            script = "SC0100"

            [[text]]
            source = "はい"

            [[text]]
            source = "いいえ"
            text = "No"
            obsolete = true

            [[text]]
            source = "{v1:num}個"

            [[text]]
            source = "剣"
            text = "Sword"
            obsolete = false
            """, "items.toml");

        Assert.Equal([("はい", false, false), ("いいえ", true, true), ("{v1:num}個", false, false), ("剣", true, false)],
                     file.Items.Select(item => (item.Entry.Source, item.HasText, item.Obsolete)));
        Assert.Equal(["剣"], file.Entries.Select(entry => entry.Source));
        // A template is checked as if its text were its source; a pattern's kinds are not text.
        Assert.Equal("{v1}個", file.Items[2].Entry.Text);

        string written = TextCatalogFileWriter.Write(file);
        TextCatalogFile read = TextCatalogFileReader.Parse(written, "items.toml");
        Assert.Equal(file.Items.Select(item => (item.Entry.Source, item.Entry.Text, item.HasText, item.Obsolete)),
                     read.Items.Select(item => (item.Entry.Source, item.Entry.Text, item.HasText, item.Obsolete)));
        Assert.Equal(1, written.Split("obsolete = true").Length - 1);
        Assert.Equal(2, written.Split("\ntext = ").Length - 1);
    }

    [Fact]
    public void ALanguagesFileReadsTheMilestoneFourKeys()
    {
        LanguageSettings language = Assert.Single(TextCatalogFileReader.ParseLanguages("""
            [[language]]
            tag = "pt-BR"
            name = "Português"
            fallback = ["pt"]
            line_break = "anywhere"
            measure = "source"
            digits = "ascii"
            glyph_delay_scale = 1.5
            min_fit_scale = 0.75

            [[language.font]]
            requested = ["ＭＳ ゴシック"]
            use = ["fonts/Sans.otf", "Noto Sans"]

            [[language.font]]
            requested = ["*"]
            use = ["Georgia"]
            """, "languages.toml"));

        Assert.Equal(("pt-br", "Português", 1.5, 0.75), (language.Tag, language.Name, language.GlyphDelayScale, language.MinFitScale));
        Assert.Equal(["pt"], language.Fallback);
        Assert.Equal((LineBreakMode.Anywhere, TextMeasure.Source, DigitsMode.Ascii),
                     (language.LineBreak!.Value, language.Measure!.Value, language.Digits!.Value));
        Assert.Equal(new[] { "fonts/Sans.otf|Noto Sans", "Georgia" },
                     language.Fonts!.Select(font => string.Join("|", font.Use)));

        var (merged, replaced) = language.Merge(new LanguageSettings("pt-br") { GlyphDelayScale = 1.0, Name = "PT" });
        Assert.Equal(["name", "glyph_delay_scale"], replaced);
        Assert.Equal(("PT", 1.0, 0.75), (merged.Name, merged.GlyphDelayScale, merged.MinFitScale));
    }

    private static object Shape(TextEntry entry)
        => (entry.Source, entry.Text, entry.Script, entry.At, entry.Align, entry.Fit, entry.Measure, entry.Cells,
            entry.RowGap, entry.Note);
}
