using Ome.Engine.Frontend.Shell;
using Ome.Engine.Localization;
using Ome.Engine.Model;

/// <summary>
/// Engine-owned text (localization spec §11; milestone 5): AGE's own boxes by key, their presentation in the shell's
/// language with the driver's Japanese kept, the stack's <c>[[engine]]</c> entries in front of the embedded tables
/// along a language's fallback chain, the checks on those entries, and the options panel's language list (§10).
/// </summary>
public sealed class EngineTextTests
{
    private static readonly AgeBoxLocation At = new("INNER", 0x3, -1, "-", 2, 1);

    private static AgeBoxParts DepthBox(bool prompt = true)
        => new(AgeBoxMessage.Keyed(AgeBoxText.DepthMismatch, "INNER", "OUTER"), At, prompt);

    private static EngineTextLayer Layer(string language, params (string Key, string Text)[] entries)
        => new(language, entries.ToDictionary(entry => entry.Key,
                                              entry => new EngineTextValue(entry.Text, $"{language}/a.toml:1")));

    // ------------------------------------------------------------------------------------------- AGE's boxes

    [Fact]
    public void TheErrorBoxComposesAgesOwnJapaneseFromItsParts()
    {
        string body = AgeBoxText.Compose(DepthBox(), AgeBoxText.JapaneseText);

        Assert.Equal(
            "Depth が不正です INNER != OUTER\n\nデバック情報：\nFILE=INNER ADDRESS=3 LINE=-1 COMMAND=-(2) DEPTH=1\n"
            + "\n\nプログラムを続行しますか？\n\n中止：プログラムを終了します．\n再試行：もう一度このコマンドを実行します．\n"
            + "        (ただし正常に動作しない場合があります)\n無視：エラーを無視しプログラムを続行します．\n"
            + "      (ただし正常に動作しない場合があります)\n",
            body);
        Assert.Equal("Debug\n\n\n\nFILE=SC0100 ADDRESS=2A43 LINE=-1 COMMAND=-(178) DEPTH=0\n",
                     AgeBoxText.Compose(new AgeBoxParts(AgeBoxMessage.Script("Debug\n"),
                                                        new("SC0100", 0x2a43, -1, "-", 0xb2, 0), Prompt: false),
                                        key => key == AgeBoxText.DebugHeading ? "" : AgeBoxText.JapaneseText(key)));
    }

    [Fact]
    public void TheShellPresentsAgesBoxesInItsLanguageAndTheMessageKeepsTheJapanese()
    {
        var message = new DiagnosticMessage(AgeBoxText.JapaneseText(AgeBoxText.Caption),
                                            AgeBoxText.Compose(DepthBox(), AgeBoxText.JapaneseText), DepthBox());
        ShellStrings en = ShellStrings.Load("en");

        (string caption, string text) = ShellBoxes.Present(message, en);

        Assert.Equal("An error occurred", caption);
        Assert.StartsWith("Invalid depth INNER != OUTER\n\nDebug information:\nFILE=INNER ADDRESS=3 LINE=-1 COMMAND=-(2) "
                          + "DEPTH=1\n\n\nContinue the program?\n\nAbort: end the program.\n", text);
        Assert.Equal(["&Abort", "&Retry", "&Ignore"], ShellBoxes.ErrorButtons(en));
        Assert.Equal(["中止(&A)", "再試行(&R)", "無視(&I)"], ShellBoxes.ErrorButtons(ShellStrings.Japanese));
        Assert.Equal((message.Caption, message.Text), ShellBoxes.Present(message, ShellStrings.Japanese));
        Assert.Equal(("t", "x"), ShellBoxes.Present(new DiagnosticMessage("t", "x"), en));      // no parts: as given
    }

    [Fact]
    public void TheJapaneseTableTakesAgesBoxesAndTheNameEntryHasKeys()
    {
        Assert.All(AgeBoxText.Japanese, pair => Assert.Equal(pair.Value, ShellStrings.Japanese[pair.Key]));
        Assert.Equal("全角文字で名前を入力してください。", ShellStrings.Japanese[ShellBoxes.NameEntryPrompt]);
        Assert.Equal("Enter the name in full-width characters.", ShellStrings.Load("en")[ShellBoxes.NameEntryPrompt]);
        Assert.Same(ShellStrings.Embedded("ja"), ShellStrings.Embedded("ja"));
        Assert.Null(ShellStrings.Embedded("de"));
        Assert.Equal("&Skip", ShellStrings.Embedded("en")!["command.skip"]);
    }

    // ------------------------------------------------------------------------------------------- the package layer

    [Fact]
    public void PackageEntriesGoBeforeTheEmbeddedTablesAlongTheFallbackChain()
    {
        // pt-br falls back to pt, then en; the stack has pt-br and en text; the engine embeds en only.
        var problems = new List<string>();
        ShellStrings strings = ShellStrings.Load("pt-br", ["pt-br", "pt", "en"],
            [Layer("pt-br", ("command.skip", "&Pular")), Layer("en", ("command.auto", "Auto &play"))], problems);

        Assert.Empty(problems);
        Assert.Equal("pt-br", strings.Language);
        Assert.Equal("&Pular", strings["command.skip"]);            // the language's package entry
        Assert.Equal("Auto &play", strings["command.auto"]);        // a fallback's package entry, over its table
        Assert.Equal("&Hide text window", strings["command.hide"]); // the fallback's embedded table
        Assert.Equal("終了", ShellStrings.Load("de", ["de"], [], null)["exit.title"]);   // then Japanese
        Assert.Same(ShellStrings.Japanese, ShellStrings.Load("de", ["de"], []));
    }

    [Fact]
    public void ALaterLayerOfOneLanguageWinsAndTheSelectedLanguagesOwnTableComesAfterItsEntries()
    {
        ShellStrings strings = ShellStrings.Load("en", ["en"],
            [Layer("en", ("exit.title", "Leave")), Layer("en", ("exit.title", "Close"))]);

        Assert.Equal("Close", strings["exit.title"]);
        Assert.Equal("&Skip", strings["command.skip"]);
        Assert.Equal("An error occurred", strings[AgeBoxText.Caption]);
    }

    [Theory]
    [InlineData("no.such.key", "x", "the engine has no text 'no.such.key'")]
    [InlineData("value.milliseconds", "{0} ms {1}", "'value.milliseconds' takes {0}, not {1}")]
    [InlineData("value.milliseconds", "{0 ms", "'value.milliseconds' does not format")]
    [InlineData("error.depth", "{1} } {0}", "'error.depth' does not format")]
    public void AnEntryTheEngineCannotUseIsLeftOutWithItsReason(string key, string text, string reason)
    {
        var problems = new List<string>();
        ShellStrings strings = ShellStrings.Load("en", ["en"], [Layer("en", (key, text))], problems);

        Assert.StartsWith($"en/a.toml:1: {reason}", Assert.Single(problems));
        Assert.Equal(ShellStrings.Load("en")[key], strings[key]);
    }

    [Theory]
    [InlineData("value.milliseconds", "{0}ミリ秒")]
    [InlineData("error.depth", "{1} が {0} ではありません")]
    [InlineData("value.window-size", "{0} by {1} {{px}}")]
    [InlineData("command.skip", "{oops}")]                  // the Japanese takes no argument, so it is not formatted
    [InlineData("value.percent", "{0,4:0}%")]
    public void AnEntryMayReorderOrDropTheArgumentsTheJapaneseTakes(string key, string text)
        => Assert.Null(ShellStrings.EntryProblem(key, text));

    // ------------------------------------------------------------------------------------------- the language list

    [Fact]
    public void ThePanelListsTheShellsLanguagesThenTheStacksByNameThenTheCurrentOne()
    {
        IReadOnlyList<(string Tag, string Label)> choices = ShellPanel.LanguageChoices(ShellStrings.Load("en"),
            [("de", "Deutsch"), ("en", "English (fan)"), ("pt-br", null)], "x-pseudo");

        Assert.Equal([("ja", "日本語"), ("en", "English"), ("de", "Deutsch"), ("pt-br", "pt-br"), ("x-pseudo", "x-pseudo")],
                     choices);
        Assert.Equal(3, ShellPanel.LanguageChoices(ShellStrings.Japanese, [("de", "Deutsch")], "de").Count);
        Assert.Equal("ﾀｲﾄﾙに戻ると、すべての表示が新しい言語になります", ShellStrings.Japanese[ShellPanel.LanguageNoteKey]);
    }
}
