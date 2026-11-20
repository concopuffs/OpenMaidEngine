using System.Numerics;
using Ome.Engine.Hosting;

namespace Ome.Engine.Natives;

/// <summary>One leaf of the leaf effect as native keeps it (<c>leaf_particle_construct@0x4722b0</c>, 0x70 bytes): its
/// position, its velocity a step, its rotation (yaw, pitch, roll, radians), its angular velocity a step, its scale and
/// the index of its mesh among the effect's.</summary>
public readonly record struct Scene3DLeaf(Vector3 Position, Vector3 Velocity, Vector3 Rotation,
                                          Vector3 AngularVelocity, float Scale, int Mesh);

/// <summary>
/// The leaf effect op 0x328 builds (native's Set3DEffectLeaf; docs/superpowers/specs/2026-11-20-title-leaves-design.md
/// §2): leaves that fall through a box, drift and spin, stepped 60 times a second, each respawned at the top when it
/// falls below the floor. Every random number is the game's generator's (<see cref="CrtRandom"/>), drawn in native's
/// order, so the effect advances op 0x60's stream as native's does.
/// </summary>
public sealed class Scene3DLeafEffect
{
    // The constants native gives every leaf: four fixed in leaf_effect_create_particles@0x472440, six in
    // display_build_leaf_effect@0x44f5c0's call of effect_leaf_construct@0x472020. No script sets them.
    /// <summary>The box's width in x and z: a leaf starts within half of it either side of 0.</summary>
    public const float BoxWidth = 10f;
    /// <summary>The box's height: a leaf starts between 0 and it, and starts again at it after the floor.</summary>
    public const float Height = 5f;
    public const float FallSlowest = -0.01f, FallFastest = -0.02f;
    public const float Drift = 0.01f, DriftJitter = 0.001f;
    public const float ScaleSmallest = 1f, ScaleLargest = 3f;
    /// <summary>A factor on the starting rotation's 2π.</summary>
    public const float Spin = 1f;
    public const float AngularSpeed = 0.03f;
    /// <summary>The π native multiplies by: a double holding the float nearest π (0x5ac780).</summary>
    internal const double NativePi = 3.1415927410125732;

    private readonly Scene3DLeaf[] _leaves;

    internal Scene3DLeafEffect(long count, IReadOnlyList<Scene3DMesh> meshes, CrtRandom random)
    {
        Count = count;
        Meshes = meshes;
        // Native's respawn takes rand() % the mesh count, which divides by zero with no mesh; the port builds no
        // leaves then, and draws nothing.
        _leaves = new Scene3DLeaf[meshes.Count == 0 || count <= 0 ? 0 : count];
        for (int index = 0; index < _leaves.Length; index++) _leaves[index] = Respawn(random);
    }

    /// <summary>Op 0x328's first operand: the number of leaves.</summary>
    public long Count { get; }
    /// <summary>The meshes of the slots named, in their order: a leaf's <see cref="Scene3DLeaf.Mesh"/> indexes them.
    /// The effect holds them itself, so a slot released later does not change it.</summary>
    public IReadOnlyList<Scene3DMesh> Meshes { get; }
    public IReadOnlyList<Scene3DLeaf> Leaves => _leaves;

    /// <summary>Put a leaf where a test wants it.</summary>
    internal void Place(int index, Scene3DLeaf leaf) => _leaves[index] = leaf;

    /// <summary><paramref name="ticks"/> steps of every leaf, in index order (leaf_effect_step_all@0x471e20).</summary>
    internal void Step(CrtRandom random, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
            for (int index = 0; index < _leaves.Length; index++)
                _leaves[index] = Step(_leaves[index], random);
    }

    // leaf_particle_step@0x471c10: move, turn, jitter the drift in x then z, and start again at the top below 0.
    private Scene3DLeaf Step(Scene3DLeaf leaf, CrtRandom random)
    {
        Vector3 position = leaf.Position + leaf.Velocity;
        Vector3 rotation = leaf.Rotation + leaf.AngularVelocity;
        Vector3 velocity = leaf.Velocity;
        velocity.X = (float)(Centered(random) * DriftJitter + velocity.X);
        velocity.Z = (float)(Centered(random) * DriftJitter + velocity.Z);
        leaf = leaf with { Position = position, Rotation = rotation, Velocity = velocity };
        if (position.Y < 0)
        {
            leaf = Respawn(random);
            leaf = leaf with { Position = leaf.Position with { Y = Height } };
        }
        return leaf;
    }

    // leaf_particle_respawn@0x471a90: fourteen draws, in this order.
    private Scene3DLeaf Respawn(CrtRandom random)
    {
        double x = Centered(random), y = Unit(random), z = Centered(random);
        var position = new Vector3((float)(x * BoxWidth), (float)(y * Height), (float)(z * BoxWidth));
        double vx = Centered(random), vy = Unit(random), vz = Centered(random);
        var velocity = new Vector3((float)(vx * Drift),
                                   (float)(((double)FallFastest - FallSlowest) * vy + FallSlowest),
                                   (float)(vz * Drift));
        float yaw = StartingAngle(Unit(random)), pitch = StartingAngle(Unit(random)), roll = StartingAngle(Unit(random));
        double turnYaw = Centered(random), turnPitch = Centered(random), turnRoll = Centered(random);
        var angularVelocity = new Vector3((float)(AngularSpeed * (double)(float)turnYaw),
                                          (float)(AngularSpeed * (double)(float)turnPitch),
                                          (float)(AngularSpeed * (double)(float)turnRoll));
        float scale = (float)(((double)ScaleLargest - ScaleSmallest) * Unit(random) + ScaleSmallest);
        int mesh = random.Next() % Meshes.Count;
        return new Scene3DLeaf(position, velocity, new Vector3(yaw, pitch, roll), angularVelocity, scale, mesh);
    }

    // The x87 code rounds to a float after each product: u·π, then ·2, then ·the spin factor.
    private static float StartingAngle(float unit)
    {
        float half = (float)(unit * NativePi);
        float whole = (float)(half * 2.0);
        return (float)(whole * (double)Spin);
    }

    /// <summary>rand_unit_percent@0x471990: (rand() % 100) / 100, kept as a float.</summary>
    private static float Unit(CrtRandom random) => (float)(random.Next() % 100 / 100.0);

    /// <summary>rand_centered@0x4719c0: the unit draw less 0.5.</summary>
    private static double Centered(CrtRandom random) => Unit(random) - 0.5;
}
