using Age.Engine.Model;
using System.Text;
namespace Age.Engine.Sys4;
public static class Sys4Loader
{
    private const int HeaderSize = 0x3C, BodyOff = 0x3C, NumFields = 13, ArrayOpcode = 0x64;

    public static Script Load(string path, OpcodeTable table)
        => Parse(File.ReadAllBytes(path), table, Path.GetFileName(path));

    public static Script Parse(byte[] data, OpcodeTable table, string name = "", uint packedId = 0)
    {
        if (data.Length < HeaderSize) throw new InvalidDataException($"{name}: too small");
        if (!(data[0] == (byte)'S' && data[1] == (byte)'Y' && data[2] == (byte)'S' && data[3] == (byte)'4'))
            throw new InvalidDataException($"{name}: bad magic");
        if (data.Length % 4 != 0) throw new InvalidDataException($"{name}: not dword-aligned");
        string engineRevision = Encoding.ASCII.GetString(data, 0, 8).TrimEnd('\0', ' ');

        var fields = new int[NumFields];
        for (int k = 0; k < NumFields; k++) fields[k] = BitConverter.ToInt32(data, 8 + k * 4);
        int nbody = (data.Length - BodyOff) / 4;
        var dw = new uint[nbody];
        for (int k = 0; k < nbody; k++) dw[k] = BitConverter.ToUInt32(data, BodyOff + k * 4);

        var header = new ScriptHeader(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]);
        var (instrs, idxByOff, strings) = DecodeCode(
            dw, fields, nbody, table, engineRevision, name, packedId);
        int messageCount = fields[7];
        int messageTableOffset = fields[8];
        if (messageCount < 0 || messageTableOffset < 0
            || messageTableOffset > nbody || messageCount > nbody - messageTableOffset)
            throw new InvalidDataException($"{name}: invalid T1 read-message table");
        int[] messageOffsets = dw.AsSpan(messageTableOffset, messageCount)
            .ToArray().Select(value => checked((int)value)).ToArray();
        int[] scriptCallOffsets = ReadOffsetTable(dw, fields[9], fields[10], nbody, name, "T2 script-call");
        int[] localCallOffsets = ReadOffsetTable(dw, fields[11], fields[12], nbody, name, "T3 local-call");
        return new Script
        {
            Name = name,
            EngineRevision = engineRevision,
            PackedId = packedId,
            Header = header,
            Instructions = instrs,
            IndexByOffset = idxByOff,
            Strings = strings,
            BodyDwords = dw,
            ReadMessageOffsets = messageOffsets,
            ScriptCallOffsets = scriptCallOffsets,
            LocalCallOffsets = localCallOffsets,
        };
    }

    private static int[] ReadOffsetTable(
        uint[] body, int count, int offset, int bodyLength, string name, string tableName)
    {
        if (count < 0 || offset < 0 || offset > bodyLength || count > bodyLength - offset)
            throw new InvalidDataException($"{name}: invalid {tableName} table");
        return body.AsSpan(offset, count).ToArray()
            .Select(value => checked((int)value)).ToArray();
    }

    private static (List<Instruction>, Dictionary<int, int>, Dictionary<int, string>)
        DecodeCode(uint[] dw, int[] fields, int nbody, OpcodeTable table,
                   string engineRevision, string scriptName, uint packedId)
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
            if (argc < 0)
                throw new Sys4OpcodeDecodeException(
                    Sys4OpcodeDecodeFailure.OpcodeAbsentFromAbi, table.AbiId, engineRevision,
                    scriptName, packedId, i, op);
            int baseI = i + 1;
            if (baseI + 2 * argc > codeEnd)
                throw new Sys4OpcodeDecodeException(
                    Sys4OpcodeDecodeFailure.TruncatedOperands, table.AbiId, engineRevision,
                    scriptName, packedId, i, op, argc);
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
