using System;
using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Localization;
using Xunit;

/// <summary>Templates, entries and the catalog's scopes (localization spec §6.1, §6.3, §6.4, §9.3).</summary>
public class LocalizationTextModelTests
{
    [Fact]
    public void TemplateSplitsLiteralsAndPlaceholders()
    {
        TextTemplate template = TextTemplate.Parse("{name}は{count:num}個の{{石}}を拾った", allowKinds: true);

        Assert.Equal(new TemplatePart[]
        {
            new PlaceholderPart("name", PlaceholderKind.Any),
            new LiteralPart("は"),
            new PlaceholderPart("count", PlaceholderKind.Number),
            new LiteralPart("個の{石}を拾った"),
        }, template.Parts);
        Assert.False(template.StartsWithLiteral);
        Assert.True(template.EndsWithLiteral);
        Assert.Equal("Ruri は 3", TextTemplate.Parse("{name} は {count}", false)
            .Render(new Dictionary<string, string> { ["name"] = "Ruri", ["count"] = "3" }));
    }

    [Fact]
    public void LiteralTemplateRoundTripsThroughEscape()
    {
        const string text = "a {b} }c{";
        TextTemplate template = TextTemplate.Parse(TextTemplate.Escape(text), allowKinds: false);

        Assert.False(template.HasPlaceholders);
        Assert.Equal(text, template.Literal);
    }

    [Theory]
    [InlineData("a } b", true, "closes no placeholder")]
    [InlineData("a { b", true, "is not closed")]
    [InlineData("{1x}", true, "needs a name")]
    [InlineData("{n:word}", true, "unknown kind")]
    [InlineData("{n:num}", false, "belongs in source")]
    [InlineData("{n}{n}", true, "appears twice")]
    public void TemplateRejectsMalformedPlaceholders(string text, bool allowKinds, string expected)
    {
        Assert.False(TextTemplate.TryParse(text, allowKinds, out _, out string? error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void TextMayRepeatAPlaceholderButNotInventOne()
    {
        Assert.True(TextEntry.TryCreate("{who}が来た", "{who}! {who} came", out _, out _));
        Assert.False(TextEntry.TryCreate("{who}が来た", "{whom} came", out _, out string? error));
        Assert.Contains("which source does not define", error);
        Assert.False(TextEntry.TryCreate("", "x", out _, out error));
        Assert.Equal("source is empty", error);
    }

    [Fact]
    public void ExactLookupPrefersSiteThenScriptThenGlobal()
    {
        var catalog = TextCatalog.Build("en", new[]
        {
            TextEntry.Literal("はい", "Yes"),
            TextEntry.Literal("はい", "Yeah") with { Script = "sc0100.bin" },
            TextEntry.Literal("はい", "Yep") with { Script = "SC0100", At = 0x2a43 },
        });

        string Resolve(string? script, int? at)
            => catalog.TryResolveExact("はい", script, at, out TextEntry entry) ? entry.Text : "(miss)";

        Assert.Equal("Yep", Resolve("SC0100", 0x2a43));
        Assert.Equal("Yeah", Resolve("SC0100.BIN", 0x2a44));
        Assert.Equal("Yeah", Resolve("sc0100", null));
        Assert.Equal("Yes", Resolve("SC0200", 0x2a43));
        Assert.Equal("Yes", Resolve(null, null));
        Assert.False(catalog.TryResolveExact("いいえ", null, null, out _));
    }

    [Fact]
    public void LaterEntryForTheSameKeyWinsAndIsRecorded()
    {
        TextEntry first = TextEntry.Literal("剣", "Sword") with { Origin = "a.toml:1" };
        TextEntry second = TextEntry.Literal("剣", "Blade") with { Origin = "b.toml:4" };
        TextEntry scoped = TextEntry.Literal("剣", "Sabre") with { Script = "ITINIT" };

        var catalog = TextCatalog.Build("en", new[] { first, scoped, second });

        Assert.Equal(2, catalog.Count);
        Assert.Equal(new[] { scoped, second }, catalog.Entries);
        TextCatalogOverride replaced = Assert.Single(catalog.Overrides);
        Assert.Same(first, replaced.Replaced);
        Assert.Same(second, replaced.Winner);
        Assert.True(catalog.TryResolveExact("剣", null, null, out TextEntry entry));
        Assert.Equal("Blade", entry.Text);
    }

    [Fact]
    public void PatternsAreKeptApartFromExactEntries()
    {
        var catalog = TextCatalog.Build("en", new[]
        {
            TextEntry.Create("{v1}を手に入れた", "Got {v1}") with { Script = "SC0100" },
            TextEntry.Literal("{v1}を手に入れた", "the literal text, braces and all"),
        });

        TextEntry pattern = Assert.Single(catalog.Patterns);
        Assert.True(pattern.IsPattern);
        // Exact lookup never returns the pattern, even in its own script: text with braces in it is literal text.
        Assert.True(catalog.TryResolveExact("{v1}を手に入れた", "SC0100", null, out TextEntry literal));
        Assert.False(literal.IsPattern);
        Assert.Equal("the literal text, braces and all", literal.TextTemplate.Literal);
    }

    [Fact]
    public void SiteEntryWithoutAScriptIsRefused()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            TextCatalog.Build("en", new[] { TextEntry.Literal("あ", "a") with { At = 4, Origin = "x.toml:2" } }));
        Assert.Contains("x.toml:2", error.Message);
    }

    [Theory]
    [InlineData("SC0100.BIN", "SC0100")]
    [InlineData(" sc0100.bin ", "SC0100")]
    [InlineData("$1$SC2600.bin", "$1$SC2600")]
    [InlineData("STATUS", "STATUS")]
    public void ScriptNamesNormalize(string written, string expected)
        => Assert.Equal(expected, TextCatalog.NormalizeScriptName(written));
}
