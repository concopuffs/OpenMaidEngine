using System.Text;
using Age.Engine.Model;
namespace Age.Engine.Sys4;

/// <summary>Assembles a SYS4 script from instructions + strings — the inverse of <see cref="Sys4Loader"/>.
/// Used to synthesize deterministic test scenes (so regression tests exercise real op handling instead
/// of running a real scene in a crippled mode) and as groundwork for the Phase-D modding assembler.
///
/// <para>A string operand is written as <c>new Operand(2, stringIndex)</c>; the assembler lays strings
/// out immediately after the code and patches each such operand's value to the string's dword offset,
/// exactly as the compiler does. Strings are cp932, null-terminated, stored XOR-0xFFFFFFFF per dword
/// (matching <see cref="Sys4StringCodec"/>).</para></summary>
public static class ScriptAssembler
{
    private const int HeaderSize = 0x3C, NumFields = 13, StringArgType = 2;
    private static readonly Encoding Cp932;
    static ScriptAssembler()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp932 = Encoding.GetEncoding(932);
    }

    public static Script Assemble(OpcodeTable table, string name,
                                  IReadOnlyList<(int Op, Operand[] Args)> instrs, IReadOnlyList<string> strings)
        => Sys4Loader.Parse(AssembleBytes(instrs, strings), table, name);

    public static byte[] AssembleBytes(IReadOnlyList<(int Op, Operand[] Args)> instrs, IReadOnlyList<string> strings)
    {
        int codeLen = 0;
        foreach (var ins in instrs) codeLen += 1 + 2 * ins.Args.Length;

        // Encode strings; record each string's starting dword offset (relative to body start).
        var strDwords = new List<uint>();
        var strOffset = new int[strings.Count];
        for (int i = 0; i < strings.Count; i++)
        {
            strOffset[i] = codeLen + strDwords.Count;
            strDwords.AddRange(EncodeString(strings[i]));
        }

        var body = new List<uint>(codeLen + strDwords.Count);
        foreach (var ins in instrs)
        {
            body.Add((uint)ins.Op);
            foreach (var arg in ins.Args)
            {
                long val = arg.Type == StringArgType ? strOffset[(int)arg.Value] : arg.Value;
                body.Add((uint)arg.Type);
                body.Add((uint)val);
            }
        }
        body.AddRange(strDwords);

        var fields = new int[NumFields];
        fields[8] = codeLen;                       // F8 = code end (strings begin here)

        var bytes = new byte[HeaderSize + body.Count * 4];
        Encoding.ASCII.GetBytes("SYS4422 ").CopyTo(bytes, 0);
        for (int k = 0; k < NumFields; k++) BitConverter.GetBytes(fields[k]).CopyTo(bytes, 8 + k * 4);
        for (int i = 0; i < body.Count; i++) BitConverter.GetBytes(body[i]).CopyTo(bytes, HeaderSize + i * 4);
        return bytes;
    }

    private static IEnumerable<uint> EncodeString(string text)
    {
        var raw = new List<byte>(Cp932.GetBytes(text)) { 0 };   // null terminator
        while (raw.Count % 4 != 0) raw.Add(0);
        for (int i = 0; i < raw.Count; i += 4)
        {
            uint le = (uint)(raw[i] | raw[i + 1] << 8 | raw[i + 2] << 16 | raw[i + 3] << 24);
            yield return le ^ 0xFFFFFFFFu;
        }
    }
}
