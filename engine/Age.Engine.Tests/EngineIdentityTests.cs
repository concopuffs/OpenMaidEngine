using Age.Engine.Frontend.NativeResources;
using Age.Engine.Sys4;

/// <summary>
/// The port's ﾍﾙﾌﾟ > ﾊﾞｰｼﾞｮﾝ情報 names Open Maid Engine where AGE names itself (docs/engine-re.md, "Native menu bar",
/// ﾊﾞｰｼﾞｮﾝ情報): the version it shows, and AGERC's dialog 2 with its engine half re-texted.
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

    // AGERC.DLL's dialog 2 as Himegari ships it (Kamidori's translation has the same controls and positions).
    private static DialogTemplate NativeVersionDialog() =>
        new(0x90c808d4, 0, 0, 7, 215, 185, "ﾊﾞｰｼﾞｮﾝ情報", 9, 0, "ＭＳ Ｐゴシック",
        [
            new(2, "Button", "OK", null, 0x50010300, 0, 158, 164, 50, 14),
            new(0x404, "Static", "SYSTEM3", null, 0x50000003, 0, 14, 12, 20, 20),
            new(-1, "Static", "ARCGameEngine", null, 0x50000000, 0, 44, 104, 80, 10),
            new(0x400, "Edit", "", null, 0x50000800, 0, 124, 104, 84, 13),
            new(-1, "Static", "Copyright (c) ARC Software Laboratory  1999-2008", null, 0x50000000, 0, 44, 114, 164, 8),
            new(-1, "Static", "", null, 0x50001007, 0, 7, 83, 201, 1),
            new(-1, "Static", "ｼｽﾃﾑのﾊﾞｰｼﾞｮﾝ", null, 0x50020000, 0, 24, 90, 57, 9),
            new(0x3ee, "Edit", "", null, 0x50000880, 0, 44, 12, 154, 12),
            new(0x3ef, "Edit", "", null, 0x50000880, 0, 44, 23, 84, 12),
            new(0x407, "Edit", "", null, 0x50000880, 0, 44, 42, 164, 12),
            new(-1, "Static", "Copyright (c) 2002, Xiphophorus. All rights reserved.", null, 0x50020000, 0, 44, 149, 164,
                10),
            new(-1, "Static", "このｿﾌﾄｳｪｱはOgg Vorbisのﾗｲﾌﾞﾗﾘを使用しており，著作権は以下に存在しています．", null, 0x50020000, 0,
                44, 131, 164, 18),
            new(0x3f0, "Edit", "", null, 0x50000881, 0, 44, 66, 164, 12),
            new(-1, "Static", "プロダクトID", null, 0x50020000, 0, 7, 66, 37, 8),
        ]);

    [Fact]
    public void VersionDialogNamesThisEngineAndDropsTheProductId()
    {
        DialogTemplate native = NativeVersionDialog();
        DialogTemplate port = EngineIdentity.VersionDialogTemplate(native);

        // The frame, the game half, the separator, the ｼｽﾃﾑのﾊﾞｰｼﾞｮﾝ label and the version edit keep AGE's controls.
        Assert.Equal((native.Title, native.Width, native.Height, native.FontFace),
                     (port.Title, port.Width, port.Height, port.FontFace));
        Assert.Equal(12, port.Items.Count);
        Assert.Equal([0x400, 0x3ee, 0x3ef, 0x407], port.Items.Where(i => i.ClassName == "Edit").Select(i => i.Id));
        Assert.Same(native.Items[1], port.Items[1]);
        Assert.Same(native.Items[5], port.Items[5]);
        Assert.Same(native.Items[6], port.Items[6]);

        // The engine half: AGE's labels in place, with this engine's texts. The name's label narrows to the name and
        // the version edit widens into the room, keeping AGE's right edge.
        Assert.Equal((44, 56), (port.Items[2].X, port.Items[2].Width));
        Assert.Equal((104, 104, 104, 13), (port.Items[3].X, port.Items[3].Y, port.Items[3].Width, port.Items[3].Height));
        Assert.Equal(
        [
            (EngineIdentity.Name, 44, 104), (EngineIdentity.License, 44, 114), (EngineIdentity.NoticesPointer, 44, 149),
            (EngineIdentity.FfmpegNotice, 44, 131),
        ],
            port.Items.Where(i => i.ClassName == "Static" && i.Y > 100).Select(i => (i.Text, i.X, i.Y)));
        Assert.DoesNotContain(port.Items, i => i.Text.Contains("ARC") || i.Text.Contains("Vorbis")
                                                 || i.Text.Contains("Xiphophorus") || i.Text == "プロダクトID");

        // FFmpeg's license checklist: the about box names FFmpeg, its license and where its source is.
        Assert.All(["FFmpeg", "LGPLv2.1", "FFmpeg-SOURCE.txt"], part => Assert.Contains(part, EngineIdentity.FfmpegNotice));
    }

    [Fact]
    public void InstalledGamesVersionDialogsHaveEveryEngineLabel()
    {
        // The clean Kamidori install's AGERC.DLL is packed: its dialogs are not in the file (backlog.md).
        foreach (string root in new[] { Paths.GameDir, Paths.KamidoriTranslatedDir })
        {
            string agerc = Path.Combine(root, "AGERC.DLL");
            if (!File.Exists(agerc)) continue;
            DialogTemplate port = EngineIdentity.VersionDialogTemplate(
                DialogTemplateResource.Load(File.ReadAllBytes(agerc), 2)!);
            Assert.Equal(12, port.Items.Count);
            string[] texts = port.Items.Select(i => i.Text).ToArray();
            Assert.Contains(EngineIdentity.Name, texts);
            Assert.Contains(EngineIdentity.License, texts);
            Assert.Contains(EngineIdentity.FfmpegNotice, texts);
            Assert.Contains(EngineIdentity.NoticesPointer, texts);
            Assert.DoesNotContain(port.Items, i => i.Text.Contains("ARC") || i.Text.Contains("Xiphophorus"));
        }
    }
}
