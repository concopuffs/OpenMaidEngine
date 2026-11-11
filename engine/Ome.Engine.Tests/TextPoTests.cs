using System.Text;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Localization;
using Ome.Mods.Localization;
using Ome.Mods.Tools;

/// <summary>PO conversion (localization spec §13; milestone 6): the PO reader and writer, and <c>omp text
/// po-export</c>/<c>po-import</c> over synthetic catalogs.</summary>
public sealed class TextPoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-po-" + Guid.NewGuid().ToString("N"));
    private static readonly EngineTextKeys Keys =
        new(ShellStrings.Embedded(ShellStrings.FallbackLanguage)!, ShellStrings.Embedded, ShellStrings.EntryProblem);
    private const string Fingerprint = "sha256:2222222222222222222222222222222222222222222222222222222222222222";

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Folder => Path.Combine(_root, "text", "de");

    private void WriteFile(string name, string text)
    {
        string path = Path.Combine(Folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"), new UTF8Encoding(false));
    }

    private string ReadFile(string name) => File.ReadAllText(Path.Combine(Folder, name));

    // ------------------------------------------------------------------------------------------------- the format

    [Fact]
    public void WrittenMessagesReadBackWithTheirContextCommentsAndEscapes()
    {
        PoEntry[] entries =
        [
            new(null, "はい", "Ja") { ExtractedComments = ["first line\nsecond line"], References = ["_global.toml:3"] },
            new("SC0100@0x2a43", "二行の\n文", "Zwei\nZeilen \"zitiert\"\t\\") { Flags = ["fuzzy"] },
            new("SC0100", "{item}を拾った", "") { Obsolete = true },
        ];
        string text = PoFile.Write("de", entries, ["a header"]);

        Assert.Contains("\"Content-Type: text/plain; charset=UTF-8\\n\"", text);
        Assert.Contains("\"Language: de\\n\"", text);
        Assert.Contains("msgid \"\"\n\"二行の\\n\"\n\"文\"\n", text);
        Assert.Contains("#~ msgctxt \"SC0100\"\n#~ msgid \"{item}を拾った\"\n#~ msgstr \"\"\n", text);
        IReadOnlyList<PoEntry> read = PoFile.Parse(text, "de.po");
        Assert.Equal(entries.Select(entry => (entry.Context, entry.Id, entry.Str, entry.Obsolete)),
                     read.Select(entry => (entry.Context, entry.Id, entry.Str, entry.Obsolete)));
        Assert.Equal(["first line", "second line"], read[0].ExtractedComments);
        Assert.Equal(["_global.toml:3"], read[0].References);
        Assert.True(read[1].Fuzzy);
    }

    [Fact]
    public void TheReaderTakesWhatGettextWrites()
    {
        IReadOnlyList<PoEntry> read = PoFile.Parse("""
            # translator note
            msgid ""
            msgstr "Content-Type: text/plain; charset=utf-8\n"

            #, fuzzy, python-format
            msgctxt "STATUS"
            msgid "A\x41\101"
            msgstr ""
            "B"
            "C"
            msgid "next"
            msgstr "x"
            """, "x.po");
        Assert.Equal([("STATUS", "AAA", "BC", true), (null, "next", "x", false)],
                     read.Select(entry => ((string?)entry.Context, entry.Id, entry.Str, entry.Fuzzy)));
        Assert.Equal(11, read[1].Line);
    }

    [Theory]
    [InlineData("msgid \"a\"\nmsgid_plural \"b\"\nmsgstr[0] \"c\"\n", "plural forms are not used")]
    [InlineData("msgid \"a\"\n", "msgid without msgstr")]
    [InlineData("msgid \"a\\q\"\nmsgstr \"\"\n", "unknown escape \\q")]
    [InlineData("msgid \"\"\nmsgstr \"Content-Type: text/plain; charset=ISO-8859-1\\n\"\n", "the file must be UTF-8")]
    [InlineData("msgid a\nmsgstr \"\"\n", "expected a quoted string")]
    [InlineData("msgfoo \"a\"\n", "unknown keyword 'msgfoo'")]
    public void FilesThatDoNotParseAreRefusedWithFileAndLine(string text, string reason)
    {
        var error = Assert.Throws<PoFileException>(() => PoFile.Parse(text, "bad.po"));
        Assert.Contains(error.Problems, problem => problem.StartsWith("bad.po:") && problem.Contains(reason));
    }

    // ------------------------------------------------------------------------------------------- export and import

    private void Catalogs()
    {
        WriteFile("_global.toml", """
            # Catalog template written by omp text extract.

            [[text]]
            source = "はい"
            text = "Ja"

            [[text]]
            source = "いいえ"
            note = "a button"

            [[text]]
            source = "古い"
            text = "alt"
            obsolete = true
            """);
        WriteFile("SC0100.toml", $"""
            script = "SC0100"
            fingerprint = "{Fingerprint}"

            [[text]]
            source = "武器ＬＶが{"{level:num}"}に上がった"
            cells = 30

            [[text]]
            source = "はい"
            at = 0x2a43
            text = "Jawohl"
            """);
        WriteFile("_engine.toml", """
            [[engine]]
            key = "exit.title"
            note = "ja: 終了"
            """);
    }

    [Fact]
    public void ExportWritesAMessagePerScopeAndSourceWithTheEnginesWordingForEngineEntries()
    {
        Catalogs();
        string po = Path.Combine(_root, "de.po");
        var console = new StringWriter();

        Assert.Equal(0, TextPoCommands.Export(Folder, "de", po, console, Keys));

        Assert.Contains("6 messages, 2 translated, 1 obsolete", console.ToString());
        IReadOnlyList<PoEntry> entries = PoFile.Parse(File.ReadAllText(po), "de.po");
        // Files in the loader's ordinal order: SC0100.toml, _engine.toml, _global.toml.
        Assert.Equal([("SC0100", "武器ＬＶが{level:num}に上がった", ""), ("SC0100@0x2a43", "はい", "Jawohl"),
                      ("engine:exit.title", "終了", ""), (null, "はい", "Ja"), (null, "いいえ", ""), (null, "古い", "alt")],
                     entries.Select(entry => (entry.Context, entry.Id, entry.Str)));
        Assert.True(entries[5].Obsolete);
        Assert.Equal(["a button"], entries[4].ExtractedComments);
        Assert.Equal(["ja: 終了"], entries[2].ExtractedComments);
        Assert.Equal(["SC0100.toml:8"], entries[1].References);
    }

    [Fact]
    public void ImportPutsTranslationsIntoTheirEntriesAndKeepsEveryOtherKey()
    {
        Catalogs();
        string po = Path.Combine(_root, "de.po");
        TextPoCommands.Export(Folder, "de", po, TextWriter.Null, Keys);
        string text = File.ReadAllText(po)
            .Replace("msgid \"いいえ\"\nmsgstr \"\"", "msgid \"いいえ\"\nmsgstr \"Nein\"")
            .Replace("msgid \"武器ＬＶが{level:num}に上がった\"\nmsgstr \"\"", "msgid \"武器ＬＶが{level:num}に上がった\"\nmsgstr \"Waffenstufe {level}\"")
            .Replace("msgid \"終了\"\nmsgstr \"\"", "msgid \"終了\"\nmsgstr \"Beenden\"")
            .Replace("msgctxt \"SC0100@0x2a43\"", "#, fuzzy\nmsgctxt \"SC0100@0x2a43\"")
            + "\nmsgctxt \"SC9999\"\nmsgid \"ない\"\nmsgstr \"nichts\"\n";
        File.WriteAllText(po, text);
        string before = ReadFile("SC0100.toml");
        var console = new StringWriter();

        Assert.Equal(0, TextPoCommands.Import(Folder, "de", po, console, Keys));

        string output = console.ToString();
        Assert.Contains("3 entries translated in 3 files, 1 unchanged; 0 untranslated and 1 fuzzy messages left out, 1 unmatched", output);
        Assert.Contains("SC9999 \"ない\": no catalog entry has this scope and source; not imported", output);
        TextCatalogFile global = TextCatalogFileReader.Read(Path.Combine(Folder, "_global.toml"), "de/_global.toml");
        Assert.Equal(["Ja", "Nein"], global.Entries.Select(entry => entry.Text));
        Assert.Equal("a button", global.Entries[1].Note);
        Assert.True(global.Items[2].Obsolete);
        Assert.StartsWith("# Catalog template written by omp text extract.", ReadFile("_global.toml"));
        TextCatalogFile script = TextCatalogFileReader.Read(Path.Combine(Folder, "SC0100.toml"), "de/SC0100.toml");
        Assert.Equal(("Waffenstufe {level}", 30), (script.Entries[0].Text, script.Entries[0].Cells));
        Assert.Equal(("Jawohl", 0x2a43), (script.Entries[1].Text, script.Entries[1].At));   // the fuzzy one is left
        Assert.Equal(Fingerprint, script.Fingerprint);
        Assert.NotEqual(before, ReadFile("SC0100.toml"));
        Assert.Equal("Beenden", TextCatalogFileReader.Read(Path.Combine(Folder, "_engine.toml"), "x").EngineEntries[0].Text);

        // Exported again and imported unchanged, nothing is written.
        TextPoCommands.Export(Folder, "de", po, TextWriter.Null, Keys);
        DateTime written = File.GetLastWriteTimeUtc(Path.Combine(Folder, "SC0100.toml"));
        console = new StringWriter();
        Assert.Equal(0, TextPoCommands.Import(Folder, "de", po, console, Keys));
        Assert.Contains("0 entries translated in 0 files, 5 unchanged", console.ToString());
        Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(Folder, "SC0100.toml")));
    }

    [Fact]
    public void ATranslationThatDoesNotFitItsEntryStopsTheImport()
    {
        Catalogs();
        WriteFile("_more.toml", "[[engine]]\nkey = \"value.milliseconds\"\n");
        string po = Path.Combine(_root, "de.po");
        TextPoCommands.Export(Folder, "de", po, TextWriter.Null, Keys);
        string text = File.ReadAllText(po);
        int engine = text.IndexOf("msgctxt \"engine:value.milliseconds\"", StringComparison.Ordinal);
        int empty = text.IndexOf("msgstr \"\"", engine, StringComparison.Ordinal);
        File.WriteAllText(po, (text[..empty] + "msgstr \"{1} ms\"" + text[(empty + "msgstr \"\"".Length)..])
            .Replace("msgid \"武器ＬＶが{level:num}に上がった\"\nmsgstr \"\"", "msgid \"武器ＬＶが{level:num}に上がった\"\nmsgstr \"Stufe {lvl}\""));
        string before = ReadFile("SC0100.toml");
        var console = new StringWriter();

        Assert.Equal(1, TextPoCommands.Import(Folder, "de", po, console, Keys));

        string output = console.ToString();
        Assert.Contains("text uses placeholder '{lvl}', which source does not define (SC0100.toml:4)", output);
        Assert.Contains("'value.milliseconds' takes {0}, not {1}", output);
        Assert.Contains("nothing written", output);
        Assert.Equal(before, ReadFile("SC0100.toml"));
    }
}
