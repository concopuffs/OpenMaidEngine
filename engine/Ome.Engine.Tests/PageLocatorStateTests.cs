using System.Text.Json;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

public class PageLocatorStateTests
{
    [Fact]
    public void DevelopmentMapsAreKeyedByProfileAndScene()
    {
        Assert.Equal(Path.Combine(Paths.GameBuild("himegari"), "page-map-SC0000.jsonl"),
                     PageLocatorState.DevelopmentMapPath("himegari", "sc0000.bin"));
        Assert.Equal(Path.Combine(Paths.GameBuild("kamidori"), "page-map-SC0000.jsonl"),
                     PageLocatorState.DevelopmentMapPath("kamidori", "SC0000"));
    }

    [Fact]
    public void AMapAnotherWriterHoldsLeavesTheLocatorWithoutAMap()
    {
        string directory = Path.Combine(Path.GetTempPath(), "page-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string map = Path.Combine(directory, PageLocatorState.MapFileName("SC0000"));
        try
        {
            using (new FileStream(map, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var locator = PageLocatorState.Open("SC0000", map, out string? warning);
                Assert.NotNull(warning);
                Assert.Contains(map, warning);
                locator.Step("SC0000.BIN", 0x10);
                locator.Wait(1);
                Assert.StartsWith("SC0000 P001 · wait SC0000@0x10", locator.CurrentDisplay);
            }
            Assert.Equal(0, new FileInfo(map).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AWritableMapRecordsEachPage()
    {
        string directory = Path.Combine(Path.GetTempPath(), "page-locator-" + Guid.NewGuid().ToString("N"));
        string map = Path.Combine(directory, PageLocatorState.MapFileName("SC0000"));
        try
        {
            using (var locator = PageLocatorState.Open("SC0000", map, out string? warning))
            {
                Assert.Null(warning);
                locator.Step("SC0000.BIN", 0x10);
                locator.Wait(1);
            }
            string[] lines = File.ReadAllLines(map);
            Assert.Single(lines);
            Assert.Contains("\"wait_offset\":\"0x10\"", lines[0]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    // ------------------------------------------------------------------- the page's text (localization spec §18.1)

    /// <summary>A run on layout 1 whose rows start at <paramref name="top"/> and end at <paramref name="lastTop"/>,
    /// at the row's start unless <paramref name="left"/> says otherwise, in 12-pixel half cells, 28 pixels a row and a
    /// right edge at 600.</summary>
    private static PageTextGeometry Run(int top, int lastInk, int firstWord, int lastTop = -1, int layout = 1,
                                        int left = 0)
        => new(layout, top, lastTop < 0 ? top : lastTop, lastInk, firstWord, SpaceAdvance: 12, Right: 600,
               LineAdvance: 28, FirstRowLeft: left, LastRowLeft: left);

    /// <summary>The records <paramref name="pages"/> writes, one per page.</summary>
    private static JsonElement[] Pages(Action<PageLocatorState> pages)
    {
        string directory = Path.Combine(Path.GetTempPath(), "page-locator-" + Guid.NewGuid().ToString("N"));
        string map = Path.Combine(directory, PageLocatorState.MapFileName("SC0000"));
        try
        {
            using (var locator = new PageLocatorState("SC0000", map)) pages(locator);
            return File.ReadAllLines(map).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void APagesRunsJoinAtNewRowsAndEachBreakIsAWrapOnlyWhenTheNextWordDidNotFit()
    {
        JsonElement page = Pages(locator =>
        {
            locator.Text(0x10, "It was a long line that reached", null, Run(0, lastInk: 590, firstWord: 0));
            locator.Text(0x20, "the edge.", null, Run(28, lastInk: 120, firstWord: 36));        // 590+12+36 > 600
            locator.Text(0x30, " (aside)", null, Run(28, lastInk: 220, firstWord: 0, left: 120)); // the same row
            locator.Text(0x40, "Short.", null, Run(56, lastInk: 72, firstWord: 72));             // 220+12+72 fits
            locator.Text(0x50, " Indented", null, Run(84, lastInk: 120, firstWord: 0));         // starts with a space
            locator.Text(0x60, "", null, null);                                                 // drew nothing
            locator.Text(0x70, "After a blank row", null, Run(140, lastInk: 210, firstWord: 60));
            locator.Text(0x80, "Wil", null, Run(400, lastInk: 36, firstWord: 36, layout: 9));   // the name plate
            locator.Text(0x90, "Cleared", null, Run(400, lastInk: 84, firstWord: 84, layout: 9)); // back to its start
            locator.Wait(1);
        })[0];

        Assert.Equal("It was a long line that reached\nthe edge. (aside)\nShort.\n Indented\n\nAfter a blank row\nWil\nCleared",
                     page.GetProperty("dialogue").GetString());
        Assert.Equal(["wrap", "break", "break", "break", "break", "break", "break"],
                     page.GetProperty("breaks").EnumerateArray().Select(item => item.GetString()));
        Assert.False(page.TryGetProperty("presented", out _));
        Assert.False(page.TryGetProperty("cut", out _));
        Assert.Equal("Cleared", page.GetProperty("text").GetString());
    }

    [Fact]
    public void TwoWideCharactersJoinWithoutASpace()
    {
        JsonElement page = Pages(locator =>
        {
            locator.Text(0x10, "王政が崩壊すると、", null, Run(0, lastInk: 590, firstWord: 0));
            locator.Text(0x20, "国は", null, Run(28, lastInk: 48, firstWord: 10));   // 590+10 fits only with no space
            locator.Wait(1);
        })[0];
        Assert.Equal(["break"], page.GetProperty("breaks").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void WithALanguageActiveThePageAlsoHoldsWhatItsRunsDrew()
    {
        JsonElement[] pages = Pages(locator =>
        {
            locator.Text(0x10, "一行目", "The first message, drawn whole", Run(0, lastInk: 72, firstWord: 24));
            locator.Text(0x20, "二行目", "", Run(28, lastInk: 72, firstWord: 24));            // drawn by the first run
            locator.Text(0x30, "次の文", "The next one", Run(56, lastInk: 72, firstWord: 24), cut: true);
            locator.Wait(1);
            locator.Text(0x40, "続き", "", Run(0, lastInk: 48, firstWord: 24));
            locator.Wait(2);
            locator.Wait(3);
        });

        Assert.Equal("一行目\n二行目\n次の文", pages[0].GetProperty("dialogue").GetString());
        Assert.Equal("The first message, drawn whole\nThe next one", pages[0].GetProperty("presented").GetString());
        Assert.True(pages[0].GetProperty("cut").GetBoolean());
        Assert.Equal("", pages[1].GetProperty("presented").GetString());
        Assert.False(pages[1].TryGetProperty("cut", out _));
        Assert.Equal(JsonValueKind.Null, pages[2].GetProperty("dialogue").ValueKind);
        Assert.False(pages[2].TryGetProperty("presented", out _));
    }

    [Fact]
    public void TheGeometryComesFromTheRunsRowsAndGlyphRecords()
    {
        static AdvRetainedGlyphRecord[] Cells(int count, int width, int left = 10, int top = 5)
            => Enumerable.Range(0, count).Select(i => new AdvRetainedGlyphRecord(0, left + i * width, top,
                                                                                    left + (i + 1) * width, top + 24)).ToArray();

        // "Go home  ": the first word is "Go", the row's text ends after "home".
        AdvRetainedGlyphRecord[] records = Cells(9, 12);
        PageTextGeometry? geometry = PageTextGeometry.Measure(
            "Go home  ", [new AdvTextRow(0, 9, 10, 5, 118, 29, false)], records, layout: 1, right: 600,
            lineAdvance: 28, spaceAdvance: 12);
        Assert.Equal(new PageTextGeometry(1, 5, 5, 94, 24, 12, 600, 28, 10, 10), geometry);

        // Two rows: the first row's first word, the last row's top and end.
        geometry = PageTextGeometry.Measure(
            "「はい」と", [new AdvTextRow(0, 3, 10, 5, 82, 29, true), new AdvTextRow(3, 2, 10, 33, 58, 57, false)],
            [.. Cells(3, 24), .. Cells(2, 24, top: 33)], layout: 1, right: 600, lineAdvance: 28, spaceAdvance: 12);
        Assert.Equal((5, 33, 58, 24), (geometry!.Value.FirstRowTop, geometry.Value.LastRowTop,
                                       geometry.Value.LastInkRight, geometry.Value.FirstWordWidth));

        Assert.Null(PageTextGeometry.Measure("", [], [], 1, 600, 28, 12));
    }
}
