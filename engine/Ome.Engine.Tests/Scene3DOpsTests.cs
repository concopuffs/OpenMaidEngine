using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// AGE 4.38D's 3D scene, ops 0x324 to 0x32f (docs/superpowers/specs/2026-11-19-polygon-objects-design.md §4), which
/// 創刻のアテリアル's title builds its drifting leaves with: these hold the state the handlers keep to what native keeps.
/// The effects themselves, their motion and their drawing are Scene3DLeavesTests' and Scene3DRainAndSnowTests'.
/// </summary>
public class Scene3DOpsTests
{
    private const int Imm = 0, Float = 1, GlobalInt = 3;
    private const int Exit = 0x2, Mov = 0x55, ResetScene = 0x1f6;
    private const int ReleaseEffects = 0x324, EffectHandles = 0x325, Snow = 0x326, FirstEffect = 0x327, Leaf = 0x328;
    private const int LoadMesh = 0x329, ReleaseMesh = 0x32a, ReleaseAllMeshes = 0x32b;
    private const int Camera = 0x32c, Ambient = 0x32d, SetLight = 0x32e, DisableLight = 0x32f;

    private static Operand I(long value) => new(Imm, value);
    private static Operand F(float value) => new(Float, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    private static Operand G(int address) => new(GlobalInt, address);

    private static (VirtualMachine Vm, RecordingWaits Waits) Run(params (int, Operand[])[] instructions)
        => Run("SYS4438", instructions);

    // 創刻のアテリアル's scripts never use ops 0x32a and 0x32f, so its table has neither: the whole catalog's has.
    private static (VirtualMachine Vm, RecordingWaits Waits) Run(string? abi, params (int, Operand[])[] instructions)
    {
        OpcodeTable table = abi == null ? OpcodeTableJson.Load(Paths.OpcodesJson)
                                        : OpcodeTableJson.Load(Paths.OpcodesJson, abi);
        var ops = new List<(int, Operand[])>(instructions) { (Exit, Array.Empty<Operand>()) };
        var waits = new RecordingWaits();
        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "SCENE3D.BIN", ops, []), table, waits);
        vm.Run();
        return (vm, waits);
    }

    [Fact]
    public void TheTitlesSequenceLeavesItsCameraLightsMeshesAndLeafEffect()
    {
        // As TITLE.BIN runs it: the handles, the ambient colour, a light, the camera, two meshes and the leaf effect.
        var (vm, waits) = Run(
            (EffectHandles, [I(2), I(2)]),
            (Ambient, [I(0x1ff), I(0x404040)]),
            (SetLight, [I(0), I(3), F(1), F(2), F(3), F(0), F(-2), F(0), I(0xff), I(0xffeedd), F(100)]),
            (Camera, [F(0), F(0), F(-10), F(0), F(1), F(0)]),
            (LoadMesh, [I(0x1234), I(0)]),
            (LoadMesh, [I(0x1235), I(1)]),
            (Mov, [G(0x400), I(0)]),
            (Mov, [G(0x401), I(1)]),
            (Leaf, [I(7), G(0x400), I(2)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Empty(waits.Warnings);
        Scene3DState scene = vm.Graphics.Scene3D;
        Assert.Equal((2L, 2L), scene.EffectHandles);
        Assert.Equal(0xff404040u, scene.AmbientArgb);       // an alpha above 0xff is 0xff
        Assert.Equal(((0f, 0f, -10f), (0f, 1f, 0f)), scene.Camera);
        // The direction is normalized, as native keeps it.
        Assert.Equal(new Scene3DLight(3, (1, 2, 3), (0, -1, 0), 0xffffeedd, 100), scene.Lights[0]);
        Assert.Equal([0, 1], scene.Meshes.Keys.Order());
        Assert.Equal(7L, scene.Leaf!.Count);                 // op 0x328's first operand is the number of leaves
        Assert.Equal([0x1234L, 0x1235L], scene.Leaf.Meshes.Select(mesh => mesh.ResourceId));
    }

    [Fact]
    public void ReleasingTheEffectsKeepsTheMeshesAndASceneResetKeepsEverything()
    {
        var (vm, _) = Run(
            (LoadMesh, [I(0x1234), I(5)]),
            (Mov, [G(0x400), I(5)]),
            (Leaf, [I(1), G(0x400), I(1)]),
            (FirstEffect, [I(300)]),
            (EffectHandles, [I(10), I(20)]),
            (SetLight, [I(2), I(1), F(0), F(0), F(0), F(0), F(0), F(0), I(0), I(0), F(1)]),
            (ResetScene, []),            // op 0x1f6 clears the retained objects; native's does not touch the 3D scene
            (ReleaseEffects, []));

        Scene3DState scene = vm.Graphics.Scene3D;
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((null, null, null), ((object?)scene.Leaf, (object?)scene.Snow, (object?)scene.Rain));
        Assert.Equal((10L, 20L), scene.EffectHandles);          // op 0x324 leaves the handles
        Assert.Equal([5], scene.Meshes.Keys);
        Assert.NotNull(scene.Lights[2]);

        var (single, _) = Run(null,
            (LoadMesh, [I(1), I(5)]), (LoadMesh, [I(2), I(6)]),
            (SetLight, [I(2), I(1), F(0), F(0), F(0), F(0), F(0), F(0), I(0), I(0), F(1)]),
            (ReleaseMesh, [I(5)]), (ReleaseMesh, [I(9)]), (DisableLight, [I(2)]));
        Assert.Equal("exit", single.HaltReason);
        Assert.Equal([6], single.Graphics.Scene3D.Meshes.Keys);
        Assert.Null(single.Graphics.Scene3D.Lights[2]);

        var (released, _) = Run((LoadMesh, [I(1), I(5)]), (LoadMesh, [I(2), I(6)]), (ReleaseAllMeshes, []));
        Assert.Empty(released.Graphics.Scene3D.Meshes);
    }

    [Fact]
    public void ALeafSlotWithNoMeshAndASnowSlotWithNoTextureAreSkippedWithNativesTrace()
    {
        var (vm, waits) = Run(
            (LoadMesh, [I(0x1234), I(0)]),
            (Mov, [G(0x400), I(0)]),
            (Mov, [G(0x401), I(3)]),
            (Leaf, [I(1), G(0x400), I(2)]),
            (Snow, [I(2), I(40), F(0.5f), I(1)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Single(vm.Graphics.Scene3D.Leaf!.Meshes);         // the effect is built with the meshes that exist
        Assert.Null(vm.Graphics.Scene3D.Snow);
        Assert.Contains(waits.Warnings, warning => warning.Contains("メッシュが作成されていません． MESH=3"));
        Assert.Contains(waits.Warnings, warning => warning.Contains("テクスチャが作成されていません． TEXTURE=40"));
    }

    // A VM over the given graphics native that runs op 0x326 with the operands given, after publishing a texture to
    // slot 40.
    private static (VirtualMachine Vm, RecordingWaits Waits) RunSnow(Graphics graphics, params Operand[] operands)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4438");
        var ops = new List<(int, Operand[])> { (Snow, operands), (Exit, []) };
        var waits = new RecordingWaits();
        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "SNOW.BIN", ops, []), table, waits,
                                    graphics: graphics);
        graphics.PublishSurfacePixels(40, GraphicsTestSupport.Image(2, 2, (_, _) => [255, 255, 255, 255]));
        vm.Run();
        return (vm, waits);
    }

    [Fact]
    public void TheSnowEffectTakesASlotThatHoldsATexture()
    {
        // As DEBUG.BIN runs it, the speed an immediate: native's float accessor converts the integer.
        var (vm, _) = RunSnow(Graphics.Headless(), I(2), I(40), I(1), I(1));

        Assert.Equal("exit", vm.HaltReason);
        Scene3DSnowEffect snow = vm.Graphics.Scene3D.Snow!;
        Assert.Equal((2L, 40, 1f, 1L, true), (snow.Count, snow.TextureSlot, snow.Speed, snow.Option, snow.Fades));
        Assert.Equal(2, snow.Flakes.Count);
        Assert.Equal(8L, vm.Graphics.GameRandom.Draws);          // four draws a flake
    }

    [Fact]
    public void WithAPixelShaderLevelOfZeroNoSnowIsBuiltAndNothingIsSaid()
    {
        var graphics = new Graphics(null, new FrameClock(), new Sys4LogicalCanvas(0, 0)) { PixelShaderLevel = 0 };
        // Slot 41 holds no texture: native does not get as far as tracing it.
        var (vm, waits) = RunSnow(graphics, I(2), I(41), F(0.5f), I(1));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Null(vm.Graphics.Scene3D.Snow);
        Assert.Empty(waits.Warnings);
        Assert.Equal(0L, graphics.GameRandom.Draws);
    }

    [Theory]
    [InlineData(-1, 2)]       // the default: what the device offers, which the port takes as 2
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]        // out of range: as -1
    [InlineData(-5, 2)]
    public void DisplayPixelShaderGivesTheLevel(int setting, int level)
        => Assert.Equal(level, Graphics.PixelShaderLevelFor(setting));

    [Fact]
    public void TooManyFlakesOrDropsHalt()
    {
        var (snow, _) = RunSnow(Graphics.Headless(), I(1 << 21), I(40), F(1), I(1));
        Assert.Equal($"set-3d-effect-snow-flakes-too-many:{1 << 21}", snow.HaltReason);

        var (rain, _) = Run((FirstEffect, [I(1 << 21)]));
        Assert.Equal($"set-3d-effect-rain-drops-too-many:{1 << 21}", rain.HaltReason);
    }

    [Fact]
    public void ALeafSlotOutsideTheMeshTableHalts()
    {
        var (vm, _) = Run((Mov, [G(0x400), I(1000)]), (Leaf, [I(1), G(0x400), I(1)]));

        Assert.StartsWith("index-out-of-range:", vm.HaltReason);
        Assert.Contains("mesh slot 1000 is outside 0..999", vm.HaltReason);
    }

    [Trait("Category", "Workspace")]
    [Trait("Profile", "arterial")]
    [Fact]
    public void TheInstalledGamesLeafMeshesLoadAndAPictureDoesNot()
    {
        string gameRoot = Environment.GetEnvironmentVariable("OME_GAME_ROOT") ?? Paths.GameInstall("arterial");
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"arterial install not found at {gameRoot}");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, gameRoot, gameRoot));
        var graphics = new Graphics(resources, new FrameClock(), new Sys4LogicalCanvas(1024, 576));
        AssetEntry leaf0 = Assert.IsType<AssetEntry>(resources.ResolveName("LEAF0.X"));
        AssetEntry leaf1 = Assert.IsType<AssetEntry>(resources.ResolveName("LEAF1.X"));
        AssetEntry picture = catalog.Files.First(
            entry => !entry.IsPlaceholder && entry.Name.EndsWith(".AGF", StringComparison.OrdinalIgnoreCase));

        Assert.True(graphics.LoadMesh(leaf0.PackedId, 0, out string first));
        Assert.True(graphics.LoadMesh(leaf1.PackedId, 1, out _));
        Assert.Equal("LEAF0.X", first, ignoreCase: true);
        // Each is one frame's mesh of 181 positions and 556 triangles with one grey material.
        foreach (int slot in new[] { 0, 1 })
        {
            XMesh mesh = Assert.IsType<XMesh>(graphics.Scene3D.Meshes[slot].Geometry);
            Assert.Equal(556, mesh.TriangleCount);
            XMeshMaterial material = Assert.Single(mesh.Materials);
            Assert.Equal(0.866667f, material.Diffuse.X, 5);
            Assert.Equal(1f, material.Diffuse.W);
        }
        // A load that fails has released the slot's mesh, and names the file for native's error.
        Assert.False(graphics.LoadMesh(picture.PackedId, 1, out string failed));
        Assert.Equal(picture.Name, failed);
        Assert.Equal([0], graphics.Scene3D.Meshes.Keys);
    }
}
