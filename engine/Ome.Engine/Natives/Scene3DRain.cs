using System.Numerics;
using Ome.Engine.Hosting;

namespace Ome.Engine.Natives;

/// <summary>One drop of the rain effect as native keeps it (<c>rain_drop_construct@0x47ab60</c>, 0x70 bytes): its place,
/// its velocity and the acceleration added to it each step, its rotation (yaw, pitch, roll, radians) and scale, and, once
/// it has landed, the ripple's age and life in steps.</summary>
public readonly record struct Scene3DRainDrop(Vector3 Position, Vector3 Velocity, Vector3 Acceleration, Vector3 Rotation,
                                              float Scale, bool Rippling, int RippleAge, int RippleLife);

/// <summary>
/// The display's first effect, which op 0x327 builds (<c>effect_rain_construct@0x47abc0</c>): rain. A drop falls as a
/// streak one unit long, a little tilted, faster each step; where it passes the floor it becomes a ring on the ground
/// that grows for 15 to 19 steps and fades, then it starts again at the top. Stepped 60 times a second. The frame walk
/// draws the rings at op 0x325's first handle and the streaks at its second. Every random number is the game's
/// generator's, drawn in native's order.
/// </summary>
public sealed class Scene3DRainEffect
{
    // The twelve constants effect_rain_construct gives every drop; no script sets them.
    /// <summary>Where a ring lies, just above the floor.</summary>
    public const float RingHeight = 0.01f;
    /// <summary>The box's height: a drop starts between 0 and it, and starts again at it after its ring.</summary>
    public const float Height = 10f;
    /// <summary>The box's width in x and z: a drop starts within half of it either side of 0.</summary>
    public const float Width = 20f;
    public const float FallSlowest = -0.1f, FallFastest = -0.2f;
    /// <summary>The most a drop moves in x and in z a step, never negative.</summary>
    public const float Drift = 0.05f;
    /// <summary>A drop's rotation: this plus up to 0.01 on each axis, so a roll of about 0.1.</summary>
    public static readonly Vector3 Tilt = new(0f, 0f, 0.1f);
    public const float TiltJitter = 0.01f;
    public const float ScaleSmallest = 1f, ScaleLargest = 1.1f;
    /// <summary>The float 0.01 native keeps as a double (0x5addd0): the acceleration is the velocity's start times it,
    /// and a ring grows by it a step.</summary>
    internal const float Hundredth = 0.01f;
    /// <summary>A ring's life is 15 plus up to 5 steps (the doubles at 0x5addd8 and 0x5adde0).</summary>
    internal const float RingLifeShortest = 15f, RingLifeSpread = 5f;

    /// <summary>A streak's two ends, before the drop's world (effect_rain_construct's line list).</summary>
    public static readonly Vector3 StreakTop = new(0f, 0.5f, 0f), StreakBottom = new(0f, -0.5f, 0f);
    /// <summary>The lines of a ring (its line strip).</summary>
    public const int RingSegments = 16;
    /// <summary>A ring's seventeen points, the last the first again: (cos θ, 0, sin θ) with θ = i · 2π/16, the 2π a
    /// float's (0x5ac7b8) and θ, its cosine and its sine each rounded to a float.</summary>
    public static readonly IReadOnlyList<Vector3> Ring = BuildRing();

    private static Vector3[] BuildRing()
    {
        const double twoPi = 6.2831854820251465;
        var points = new Vector3[RingSegments + 1];
        for (int index = 0; index < points.Length; index++)
        {
            float angle = (float)(index * twoPi * 0.0625);
            points[index] = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
        }
        return points;
    }

    private readonly Scene3DRainDrop[] _drops;

    internal Scene3DRainEffect(long count, CrtRandom random)
    {
        Count = count;
        _drops = new Scene3DRainDrop[count <= 0 ? 0 : count];
        for (int index = 0; index < _drops.Length; index++) _drops[index] = Born(random);
    }

    /// <summary>Op 0x327's operand: the number of drops.</summary>
    public long Count { get; }
    public IReadOnlyList<Scene3DRainDrop> Drops => _drops;

    /// <summary>Put a drop where a test wants it.</summary>
    internal void Place(int index, Scene3DRainDrop drop) => _drops[index] = drop;

    /// <summary>A step of every drop, in index order (<c>effect_rain_step@0x47aad0</c>).</summary>
    internal void Step(CrtRandom random)
    {
        for (int index = 0; index < _drops.Length; index++) _drops[index] = Step(_drops[index], random);
    }

    /// <summary>A streak's alpha (<c>rain_drop_draw@0x47a800</c>): sin(π(10 - y)/10) / 2; a ring's: its steps left over
    /// its life.</summary>
    public static float Alpha(Scene3DRainDrop drop)
    {
        if (drop.Rippling) return (float)(drop.RippleLife - drop.RippleAge) / drop.RippleLife;
        float angle = (Height - drop.Position.Y) * Scene3DLeafEffect.NativePi / Height;
        return (float)Math.Sin(angle) * 0.5f;
    }

    // rain_drop_init@0x47a5d0: thirteen draws, in this order. Like all of the effects' arithmetic, in floats: native's
    // x87 runs at single precision and rounds every operation to a float (engine-re.md, "The x87 runs at single
    // precision"), which .NET's float arithmetic does too.
    private static Scene3DRainDrop Born(CrtRandom random)
    {
        float c0 = Scene3DRandom.Centered(random), u1 = Scene3DRandom.Unit(random), c2 = Scene3DRandom.Centered(random);
        var position = new Vector3(c0 * Width, u1 * Height, c2 * Width);
        float u3 = Scene3DRandom.Unit(random), u4 = Scene3DRandom.Unit(random), u5 = Scene3DRandom.Unit(random);
        var velocity = new Vector3(u3 * Drift, Fall(u4), u5 * Drift);
        float u6 = Scene3DRandom.Unit(random), u7 = Scene3DRandom.Unit(random), u8 = Scene3DRandom.Unit(random);
        var acceleration = new Vector3(u6 * Drift * Hundredth, Fall(u7) * Hundredth, u8 * Drift * Hundredth);
        float u9 = Scene3DRandom.Unit(random), u10 = Scene3DRandom.Unit(random), u11 = Scene3DRandom.Unit(random);
        var rotation = new Vector3(Tilt.X + TiltJitter * u9, Tilt.Y + TiltJitter * u10, Tilt.Z + TiltJitter * u11);
        float scale = (ScaleLargest - ScaleSmallest) * Scene3DRandom.Unit(random) + ScaleSmallest;
        return new Scene3DRainDrop(position, velocity, acceleration, rotation, scale, false, 0, 0);
    }

    /// <summary>A fall speed from a unit draw: (fastest - slowest) · unit + slowest.</summary>
    internal static float Fall(float unit) => (FallFastest - FallSlowest) * unit + FallSlowest;

    // rain_drop_step@0x47a730. In flight: move, then add the acceleration to the velocity; past the floor, a draw for
    // the ring's life, and the drop lies at the ring's height with no scale or rotation. A ring: one step older and
    // 0.01 larger; past its life, the drop is born again (thirteen draws) at the top.
    private static Scene3DRainDrop Step(Scene3DRainDrop drop, CrtRandom random)
    {
        if (!drop.Rippling)
        {
            Vector3 position = drop.Position + drop.Velocity;
            drop = drop with { Position = position, Velocity = drop.Acceleration + drop.Velocity };
            if (position.Y >= 0) return drop;
            // The float sum truncated, as native's _ftol truncates its single-precision sum.
            int life = (int)(Scene3DRandom.Unit(random) * RingLifeSpread + RingLifeShortest);
            return drop with
            {
                Position = position with { Y = RingHeight }, Rotation = Vector3.Zero, Scale = 0f,
                Rippling = true, RippleAge = 0, RippleLife = life,
            };
        }
        int age = drop.RippleAge + 1;
        if (drop.RippleLife < age)
        {
            Scene3DRainDrop born = Born(random);
            return born with { Position = born.Position with { Y = Height } };
        }
        return drop with { RippleAge = age, Scale = age * Hundredth };
    }
}
