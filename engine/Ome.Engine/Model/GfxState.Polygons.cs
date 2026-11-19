using Ome.Engine.Devices;

namespace Ome.Engine.Model;

// AGE 4.38D's polygon objects (ops 0x320 to 0x323): a second kind of retained object, kept in a map of its own beside
// the retained objects' and keyed by the same handles. What native does with one, function by function, is in
// docs/superpowers/specs/2026-11-19-polygon-objects-design.md §2.
public sealed partial class GfxState
{
    /// <summary>One vertex as op 0x320 gives it: its place on the canvas, its own colour (0xAARRGGBB) and its texture
    /// coordinates. Native draws it pre-transformed, with a w of 1.</summary>
    public readonly record struct PolygonVertex(float X, float Y, float Z, uint Argb, float U, float V);

    /// <summary>A polygon record (native's 15 dwords; gfx_polygon_record_init@0x488f70 gives the defaults).</summary>
    public sealed class GfxPolygon
    {
        /// <summary>The vertices op 0x320 last gave, with each one's own colour.</summary>
        public PolygonVertex[] Vertices = [];
        /// <summary>Record flag 1: op 0x320 has built the vertices, so the polygon is drawn.</summary>
        public bool Made;
        /// <summary>Op 0x320's flags, bit 0: a triangle fan; otherwise a triangle strip.</summary>
        public bool Fan;
        /// <summary>The surface slots of texture stages 0 and 1 (op 0x321); negative is none.</summary>
        public int TextureSlot0 = -1, TextureSlot1 = -1;
        /// <summary>Op 0x322's mode: 1 additive, 2 replace, 3 subtractive, anything else alpha.</summary>
        public int BlendMode;
        /// <summary>The polygon's colour (record +0x34), which multiplies each vertex's own. A fade starts from it.
        /// </summary>
        public uint Color = 0xffffffff;
        /// <summary>The colour the vertices were last multiplied by: native writes it into the vertex buffer when the
        /// colour is set, when the polygon is built and at each draw of a running fade past its delay, and the buffer
        /// keeps it in between. So a fade that another replaces leaves its last drawn colour showing through the new
        /// one's delay.</summary>
        public uint ShownColor = 0xffffffff;
        /// <summary>Record flag 2: a fade to <see cref="FadeTarget"/> runs (op 0x323).</summary>
        public bool FadeEnabled;
        /// <summary>The frame time of the fade's first draw, or -1 before it (native keeps 0).</summary>
        public long FadeStartMs = -1;
        public long FadeDelayMs, FadeDurationMs;
        public uint FadeTarget = 0xffffffff;

        // The triangles last lowered and what they were lowered for: replaced, never changed, so that a frame
        // description that holds them stays valid (FramePolygonGeometry).
        internal FramePolygonGeometry? Geometry;
        internal uint GeometryColor;
        internal int GeometryBlendMode;

        internal GfxPolygon Clone() => new()
        {
            Vertices = Vertices, Made = Made, Fan = Fan, TextureSlot0 = TextureSlot0, TextureSlot1 = TextureSlot1,
            BlendMode = BlendMode, Color = Color, ShownColor = ShownColor, FadeEnabled = FadeEnabled,
            FadeStartMs = FadeStartMs, FadeDelayMs = FadeDelayMs, FadeDurationMs = FadeDurationMs,
            FadeTarget = FadeTarget,
        };
    }

    /// <summary>The polygon record of a handle, or null.</summary>
    public GfxPolygon? TryGetPolygon(long handle) => _scene.Polygons.GetValueOrDefault(handle);

    /// <summary>How many polygon records the scene holds.</summary>
    public int PolygonCount => _scene.Polygons.Count;

    // gfx_polygon_get_or_create@0x4938f0: every polygon op makes the handle's record when it has none.
    private GfxPolygon GetOrCreatePolygon(long handle)
    {
        if (_scene.Polygons.TryGetValue(handle, out GfxPolygon? polygon)) return polygon;
        polygon = new GfxPolygon();
        _scene.Polygons[handle] = polygon;
        int index = _scene.OrderedPolygonHandles.BinarySearch(handle);
        _scene.OrderedPolygonHandles.Insert(index < 0 ? ~index : index, handle);
        return polygon;
    }

    private bool RemovePolygon(long handle)
    {
        if (!_scene.Polygons.Remove(handle)) return false;
        int index = _scene.OrderedPolygonHandles.BinarySearch(handle);
        if (index >= 0) _scene.OrderedPolygonHandles.RemoveAt(index);
        return true;
    }

    /// <summary>Op 0x320 (gfx_polygon_create@0x4965c0): build the handle's polygon from <paramref name="vertices"/>.
    /// Bit 0 of <paramref name="flags"/> makes it a triangle fan. The record's textures, blend mode, colour and fade
    /// stay; the new vertices take the polygon's colour at once.</summary>
    public void CreatePolygon(long handle, PolygonVertex[] vertices, long flags)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        GfxPolygon polygon = GetOrCreatePolygon(handle);
        polygon.Vertices = vertices;
        polygon.Made = true;
        polygon.Fan = (flags & 1) == 1;
        polygon.ShownColor = polygon.Color;
        polygon.Geometry = null;
        MarkRetainedMutation();
    }

    /// <summary>Op 0x321 (gfx_polygon_set_texture@0x496860): the surface slot of texture stage 0 or 1. The caller
    /// keeps <paramref name="stage"/> to those two: native writes at record +0x1c + stage * 4 unchecked.</summary>
    public void SetPolygonTexture(long handle, int stage, int surfaceSlot)
    {
        GfxPolygon polygon = GetOrCreatePolygon(handle);
        if (stage == 0) polygon.TextureSlot0 = surfaceSlot;
        else polygon.TextureSlot1 = surfaceSlot;
        MarkRetainedMutation();
    }

    /// <summary>The colour ops 0x322 and 0x323 build from their alpha and RGB operands: an alpha of 0x100 or more is
    /// 0xff, and a negative alpha or RGB keeps the polygon's current one (gfx_polygon_get_color@0x4969a0, which gives
    /// 0xffffffff for a handle with no record). During a fade the current colour is the one the fade started from.
    /// </summary>
    public uint ResolvePolygonColor(long handle, long alpha, long rgb)
    {
        uint current = _scene.Polygons.TryGetValue(handle, out GfxPolygon? polygon) ? polygon.Color : 0xffffffff;
        int a = unchecked((int)alpha), c = unchecked((int)rgb);
        uint alphaByte = a >= 0x100 ? 0xff : a < 0 ? current >> 24 : (uint)a;
        uint color = c < 0 ? current : unchecked((uint)c);
        return (alphaByte << 24) | (color & 0xffffff);
    }

    /// <summary>Op 0x322 (gfx_polygon_set_color@0x4968a0): the polygon's blend mode and colour. A running fade goes
    /// on, now from this colour.</summary>
    public void SetPolygonColor(long handle, int blendMode, uint argb)
    {
        GfxPolygon polygon = GetOrCreatePolygon(handle);
        polygon.BlendMode = blendMode;
        polygon.Color = argb;
        polygon.ShownColor = argb;
        MarkRetainedMutation();
    }

    /// <summary>Op 0x323 (gfx_polygon_fade_color@0x496910): fade the polygon's colour to <paramref name="target"/>
    /// after a delay, over a duration. The fade starts at the polygon's next draw, from its colour: one that replaces
    /// a running fade does not start from where that one had come to.</summary>
    public void FadePolygonColor(long handle, long delayMs, long durationMs, uint target)
    {
        GfxPolygon polygon = GetOrCreatePolygon(handle);
        polygon.FadeEnabled = true;
        polygon.FadeStartMs = -1;
        polygon.FadeDelayMs = delayMs;
        polygon.FadeDurationMs = durationMs;
        polygon.FadeTarget = target;
        MarkRetainedMutation();
    }

    // gfx_render_polygon@0x496d10's fade, at a draw: the start is the frame time of the first draw; a duration below 1
    // only ends the fade, its target never applied; at or past the end the colour becomes the target; between the
    // delay and the end the vertices take the two colours mixed. Native compares the times as unsigned 32-bit values.
    private static void SamplePolygonFade(GfxPolygon polygon, long nowMs)
    {
        if (!polygon.FadeEnabled) return;
        if (polygon.FadeStartMs < 0) polygon.FadeStartMs = nowMs;
        int duration = unchecked((int)polygon.FadeDurationMs);
        if (duration < 1)
        {
            polygon.FadeEnabled = false;
            polygon.FadeStartMs = -1;
            return;
        }
        uint now = unchecked((uint)nowMs), start = unchecked((uint)polygon.FadeStartMs);
        uint delay = unchecked((uint)polygon.FadeDelayMs);
        if (unchecked((uint)duration + delay + start) <= now)
        {
            CommitPolygonFade(polygon);
            return;
        }
        if (unchecked(delay + start) < now)
        {
            // (float)elapsed / (float)duration, stored as a float, then each channel on the x87 stack.
            float progress = (float)((double)unchecked(now - delay - start) / duration);
            polygon.ShownColor = MixPolygonColor(polygon.Color, polygon.FadeTarget, progress);
        }
    }

    // The end of a fade, or the force flag (manager +0xb590) at a draw: the colour becomes the target.
    private static void CommitPolygonFade(GfxPolygon polygon)
    {
        polygon.Color = polygon.FadeTarget;
        polygon.ShownColor = polygon.Color;
        polygon.FadeTarget = 0xffffffff;
        polygon.FadeDelayMs = 0;
        polygon.FadeDurationMs = 0;
        polygon.FadeEnabled = false;
        polygon.FadeStartMs = -1;
    }

    // What a forced render does to a polygon it draws: a fade ends at its target, and one with a duration below 1
    // just ends. A polygon not yet built is not drawn, and keeps its fade.
    private int ForceCompletePolygonFades()
    {
        int completed = 0;
        foreach (GfxPolygon polygon in _scene.Polygons.Values)
        {
            if (!polygon.Made || !polygon.FadeEnabled) continue;
            if (unchecked((int)polygon.FadeDurationMs) < 1)
            {
                polygon.FadeEnabled = false;
                polygon.FadeStartMs = -1;
            }
            else CommitPolygonFade(polygon);
            completed++;
        }
        return completed;
    }

    // A fade that keeps an effect wait waiting (manager +0xb594): one a draw would leave running at nowMs.
    private static bool PolygonFadeRunning(GfxPolygon polygon, long nowMs)
    {
        if (!polygon.Made || !polygon.FadeEnabled) return false;
        int duration = unchecked((int)polygon.FadeDurationMs);
        if (duration < 1) return false;
        if (polygon.FadeStartMs < 0) return true;
        uint now = unchecked((uint)nowMs), start = unchecked((uint)polygon.FadeStartMs);
        return unchecked((uint)duration + unchecked((uint)polygon.FadeDelayMs) + start) > now;
    }

    /// <summary>gfx_polygon_calc_diffuse@0x48b9d0's colour at <paramref name="progress"/>: each channel is
    /// <c>target * progress + current * (1 - progress)</c>, truncated.</summary>
    public static uint MixPolygonColor(uint current, uint target, float progress)
    {
        double weight = progress, rest = 1.0 - weight;
        uint mixed = 0;
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            double channel = ((target >> shift) & 0xff) * weight + ((current >> shift) & 0xff) * rest;
            mixed |= (unchecked((uint)(int)channel) & 0xff) << shift;
        }
        return mixed;
    }

    /// <summary>A vertex's colour under the polygon's (gfx_polygon_calc_diffuse): each channel is the vertex's own
    /// times the polygon's, divided by 255; the colour 0xffffffff leaves it as it is.</summary>
    public static uint ModulatePolygonColor(uint vertex, uint color)
    {
        if (color == 0xffffffff) return vertex;
        uint modulated = 0;
        for (int shift = 24; shift >= 0; shift -= 8)
            modulated |= (((vertex >> shift) & 0xff) * ((color >> shift) & 0xff) / 0xff) << shift;
        return modulated;
    }

    // The polygon's triangles for a frame. A strip's triangle i is vertices i, i + 1, i + 2 and a fan's is 0, i + 1,
    // i + 2; fewer than three vertices draw nothing. Built again only when the vertices, the colour shown or the blend
    // mode changed.
    private static FramePolygonGeometry LowerPolygon(GfxPolygon polygon)
    {
        if (polygon.Geometry is { } lowered && polygon.GeometryColor == polygon.ShownColor
            && polygon.GeometryBlendMode == polygon.BlendMode)
            return lowered;
        PolygonVertex[] source = polygon.Vertices;
        int triangles = System.Math.Max(0, source.Length - 2);
        var vertices = new FramePolygonVertex[triangles * 3];
        for (int triangle = 0; triangle < triangles; triangle++)
        {
            vertices[triangle * 3] = Lower(source[polygon.Fan ? 0 : triangle]);
            vertices[triangle * 3 + 1] = Lower(source[triangle + 1]);
            vertices[triangle * 3 + 2] = Lower(source[triangle + 2]);
        }
        FramePolygonBlend blend = polygon.BlendMode switch
        {
            1 => FramePolygonBlend.Additive,
            2 => FramePolygonBlend.Replace,
            3 => FramePolygonBlend.Subtractive,
            _ => FramePolygonBlend.Alpha,
        };
        polygon.Geometry = new FramePolygonGeometry(vertices, blend);
        polygon.GeometryColor = polygon.ShownColor;
        polygon.GeometryBlendMode = polygon.BlendMode;
        return polygon.Geometry;

        FramePolygonVertex Lower(PolygonVertex vertex)
            => new(vertex.X, vertex.Y, ModulatePolygonColor(vertex.Argb, polygon.ShownColor), vertex.U, vertex.V);
    }

    // The polygon of a handle in a snapshot, in its turn of the walk: gfx_render_frame draws one that is built, after
    // running its fade.
    private void SnapshotPolygon(long handle, long nowMs, List<RenderObject> list)
    {
        GfxPolygon polygon = _scene.Polygons[handle];
        if (!polygon.Made) return;
        SamplePolygonFade(polygon, nowMs);
        var state = new PolygonRenderState(LowerPolygon(polygon), Texture(polygon.TextureSlot0),
                                           Texture(polygon.TextureSlot1));
        // To code that knows no polygons this is an object that draws nothing: no surface, and opaque.
        list.Add(new RenderObject(handle, 0, -1, 0, 0, 0, 0, 0, 0, default, default, 255, 0xffffff, 0,
                                  BlendKind.Opaque, false, TimeVarying: polygon.FadeEnabled, Polygon: state));

        PolygonTextureState Texture(int slot)
        {
            if (slot < 0) return PolygonTextureState.None;
            var (resourceId, colorKey) = _surfaces.TryGetValue(slot, out var surface) ? surface : (0L, -1L);
            return new PolygonTextureState(slot, resourceId, colorKey);
        }
    }
}
