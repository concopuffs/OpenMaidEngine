using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Localization;
using Ome.Mods.Tools;
using Ome.Script.Abi;
using Ome.Script.Patch;

/// <summary><c>omp text extract</c>, <c>check</c> and <c>stats</c> (localization spec §13; milestone 4) on a script
/// assembled for the test, and the font reader behind check's glyph coverage, on font files synthesized here.</summary>
public sealed class TextToolTests : IDisposable
{
    private const int ShowText = 0x6e, SetString = 0x192, Exit = 0x2;

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-texttool-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Operand I(long value) => new(0, value);
    private static Operand GlobalString(long index) => new(5, index);

    /// <summary>SC0100: a message, a message with a variable run, and a stored literal.</summary>
    private static byte[] Scene()
    {
        var asm = new TextImportTests.Asm();
        asm.Say("はい、そうです。", "二行目。");
        asm.Op(ShowText, I(0), GlobalString(0x24ab));
        asm.Say("を手に入れた");
        asm.Op(SetString, GlobalString(1), asm.S("剣"));
        asm.Op(Exit);
        return asm.Bytes();
    }

    private static ScriptTextCorpus Corpus(byte[] scene)
        => ScriptTextCorpus.Scan(new ScriptTextScanner(Table, Contracts), [("SC0100.BIN", () => scene)]);

    private void WriteFile(string relative, string content) => WriteBytes(relative, new UTF8Encoding(false).GetBytes(content));

    private void WriteBytes(string relative, byte[] content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    // ---- the scanner ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheScannerFindsMessagesPatternsAndStoredLiterals()
    {
        ScriptTextCorpus corpus = Corpus(Scene());

        var strings = corpus.Scripts["SC0100"];
        // A message of several runs also gives each literal run, which the presenter falls back to.
        Assert.Equal([(ScriptStringKind.Message, "はい、そうです。二行目。"), (ScriptStringKind.Run, "はい、そうです。"),
                      (ScriptStringKind.Run, "二行目。"), (ScriptStringKind.Run, "を手に入れた"),
                      (ScriptStringKind.Pattern, "{v1}を手に入れた"), (ScriptStringKind.StoredLiteral, "剣")],
                     strings.Select(found => (found.Kind, found.Source)).OrderBy(pair => pair.Kind));
        Assert.Equal("はい、そうです。\n二行目。", strings.Single(found => found.Kind == ScriptStringKind.Message).Authored);
        Assert.True(corpus.Reaches(TextEntry.Literal("剣", "Sword")));
        Assert.True(corpus.Reaches(TextEntry.Literal("剣", "Sword") with { Script = "SC0100" }));
        Assert.False(corpus.Reaches(TextEntry.Literal("剣", "Sword") with { Script = "SC0200" }));
        Assert.False(corpus.Reaches(TextEntry.Literal("盾", "Shield")));
        Assert.Equal(OmsPatchPlan.FingerprintOf(Scene()), corpus.Fingerprints["SC0100"]);
    }

    // ---- extract -------------------------------------------------------------------------------------------------

    private static Dictionary<string, TextCatalogFile> Reread(IReadOnlyDictionary<string, TextCatalogFile> files,
                                                              Func<string, string, string>? edit = null)
        => files.ToDictionary(pair => pair.Key, pair => TextCatalogFileReader.Parse(
            (edit ?? ((_, text) => text))(pair.Key, TextCatalogFileWriter.Write(pair.Value)), pair.Key));

    [Fact]
    public void ExtractWritesTemplatesAndKeepsEveryEntryOnTheNextRun()
    {
        ScriptTextCorpus corpus = Corpus(Scene());
        var first = new ExtractCounts();

        var files = TextTemplateExtractor.Build(corpus, new Dictionary<string, TextCatalogFile>(), first);

        Assert.Equal(["SC0100.toml", "_global.toml"], files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((2, 3), (first.FilesWritten, first.NewTemplates));
        TextCatalogFile scene = files["SC0100.toml"];
        Assert.Equal("SC0100", scene.Script);
        Assert.Equal(["はい、そうです。二行目。", "{v1}を手に入れた"], scene.Items.Select(item => item.Entry.Source));
        Assert.All(scene.Items, item => Assert.False(item.HasText));
        Assert.Empty(scene.Entries);
        Assert.Equal("はい、そうです。\n二行目。", scene.Items[0].Entry.Note);
        TextCatalogItem sword = Assert.Single(files["_global.toml"].Items);
        Assert.Equal(("剣", null), (sword.Entry.Source, sword.Entry.Script));
        Assert.StartsWith("SC0100@0x", sword.Entry.Note);

        // A translator fills in the sword, marks the message obsolete by hand and adds a line the scripts lack.
        var existing = Reread(files, (name, text) => name == "_global.toml"
            ? text.Replace("source = \"剣\"\n", "source = \"剣\"\ntext = \"Sword\"\n")
              + "\n[[text]]\nsource = \"盾\"\ntext = \"Shield\"\n"
            : text.Replace("source = \"はい、そうです。二行目。\"\n", "source = \"はい、そうです。二行目。\"\nobsolete = true\n"));
        Assert.Equal("Sword", existing["_global.toml"].Entries[0].Text);
        var second = new ExtractCounts();

        var updated = TextTemplateExtractor.Build(corpus, existing, second);

        Assert.Equal((0, 2, 2, 1, 1, 2), (second.NewTemplates, second.KeptTranslated, second.KeptUntranslated,
                                          second.MarkedObsolete, second.Revived, second.FilesWritten));
        TextCatalogFile global = updated["_global.toml"];
        Assert.Equal([("剣", true, false), ("盾", true, true)],
                     global.Items.Select(item => (item.Entry.Source, item.HasText, item.Obsolete)));
        Assert.Equal(["剣"], global.Entries.Select(entry => entry.Source));
        Assert.All(updated["SC0100.toml"].Items, item => Assert.False(item.Obsolete));

        // Run again over its own output, it changes nothing.
        var third = new ExtractCounts();
        Assert.Empty(TextTemplateExtractor.Build(corpus, Reread(updated), third));
        Assert.Equal((0, 2), (third.FilesWritten, third.FilesUnchanged));
    }

    [Fact]
    public void ExtractToAFolderWritesFilesThatReadBack()
    {
        string package = Path.Combine(_root, "pkg");
        var console = new StringWriter();

        Assert.Equal(0, TextCommands.Extract(Corpus(Scene()), "en", package, "the test install", console));
        Assert.Contains("1 scripts -> ", console.ToString());
        Assert.Contains("2 files written, 0 unchanged; 3 new templates", console.ToString());
        string written = File.ReadAllText(Path.Combine(package, "text", "en", "SC0100.toml"));
        Assert.StartsWith("# Catalog template written by omp text extract from the test install.", written);
        Assert.DoesNotContain("\ntext = ", written);

        console = new StringWriter();
        Assert.Equal(0, TextCommands.Extract(Corpus(Scene()), "en", package, "the test install", console));
        Assert.Contains("0 files written, 2 unchanged; 0 new templates", console.ToString());

        // A file that does not read stops extract before it writes anything.
        File.AppendAllText(Path.Combine(package, "text", "en", "_global.toml"), "\n[[text]]\nsource = 3\n");
        console = new StringWriter();
        Assert.Equal(1, TextCommands.Extract(Corpus(Scene()), "en", package, "the test install", console));
        Assert.Contains("nothing written", console.ToString());
    }

    // ---- check and stats -----------------------------------------------------------------------------------------

    private TextSet Root()
    {
        var set = new TextSet();
        set.AddRoot(_root);
        return set;
    }

    [Fact]
    public void CheckFailsOnPatternsThatCanMatchOneString()
    {
        WriteFile("en/SC0100.toml", """
            script = "SC0100"

            [[text]]
            source = "{a}の{b}"
            text = "{b} of {a}"

            [[text]]
            source = "{a}が{b}"
            text = "{a} has {b}"
            """);
        var console = new StringWriter();

        Assert.Equal(1, TextCommands.Check(Root(), null, null, console));

        string output = console.ToString();
        Assert.Contains("error: text en: en/SC0100.toml:3 and en/SC0100.toml:7: the patterns \"{a}の{b}\" and \"{a}が{b}\" "
                        + "can match one string", output);
        Assert.Contains("no installed scripts given", output);
        Assert.Contains("omp text check: fails: 1 errors", output);
    }

    [Fact]
    public void CheckWarnsAboutSourcesTheScriptsLackAndSitesThatDoNotBind()
    {
        string other = "sha256:" + new string('2', 64);
        WriteFile("en/_global.toml", """
            [[text]]
            source = "剣"
            text = "Sword"

            [[text]]
            source = "盾"
            text = "Shield"

            [[text]]
            source = "鎧"
            """);
        WriteFile("en/SC0100.toml", $$"""
            script = "SC0100"
            fingerprint = "{{other}}"

            [[text]]
            source = "剣"
            at = 0x10
            text = "Blade"
            """);
        var console = new StringWriter();

        Assert.Equal(0, TextCommands.Check(Root(), Corpus(Scene()), null, console));

        string output = console.ToString();
        Assert.Contains("text en: 3 entries from 2 files, 1 without text (no [[language]] table: defaults)", output);
        Assert.Contains("warning: en/_global.toml: 1 entry matches nothing in the installed scripts: line 5 \"盾\"", output);
        Assert.Contains($"warning: en/SC0100.toml: 1 site entry for SC0100 does not bind: the file names {other}", output);
        Assert.Contains("omp text check: passes: 0 errors, 2 warnings", output);
    }

    [Fact]
    public void CheckWarnsAboutCharactersNoBundledFontHas()
    {
        WriteBytes("fonts/Body.ttf", Fonts.Sfnt(Fonts.Cmap()));
        WriteFile("languages.toml", """
            [[language]]
            tag = "en"

            [[language.font]]
            requested = ["*"]
            use = ["fonts/Body.ttf", "Georgia"]
            """);
        WriteFile("en/_global.toml", """
            [[text]]
            source = "はい"
            text = "ABC"

            [[text]]
            source = "いいえ"
            text = "CAZ"
            """);
        var console = new StringWriter();

        Assert.Equal(0, TextCommands.Check(Root(), null, null, console));

        Assert.Contains("warning: text en: 1 character is in none of the bundled fonts for * (Body.ttf): 'Z' U+005A "
                        + "(en/_global.toml:5)", console.ToString());
    }

    [Fact]
    public void StatsCountsWhatEachLanguageTranslates()
    {
        WriteFile("en/_global.toml", """
            [[text]]
            source = "剣"
            text = "Sword"
            """);
        WriteFile("en/SC0100.toml", """
            script = "SC0100"

            [[text]]
            source = "{v1}を手に入れた"
            text = "Got {v1}"

            [[text]]
            source = "はい、そうです。二行目。"
            """);
        var console = new StringWriter();

        Assert.Equal(0, TextCommands.Stats(Root(), Corpus(Scene()), null, perScript: true, console));

        string output = console.ToString();
        Assert.Contains("text en: messages 1/2 (50.0%), strings 1/1 (100.0%), composed 0/0 (2 entries, 1 without text, "
                        + "0 obsolete; chain en)", output);
        Assert.Contains("  SC0100       messages 1/2 (50.0%)", output);
    }

    // ---- font coverage -------------------------------------------------------------------------------------------

    /// <summary>Font files with only what the coverage reader reads: a table directory and a <c>cmap</c>.</summary>
    internal static class Fonts
    {
        /// <summary>A cmap with a format 4 subtable (A–C by delta; D–E through the glyph array, D unmapped) and a
        /// format 12 one (あ, and two emoji).</summary>
        public static byte[] Cmap()
        {
            // Format 4: segments A-C, D-E, and the closing 0xFFFF.
            ushort[] ends = [0x43, 0x45, 0xFFFF], starts = [0x41, 0x44, 0xFFFF];
            short[] deltas = [(short)(1 - 0x41), 0, 1];
            ushort[] ranges = [0, 4, 0];
            ushort[] glyphs = [0, 7];   // D -> 0 (missing), E -> 7
            var format4 = new List<byte>();
            void U16(List<byte> to, int value) => to.AddRange([(byte)(value >> 8), (byte)value]);
            void U32(List<byte> to, uint value) => to.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
            int length4 = 16 + 8 * ends.Length + 2 * glyphs.Length;
            U16(format4, 4); U16(format4, length4); U16(format4, 0); U16(format4, 2 * ends.Length);
            U16(format4, 0); U16(format4, 0); U16(format4, 0);
            foreach (ushort end in ends) U16(format4, end);
            U16(format4, 0);
            foreach (ushort start in starts) U16(format4, start);
            foreach (short delta in deltas) U16(format4, (ushort)delta);
            foreach (ushort range in ranges) U16(format4, range);
            foreach (ushort glyph in glyphs) U16(format4, glyph);

            var format12 = new List<byte>();
            (uint Start, uint End, uint Glyph)[] groups = [(0x3042, 0x3042, 20), (0x1F600, 0x1F601, 30)];
            U16(format12, 12); U16(format12, 0); U32(format12, (uint)(16 + 12 * groups.Length)); U32(format12, 0);
            U32(format12, (uint)groups.Length);
            foreach (var (start, end, glyph) in groups) { U32(format12, start); U32(format12, end); U32(format12, glyph); }

            var cmap = new List<byte>();
            U16(cmap, 0); U16(cmap, 3);
            int first = 4 + 8 * 3;
            U16(cmap, 3); U16(cmap, 1); U32(cmap, (uint)first);
            U16(cmap, 3); U16(cmap, 10); U32(cmap, (uint)(first + format4.Count));
            U16(cmap, 1); U16(cmap, 0); U32(cmap, (uint)first);   // a Macintosh subtable: not Unicode, skipped
            cmap.AddRange(format4);
            cmap.AddRange(format12);
            return cmap.ToArray();
        }

        private static byte[] Header(int size, params (int At, uint Value, int Bytes)[] fields)
        {
            var header = new byte[size];
            foreach (var (at, value, bytes) in fields)
                if (bytes == 2) BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(at), (ushort)value);
                else BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(at), value);
            return header;
        }

        /// <summary>A TrueType file whose only table is <paramref name="cmap"/>, after a dummy one.</summary>
        public static byte[] Sfnt(byte[] cmap, int start = 0)
        {
            int directory = 12 + 16 * 2;
            byte[] head = Header(directory, (0, 0x00010000, 4), (4, 2, 2),
                (12, 0x68656164, 4), (12 + 8, (uint)(start + directory), 4), (12 + 12, 4, 4),        // 'head', 4 bytes
                (28, 0x636D6170, 4), (28 + 8, (uint)(start + directory + 4), 4), (28 + 12, (uint)cmap.Length, 4));
            return [.. head, 0, 0, 0, 0, .. cmap];
        }

        public static byte[] Collection(byte[] cmap)
        {
            byte[] header = Header(16, (0, 0x74746366, 4), (4, 0x00010000, 4), (8, 1, 4), (12, 16, 4));
            return [.. header, .. Sfnt(cmap, 16)];
        }

        public static byte[] Woff(byte[] cmap)
        {
            // Padded so that it compresses: a table that does not is stored as it is.
            byte[] padded = [.. cmap, .. new byte[512]];
            var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(padded);
            byte[] stored = compressed.ToArray();
            if (stored.Length >= padded.Length) throw new InvalidOperationException("the test cmap did not compress");
            int offset = 44 + 20;
            byte[] header = Header(offset, (0, 0x774F4646, 4), (4, 0x00010000, 4), (12, 1, 2),
                (44, 0x636D6170, 4), (48, (uint)offset, 4), (52, (uint)stored.Length, 4), (56, (uint)padded.Length, 4));
            return [.. header, .. stored];
        }

        public static byte[] Woff2(byte[] cmap)
        {
            var compressed = new MemoryStream();
            using (var brotli = new BrotliStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) brotli.Write(cmap);
            byte[] stream = compressed.ToArray();
            // One table entry: flags 0 (cmap, untransformed), its length in UIntBase128.
            var directory = new List<byte> { 0 };
            uint length = (uint)cmap.Length;
            var digits = new Stack<byte>();
            do { digits.Push((byte)(length & 0x7f)); length >>= 7; } while (length > 0);
            while (digits.Count > 0) directory.Add((byte)(digits.Pop() | (digits.Count > 0 ? 0x80 : 0)));
            byte[] header = Header(48, (0, 0x774F4632, 4), (4, 0x00010000, 4), (12, 1, 2), (20, (uint)stream.Length, 4));
            return [.. header, .. directory, .. stream];
        }
    }

    public static TheoryData<string> Containers => new() { "ttf", "ttc", "woff", "woff2" };

    [Theory]
    [MemberData(nameof(Containers))]
    public void TheCoverageReaderReadsEveryUnicodeSubtableInEachContainer(string container)
    {
        byte[] cmap = Fonts.Cmap();
        byte[] file = container switch
        {
            "ttc" => Fonts.Collection(cmap),
            "woff" => Fonts.Woff(cmap),
            "woff2" => Fonts.Woff2(cmap),
            _ => Fonts.Sfnt(cmap),
        };

        FontCoverage coverage = FontCoverage.Read(file);

        Assert.Equal([true, true, true, false, true, true, true, true, false],
                     new[] { 'A', 'B', 'C', 'D', 'E', 'あ', 0x1F600, 0x1F601, 'F' }.Select(c => coverage.Contains(c)));
        Assert.Equal(7, coverage.Count);
    }

    [Fact]
    public void AMalformedFontIsRefusedWithoutReadingPastItsEnd()
    {
        byte[] font = Fonts.Sfnt(Fonts.Cmap());
        Assert.Throws<InvalidDataException>(() => FontCoverage.Read(font[..40]));
        Assert.Throws<InvalidDataException>(() => FontCoverage.Read("not a font"u8.ToArray()));

        byte[] outside = (byte[])font.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(outside.AsSpan(28 + 12), 0x7fff_0000);   // cmap length past the file
        Assert.Throws<InvalidDataException>(() => FontCoverage.Read(outside));

        byte[] groups = (byte[])font.Clone();
        const int format12 = 44 + 4 + 28 + 44;   // table directory, 'head', cmap header, format 4
        Assert.Equal(12, BinaryPrimitives.ReadUInt16BigEndian(groups.AsSpan(format12)));
        BinaryPrimitives.WriteUInt32BigEndian(groups.AsSpan(format12 + 12), 0x1000_0000);   // more groups than bytes
        Assert.Throws<InvalidDataException>(() => FontCoverage.Read(groups));
    }

    // ---- engine text (milestone 5) -------------------------------------------------------------------------------

    private static readonly EngineTextKeys Keys =
        new(ShellStrings.Embedded(ShellStrings.FallbackLanguage)!, ShellStrings.Embedded, ShellStrings.EntryProblem);

    [Fact]
    public void ExtractWritesEngineTemplatesForALanguageTheEngineHasNoTableFor()
    {
        string package = Path.Combine(_root, "pkg");
        string path = Path.Combine(package, "text", "de", EngineTextKeys.TemplateFile);
        var console = new StringWriter();

        Assert.Equal(0, TextCommands.Extract(Corpus(Scene()), "de", package, "the test install", console, Keys));

        Assert.Contains($"; {Keys.Japanese.Count} engine text templates in _engine.toml", console.ToString());
        TextCatalogFile engine = TextCatalogFileReader.Read(path, "de/_engine.toml");
        Assert.Equal(Keys.Japanese.Keys.Order(StringComparer.Ordinal), engine.EngineEntries.Select(entry => entry.Key));
        Assert.All(engine.EngineEntries, entry => Assert.Null(entry.Text));
        Assert.Equal("ja: 終了\nen: Quit", engine.EngineEntries.Single(entry => entry.Key == "exit.title").Note);

        // Translated and extracted again: the translation stays, and no key gets a second template.
        File.WriteAllText(path, File.ReadAllText(path).Replace("key = \"exit.title\"\n",
                                                               "key = \"exit.title\"\ntext = \"Beenden\"\n"));
        console = new StringWriter();
        Assert.Equal(0, TextCommands.Extract(Corpus(Scene()), "de", package, "the test install", console, Keys));
        Assert.DoesNotContain("engine text templates", console.ToString());
        engine = TextCatalogFileReader.Read(path, "de/_engine.toml");
        Assert.Equal(Keys.Japanese.Count, engine.EngineEntries.Count);
        Assert.Equal("Beenden", engine.EngineEntries.Single(entry => entry.Key == "exit.title").Text);

        // English has the engine's own table.
        Assert.Equal(0, TextCommands.Extract(Corpus(Scene()), "en", package, "the test install", TextWriter.Null, Keys));
        Assert.False(File.Exists(Path.Combine(package, "text", "en", EngineTextKeys.TemplateFile)));
    }

    [Fact]
    public void CheckWarnsAboutUnknownEngineKeysAndFailsOnArgumentsTheTextCannotTake()
    {
        WriteFile("de/_engine.toml", """
            [[engine]]
            key = "exit.title"
            text = "Beenden"

            [[engine]]
            key = "no.such.key"
            text = "x"

            [[engine]]
            key = "value.milliseconds"
            text = "{1} ms"

            [[engine]]
            key = "command.auto"
            note = "a template"
            """);
        var console = new StringWriter();

        Assert.Equal(1, TextCommands.Check(Root(), null, null, console, Keys));

        string output = console.ToString();
        Assert.Contains("text de: 0 entries from 1 files, 3 engine keys", output);
        Assert.Contains("warning: de/_engine.toml:5: the engine has no text 'no.such.key'; the entry is not used", output);
        Assert.Contains("error: de/_engine.toml:9: 'value.milliseconds' takes {0}, not {1}", output);
        Assert.Contains("omp text check: fails: 1 errors, 1 warnings", output);
    }
}
