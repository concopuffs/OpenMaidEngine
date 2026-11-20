using System.Numerics;
using Ome.Engine.Devices;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

// The leaf effect in the frame lowering (docs/superpowers/specs/2026-11-20-title-leaves-design.md §3). Native draws
// the effect with Direct3D's fixed function in the frame walk, depth off and alpha blended; the port transforms and
// lights each leaf here, on the engine thread, and hands the renderers a polygon item a leaf, so neither has a 3D pass.
public sealed partial class Graphics
{
    /// <summary>The far plane native's projection takes, and its near one (gfx_setup_3d_transforms_and_lights@0x486260).
    /// </summary>
    private const float NearPlane = 1f, FarPlane = 1000f;
    /// <summary>The most steps one frame takes (display_scene_effects_step_60hz@0x44f6f0).</summary>
    private const int MostStepsAFrame = 100;
    private const int DirectionalLight = 3;

    private long? _sceneEffectsLastMs;
    private readonly bool[] _skippedLightReported = new bool[Scene3DState.LightCount];
    // Scratch for one leaf's corners: where each lands and the light it gets, reused leaf to leaf.
    private Vector4[] _leafClip = [];
    private Vector3[] _leafLight = [];

    /// <summary>Whether the frame walk draws the leaves: the effect exists and has leaves.</summary>
    private bool HasLeavesToDraw => _scene3D.Leaf is { Leaves.Count: > 0 };

    /// <summary>
    /// display_scene_effects_step_60hz@0x44f6f0, which runs after each frame walk: the 1/60 s ticks between the last
    /// lowering and this one, at most 100, each a step of every leaf. It runs whether or not an effect exists, so a new
    /// effect starts from the present.
    /// </summary>
    private void StepSceneEffects(long nowMs)
    {
        long last = _sceneEffectsLastMs ?? nowMs;
        _sceneEffectsLastMs = nowMs;
        long ticks = Math.Min(FloorDiv(nowMs * 60, 1000) - FloorDiv(last * 60, 1000), MostStepsAFrame);
        if (ticks > 0) _scene3D.Leaf?.Step(GameRandom, (int)ticks);

        static long FloorDiv(long value, long divisor)
            => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
    }

    /// <summary>
    /// The leaves where they are, one polygon item each in index order, as leaf_effect_draw_all@0x471e50 draws them:
    /// world = scale · yaw-pitch-roll · translation; every material colour times (1, 1, 1, sin(π(5 - y)/5)); Direct3D 9's
    /// fixed-function lighting; counter-clockwise triangles on screen culled; the alpha blend; no depth test.
    /// </summary>
    private void LowerLeaves(List<FrameItem> items)
    {
        if (_scene3D.Leaf is not { } effect || _screenWidth <= 0 || _screenHeight <= 0) return;
        Matrix4x4 viewProjection = View() * Projection(_screenWidth, _screenHeight);
        Vector3 ambient = Rgb(_scene3D.AmbientArgb ?? 0);
        foreach (Scene3DLeaf leaf in effect.Leaves)
        {
            if (effect.Meshes[leaf.Mesh].Geometry is not { TriangleCount: > 0 } mesh) continue;
            Matrix4x4 world = Matrix4x4.CreateScale(leaf.Scale)
                              * Matrix4x4.CreateFromYawPitchRoll(leaf.Rotation.X, leaf.Rotation.Y, leaf.Rotation.Z)
                              * Matrix4x4.CreateTranslation(leaf.Position);
            float fade = (float)Math.Sin((float)(((double)Scene3DLeafEffect.Height - leaf.Position.Y)
                                                 * Scene3DLeafEffect.NativePi / Scene3DLeafEffect.Height));
            if (LowerLeaf(mesh, world, world * viewProjection, ambient, fade) is { } geometry)
                items.Add(FrameItem.PolygonItem(geometry, 1f));
        }
    }

    private FramePolygonGeometry? LowerLeaf(XMesh mesh, Matrix4x4 world, Matrix4x4 clipFromMesh, Vector3 ambient,
                                            float fade)
    {
        XMeshVertex[] vertices = mesh.VertexArray;
        if (_leafClip.Length < vertices.Length)
        {
            _leafClip = new Vector4[vertices.Length];
            _leafLight = new Vector3[vertices.Length];
        }
        // Direct3D lights in camera space, with the normal through the inverse transpose of world·view and not
        // renormalized (D3DRS_NORMALIZENORMALS is off). The view is a rotation, so a world-space dot product is the same.
        Matrix4x4 normalMatrix = Matrix4x4.Invert(world, out Matrix4x4 inverse) ? Matrix4x4.Transpose(inverse) : world;
        for (int index = 0; index < vertices.Length; index++)
        {
            _leafClip[index] = Vector4.Transform(new Vector4(vertices[index].Position, 1f), clipFromMesh);
            _leafLight[index] = LightAt(Vector3.TransformNormal(vertices[index].Normal, normalMatrix), ambient);
        }

        int[] indices = mesh.IndexArray;
        int[] triangleMaterials = mesh.TriangleMaterialArray;
        XMeshMaterial[] materials = mesh.MaterialArray;
        Span<bool> kept = mesh.TriangleCount <= 4096 ? stackalloc bool[mesh.TriangleCount] : new bool[mesh.TriangleCount];
        int keptCount = 0;
        for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            kept[triangle] = Faces(_leafClip[indices[triangle * 3]], _leafClip[indices[triangle * 3 + 1]],
                                   _leafClip[indices[triangle * 3 + 2]]);
            if (kept[triangle]) keptCount++;
        }
        if (keptCount == 0) return null;

        var corners = new FramePolygonVertex[keptCount * 3];
        int at = 0;
        for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            if (!kept[triangle]) continue;
            XMeshMaterial material = materials[triangleMaterials[triangle]];
            for (int corner = 0; corner < 3; corner++)
            {
                int vertex = indices[triangle * 3 + corner];
                (float x, float y) = Screen(_leafClip[vertex]);
                corners[at++] = new FramePolygonVertex(x, y, LitColor(material, _leafLight[vertex], fade), 0, 0);
            }
        }
        return new FramePolygonGeometry(corners, FramePolygonBlend.Alpha);
    }

    // The light a corner gets, before the material: the ambient colour plus each directional light's diffuse times
    // max(0, N·-direction). Native's lights have no ambient or specular part (gfx_set_light@0x4867a0), and Direct3D's
    // specular is off by default. A light of another type is skipped, and reported once.
    private Vector3 LightAt(Vector3 normal, Vector3 ambient)
    {
        Vector3 light = ambient;
        IReadOnlyList<Scene3DLight?> lights = _scene3D.Lights;
        for (int index = 0; index < lights.Count; index++)
        {
            if (lights[index] is not { } set) continue;
            if (set.Type != DirectionalLight)
            {
                if (!_skippedLightReported[index])
                {
                    _skippedLightReported[index] = true;
                    _timeline?.Event("leaf-light-skipped", new() { ["light"] = index, ["type"] = set.Type });
                }
                continue;
            }
            var toLight = new Vector3(-set.Direction.X, -set.Direction.Y, -set.Direction.Z);
            float lit = Vector3.Dot(normal, toLight);
            if (lit > 0) light += Rgb(set.DiffuseArgb) * lit;
        }
        return light;
    }

    // The fixed function's colour with the material native draws: its ambient is its diffuse (mesh_load_from_x_memory
    // copies it), and every colour is multiplied by (1, 1, 1, fade). Each channel is clamped, then rounded to a byte.
    private static uint LitColor(XMeshMaterial material, Vector3 light, float fade)
    {
        Vector4 diffuse = material.Diffuse;
        Vector3 color = material.Emissive + new Vector3(diffuse.X, diffuse.Y, diffuse.Z) * light;
        return Byte(diffuse.W * fade) << 24 | Byte(color.X) << 16 | Byte(color.Y) << 8 | Byte(color.Z);

        static uint Byte(float value) => (uint)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
    }

    // A triangle Direct3D draws: every corner inside the depth range (0 <= z <= w), and wound clockwise on screen,
    // since D3DCULL_CCW, the default, culls the other winding (no SetRenderState of D3DRS_CULLMODE was found).
    private bool Faces(Vector4 a, Vector4 b, Vector4 c)
    {
        if (!InDepth(a) || !InDepth(b) || !InDepth(c)) return false;
        (float ax, float ay) = Screen(a);
        (float bx, float by) = Screen(b);
        (float cx, float cy) = Screen(c);
        // On a screen whose y runs down, a positive cross product is a clockwise winding.
        return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax) > 0;

        static bool InDepth(Vector4 clip) => clip.W > 0 && clip.Z >= 0 && clip.Z <= clip.W;
    }

    // The viewport's mapping (the canvas), plus the half pixel that makes the renderers' Direct3D 9 pixel-centre rule
    // land a corner where Direct3D does: a polygon item's coordinates are native's vertex position + 0.5.
    private (float X, float Y) Screen(Vector4 clip)
    {
        float x = clip.X / clip.W, y = clip.Y / clip.W;
        return ((1f + x) * _screenWidth / 2f + 0.5f, (1f - y) * _screenHeight / 2f + 0.5f);
    }

    /// <summary>Op 0x32c's view, D3DXMatrixLookAtLH with the y axis up; identity until the op runs, as Direct3D's view
    /// starts.</summary>
    private Matrix4x4 View()
    {
        if (_scene3D.Camera is not { } camera) return Matrix4x4.Identity;
        var eye = new Vector3(camera.Eye.X, camera.Eye.Y, camera.Eye.Z);
        var target = new Vector3(camera.Target.X, camera.Target.Y, camera.Target.Z);
        return LookAtLeftHanded(eye, target, Vector3.UnitY);
    }

    /// <summary>D3DXMatrixLookAtLH.</summary>
    internal static Matrix4x4 LookAtLeftHanded(Vector3 eye, Vector3 target, Vector3 up)
    {
        Vector3 zAxis = Vector3.Normalize(target - eye);
        Vector3 xAxis = Vector3.Normalize(Vector3.Cross(up, zAxis));
        Vector3 yAxis = Vector3.Cross(zAxis, xAxis);
        return new Matrix4x4(xAxis.X, yAxis.X, zAxis.X, 0,
                             xAxis.Y, yAxis.Y, zAxis.Y, 0,
                             xAxis.Z, yAxis.Z, zAxis.Z, 0,
                             -Vector3.Dot(xAxis, eye), -Vector3.Dot(yAxis, eye), -Vector3.Dot(zAxis, eye), 1);
    }

    /// <summary>Native's projection: D3DXMatrixPerspectiveFovLH(π/4, the viewport's width over its height, 1, 1000).
    /// </summary>
    internal static Matrix4x4 Projection(int width, int height)
    {
        float yScale = 1f / MathF.Tan(MathF.PI / 8f);
        float xScale = yScale / ((float)width / height);
        float depth = FarPlane / (FarPlane - NearPlane);
        return new Matrix4x4(xScale, 0, 0, 0,
                             0, yScale, 0, 0,
                             0, 0, depth, 1,
                             0, 0, -NearPlane * depth, 0);
    }

    // A D3DCOLOR's red, green and blue, 0 to 1, as Direct3D reads a colour render state and as native's ops convert
    // their bytes (each times 1/255).
    private static Vector3 Rgb(uint argb)
        => new(((argb >> 16) & 0xff) / 255f, ((argb >> 8) & 0xff) / 255f, (argb & 0xff) / 255f);
}
