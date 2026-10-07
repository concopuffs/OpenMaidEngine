using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Script.Abi;

namespace Age.Script.Ir;

/// <summary>
/// Decodes SYS4 script bytes into the intermediate form. Framing follows <see cref="Sys4Loader"/>: instructions run
/// from offset 0 until the first referenced string or op-0x64 array, the data area runs to F8, and T1/T2/T3 follow.
/// Code-target operands become instruction labels and data operands become data-item references wherever they
/// resolve exactly, and so do immediates that data flow proves to be code positions (<see cref="IrCodeValues"/>);
/// anything else stays literal, so <see cref="IrEncoder"/> reproduces the input byte for byte.
/// </summary>
public static class IrDecoder
{
    private const int StringType = 2, ArrayOpcode = 0x64;
    private const uint NoTarget = 0xFFFFFFFF;

    public static IrScript Decode(byte[] bytes, OpcodeTable table, OpcodeContracts contracts, string name = "")
        => Decode(bytes, table, contracts, name, out _);

    /// <param name="literalCodeValues">Proven code values that are not an instruction start and stay literal.</param>
    public static IrScript Decode(byte[] bytes, OpcodeTable table, OpcodeContracts contracts, string name,
                                  out int literalCodeValues)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < IrScript.HeaderSize || bytes.Length % 4 != 0)
            throw new InvalidDataException($"{name}: not a SYS4 script ({bytes.Length} bytes)");
        if (bytes[0] != (byte)'S' || bytes[1] != (byte)'Y' || bytes[2] != (byte)'S' || bytes[3] != (byte)'4')
            throw new InvalidDataException($"{name}: bad magic");
        var fields = new uint[13];
        for (int k = 0; k < 13; k++) fields[k] = BitConverter.ToUInt32(bytes, 8 + k * 4);
        int nbody = (bytes.Length - IrScript.HeaderSize) / 4;
        var body = new uint[nbody];
        for (int k = 0; k < nbody; k++) body[k] = BitConverter.ToUInt32(bytes, IrScript.HeaderSize + k * 4);

        int t1Count = Checked(fields[7]), dataEnd = Checked(fields[8]);
        int t2Count = Checked(fields[9]), t2Offset = Checked(fields[10]);
        int t3Count = Checked(fields[11]), t3Offset = Checked(fields[12]);
        if (dataEnd > nbody || t2Offset != dataEnd + t1Count || t3Offset != t2Offset + t2Count
            || nbody != t3Offset + t3Count)
            throw new InvalidDataException($"{name}: table geometry F7..F12 does not tile the body");

        // Instructions: identical framing to Sys4Loader.DecodeCode.
        var raw = new List<(int Offset, int Opcode, Operand[] Args)>();
        int codeEnd = dataEnd, i = 0;
        while (i < codeEnd)
        {
            int op = (int)body[i];
            int argc = table.Argc(op);
            if (argc < 0)
                throw new InvalidDataException($"{name}: opcode 0x{op:x} at 0x{i:x} is absent from ABI {table.AbiId}");
            int baseI = i + 1;
            if (baseI + 2 * argc > codeEnd)
                throw new InvalidDataException($"{name}: opcode 0x{op:x} at 0x{i:x} overruns the code");
            var args = new Operand[argc];
            for (int a = 0; a < argc; a++)
            {
                int type = (int)body[baseI + 2 * a];
                uint value = body[baseI + 2 * a + 1];
                args[a] = new Operand(type, value);
                bool dataRef = type == StringType || (op == ArrayOpcode && a == 1);
                if (dataRef && value < nbody && value < codeEnd) codeEnd = (int)value;
            }
            raw.Add((i, op, args));
            i = baseI + 2 * argc;
        }
        if (i != codeEnd)
            throw new InvalidDataException($"{name}: data reference 0x{codeEnd:x} falls inside decoded code");

        // Data area [codeEnd, dataEnd): split at every referenced offset and type each segment.
        var stringRefs = new SortedSet<int>();
        var arrayRefs = new SortedSet<int>();
        foreach (var (_, op, args) in raw)
            for (int a = 0; a < args.Length; a++)
            {
                uint value = (uint)args[a].Value;
                if (value < codeEnd || value >= dataEnd) continue;
                if (args[a].Type == StringType) stringRefs.Add((int)value);
                else if (op == ArrayOpcode && a == 1) arrayRefs.Add((int)value);
            }
        var cuts = new SortedSet<int>(stringRefs.Concat(arrayRefs)) { codeEnd, dataEnd };
        var data = new List<IrDataItem>();
        var itemByOffset = new Dictionary<int, int>();
        int[] cutList = cuts.ToArray();
        for (int c = 0; c + 1 < cutList.Length; c++)
        {
            int start = cutList[c], end = cutList[c + 1];
            int used = 0;
            if (arrayRefs.Contains(start))
            {
                long size = (long)body[start] + 1;
                if (size <= end - start)
                {
                    itemByOffset[start] = data.Count;
                    data.Add(new IrDataItem(IrDataKind.IntArray, Slice(body, start, (int)size), null, start));
                    used = (int)size;
                }
            }
            else if (stringRefs.Contains(start))
            {
                var (text, ndwords) = Sys4StringCodec.Decode(body, start);
                if (text != null && ndwords > 0 && ndwords <= end - start)
                {
                    itemByOffset[start] = data.Count;
                    data.Add(new IrDataItem(IrDataKind.String, Slice(body, start, ndwords), text, start));
                    used = ndwords;
                }
            }
            if (used < end - start)
            {
                if (used == 0) itemByOffset[start] = data.Count;   // referenced but unparsed: referenceable raw item
                data.Add(new IrDataItem(IrDataKind.Raw, Slice(body, start + used, end - start - used), null, start + used));
            }
        }

        // Symbolic operands.
        var indexByOffset = new Dictionary<int, int>(raw.Count);
        for (int k = 0; k < raw.Count; k++) indexByOffset[raw[k].Offset] = k;
        var code = new List<IrInstruction>(raw.Count);
        foreach (var (offset, op, args) in raw)
        {
            var operands = new IrOperand[args.Length];
            for (int a = 0; a < args.Length; a++)
            {
                int type = args[a].Type;
                uint value = (uint)args[a].Value;
                bool isDataRef = type == StringType || (op == ArrayOpcode && a == 1);
                if (contracts.IsCodeTarget(op, a + 1) && type == 0 && value != NoTarget
                    && value < codeEnd && indexByOffset.TryGetValue((int)value, out int target))
                    operands[a] = IrOperand.Label(type, target);
                else if (isDataRef && value < dataEnd && itemByOffset.TryGetValue((int)value, out int item))
                    operands[a] = IrOperand.Data(type, item);
                else
                    operands[a] = IrOperand.Literal(type, value);
            }
            code.Add(new IrInstruction(op, operands, offset));
        }
        literalCodeValues = IrCodeValues.Resolve(code, indexByOffset, contracts, table.AbiId);

        // Tables: derived when they equal the canonical site lists, otherwise explicit.
        uint[] t1 = Slice(body, dataEnd, t1Count), t2 = Slice(body, t2Offset, t2Count), t3 = Slice(body, t3Offset, t3Count);
        bool derived = t1.SequenceEqual(Sites(raw, 0x71)) && t2.SequenceEqual(Sites(raw, 0x03))
                       && t3.SequenceEqual(Sites(raw, 0x8F));
        return new IrScript
        {
            Name = name,
            Magic = bytes[..8],
            LocalCounts = Enumerable.Range(0, IrScript.LocalBankCount).Select(k => Checked(fields[k])).ToArray(),
            Field6 = fields[6],
            Code = code,
            Data = data,
            Tables = derived ? IrTables.Derived : new IrTables(t1, t2, t3),
        };
    }

    private static IEnumerable<uint> Sites(List<(int Offset, int Opcode, Operand[] Args)> raw, int opcode)
        => raw.Where(entry => entry.Opcode == opcode).Select(entry => (uint)entry.Offset);

    private static uint[] Slice(uint[] body, int start, int count) => body.AsSpan(start, count).ToArray();

    private static int Checked(uint value)
        => value <= int.MaxValue ? (int)value : throw new InvalidDataException($"header field 0x{value:x} out of range");
}
