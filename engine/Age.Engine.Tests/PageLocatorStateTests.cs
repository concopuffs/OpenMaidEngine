using Age.Engine.Sys4;

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
}
