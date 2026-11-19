using System.Globalization;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

/// <summary>A mesh slot's file (AGE 4.38D, op 0x329): the catalog record it was loaded from.</summary>
public readonly record struct Scene3DMesh(long ResourceId, string Name);

/// <summary>A Direct3D light as op 0x32e sets it (native keeps a D3DLIGHT9): its type, its position, its direction
/// (normalized, as native stores it), its diffuse colour (0xAARRGGBB) and its range.</summary>
public readonly record struct Scene3DLight(int Type, (float X, float Y, float Z) Position,
                                           (float X, float Y, float Z) Direction, uint DiffuseArgb, float Range);

/// <summary>The leaf effect op 0x328 builds (native's Set3DEffectLeaf): its id and the meshes it was given.</summary>
public sealed record Scene3DLeafEffect(long Id, IReadOnlyList<Scene3DMesh> Meshes);

/// <summary>The snow effect op 0x326 builds (native's Set3DEffectSnow): its id, the surface slot whose texture it
/// draws with, and its two other operands, a float and an integer, whose meaning is not read.</summary>
public sealed record Scene3DSnowEffect(long Id, int TextureSlot, float Value, long Option);

/// <summary>
/// AGE 4.38D's 3D scene as the scripts set it (ops 0x324 to 0x32f): mesh slots, a camera, an ambient colour, lights,
/// and the display's three effect objects with the two handles the frame walk draws them at. 創刻のアテリアル's title
/// builds its drifting leaves with it. The port keeps this state and draws none of it yet: the user decided so on
/// 2026-11-19, and drawing the leaves is owed (docs/backlog.md, "The title's 3D leaves are not drawn";
/// docs/superpowers/specs/2026-11-19-polygon-objects-design.md §4).
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
    /// <summary>Op 0x327's effect, the display's first: its element count. What it draws is not read.</summary>
    public long? FirstEffectElements { get; internal set; }
    public Scene3DSnowEffect? Snow { get; internal set; }
    public Scene3DLeafEffect? Leaf { get; internal set; }

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

    /// <summary>The 3D scene the scripts have set. Nothing draws it yet.</summary>
    public Scene3DState Scene3D => _scene3D;

    /// <summary>Op 0x329: load a catalog file into a mesh slot. Native releases the slot's mesh first, whether or not
    /// the load then succeeds. False when the file is no Direct3D .X mesh; <paramref name="name"/> is then the file
    /// native's error names. With no catalog (a headless run) every load succeeds.</summary>
    public bool LoadMesh(long resourceId, int slot, out string name)
    {
        _scene3D.RemoveMesh(slot);
        name = resourceId.ToString(CultureInfo.InvariantCulture);
        if (_res != null)
        {
            name = _res.RecordName(resourceId) ?? name;
            AssetEntry? entry = _res.ResolveMesh(resourceId);
            if (entry == null || !_res.IsMesh(entry)) return false;
            name = entry.Name;
        }
        _scene3D.SetMesh(slot, new Scene3DMesh(resourceId, name));
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
        _scene3D.FirstEffectElements = null;
        _scene3D.Snow = null;
        _scene3D.Leaf = null;
    }

    /// <summary>Op 0x327: build the display's first effect, with <paramref name="elements"/> elements, in place of
    /// the one it had.</summary>
    public void SetFirstEffect(long elements) => _scene3D.FirstEffectElements = elements;

    /// <summary>Op 0x326 (Set3DEffectSnow): build the snow effect from a surface slot's texture, in place of the one
    /// the display had. False when the slot holds no texture: native traces it and leaves the effect as it was.
    /// Native builds it only when the manager's +0xb62c is above 0, which is not read; the port takes it as so.
    /// </summary>
    public bool SetSnowEffect(long id, int textureSlot, float value, long option)
    {
        if (!_surfaceImages.ContainsKey(textureSlot) && !_surfaceResources.ContainsKey(textureSlot)) return false;
        _scene3D.Snow = new Scene3DSnowEffect(id, textureSlot, value, option);
        return true;
    }

    /// <summary>Op 0x328 (Set3DEffectLeaf): build the leaf effect, in place of the one the display had, and give it
    /// the mesh of each named slot. Returns the slots that hold no mesh, which native traces and skips.</summary>
    public IReadOnlyList<int> SetLeafEffect(long id, IReadOnlyList<int> meshSlots)
    {
        var meshes = new List<Scene3DMesh>(meshSlots.Count);
        List<int>? empty = null;
        foreach (int slot in meshSlots)
        {
            if (_scene3D.Meshes.TryGetValue(slot, out Scene3DMesh mesh)) meshes.Add(mesh);
            else (empty ??= new List<int>()).Add(slot);
        }
        _scene3D.Leaf = new Scene3DLeafEffect(id, meshes);
        _timeline?.Event("leaf-effect", new() { ["id"] = id, ["meshes"] = meshes.Count });
        return empty ?? (IReadOnlyList<int>)System.Array.Empty<int>();
    }
}
