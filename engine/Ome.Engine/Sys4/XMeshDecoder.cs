using System.Globalization;
using System.Numerics;
using System.Text;

namespace Ome.Engine.Sys4;

/// <summary>A material of a Direct3D <c>.X</c> mesh: its face colour (the diffuse, alpha included), its power, and its
/// specular and emissive colours, each channel 0 to 1 as the file writes it.</summary>
public sealed record XMeshMaterial(Vector4 Diffuse, float Power, Vector3 Specular, Vector3 Emissive);

/// <summary>A vertex of a decoded mesh: a position and a normal, in the mesh's space after its frames.</summary>
public readonly record struct XMeshVertex(Vector3 Position, Vector3 Normal);

/// <summary>A Direct3D <c>.X</c> file as D3DX's <c>D3DXLoadMeshFromX</c> gives it: one mesh, the file's frames
/// collapsed into it (docs/superpowers/specs/2026-11-20-title-leaves-design.md §3). A vertex is a position with one
/// of its normals, as D3DX splits them; the triangles are sorted by material, which is the order a subset draw takes
/// them in.</summary>
public sealed class XMesh
{
    internal XMesh(XMeshVertex[] vertices, int[] indices, int[] triangleMaterials, XMeshMaterial[] materials)
    {
        VertexArray = vertices;
        IndexArray = indices;
        TriangleMaterialArray = triangleMaterials;
        MaterialArray = materials;
    }

    internal XMeshVertex[] VertexArray { get; }
    internal int[] IndexArray { get; }
    internal int[] TriangleMaterialArray { get; }
    internal XMeshMaterial[] MaterialArray { get; }

    public IReadOnlyList<XMeshVertex> Vertices => VertexArray;
    /// <summary>Three vertex indices a triangle.</summary>
    public IReadOnlyList<int> Indices => IndexArray;
    /// <summary>Each triangle's material: ascending, as the triangles are sorted by it.</summary>
    public IReadOnlyList<int> TriangleMaterials => TriangleMaterialArray;
    public IReadOnlyList<XMeshMaterial> Materials => MaterialArray;
    public int TriangleCount => TriangleMaterialArray.Length;
}

/// <summary>A file the <c>.X</c> reader refuses: not a text <c>.X</c> file, or one it cannot parse.</summary>
public sealed class XMeshFormatException(string message) : FormatException(message);

/// <summary>
/// The reader for text Direct3D <c>.X</c> files (<c>xof 0302txt</c> or <c>0303txt</c>, 32- or 64-bit floats), which
/// AGE 4.38D's op 0x329 loads (創刻のアテリアル's <c>LEAF0.X</c> and <c>LEAF1.X</c>).
/// </summary>
/// <remarks>
/// <para>It reads <c>Frame</c>, <c>FrameTransformMatrix</c>, <c>Mesh</c>, <c>MeshNormals</c>, <c>MeshMaterialList</c>
/// and <c>Material</c>, top-level materials named by reference included, and skips templates and every other data
/// object by its braces (texture coordinates, animation, a texture's file name: AGE keeps no texture for a mesh).</para>
/// <para>Frames collapse as D3DX documents it: each mesh's positions are multiplied by the product of its frames'
/// matrices, its normals by that product's inverse transpose, then normalized (how D3DX transforms the normals was not
/// read). A face of more than three corners becomes a fan. A mesh with no <c>MeshNormals</c> has zero normals, as a
/// vertex format without them lights with none. Several meshes become one, their materials in file order.</para>
/// <para>A binary or compressed file (<c>bin </c>, <c>tzip</c>, <c>bzip</c>) is refused: no shipped game has one.</para>
/// </remarks>
public static class XMeshDecoder
{
    private const int HeaderLength = 16;

    public static XMesh Decode(byte[] bytes, string name)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < HeaderLength || Encoding.ASCII.GetString(bytes, 0, 4) != "xof ")
            throw new XMeshFormatException($"{name}: not a Direct3D .X file (no 'xof ' signature)");
        string version = Encoding.ASCII.GetString(bytes, 4, 4);
        string format = Encoding.ASCII.GetString(bytes, 8, 4);
        string floatSize = Encoding.ASCII.GetString(bytes, 12, 4);
        if (format != "txt ")
            throw new XMeshFormatException($"{name}: a '{format.TrimEnd()}' .X file; only text .X files are read");
        if (version != "0302" && version != "0303")
            throw new XMeshFormatException($"{name}: .X version {version}; versions 0302 and 0303 are read");
        if (floatSize != "0032" && floatSize != "0064")
            throw new XMeshFormatException($"{name}: .X float size {floatSize}; 0032 and 0064 are read");
        var parser = new Parser(Encoding.Latin1.GetString(bytes, HeaderLength, bytes.Length - HeaderLength), name);
        return parser.Parse();
    }

    private enum TokenKind { End, Name, Number, String, Guid, OpenBrace, CloseBrace, Semicolon, Comma, Bracket }

    private readonly record struct Token(TokenKind Kind, string Text, int Line);

    private sealed class RawMesh
    {
        public Vector3[] Positions = [];
        public int[][] Faces = [];
        public Vector3[]? Normals;
        public int[][]? NormalFaces;
        public int[]? FaceMaterials;
        public List<XMeshMaterial> Materials = new();
    }

    private sealed class FrameNode
    {
        public Matrix4x4 Matrix = Matrix4x4.Identity;
        public readonly List<RawMesh> Meshes = new();
        public readonly List<FrameNode> Children = new();
    }

    private sealed class Parser(string text, string name)
    {
        private int _at;
        private int _line = 1;
        private Token? _peeked;
        private readonly Dictionary<string, XMeshMaterial> _namedMaterials = new(StringComparer.Ordinal);

        public XMesh Parse()
        {
            var root = new FrameNode();
            while (Peek().Kind != TokenKind.End)
            {
                Token token = Next();
                if (token.Kind != TokenKind.Name) throw Error(token, "a data object's type");
                if (token.Text == "template") SkipTemplate();
                else ParseObject(token.Text, root, topLevel: true);
            }
            var placed = new List<(RawMesh Mesh, Matrix4x4 Matrix)>();
            Collect(root, Matrix4x4.Identity, placed);
            if (placed.Count == 0) throw new XMeshFormatException($"{name}: the file holds no Mesh");
            return Build(placed);
        }

        private static void Collect(FrameNode frame, Matrix4x4 parent, List<(RawMesh, Matrix4x4)> placed)
        {
            // Row vectors, as Direct3D's: a frame's matrix applies before its parent's.
            Matrix4x4 matrix = frame.Matrix * parent;
            foreach (RawMesh mesh in frame.Meshes) placed.Add((mesh, matrix));
            foreach (FrameNode child in frame.Children) Collect(child, matrix, placed);
        }

        private XMesh Build(List<(RawMesh Mesh, Matrix4x4 Matrix)> placed)
        {
            var vertices = new List<XMeshVertex>();
            var triangles = new List<(int A, int B, int C, int Material)>();
            var materials = new List<XMeshMaterial>();
            foreach ((RawMesh mesh, Matrix4x4 matrix) in placed)
            {
                Matrix4x4 normalMatrix = Matrix4x4.Invert(matrix, out Matrix4x4 inverse)
                    ? Matrix4x4.Transpose(inverse) : matrix;
                int materialBase = materials.Count;
                materials.AddRange(mesh.Materials);
                var split = new Dictionary<(int Position, int Normal), int>();
                for (int face = 0; face < mesh.Faces.Length; face++)
                {
                    int[] corners = mesh.Faces[face];
                    int[]? normalCorners = mesh.NormalFaces is { } normalFaces && face < normalFaces.Length
                        ? normalFaces[face] : null;
                    var indices = new int[corners.Length];
                    for (int corner = 0; corner < corners.Length; corner++)
                    {
                        int position = corners[corner];
                        if ((uint)position >= (uint)mesh.Positions.Length)
                            throw new XMeshFormatException(
                                $"{name}: face {face} names vertex {position} of {mesh.Positions.Length}");
                        int normal = normalCorners != null && corner < normalCorners.Length ? normalCorners[corner] : -1;
                        if (normal >= 0 && (mesh.Normals == null || normal >= mesh.Normals.Length))
                            throw new XMeshFormatException($"{name}: face {face} names a normal the file does not have");
                        if (!split.TryGetValue((position, normal), out int vertex))
                        {
                            Vector3 placedNormal = Vector3.Zero;
                            if (normal >= 0)
                            {
                                placedNormal = Vector3.TransformNormal(mesh.Normals![normal], normalMatrix);
                                float length = placedNormal.Length();
                                if (length > 0) placedNormal /= length;
                            }
                            vertex = vertices.Count;
                            vertices.Add(new XMeshVertex(Vector3.Transform(mesh.Positions[position], matrix),
                                                         placedNormal));
                            split.Add((position, normal), vertex);
                        }
                        indices[corner] = vertex;
                    }
                    int material = MaterialOf(mesh, face);
                    if (material >= 0) material += materialBase;
                    for (int corner = 1; corner + 1 < indices.Length; corner++)
                        triangles.Add((indices[0], indices[corner], indices[corner + 1], material));
                }
            }
            // A face with no material (a mesh with no MeshMaterialList) belongs to no subset, so no draw takes it.
            var sorted = triangles.Where(triangle => triangle.Material >= 0)
                                  .OrderBy(triangle => triangle.Material).ToArray();   // OrderBy is stable
            var indexArray = new int[sorted.Length * 3];
            var materialArray = new int[sorted.Length];
            for (int index = 0; index < sorted.Length; index++)
            {
                indexArray[index * 3] = sorted[index].A;
                indexArray[index * 3 + 1] = sorted[index].B;
                indexArray[index * 3 + 2] = sorted[index].C;
                materialArray[index] = sorted[index].Material;
            }
            return new XMesh(vertices.ToArray(), indexArray, materialArray, materials.ToArray());
        }

        // The .X format: fewer face indices than faces give the rest the last one.
        private static int MaterialOf(RawMesh mesh, int face)
        {
            if (mesh.FaceMaterials is not { Length: > 0 } faceMaterials || mesh.Materials.Count == 0) return -1;
            int material = faceMaterials[Math.Min(face, faceMaterials.Length - 1)];
            return (uint)material < (uint)mesh.Materials.Count ? material : -1;
        }

        private void ParseObject(string type, FrameNode parent, bool topLevel)
        {
            string? objectName = null;
            if (Peek().Kind == TokenKind.Name) objectName = Next().Text;
            Expect(TokenKind.OpenBrace);
            switch (type)
            {
                case "Frame":
                {
                    var frame = new FrameNode();
                    ParseFrameBody(frame);
                    parent.Children.Add(frame);
                    break;
                }
                case "Mesh":
                    parent.Meshes.Add(ParseMeshBody());
                    break;
                case "Material" when topLevel:
                {
                    XMeshMaterial material = ParseMaterialBody();
                    if (objectName != null) _namedMaterials[objectName] = material;
                    break;
                }
                default:
                    SkipBody();
                    break;
            }
        }

        private void ParseFrameBody(FrameNode frame)
        {
            while (true)
            {
                Token token = NextSkippingSeparators();
                if (token.Kind == TokenKind.CloseBrace) return;
                if (token.Kind == TokenKind.OpenBrace) { SkipBody(); continue; }   // a reference to another frame
                if (token.Kind != TokenKind.Name) throw Error(token, "a data object in a Frame");
                if (token.Text == "FrameTransformMatrix")
                {
                    if (Peek().Kind == TokenKind.Name) Next();
                    Expect(TokenKind.OpenBrace);
                    var m = new float[16];
                    for (int index = 0; index < 16; index++) m[index] = Float();
                    frame.Matrix = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                                                 m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
                    SkipBody();
                }
                else ParseObject(token.Text, frame, topLevel: false);
            }
        }

        private RawMesh ParseMeshBody()
        {
            var mesh = new RawMesh();
            mesh.Positions = Vectors(Count("vertices"));
            mesh.Faces = Faces(Count("faces"));
            while (true)
            {
                Token token = NextSkippingSeparators();
                if (token.Kind == TokenKind.CloseBrace) return mesh;
                if (token.Kind == TokenKind.OpenBrace) { SkipBody(); continue; }
                if (token.Kind != TokenKind.Name) throw Error(token, "a data object in a Mesh");
                if (Peek().Kind == TokenKind.Name) Next();
                Expect(TokenKind.OpenBrace);
                switch (token.Text)
                {
                    case "MeshNormals":
                        mesh.Normals = Vectors(Count("normals"));
                        mesh.NormalFaces = Faces(Count("normal faces"));
                        SkipBody();
                        break;
                    case "MeshMaterialList":
                        ParseMaterialList(mesh);
                        break;
                    default:
                        SkipBody();
                        break;
                }
            }
        }

        private void ParseMaterialList(RawMesh mesh)
        {
            int materials = Count("materials");
            int faceIndexes = Count("face material indices");
            mesh.FaceMaterials = new int[faceIndexes];
            for (int index = 0; index < faceIndexes; index++) mesh.FaceMaterials[index] = Int();
            while (true)
            {
                Token token = NextSkippingSeparators();
                if (token.Kind == TokenKind.CloseBrace) break;
                if (token.Kind == TokenKind.OpenBrace)
                {
                    Token reference = Next();
                    if (reference.Kind != TokenKind.Name) throw Error(reference, "a material's name");
                    Expect(TokenKind.CloseBrace);
                    if (!_namedMaterials.TryGetValue(reference.Text, out XMeshMaterial? named))
                        throw new XMeshFormatException($"{name}: line {reference.Line}: no material '{reference.Text}'");
                    mesh.Materials.Add(named);
                    continue;
                }
                if (token.Kind != TokenKind.Name) throw Error(token, "a Material");
                if (Peek().Kind == TokenKind.Name) Next();
                Expect(TokenKind.OpenBrace);
                if (token.Text == "Material") mesh.Materials.Add(ParseMaterialBody());
                else SkipBody();
            }
            if (mesh.Materials.Count != materials)
                throw new XMeshFormatException(
                    $"{name}: a MeshMaterialList counts {materials} materials and holds {mesh.Materials.Count}");
        }

        private XMeshMaterial ParseMaterialBody()
        {
            var diffuse = new Vector4(Float(), Float(), Float(), Float());
            float power = Float();
            var specular = new Vector3(Float(), Float(), Float());
            var emissive = new Vector3(Float(), Float(), Float());
            SkipBody();   // a TextureFilename, which AGE does not load
            return new XMeshMaterial(diffuse, power, specular, emissive);
        }

        private Vector3[] Vectors(int count)
        {
            var vectors = new Vector3[count];
            for (int index = 0; index < count; index++) vectors[index] = new Vector3(Float(), Float(), Float());
            return vectors;
        }

        private int[][] Faces(int count)
        {
            var faces = new int[count][];
            for (int index = 0; index < count; index++)
            {
                var corners = new int[Count("face corners")];
                for (int corner = 0; corner < corners.Length; corner++) corners[corner] = Int();
                faces[index] = corners;
            }
            return faces;
        }

        private int Count(string what)
        {
            Token token = NumberToken();
            if (!int.TryParse(token.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                || count < 0 || count > 10_000_000)
                throw Error(token, $"a count of {what}");
            return count;
        }

        private int Int()
        {
            Token token = NumberToken();
            if (!int.TryParse(token.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw Error(token, "an integer");
            return value;
        }

        private float Float()
        {
            Token token = NumberToken();
            if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw Error(token, "a number");
            return (float)value;
        }

        private Token NumberToken()
        {
            Token token = NextSkippingSeparators();
            if (token.Kind != TokenKind.Number) throw Error(token, "a number");
            return token;
        }

        private void SkipTemplate()
        {
            if (Peek().Kind == TokenKind.Name) Next();
            Expect(TokenKind.OpenBrace);
            SkipBody();
        }

        // Past the matching close brace of a body whose open brace has been read.
        private void SkipBody()
        {
            int depth = 1;
            while (depth > 0)
            {
                Token token = Next();
                if (token.Kind == TokenKind.End) throw Error(token, "a closing brace");
                if (token.Kind == TokenKind.OpenBrace) depth++;
                else if (token.Kind == TokenKind.CloseBrace) depth--;
            }
        }

        private void Expect(TokenKind kind)
        {
            Token token = Next();
            if (token.Kind != kind) throw Error(token, kind == TokenKind.OpenBrace ? "'{'" : "'}'");
        }

        private Token NextSkippingSeparators()
        {
            Token token = Next();
            while (token.Kind is TokenKind.Semicolon or TokenKind.Comma) token = Next();
            return token;
        }

        private XMeshFormatException Error(Token token, string expected)
            => new($"{name}: line {token.Line}: expected {expected}, found "
                   + (token.Kind == TokenKind.End ? "the end of the file" : $"'{token.Text}'"));

        private Token Peek() => _peeked ??= Read();

        private Token Next()
        {
            if (_peeked is { } peeked)
            {
                _peeked = null;
                return peeked;
            }
            return Read();
        }

        private Token Read()
        {
            while (_at < text.Length)
            {
                char c = text[_at];
                if (c == '\n') { _line++; _at++; continue; }
                if (char.IsWhiteSpace(c) || c == '\0') { _at++; continue; }
                if (c == '#' || (c == '/' && _at + 1 < text.Length && text[_at + 1] == '/'))
                {
                    while (_at < text.Length && text[_at] != '\n') _at++;
                    continue;
                }
                break;
            }
            if (_at >= text.Length) return new Token(TokenKind.End, "", _line);
            char first = text[_at];
            int start = _at;
            switch (first)
            {
                case '{': _at++; return new Token(TokenKind.OpenBrace, "{", _line);
                case '}': _at++; return new Token(TokenKind.CloseBrace, "}", _line);
                case ';': _at++; return new Token(TokenKind.Semicolon, ";", _line);
                case ',': _at++; return new Token(TokenKind.Comma, ",", _line);
                // A template's array member: array DWORD vertexIndices[nWeights];
                case '[': _at++; return new Token(TokenKind.Bracket, "[", _line);
                case ']': _at++; return new Token(TokenKind.Bracket, "]", _line);
                case '<':
                {
                    int end = text.IndexOf('>', _at);
                    if (end < 0) throw new XMeshFormatException($"{name}: line {_line}: an unclosed GUID");
                    _at = end + 1;
                    return new Token(TokenKind.Guid, text[start.._at], _line);
                }
                case '"':
                {
                    int end = text.IndexOf('"', _at + 1);
                    if (end < 0) throw new XMeshFormatException($"{name}: line {_line}: an unclosed string");
                    _at = end + 1;
                    return new Token(TokenKind.String, text[(start + 1)..end], _line);
                }
            }
            if (char.IsDigit(first) || first is '-' or '+' or '.')
            {
                _at++;
                while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] is '.' or 'e' or 'E'
                                             || (text[_at] is '-' or '+' && text[_at - 1] is 'e' or 'E')))
                    _at++;
                return new Token(TokenKind.Number, text[start.._at], _line);
            }
            if (char.IsLetter(first) || first == '_')
            {
                _at++;
                while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] is '_' or '-' or '.'))
                    _at++;
                return new Token(TokenKind.Name, text[start.._at], _line);
            }
            throw new XMeshFormatException($"{name}: line {_line}: unexpected '{first}'");
        }
    }
}
