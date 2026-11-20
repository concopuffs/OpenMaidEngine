using System.Globalization;
using System.Numerics;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

/// <summary>A mesh slot's file (AGE 4.38D, op 0x329): the catalog record it was loaded from and the mesh read from it,
/// which a run with no catalog has none of.</summary>
public readonly record struct Scene3DMesh(long ResourceId, string Name, XMesh? Geometry = null);

/// <summary>A Direct3D light as op 0x32e sets it (native keeps a D3DLIGHT9): its type, its position, its direction
/// (normalized, as native stores it), its diffuse colour (0xAARRGGBB) and its range.</summary>
public readonly record struct Scene3DLight(int Type, (float X, float Y, float Z) Position,
                                           (float X, float Y, float Z) Direction, uint DiffuseArgb, float Range);

/// <summary>
/// AGE 4.38D's 3D scene as the scripts set it (ops 0x324 to 0x32f): mesh slots, a camera, an ambient colour, lights,
/// and the display's three effect objects (the rain, the snow and the leaves) with the two handles the frame walk draws
/// them at. 創刻のアテリアル's title builds its drifting leaves with it, and DEBUG.BIN all three; the frame lowering
/// draws them (Graphics.Scene3DLowering.cs; docs/superpowers/specs/2026-11-20-title-leaves-design.md for the leaves,
/// docs/engine-re.md, "The 3D scene", for the other two).
/// </summary>
/// <remarks>Native keeps all of it across a scene reset: only the opcodes, the manager's constructor and a lost device
/// touch the mesh slots, the lights and the camera, and only op 0x324 and the display's destructor release the
/// effects. So it is not scene state here either.</remarks>
public sealed class Scene3DState
{
    /// <summary>Native's mesh slots (manager +0xc5f4), which op 0x32b releases one by one.</summary>
    public const int MeshSlotCount = 1000;
    /// <summary>Native's light records (manager +0xd5bc) and their enabled flags (+0xd594).</summary>
    public const int LightCount = 10;

    private readonly Dictionary<int, Scene3DMesh> _meshes = new();
    private readonly Scene3DLight?[] _lights = new Scene3DLight?[LightCount];

    /// <summary>The mesh each loaded slot holds.</summary>
    public IReadOnlyDictionary<int, Scene3DMesh> Meshes => _meshes;
    /// <summary>The lights that are set and enabled, by index.</summary>
    public IReadOnlyList<Scene3DLight?> Lights => _lights;
    /// <summary>Op 0x32c's camera: an eye point and the point it looks at, with the y axis up. Null until set.</summary>
    public ((float X, float Y, float Z) Eye, (float X, float Y, float Z) Target)? Camera { get; internal set; }
    /// <summary>Op 0x32d's ambient colour (0xAARRGGBB). Null until set.</summary>
    public uint? AmbientArgb { get; internal set; }
    /// <summary>Op 0x325's two handles: the frame walk starts the first effect when it reaches the first, and draws
    /// the effects when it reaches the second.</summary>
    public (long First, long Second) EffectHandles { get; internal set; }
    /// <summary>Op 0x327's effect, the display's first (+0x408).</summary>
    public Scene3DRainEffect? Rain { get; internal set; }
    /// <summary>Op 0x326's effect, the display's second (+0x40c).</summary>
    public Scene3DSnowEffect? Snow { get; internal set; }
    /// <summary>Op 0x328's effect, the display's third (+0x410).</summary>
    public Scene3DLeafEffect? Leaf { get; internal set; }
    /// <summary>
    /// The view the display last handed its effects (+0x458). Native copies it from the manager's view (+0xa3a8) only
    /// when it sets the Direct3D device up (gfx_setup_3d_transforms_and_lights@0x486260): at the start, when the view
    /// is the identity, and on a screen-mode switch or a reset device, when it is op 0x32c's camera. Op 0x32c sets only
    /// the device's view. The snow projects with this one; the rain and the leaves are drawn with the device's.
    /// </summary>
    public Matrix4x4 EffectView { get; internal set; } = Matrix4x4.Identity;

    internal void SetMesh(int slot, Scene3DMesh mesh) => _meshes[slot] = mesh;
    internal bool RemoveMesh(int slot) => _meshes.Remove(slot);
    internal void ClearMeshes() => _meshes.Clear();
    internal void SetLight(int index, Scene3DLight? light) => _lights[index] = light;
}

// The 3D scene's ops, each one call as native runs it (gfx_mesh_slot_load, gfx_set_view_look_at, gfx_set_light and
// the rest; each carries its reading in the Ghidra image /arterial/4.38D).
public sealed partial class Graphics
{
    private readonly Scene3DState _scene3D = new();

    /// <summary>The 3D scene the scripts have set.</summary>
    public Scene3DState Scene3D => _scene3D;

    /// <summary>Op 0x329: load a catalog file into a mesh slot. Native releases the slot's mesh first, whether or not
    /// the load then succeeds. False when the file is no text Direct3D .X mesh (<see cref="XMeshDecoder"/>);
    /// <paramref name="name"/> is then the file native's error names. With no catalog (a headless run) every load
    /// succeeds, and the slot holds no geometry.</summary>
    public bool LoadMesh(long resourceId, int slot, out string name)
    {
        _scene3D.RemoveMesh(slot);
        name = resourceId.ToString(CultureInfo.InvariantCulture);
        XMesh? geometry = null;
        if (_res != null)
        {
            name = _res.RecordName(resourceId) ?? name;
            AssetEntry? entry = _res.ResolveMesh(resourceId);
            if (entry == null) return false;
            name = entry.Name;
            try
            {
                geometry = _res.DecodeMesh(entry);
            }
            catch (XMeshFormatException error)
            {
                _print(error.Message);
                return false;
            }
        }
        _scene3D.SetMesh(slot, new Scene3DMesh(resourceId, name, geometry));
        _timeline?.Event("mesh-load", new() { ["slot"] = slot, ["resource"] = resourceId, ["name"] = name });
        return true;
    }

    /// <summary>Op 0x32a: release a mesh slot.</summary>
    public void ReleaseMesh(int slot) => _scene3D.RemoveMesh(slot);

    /// <summary>Op 0x32b: release every mesh slot.</summary>
    public void ReleaseAllMeshes() => _scene3D.ClearMeshes();

    /// <summary>Op 0x32c: the camera, a left-handed look-at view from <paramref name="eye"/> to
    /// <paramref name="target"/> with the y axis up.</summary>
    public void SetCameraLookAt((float X, float Y, float Z) eye, (float X, float Y, float Z) target)
        => _scene3D.Camera = (eye, target);

    /// <summary>Op 0x32d: the ambient light colour (0xAARRGGBB).</summary>
    public void SetAmbientLight(uint argb) => _scene3D.AmbientArgb = argb;

    /// <summary>Op 0x32e: set a light and enable it. Native normalizes the direction.</summary>
    public void SetLight(int index, int type, (float X, float Y, float Z) position,
                         (float X, float Y, float Z) direction, uint diffuseArgb, float range)
    {
        float length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
        if (length > 0) direction = (direction.X / length, direction.Y / length, direction.Z / length);
        _scene3D.SetLight(index, new Scene3DLight(type, position, direction, diffuseArgb, range));
    }

    /// <summary>Op 0x32f: disable a light.</summary>
    public void DisableLight(int index) => _scene3D.SetLight(index, null);

    /// <summary>Op 0x325: the two handles the frame walk draws the effects at.</summary>
    public void SetEffectHandles(long first, long second) => _scene3D.EffectHandles = (first, second);

    /// <summary>Op 0x324: release the display's three effect objects. The two handles stay.</summary>
    public void ReleaseEffects()
    {
        _scene3D.Rain = null;
        _scene3D.Snow = null;
        _scene3D.Leaf = null;
    }

    /// <summary>gfx_setup_3d_transforms_and_lights@0x486260, as a screen-mode switch runs it: the display hands its
    /// effects the projection, the camera's view and the world again (<see cref="Scene3DState.EffectView"/>).</summary>
    public void SetUpDeviceTransforms() => _scene3D.EffectView = View();

    /// <summary>
    /// The pixel shader level native keeps at manager +0xb62c (gfx_create_device_objects@0x4907f0, at device creation):
    /// display:PixelShader when it is 0, 1 or 2, otherwise what the device offers (2 for pixel shader 2.0 or above, 1
    /// for 1.x, 0 below). The port's renderers take the device as 2. Op 0x326 is the only reader here: native builds the
    /// snow only above 0.
    /// </summary>
    public int PixelShaderLevel { get; init; } = 2;

    /// <summary>The level a display:PixelShader setting gives the port.</summary>
    public static int PixelShaderLevelFor(int setting) => setting is >= 0 and <= 2 ? setting : 2;

    /// <summary>Op 0x327: build the display's first effect, the rain, with <paramref name="drops"/> drops, in place of
    /// the one it had. Its drops draw from the game's generator.</summary>
    public void SetRainEffect(long drops)
    {
        _scene3D.Rain = new Scene3DRainEffect(drops, GameRandom);
        _timeline?.Event("rain-effect", new() { ["drops"] = drops });
    }

    /// <summary>Op 0x326 (Set3DEffectSnow): build the snow effect with <paramref name="flakes"/> flakes of a surface
    /// slot's texture, in place of the one the display had. Its flakes draw from the game's generator. Native acts only
    /// when the pixel shader level is above 0 (<see cref="PixelShaderLevel"/>), and says nothing otherwise. False when
    /// the slot holds no texture: native traces it and leaves the effect as it was.</summary>
    public bool SetSnowEffect(long flakes, int textureSlot, float speed, long option)
    {
        if (PixelShaderLevel <= 0) return true;
        if (!_surfaceImages.ContainsKey(textureSlot) && !_surfaceResources.ContainsKey(textureSlot)) return false;
        _scene3D.Snow = new Scene3DSnowEffect(flakes, textureSlot, speed, option, GameRandom);
        _timeline?.Event("snow-effect", new() { ["flakes"] = flakes, ["texture"] = textureSlot });
        return true;
    }

    /// <summary>Op 0x328 (Set3DEffectLeaf): build the leaf effect with <paramref name="count"/> leaves, in place of
    /// the one the display had, and give it the mesh of each named slot. Its leaves draw from the game's generator
    /// (<see cref="GameRandom"/>). Returns the slots that hold no mesh, which native traces and skips.</summary>
    public IReadOnlyList<int> SetLeafEffect(long count, IReadOnlyList<int> meshSlots)
    {
        var meshes = new List<Scene3DMesh>(meshSlots.Count);
        List<int>? empty = null;
        foreach (int slot in meshSlots)
        {
            if (_scene3D.Meshes.TryGetValue(slot, out Scene3DMesh mesh)) meshes.Add(mesh);
            else (empty ??= new List<int>()).Add(slot);
        }
        _scene3D.Leaf = new Scene3DLeafEffect(count, meshes, GameRandom);
        _timeline?.Event("leaf-effect", new() { ["leaves"] = count, ["meshes"] = meshes.Count });
        return empty ?? (IReadOnlyList<int>)System.Array.Empty<int>();
    }
}
