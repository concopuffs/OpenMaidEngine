using Age.Engine.Model;
namespace Age.Engine.Sys4;
public static class Sys4Loader
{
    private const int HeaderSize = 0x3C, BodyOff = 0x3C, NumFields = 13, ArrayOpcode = 0x64;

    public static Script Load(string path, OpcodeTable table)
        => Parse(File.ReadAllBytes(path), table, Path.GetFileName(path));

    public static Script Parse(byte[] data, OpcodeTable table, string name = "")
    {
        if (data.Length < HeaderSize) throw new InvalidDataException($"{name}: too small");
        if (!(data[0] == (byte)'S' && data[1] == (byte)'Y' && data[2] == (byte)'S' && data[3] == (byte)'4'))
            throw new InvalidDataException($"{name}: bad magic");
        if (data.Length % 4 != 0) throw new InvalidDataException($"{name}: not dword-aligned");

        var fields = new int[NumFields];
        for (int k = 0; k < NumFields; k++) fields[k] = BitConverter.ToInt32(data, 8 + k * 4);
        int nbody = (data.Length - BodyOff) / 4;
        var dw = new uint[nbody];
        for (int k = 0; k < nbody; k++) dw[k] = BitConverter.ToUInt32(data, BodyOff + k * 4);

        var header = new ScriptHeader(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]);
        var (instrs, idxByOff, strings) = DecodeCode(dw, fields, nbody, table);
        return new Script { Name = name, Header = header, Instructions = instrs, IndexByOffset = idxByOff, Strings = strings };
    }

    private static (List<Instruction>, Dictionary<int, int>, Dictionary<int, string>)
        DecodeCode(uint[] dw, int[] fields, int nbody, OpcodeTable table)
    {
        int codeEnd = fields[8];                       // F8; shrinks to first inline string/array offset
        var instrs = new List<Instruction>();
        var idx = new Dictionary<int, int>();
        var strings = new Dictionary<int, string>();
        int i = 0;
        while (i < codeEnd)
        {
            int op = (int)dw[i];
            int argc = table.Argc(op);
            if (argc < 0) { idx[i] = instrs.Count; instrs.Add(new Instruction(i, op, Array.Empty<Operand>())); break; }
            int baseI = i + 1;
            if (baseI + 2 * argc > codeEnd) { idx[i] = instrs.Count; instrs.Add(new Instruction(i, op, Array.Empty<Operand>())); break; }
            var args = new Operand[argc];
            for (int a = 0; a < argc; a++)
            {
                int atype = (int)dw[baseI + 2 * a];
                long aval = dw[baseI + 2 * a + 1];
                args[a] = new Operand(atype, aval);
                if (atype == 2 && aval >= 0 && aval < nbody)
                {
                    if (aval < codeEnd) codeEnd = (int)aval;
                    if (!strings.ContainsKey((int)aval))
                    {
                        var (text, _) = Sys4StringCodec.Decode(dw, (int)aval);
                        if (text != null) strings[(int)aval] = text;
                    }
                }
                else if (op == ArrayOpcode && a == 1 && aval >= 0 && aval < nbody)
                {
                    if (aval < codeEnd) codeEnd = (int)aval;
                }
            }
            idx[i] = instrs.Count;
            instrs.Add(new Instruction(i, op, args));
            i = baseI + 2 * argc;
        }
        return (instrs, idx, strings);
    }
}
