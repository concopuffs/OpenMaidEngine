using System.Text;
using Ome.Engine.Frontend.NativeMenu;
using Ome.Engine.Frontend.Shell;

/// <summary>
/// The texts AGERC's 設定 dialog writes into its pages (docs/engine-re.md, "設定 dialog"), as its wsprintfA formats
/// build them. Expected strings are native's, captured from AGE's dialog (2026-11-05) where the machine allows.
/// </summary>
public class AgercSettingsTextTests
{
    [Fact]
    public void EachSettingsSectionOpensItsTab()
    {
        Assert.Equal(["ﾒｯｾｰｼﾞ", "ｻｳﾝﾄﾞ", "その他", "動作環境"], AgercSettingsText.Tabs);
        Assert.Equal(0, AgercSettingsText.StartTab(ShellSection.Message));                  // 0x9c6c
        Assert.Equal(1, AgercSettingsText.StartTab(ShellSection.Sound));                    // 0x9c68
        Assert.Equal(2, AgercSettingsText.StartTab(ShellSection.Other));                    // 0x9c6e
        Assert.Equal(3, AgercSettingsText.StartTab(ShellSection.Info, diagnostics: true));  // 0x9c6d
        Assert.Equal(-1, AgercSettingsText.StartTab(ShellSection.Info));                    // ﾊﾞｰｼﾞｮﾝ情報's own dialog
    }

    [Fact]
    public void MessagePageEditsFollowTheirTrackbars()
    {
        // Trackbar 0x414 is 100 − MessageSpeed: native's default 5 reads ｳｴｲﾄ5, 0 reads ﾉｰｳｴｲﾄ.
        Assert.Equal("ｳｴｲﾄ5", AgercSettingsText.MessageSpeed(95));
        Assert.Equal("ﾉｰｳｴｲﾄ", AgercSettingsText.MessageSpeed(100));
        Assert.Equal("ｳｴｲﾄ100", AgercSettingsText.MessageSpeed(0));
        // Trackbar 0x415 is 16 − MesWinAlpha; the percentage truncates.
        Assert.Equal("50%", AgercSettingsText.WindowOpacity(16 - 8));
        Assert.Equal("31%", AgercSettingsText.WindowOpacity(5));
        Assert.Equal("100%", AgercSettingsText.WindowOpacity(16));
        // Trackbars 0x416 and 0x417 are AutoMessageTime0/1 in tenths of a second.
        Assert.Equal("0.5秒", AgercSettingsText.AutoWait(500 / 100));
        Assert.Equal("2.0秒", AgercSettingsText.AutoWait(2000 / 100));
        Assert.Equal("10.0秒", AgercSettingsText.AutoWait(100));
        Assert.Equal("50%", AgercSettingsText.Percent(50));
    }

    [Theory]
    [InlineData(10000, 0)]       // unconfigured volumes read 10000 (the sound object's +0x530)
    [InlineData(0, 100)]
    [InlineData(2550, 75)]
    public void VolumeTrackbarsRunFromLoudAtTheTopToSilentAtTheBottom(int basisPoints, int position)
    {
        Assert.Equal(position, AgercSettingsText.VolumePosition(basisPoints));
        Assert.Equal(basisPoints / 100 * 100, AgercSettingsText.VolumeBasisPoints(position));
    }

    [Fact]
    public void DriveSpaceSwitchesToGigabytesPast1000Megabytes()
    {
        Assert.Equal(" C:  476.93GByte / 512MByte", AgercSettingsText.DriveSpace('C', 476937L << 20, 512L << 20));
        Assert.Equal(" S:  1000MByte / 1.00GByte", AgercSettingsText.DriveSpace('S', 1000L << 20, 1005L << 20));
        Assert.Equal(" D:  1.05GByte / 0MByte", AgercSettingsText.DriveSpace('D', 1050L << 20, 1023));
    }

    [Fact]
    public void MemoryCapsPhysicalAt2GiBAndWrapsTheVramProduct()
    {
        // A 32-bit AGE sees at most 2 GiB − 1 of physical memory; 4095 MB of display memory × 1,000,000 wraps.
        Assert.Equal(" 2048MByte / -199MByte", AgercSettingsText.Memory(64L << 30, 4095));
        Assert.Equal(" 1025MByte / 512MByte", AgercSettingsText.Memory(1L << 30, 512));
        Assert.Equal(" 2048MByte / 2147MByte", AgercSettingsText.Memory(int.MaxValue, 2147));
        Assert.Equal(" 2048MByte / -2146MByte", AgercSettingsText.Memory(int.MaxValue, 2148));
    }

    [Fact]
    public void CpuKeepsTheBrandsTrailingSpaces()
    {
        string brand = "AMD Ryzen 7 9800X3D 8-Core Processor".PadRight(47);   // 47 characters and a NUL
        var registers = new int[12];
        byte[] bytes = Encoding.ASCII.GetBytes(brand);
        for (int i = 0; i < bytes.Length; i++) registers[i / 4] |= bytes[i] << (8 * (i % 4));
        Assert.Equal(brand, AgercSettingsText.CpuBrand(registers));
        Assert.Equal(" AMD Ryzen 7 9800X3D 8-Core Processor            / 0MHz",
                     AgercSettingsText.Cpu(brand, 0));
        Assert.Equal(" Intel(R) Core(TM) i7 / 3600MHz", AgercSettingsText.Cpu("     Intel(R) Core(TM) i7", 3600));
    }

    [Fact]
    public void WindowsAndDirectXVersionsReadAsAgeReportsThem()
    {
        Assert.Equal(" Windows Vista 以降  (6.2.9200)", AgercSettingsText.WindowsVersion(6, 2, 9200, ""));
        Assert.Equal(" Windows XP Service Pack 3 (5.1.2600)",
                     AgercSettingsText.WindowsVersion(5, 1, 2600, "Service Pack 3"));
        Assert.Equal(" Windows 2000  (5.0.2195)", AgercSettingsText.WindowsVersion(5, 0, 2195, ""));
        Assert.Equal(" DirectX 9.0 (4.09.00.0904)",
                     AgercSettingsText.DirectXVersion([0, 0, 0, 9, 0, 0, 0, 0], "4.09.00.0904"));
        Assert.Equal("", AgercSettingsText.DirectXVersion([0, 0, 0, 9, 0, 0, 0, 0], null));
        Assert.Equal("", AgercSettingsText.DirectXVersion([], ""));
    }
}
