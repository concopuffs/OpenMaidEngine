using System.Numerics;
using Ome.Engine.Devices;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

// The 3D effects in the frame lowering (docs/superpowers/specs/2026-11-20-title-leaves-design.md §3 for the leaves;
// docs/engine-re.md, "The 3D scene", for the rain and the snow). Native draws them in the frame walk, depth off and
// alpha blended: the leaves and the rain with Direct3D's fixed function, the snow with a vertex and pixel shader. The
// port transforms and lights each here, on the engine thread, and hands the renderers polygon items, so neither has a
// 3D pass.
public sealed partial class Graphics
{
    /// <summary>The far plane native's projection takes, and its near one (gfx_setup_3d_transforms_and_lights@0x486260).
    /// </summary>
    private const float NearPlane = 1f, FarPlane = 1000f;
    /// <summary>The most steps one frame takes (display_scene_effects_step_60hz@0x44f6f0).</summary>
    private const int MostStepsAFrame = 100;
    private const int DirectionalLight = 3;

    private long? _sceneEffectsLastMs;
    // The viewport the effects land in: the screen, or the surface a render target publication draws into, whose
    // whole size Direct3D's SetRenderTarget makes the viewport. The projection stays the device's (the screen's).
    private (int Width, int Height) _effectViewport;
    private readonly bool[] _skippedLightReported = new bool[Scene3DState.LightCount];
    // Scratch for one leaf's corners: where each lands and the light it gets, reused leaf to leaf.
    private Vector4[] _leafClip = [];
    private Vector3[] _leafLight = [];

    /// <summary>Whether a walk has an effect to draw at op 0x325's first handle: the rain's rings.</summary>
    private bool HasFirstHandleEffects => _scene3D.Rain is { Drops.Count: > 0 };

    /// <summary>Whether it has one at the second: the rain's streaks, the snow, the leaves.</summary>
    private bool HasSecondHandleEffects
        => HasFirstHandleEffects || _scene3D.Snow is { Flakes.Count: > 0 } || _scene3D.Leaf is { Leaves.Count: > 0 };

    /// <summary>
    /// display_scene_effects_at_handle@0x44f7a0 through one walk of the objects and polygons, as the frame render
    /// (gfx_render_frame@0x49ab00) and a range present (gfx_present_object_range@0x49adf0) call it, into the screen or a
    /// render target: each group once, the rain's rings before the first object or polygon whose handle is at or past op
    /// 0x325's first handle, the rain's streaks, the snow and the leaves before the first at or past its second. At its
    /// end the walk calls it once more: the frame render with -1, which draws every group still pending, a range present
    /// with its first handle plus its count, which leaves a group whose handle is past that undrawn. Native calls it for
    /// hidden objects too, which puts a group in the same place among the visible ones.
    /// </summary>
    private struct SceneEffectWalk(Graphics graphics, bool draws, int viewportWidth, int viewportHeight)
    {
        private bool _firstPending = draws && graphics.HasFirstHandleEffects;
        private bool _secondPending = draws && graphics.HasSecondHandleEffects;

        /// <summary>The walk reaches an object or polygon with this handle.</summary>
        public void At(long handle, List<FrameItem> items)
        {
            if (!_firstPending && !_secondPending) return;
            (long first, long second) = graphics._scene3D.EffectHandles;
            graphics._effectViewport = (viewportWidth, viewportHeight);
            if (_firstPending && handle >= first)
            {
                graphics.LowerRain(items, rings: true);
                _firstPending = false;
            }
            if (_secondPending && handle >= second)
            {
                graphics.LowerSecondHandleEffects(items);
                _secondPending = false;
            }
        }

        /// <summary>The walk ends: <paramref name="lastHandle"/> is a range's first handle plus its count, or
        /// <see cref="long.MaxValue"/> for a walk of every object.</summary>
        public void End(long lastHandle, List<FrameItem> items) => At(lastHandle, items);
    }

    // Whether the device's projection and the viewport have a size to draw at.
    private bool EffectsDrawable
        => _screenWidth > 0 && _screenHeight > 0 && _effectViewport.Width > 0 && _effectViewport.Height > 0;

    /// <summary>The handle a range present's walk ends with: its first handle plus its count, saturated.</summary>
    private static long WalkEnd(long firstHandle, long count)
        => count >= long.MaxValue - Math.Max(firstHandle, 0) ? long.MaxValue : firstHandle + count;

    /// <summary>
    /// display_scene_effects_step_60hz@0x44f6f0, which runs after each frame walk: the 1/60 s ticks between the last
    /// lowering and this one, at most 100, each a step of the rain, then the snow, then the leaves. It runs whether or
    /// not an effect exists, so a new effect starts from the present.
    /// </summary>
    private void StepSceneEffects(long nowMs)
    {
        long last = _sceneEffectsLastMs ?? nowMs;
        _sceneEffectsLastMs = nowMs;
        long ticks = Math.Min(FloorDiv(nowMs * 60, 1000) - FloorDiv(last * 60, 1000), MostStepsAFrame);
        for (long tick = 0; tick < ticks; tick++)
        {
            _scene3D.Rain?.Step(GameRandom);
            _scene3D.Snow?.Step(GameRandom);
            _scene3D.Leaf?.Step(GameRandom, 1);
        }

        static long FloorDiv(long value, long divisor)
            => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
    }

    /// <summary>display_scene_effects_at_handle@0x44f7a0 at op 0x325's second handle: the rain's streaks, the snow, then
    /// the leaves.</summary>
    private void LowerSecondHandleEffects(List<FrameItem> items)
    {
        LowerRain(items, rings: false);
        LowerSnow(items);
        LowerLeaves(items);
    }

    /// <summary>
    /// The leaves where they are, one polygon item each in index order, as leaf_effect_draw_all@0x471e50 draws them:
    /// world = scale · yaw-pitch-roll · translation; every material colour times (1, 1, 1, sin(π(5 - y)/5)); Direct3D 9's
    /// fixed-function lighting; counter-clockwise triangles on screen culled; the alpha blend; no depth test.
    /// </summary>
    private void LowerLeaves(List<FrameItem> items)
    {
        if (_scene3D.Leaf is not { } effect || !EffectsDrawable) return;
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
        return UnitByte(diffuse.W * fade) << 24 | UnitByte(color.X) << 16 | UnitByte(color.Y) << 8 | UnitByte(color.Z);
    }

    /// <summary>A colour channel from 0 to 1 as a byte: clamped, then rounded.</summary>
    private static uint UnitByte(float value) => (uint)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);

    /// <summary>
    /// The rain's drops in index order, as effect_rain_draw@0x47ab00 draws them: at the first handle the rings, at the
    /// second the streaks (rain_drop_draw@0x47a800 draws a drop only for its part). A drop's world is scale ·
    /// yaw-pitch-roll · translation (the display's world, which follows, is the identity), then the device's view and
    /// projection. Direct3D lights it with no normal in its vertices, so the ambient part alone is left: op 0x32d's
    /// colour, times the material's ambient of (1, 1, 1), with the material's alpha (<see cref="Scene3DRainEffect.Alpha"/>).
    /// A streak is the line from (0, 0.5, 0) to (0, -0.5, 0), a ring the sixteen lines round the unit circle in y = 0.
    /// One polygon item a drop.
    /// </summary>
    private void LowerRain(List<FrameItem> items, bool rings)
    {
        if (_scene3D.Rain is not { } effect || !EffectsDrawable) return;
        Matrix4x4 viewProjection = View() * Projection(_screenWidth, _screenHeight);
        uint rgb = (_scene3D.AmbientArgb ?? 0) & 0xffffff;
        var corners = new List<FramePolygonVertex>();
        foreach (Scene3DRainDrop drop in effect.Drops)
        {
            if (drop.Rippling != rings) continue;
            Matrix4x4 clipFromDrop = Matrix4x4.CreateScale(drop.Scale)
                                     * Matrix4x4.CreateFromYawPitchRoll(drop.Rotation.X, drop.Rotation.Y, drop.Rotation.Z)
                                     * Matrix4x4.CreateTranslation(drop.Position) * viewProjection;
            uint argb = UnitByte(Scene3DRainEffect.Alpha(drop)) << 24 | rgb;
            corners.Clear();
            if (rings)
            {
                for (int segment = 0; segment < Scene3DRainEffect.RingSegments; segment++)
                    LowerLine(Scene3DRainEffect.Ring[segment], Scene3DRainEffect.Ring[segment + 1], clipFromDrop, argb,
                              corners);
            }
            else LowerLine(Scene3DRainEffect.StreakTop, Scene3DRainEffect.StreakBottom, clipFromDrop, argb, corners);
            if (corners.Count > 0)
                items.Add(FrameItem.PolygonItem(new FramePolygonGeometry([.. corners], FramePolygonBlend.Alpha), 1f));
        }
    }

    // A line as Direct3D 9 draws one, a pixel thick: the part of it inside the depth range, then a parallelogram that
    // reaches half a pixel either side of it along its shorter screen axis. With the item's pixel-centre rule that
    // covers one pixel a column of a wide line and one a row of a tall one, as the line rule does; the pixel where two
    // lines of a ring meet may be covered by both.
    private void LowerLine(Vector3 from, Vector3 to, Matrix4x4 clipFromLocal, uint argb, List<FramePolygonVertex> corners)
    {
        Vector4 a = Vector4.Transform(new Vector4(from, 1f), clipFromLocal);
        Vector4 b = Vector4.Transform(new Vector4(to, 1f), clipFromLocal);
        if (!ClipToDepth(ref a, ref b)) return;
        (float ax, float ay) = Screen(a);
        (float bx, float by) = Screen(b);
        (float dx, float dy) = Math.Abs(bx - ax) >= Math.Abs(by - ay) ? (0f, 0.5f) : (0.5f, 0f);
        var a0 = new FramePolygonVertex(ax - dx, ay - dy, argb, 0, 0);
        var a1 = new FramePolygonVertex(ax + dx, ay + dy, argb, 0, 0);
        var b0 = new FramePolygonVertex(bx - dx, by - dy, argb, 0, 0);
        var b1 = new FramePolygonVertex(bx + dx, by + dy, argb, 0, 0);
        corners.Add(a0);
        corners.Add(b0);
        corners.Add(b1);
        corners.Add(a0);
        corners.Add(b1);
        corners.Add(a1);
    }

    // The part of a line in clip space inside 0 <= z <= w, Direct3D's near and far planes; false when none is.
    private static bool ClipToDepth(ref Vector4 a, ref Vector4 b)
    {
        float start = 0f, end = 1f;
        if (!Keep(a.Z, b.Z, ref start, ref end) || !Keep(a.W - a.Z, b.W - b.Z, ref start, ref end)) return false;
        (Vector4 from, Vector4 to) = (a, b);
        a = Vector4.Lerp(from, to, start);
        b = Vector4.Lerp(from, to, end);
        return true;

        // Narrow start..end to where f, running from fa to fb along the line, is at least 0.
        static bool Keep(float fa, float fb, ref float start, ref float end)
        {
            if (fa < 0 && fb < 0) return false;
            if (fa < 0) start = Math.Max(start, fa / (fa - fb));
            else if (fb < 0) end = Math.Min(end, fa / (fa - fb));
            return start <= end;
        }
    }

    /// <summary>
    /// The snow as effect_snow_draw@0x49bdb0 draws it with resource 202's "Snow" shader: each flake's centre through the
    /// world (the identity) and the display's view (<see cref="Scene3DState.EffectView"/>); the flakes sorted there,
    /// farthest first, which keeps their new order; then each flake a square facing the viewer, its size either side of
    /// the centre, projected, with the slot's whole texture times (1, 1, 1, alpha) (<see cref="Scene3DSnowEffect.Alpha"/>),
    /// one polygon item a flake. A flake outside the depth range is not drawn.
    /// </summary>
    private void LowerSnow(List<FrameItem> items)
    {
        if (_scene3D.Snow is not { } effect || effect.Flakes.Count == 0 || !EffectsDrawable)
            return;
        Matrix4x4 view = _scene3D.EffectView, projection = Projection(_screenWidth, _screenHeight);
        var depth = new float[effect.Flakes.Count];
        for (int index = 0; index < depth.Length; index++)
            depth[index] = Vector4.Transform(new Vector4(effect.Flakes[index].Position, 1f), view).Z;
        effect.SortFarthestFirst(depth);
        SurfaceTexture? texture = ResolveSurfaceTexture(effect.TextureSlot,
                                                        _surfaceResources.GetValueOrDefault(effect.TextureSlot));
        foreach (Scene3DSnowFlake flake in effect.Flakes)
        {
            Vector4 centre = Vector4.Transform(new Vector4(flake.Position, 1f), view);
            float size = flake.Size;
            Vector4 topLeft = Corner(centre.X - size, centre.Y + size), topRight = Corner(centre.X + size, centre.Y + size);
            Vector4 bottomLeft = Corner(centre.X - size, centre.Y - size);
            Vector4 bottomRight = Corner(centre.X + size, centre.Y - size);
            if (topLeft.W <= 0 || topLeft.Z < 0 || topLeft.Z > topLeft.W) continue;
            uint argb = UnitByte(effect.Alpha(flake)) << 24 | 0xffffff;
            FramePolygonVertex v0 = Vertex(topLeft, 0, 0), v1 = Vertex(topRight, 1, 0);
            FramePolygonVertex v2 = Vertex(bottomLeft, 0, 1), v3 = Vertex(bottomRight, 1, 1);
            var geometry = new FramePolygonGeometry([v0, v1, v2, v1, v3, v2], FramePolygonBlend.Alpha);
            items.Add(texture is { } image
                ? FrameItem.PolygonItem(geometry, 1f, image.Image, image.AssetId, image.IsDynamic, effect.TextureSlot, -1)
                : FrameItem.PolygonItem(geometry, 1f));

            Vector4 Corner(float x, float y) => Vector4.Transform(new Vector4(x, y, centre.Z, 1f), projection);

            FramePolygonVertex Vertex(Vector4 clip, float u, float v)
            {
                (float x, float y) = Screen(clip);
                return new FramePolygonVertex(x, y, argb, u, v);
            }
        }
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

    // The viewport's mapping (the screen, or a render target's surface), plus the half pixel that makes the renderers' Direct3D 9 pixel-centre rule
    // land a corner where Direct3D does: a polygon item's coordinates are native's vertex position + 0.5.
    private (float X, float Y) Screen(Vector4 clip)
    {
        float x = clip.X / clip.W, y = clip.Y / clip.W;
        return ((1f + x) * _effectViewport.Width / 2f + 0.5f, (1f - y) * _effectViewport.Height / 2f + 0.5f);
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
