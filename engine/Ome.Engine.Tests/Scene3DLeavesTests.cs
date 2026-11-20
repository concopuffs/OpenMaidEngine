using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Ome.Engine.Devices;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// The title's leaves (docs/superpowers/specs/2026-11-20-title-leaves-design.md): the text .X reader, native's leaf
/// effect on the game's generator, and its lowering into polygon items. Every mesh here is written for the test.
/// </summary>
public class Scene3DLeavesTests
{
    private const int Width = 1024, Height = 576;

    private static byte[] Text(string body) => Encoding.ASCII.GetBytes("xof 0303txt 0032\n" + body);

    private const string FramedQuad = """
        template Header {
         <3D82AB43-62DA-11cf-AB39-0020AF71E433>
         WORD major;
         array DWORD minor[major];
        }
        Header { 1; 0; 1; }
        // a top-level material, named by reference below
        Material Red {
         1.000000;0.000000;0.000000;1.000000;;
         5.000000;
         0.100000;0.200000;0.300000;;
         0.000000;0.000000;0.000000;;
        }
        Frame Outer {
         FrameTransformMatrix {
          2.0,0.0,0.0,0.0, 0.0,2.0,0.0,0.0, 0.0,0.0,2.0,0.0, 1.0,2.0,3.0,1.0;;
         }
         Frame Inner {
          FrameTransformMatrix {
           1.0,0.0,0.0,0.0, 0.0,1.0,0.0,0.0, 0.0,0.0,1.0,0.0, 0.5,0.0,0.0,1.0;;
          }
          Mesh Quad {
           5;
           0.0;0.0;0.0;,
           1.0;0.0;0.0;,
           1.0;1.0;0.0;,
           0.0;1.0;0.0;,
           2.0;2.0;0.0;;
           2;
           4;0,1,2,3;,
           3;1,4,2;;
           MeshNormals {
            2;
            0.0;0.0;-2.0;,
            0.0;3.0;0.0;;
            2;
            4;0,0,0,0;,
            3;1,1,1;;
           }
           MeshTextureCoords { 5; 0.0;0.0;, 1.0;0.0;, 1.0;1.0;, 0.0;1.0;, 0.5;0.5;; }
           MeshMaterialList {
            2;
            2;
            1,
            0;;
            { Red }
            Material Blue {
             0.0;0.0;1.0;0.5;;
             0.0;
             0.0;0.0;0.0;;
             0.0;0.0;0.25;;
             TextureFilename { "blue.png"; }
            }
           }
          }
         }
        }
        AnimationSet default { }
        """;

    // A small triangle in the z = 0 plane facing -z, wound clockwise as a camera on -z sees it, with an optional twin
    // wound the other way; its material's diffuse is (0.5, 1, 0.25, 1).
    private static XMesh Triangle(bool withTwin = false) => XMeshDecoder.Decode(Text($$"""
        Mesh {
         3;
         -0.1;-0.1;0.0;,
         0.0;0.1;0.0;,
         0.1;-0.1;0.0;;
         {{(withTwin ? "2" : "1")}};
         3;0,1,2;{{(withTwin ? ",\n 3;0,2,1;" : "")}};
         MeshNormals { 1; 0.0;0.0;-1.0;; {{(withTwin ? "2" : "1")}}; 3;0,0,0;{{(withTwin ? ", 3;0,0,0;" : "")}}; }
         MeshMaterialList {
          1; 1; 0;;
          Material { 0.5;1.0;0.25;1.0;; 0.0; 0.0;0.0;0.0;; 0.0;0.0;0.0;; }
         }
        }
        """), "TRIANGLE.X");

    [Fact]
    public void TheReaderCollapsesFramesSplitsVerticesByNormalAndSortsTrianglesByMaterial()
    {
        XMesh mesh = XMeshDecoder.Decode(Text(FramedQuad), "QUAD.X");

        // Inner's matrix applies first, then Outer's: (x + 0.5) * 2 + 1, y * 2 + 2, z * 2 + 3.
        Assert.Equal(new Vector3(2, 2, 3), mesh.Vertices[0].Position);
        // Positions 1 and 2 are corners of both faces, each face with its own normal: seven vertices.
        Assert.Equal(7, mesh.Vertices.Count);
        Assert.Equal(new Vector3(6, 6, 3), mesh.Vertices[5].Position);
        Assert.Equal(new Vector3(0, 0, -1), mesh.Vertices[0].Normal);      // the inverse transpose, normalized
        Assert.Equal(new Vector3(0, 1, 0), mesh.Vertices[4].Normal);
        // The triangle is Red's (material 0) and comes first; the quad, Blue's, is a fan of two.
        Assert.Equal([4, 5, 6, 0, 1, 2, 0, 2, 3], mesh.Indices);
        Assert.Equal([0, 1, 1], mesh.TriangleMaterials);
        Assert.Equal(new XMeshMaterial(new Vector4(1, 0, 0, 1), 5, new Vector3(0.1f, 0.2f, 0.3f), Vector3.Zero),
                     mesh.Materials[0]);
        Assert.Equal(new XMeshMaterial(new Vector4(0, 0, 1, 0.5f), 0, Vector3.Zero, new Vector3(0, 0, 0.25f)),
                     mesh.Materials[1]);
    }

    [Theory]
    [InlineData("xof 0303bin 0032", "only text .X files are read")]
    [InlineData("xof 0303tzip0032", "only text .X files are read")]
    [InlineData("AGF\0 not a mesh at all", "no 'xof ' signature")]
    public void TheReaderRefusesAFileItCannotRead(string header, string message)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(header + "\nMesh { 0;; 0;; }\n");
        var error = Assert.Throws<XMeshFormatException>(() => XMeshDecoder.Decode(bytes, "BAD.X"));
        Assert.Contains(message, error.Message);
        Assert.StartsWith("BAD.X: ", error.Message);
    }

    [Fact]
    public void TheReaderRefusesABrokenBodyAndAFileWithNoMesh()
    {
        Assert.Throws<XMeshFormatException>(() => XMeshDecoder.Decode(Text("Mesh { 3; 0.0;0.0;"), "CUT.X"));
        Assert.Throws<XMeshFormatException>(() => XMeshDecoder.Decode(Text("Mesh { 1; 0;0;0;; 1; 3;0,1,2;; }"), "IDX.X"));
        var error = Assert.Throws<XMeshFormatException>(() => XMeshDecoder.Decode(Text("Header { 1; 0; 1; }"), "NONE.X"));
        Assert.Contains("no Mesh", error.Message);
    }

    [Fact]
    public void TheGameGeneratorIsMsvcRand()
    {
        // MSVC's rand() after srand(1), the C runtime's own start.
        var random = new CrtRandom(1);
        Assert.Equal([41, 18467, 6334, 26500, 19169], Enumerable.Range(0, 5).Select(_ => random.Next()));
        Assert.Equal(5, random.Draws);
    }

    // The game's generator from the same seed, read as native's leaf code reads it.
    private sealed class Draws(uint seed)
    {
        private readonly CrtRandom _random = new(seed);
        public int Raw() => _random.Next();
        public float Unit() => (float)(_random.Next() % 100 / 100.0);
        public float Centered() => (float)(Unit() - 0.5);
    }

    [Fact]
    public void ALeafIsBornFromFourteenDrawsInNativesOrder()
    {
        var graphics = new Graphics(null, new FrameClock(), new Sys4LogicalCanvas(16, 16), gameRandom: new CrtRandom(7));
        graphics.Scene3D.SetMesh(0, new Scene3DMesh(1, "A.X"));
        graphics.Scene3D.SetMesh(1, new Scene3DMesh(2, "B.X"));
        graphics.SetLeafEffect(2, [0, 1]);

        Assert.Equal(28, graphics.GameRandom.Draws);
        var draws = new Draws(7);
        foreach (Scene3DLeaf leaf in graphics.Scene3D.Leaf!.Leaves)
        {
            // Every operation rounded to a float, in native's order, as its single-precision x87 does.
            float x = draws.Centered() * 10f, y = draws.Unit() * 5f;
            float z = draws.Centered() * 10f;
            Assert.Equal(new Vector3(x, y, z), leaf.Position);
            float vx = draws.Centered() * 0.01f;
            float vy = (-0.02f - -0.01f) * draws.Unit() + -0.01f;
            float vz = draws.Centered() * 0.01f;
            Assert.Equal(new Vector3(vx, vy, vz), leaf.Velocity);
            Assert.InRange(vy, -0.02f, -0.01f);
            for (int axis = 0; axis < 3; axis++)
                Assert.Equal(draws.Unit() * 3.14159274f * 2f, leaf.Rotation[axis]);
            for (int axis = 0; axis < 3; axis++)
                Assert.Equal(0.03f * draws.Centered(), leaf.AngularVelocity[axis]);
            Assert.Equal((3f - 1f) * draws.Unit() + 1f, leaf.Scale);
            Assert.Equal(draws.Raw() % 2, leaf.Mesh);
        }
    }

    [Fact]
    public void LeavesStepSixtyTimesASecondAtMostAHundredAFrameAndStartAgainAtTheTop()
    {
        // A seed whose one leaf starts above 2.5: a hundred steps of at most 0.02 cannot take it below the floor.
        uint seed = 1;
        Graphics graphics;
        while (true)
        {
            graphics = new Graphics(null, new FrameClock(), new Sys4LogicalCanvas(16, 16), gameRandom: new CrtRandom(seed));
            graphics.Scene3D.SetMesh(0, new Scene3DMesh(1, "A.X"));
            graphics.SetLeafEffect(1, [0]);
            if (graphics.Scene3D.Leaf!.Leaves[0].Position.Y > 2.5f) break;
            seed++;
        }
        Scene3DLeafEffect effect = graphics.Scene3D.Leaf!;
        Scene3DLeaf born = effect.Leaves[0];

        graphics.LowerFrame(1000);            // the first lowering only sets the clock
        graphics.LowerFrame(1016);            // 60.96 ticks: still the same one
        Assert.Equal((born, 14L), (effect.Leaves[0], graphics.GameRandom.Draws));
        graphics.LowerFrame(1017);            // 61.02: one step, two draws for the drift's jitter
        Assert.Equal(born.Position + born.Velocity, effect.Leaves[0].Position);
        Assert.Equal(born.Rotation + born.AngularVelocity, effect.Leaves[0].Rotation);
        Assert.Equal(16L, graphics.GameRandom.Draws);
        graphics.LowerFrame(60_000);          // thousands of ticks, cut to a hundred
        Assert.Equal(16L + 200, graphics.GameRandom.Draws);

        // Step by step until the leaf passes the floor: it starts again at the top.
        float previous = effect.Leaves[0].Position.Y;
        for (int step = 0; step < 1000; step++)
        {
            effect.Step(graphics.GameRandom, 1);
            float y = effect.Leaves[0].Position.Y;
            if (y > previous)
            {
                Assert.Equal(Scene3DLeafEffect.Height, y);
                return;
            }
            previous = y;
        }
        Assert.Fail("the leaf never reached the floor");
    }

    [Fact]
    public void TheLeavesDrawFromTheStreamOp60DrawsFrom()
    {
        int Op60(bool leaves)
        {
            OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4438");
            var ops = new List<(int, Operand[])> { (0x329, [new(0, 0x1234), new(0, 0)]), (0x55, [new(3, 0x400), new(0, 0)]) };
            if (leaves) ops.Add((0x328, [new(0, 30), new(3, 0x400), new(0, 1)]));
            ops.Add((0x60, [new(3, 0x100), new(0, 30000)]));
            ops.Add((0x2, []));
            var graphics = new Graphics(null, new FrameClock(), new Sys4LogicalCanvas(0, 0), gameRandom: new CrtRandom(1));
            var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "LEAVES.BIN", ops, []), table,
                                        new RecordingWaits(), graphics: graphics);
            vm.Run();
            Assert.Equal("exit", vm.HaltReason);
            Assert.Same(graphics.GameRandom, vm.Memory.GameRandom);
            return (int)vm.Globals[0x100];
        }

        var random = new CrtRandom(1);
        Assert.Equal(random.Next() % 30000, Op60(leaves: false));
        // Thirty leaves take fourteen draws each before op 0x60 takes its own, as on native.
        random = new CrtRandom(1);
        for (int draw = 0; draw < 30 * 14; draw++) random.Next();
        Assert.Equal(random.Next() % 30000, Op60(leaves: true));
    }

    // A graphics native with the camera ten units in front of a leaf at half height, the ambient colour and one light
    // as given, and one leaf of the mesh placed there.
    private static Graphics Scene(XMesh mesh, uint ambient, (float X, float Y, float Z)? lightDirection,
                                  float leafY = 2.5f, float scale = 1f)
    {
        var graphics = new Graphics(null, new FrameClock(), new Sys4LogicalCanvas(Width, Height),
                                    gameRandom: new CrtRandom(3));
        graphics.SetCameraLookAt((0, 2.5f, -10), (0, 2.5f, 0));
        graphics.SetAmbientLight(ambient);
        if (lightDirection is { } direction) graphics.SetLight(0, 3, (0, 0, 0), direction, 0xff808080, 1000);
        graphics.Scene3D.SetMesh(0, new Scene3DMesh(1, "TRIANGLE.X", mesh));
        graphics.SetEffectHandles(2, 2);
        graphics.SetLeafEffect(1, [0]);
        graphics.Scene3D.Leaf!.Place(0, new Scene3DLeaf(new Vector3(0, leafY, 0), Vector3.Zero, Vector3.Zero,
                                                        Vector3.Zero, scale, 0));
        return graphics;
    }

    private static FramePolygonVertex[] LeafCorners(Graphics graphics)
        => Assert.Single(graphics.LowerFrame(0).Items, item => item.Kind == FrameItemKind.Polygon).Polygon!.Vertices;

    [Fact]
    public void ALeafIsProjectedAsNativesCameraAndProjectionPlaceIt()
    {
        FramePolygonVertex[] corners = LeafCorners(Scene(Triangle(), 0xffffffff, null));

        // Ten units ahead, 0.1 off the axis: 0.1 / 10 · cot(π/8) · 288 = 6.95294 pixels, in x as in y since the
        // projection keeps pixels square; the half pixel is Direct3D 9's pixel centre.
        const float Off = 6.95294f;
        Assert.Equal(3, corners.Length);
        Assert.Equal((512.5f - Off, 288.5f + Off), (corners[0].X, corners[0].Y), new Near());
        Assert.Equal((512.5f, 288.5f - Off), (corners[1].X, corners[1].Y), new Near());
        Assert.Equal((512.5f + Off, 288.5f + Off), (corners[2].X, corners[2].Y), new Near());
    }

    private sealed class Near : IEqualityComparer<(float, float)>
    {
        public bool Equals((float, float) a, (float, float) b)
            => Math.Abs(a.Item1 - b.Item1) < 1e-3 && Math.Abs(a.Item2 - b.Item2) < 1e-3;
        public int GetHashCode((float, float) value) => 0;
    }

    [Fact]
    public void ATriangleWoundTheOtherWayOnScreenIsCulled()
    {
        Assert.Equal(3, LeafCorners(Scene(Triangle(withTwin: true), 0xffffffff, null)).Length);
    }

    [Theory]
    // Ambient 64/255 plus the light's 128/255 times N·-direction, times the diffuse (0.5, 1, 0.25).
    [InlineData(1f, 0f, 0f, 1f, 0xff60c030u)]       // the light shines on the face: (96, 192, 48)
    [InlineData(1f, 0f, 0f, -1f, 0xff204010u)]      // from behind it: the ambient part alone, (32, 64, 16)
    [InlineData(2f, 0f, 0f, 1f, 0xff408020u)]       // scaled by 2, its normal is halved and so is the light
    public void ALeafIsLitAsDirect3DsFixedFunctionLightsIt(float scale, float x, float y, float z, uint argb)
    {
        FramePolygonVertex[] corners = LeafCorners(Scene(Triangle(), 0xff404040, (x, y, z), scale: scale));
        Assert.All(corners, corner => Assert.Equal(argb, corner.Argb));
    }

    [Theory]
    [InlineData(5f, 0u)]            // at the top: sin 0
    [InlineData(2.5f, 255u)]        // half way down: sin(π/2)
    [InlineData(1.25f, 180u)]       // sin(3π/4) = 0.7071
    public void ALeafFadesInAtTheTopAndOutAtTheFloor(float leafY, uint alpha)
    {
        FramePolygonVertex[] corners = LeafCorners(Scene(Triangle(), 0xffffffff, null, leafY));
        Assert.All(corners, corner => Assert.Equal(alpha, corner.Argb >> 24));
    }

    private static GfxState.PolygonVertex[] Square(uint argb) =>
    [
        new(0, 0, 0, argb, 0, 0), new(4, 0, 0, argb, 1, 0), new(0, 4, 0, argb, 0, 1), new(4, 4, 0, argb, 1, 1),
    ];

    [Theory]
    [InlineData(5, new[] { 6, 3, 6 })]          // before the first item at or past the handle
    [InlineData(10, new[] { 6, 3, 6 })]         // an item at the handle comes after the leaves
    [InlineData(11, new[] { 6, 6, 3 })]         // with none at or past it, after the last
    [InlineData(0, new[] { 3, 6, 6 })]
    public void TheLeavesAreDrawnWhereTheWalkReachesTheSecondHandle(int second, int[] vertexCounts)
    {
        Graphics graphics = Scene(Triangle(), 0xffffffff, null);
        graphics.SetEffectHandles(0, second);
        graphics.State.CreatePolygon(1, Square(0xff000000), 0);
        graphics.State.CreatePolygon(10, Square(0xffffffff), 0);

        FrameDescription frame = graphics.LowerFrame(0);

        Assert.Equal(vertexCounts, frame.Items.Select(item => item.Polygon!.Vertices.Length));
    }

    [Fact]
    public void WithNoMeshNoLeavesAreBornAndWithNoGeometryNoneIsDrawn()
    {
        var graphics = new Graphics(null, new FrameClock(), new Sys4LogicalCanvas(Width, Height),
                                    gameRandom: new CrtRandom(5));
        graphics.SetLeafEffect(30, [0]);            // slot 0 holds no mesh: native would divide by zero
        Assert.Empty(graphics.Scene3D.Leaf!.Leaves);
        Assert.Equal(0L, graphics.GameRandom.Draws);
        graphics.Scene3D.SetMesh(0, new Scene3DMesh(1, "A.X"));   // a headless load: no geometry to draw
        graphics.SetLeafEffect(30, [0]);
        Assert.Equal(30, graphics.Scene3D.Leaf!.Leaves.Count);
        Assert.Empty(graphics.LowerFrame(0).Items);
    }
}
