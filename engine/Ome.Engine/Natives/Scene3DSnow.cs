using System.Numerics;
using Ome.Engine.Hosting;

namespace Ome.Engine.Natives;

/// <summary>One flake of the snow effect as native keeps it (<c>snow_flake_construct@0x49bec0</c>, 0x7c bytes): its
/// position and its half size, both in the units the effect's view is in.</summary>
public readonly record struct Scene3DSnowFlake(Vector3 Position, float Size);

/// <summary>
/// The snow effect op 0x326 builds (native's Set3DEffectSnow, <c>effect_snow_construct@0x49bfa0</c>): flakes that fall
/// through a box and drift in x, each started again at the top when it falls below the floor, stepped 60 times a second.
/// They are squares of the surface slot's texture facing the viewer, drawn farthest first, each times (1, 1, 1, alpha);
/// with the fade on, alpha is sin(π(5 - y)/5). Every random number is the game's generator's, drawn in native's order.
/// </summary>
/// <remarks>The order of the flakes is native's pointer array, which each draw sorts in place (<see cref="MsvcQsort"/>);
/// a step goes through them in that order, so which flake draws which random numbers follows the last draw.</remarks>
public sealed class Scene3DSnowEffect
{
    // The constants effect_snow_construct gives every flake; no script sets them.
    /// <summary>A step moves a flake down by the speed times this, and across in x by the speed times its negative.
    /// </summary>
    public const float Fall = -0.01f;
    /// <summary>The box's width in x and z: a flake starts again within half of it either side of 0.</summary>
    public const float Spread = 20f;
    /// <summary>The box's height: a flake starts between 0 and it, and starts again at it after the floor.</summary>
    public const float Top = 5f;
    public const float SizeSmallest = 0.03f, SizeLargest = 0.06f;

    private Scene3DSnowFlake[] _flakes;

    internal Scene3DSnowEffect(long count, int textureSlot, float speed, long option, CrtRandom random)
    {
        Count = count;
        TextureSlot = textureSlot;
        Speed = speed;
        Option = option;
        _flakes = new Scene3DSnowFlake[count <= 0 ? 0 : count];
        for (int index = 0; index < _flakes.Length; index++) _flakes[index] = Born(random);
    }

    /// <summary>Op 0x326's first operand: the number of flakes.</summary>
    public long Count { get; }
    /// <summary>Op 0x326's second operand: the surface slot whose texture each flake shows.</summary>
    public int TextureSlot { get; }
    /// <summary>Op 0x326's third operand, a float: the fall and the drift a step are proportional to it.</summary>
    public float Speed { get; }
    /// <summary>Op 0x326's fourth operand: when it is not 0, a flake fades in below the top and out at the floor.</summary>
    public long Option { get; }
    /// <summary>Whether the flakes fade: native tests the operand's 32 bits.</summary>
    public bool Fades => unchecked((int)Option) != 0;
    /// <summary>The flakes in native's order.</summary>
    public IReadOnlyList<Scene3DSnowFlake> Flakes => _flakes;

    /// <summary>Put a flake where a test wants it.</summary>
    internal void Place(int index, Scene3DSnowFlake flake) => _flakes[index] = flake;

    /// <summary>A step of every flake, in the order of the last draw (<c>effect_snow_step@0x49bd80</c>).</summary>
    internal void Step(CrtRandom random)
    {
        for (int index = 0; index < _flakes.Length; index++) _flakes[index] = Step(_flakes[index], random);
    }

    /// <summary>The draw's sort: the flakes ordered by <paramref name="depth"/>, the greatest first, with native's
    /// comparison (<c>snow_flake_compare_depth@0x49bbe0</c>) and its <c>qsort</c>.</summary>
    internal void SortFarthestFirst(float[] depth)
    {
        var order = new int[_flakes.Length];
        for (int index = 0; index < order.Length; index++) order[index] = index;
        MsvcQsort.Sort(order, (a, b) => CompareDepth(depth[a], depth[b]));
        var sorted = new Scene3DSnowFlake[_flakes.Length];
        for (int index = 0; index < order.Length; index++) sorted[index] = _flakes[order[index]];
        _flakes = sorted;
    }

    /// <summary>Native's comparison: ceil(d) + floor(d), truncated, where d is b's depth less a's as a float; above 0
    /// puts a after b.</summary>
    internal static int CompareDepth(float a, float b)
    {
        float d = b - a;
        return (int)(Math.Ceiling((double)d) + Math.Floor((double)d));
    }

    /// <summary>A flake's alpha (<c>snow_flake_draw@0x49bc30</c>): 1 without the fade, else sin(π(top - y)/top).
    /// </summary>
    public float Alpha(Scene3DSnowFlake flake)
        => Fades ? (float)Math.Sin((Top - flake.Position.Y) * Scene3DLeafEffect.NativePi / Top) : 1f;

    // snow_flake_construct@0x49bec0: four draws, the first two units and a centred one for the place, a unit for the
    // size. x starts at -a · speed · spread, so on the negative side alone. Like all of the effects' arithmetic, in
    // floats: native's x87 runs at single precision and rounds every operation to a float (engine-re.md, "The x87 runs
    // at single precision"), which .NET's float arithmetic does too.
    private Scene3DSnowFlake Born(CrtRandom random)
    {
        float a = Scene3DRandom.Unit(random), b = Scene3DRandom.Unit(random), c = Scene3DRandom.Centered(random);
        var position = new Vector3(-a * Speed * Spread, b * Top, c * Spread);
        return new Scene3DSnowFlake(position, Size(random));
    }

    // snow_flake_step@0x49ba70: down by speed · fall, across by speed · -fall; below 0, three draws start it again at
    // the top, a centred x and z and a unit for the size.
    private Scene3DSnowFlake Step(Scene3DSnowFlake flake, CrtRandom random)
    {
        float y = Speed * Fall + flake.Position.Y;
        float x = flake.Position.X - Speed * Fall;
        if (y >= 0) return flake with { Position = flake.Position with { X = x, Y = y } };
        float across = Scene3DRandom.Centered(random), deep = Scene3DRandom.Centered(random);
        var position = new Vector3(across * Spread, Top, deep * Spread);
        return new Scene3DSnowFlake(position, Size(random));
    }

    private static float Size(CrtRandom random)
        => (SizeLargest - SizeSmallest) * Scene3DRandom.Unit(random) + SizeSmallest;
}
