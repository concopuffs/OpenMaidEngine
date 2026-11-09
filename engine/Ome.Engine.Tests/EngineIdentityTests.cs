using Ome.Engine.Frontend.Shell;

/// <summary>
/// The options panel's 情報 names Open Maid Engine where AGE's ﾊﾞｰｼﾞｮﾝ情報 names AGE (engine shell spec §5.5): the
/// version it shows and the FFmpeg notice.
/// </summary>
public class EngineIdentityTests
{
    [Theory]
    [InlineData("v0.4.0", "0.4.0")]
    [InlineData("v0.4.0-32-gabc2403", "0.4.0-32-gabc2403")]
    [InlineData("v0.4.0-32-gabc2403+\n", "0.4.0-32-gabc2403+")]
    [InlineData("abc2403", "dev-abc2403")]
    [InlineData("abc2403+", "dev-abc2403+")]
    [InlineData("0.5.0-rc1", "0.5.0-rc1")]
    [InlineData("1234", "1234")]
    [InlineData("vintage", "vintage")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void DisplayVersionDropsTheTagsVAndMarksBareCommits(string? described, string shown) =>
        Assert.Equal(shown, EngineIdentity.DisplayVersion(described));

    [Fact]
    public void TheFfmpegNoticeNamesFfmpegItsLicenseAndItsSource() =>
        // FFmpeg's license checklist: the about box names FFmpeg, its license and where its source is.
        Assert.All(["FFmpeg", "LGPLv2.1", "FFmpeg-SOURCE.txt"], part => Assert.Contains(part, EngineIdentity.FfmpegNotice));
}
