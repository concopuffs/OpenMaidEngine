using System.Security.Cryptography;
using Age.Engine.Model;
using Age.Engine.Sys4;

namespace Age.Engine.Tests;

public sealed class KamidoriTranslationOverlayTests
{
    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void InstalledTranslationOverlaySelectsTranslatedTitleAndDecodesBmpGraphics()
    {
        string gameRoot = Path.Combine(Paths.Workspace, "Kamidori");
        string patchRoot = Path.Combine(gameRoot, "patch");
        Assert.True(File.Exists(Path.Combine(gameRoot, "SYS4INI.BIN")));
        Assert.True(Directory.Exists(patchRoot));

        AssetLaunchOptions options = AssetLaunchOptions.Resolve(
            ["--overlay-root", "patch", "--allow-bmp-as-agf"], gameRoot);
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(Path.Combine(gameRoot, "SYS4INI.BIN"));
        var store = new Sys4AssetStore(catalog, gameRoot, options.LooseRoots.ToArray());
        var resources = new ResourceMap(
            catalog, store, allowBmpAsAgf: options.AllowBmpAsAgf);

        byte[] title = store.ReadAll(catalog.ResolveName("TITLE.BIN")!);
        Assert.Equal(
            "d50d2a6b0de63f53ae350a41494009e1e0ad0dafb2cd39121a7f7f459546c899",
            Convert.ToHexString(SHA256.HashData(title)).ToLowerInvariant());

        var menu = resources.DecodeTexture(catalog.ResolveRaw(0x4552)!);
        var background = resources.DecodeTexture(catalog.ResolveRaw(0x4553)!);
        Assert.Equal((720, 700), (menu.Width, menu.Height));
        Assert.Equal((1024, 576), (background.Width, background.Height));
        Assert.Contains(menu.Pixels.Where((_, index) => (index & 3) == 3), alpha => alpha == 0);
        Assert.Contains(menu.Pixels.Where((_, index) => (index & 3) == 3), alpha => alpha is > 0 and < 255);
        Assert.All(background.Pixels.Where((_, index) => (index & 3) == 3),
                   alpha => Assert.Equal(255, alpha));
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void InstalledTranslationSc2310UsesReusableDynamicSpeakerNameEmitter()
    {
        string gameRoot = Path.Combine(Paths.Workspace, "Kamidori");
        string patchRoot = Path.Combine(gameRoot, "patch");
        Assert.True(File.Exists(Path.Combine(patchRoot, "SC2310.BIN")));

        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(Path.Combine(gameRoot, "SYS4INI.BIN"));
        var store = new Sys4AssetStore(catalog, gameRoot, patchRoot, gameRoot);
        var scripts = new Sys4ScriptProvider(table, catalog, store);

        Instruction speakerName = Assert.Single(scripts.RequireByName("SC2310.BIN").Instructions,
            instruction => instruction.Offset == 0x57b7);

        Assert.Equal(0x6e, speakerName.Opcode);
        Assert.Equal(
            new[] { new Operand(0, 9), new Operand(14, 0) },
            speakerName.Args);
    }
}
