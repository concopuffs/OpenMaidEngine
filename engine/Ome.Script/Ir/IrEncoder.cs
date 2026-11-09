namespace Ome.Script.Ir;

/// <summary>
/// Encodes the intermediate form into SYS4 script bytes: code in order, data items after it, then T1/T2/T3.
/// Labels and data references resolve to the laid-out offsets; header F7..F12 follow from the layout.
/// </summary>
public static class IrEncoder
{
    public static byte[] Encode(IrScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Magic.Length != 8)
            throw new ArgumentException("magic must be eight bytes", nameof(script));
        if (script.LocalCounts.Length != IrScript.LocalBankCount)
            throw new ArgumentException($"expected {IrScript.LocalBankCount} local counts", nameof(script));

        var instructionOffsets = new int[script.Code.Count + 1];
        for (int k = 0; k < script.Code.Count; k++)
            instructionOffsets[k + 1] = instructionOffsets[k] + script.Code[k].SizeInDwords;
        int codeLength = instructionOffsets[^1];
        var dataOffsets = new int[script.Data.Count + 1];
        dataOffsets[0] = codeLength;
        for (int k = 0; k < script.Data.Count; k++)
            dataOffsets[k + 1] = dataOffsets[k] + script.Data[k].SizeInDwords;
        int dataEnd = dataOffsets[^1];

        var body = new List<uint>(dataEnd + 64);
        foreach (IrInstruction instruction in script.Code)
        {
            body.Add((uint)instruction.Opcode);
            foreach (IrOperand operand in instruction.Operands)
            {
                body.Add((uint)operand.Type);
                body.Add(operand.Kind switch
                {
                    IrOperandKind.Literal => operand.Value,
                    IrOperandKind.CodeLabel => operand.Value <= script.Code.Count
                        ? (uint)instructionOffsets[operand.Value]
                        : throw new InvalidOperationException($"label {operand.Value} is past the end of the code"),
                    IrOperandKind.DataRef => operand.Value < script.Data.Count
                        ? (uint)dataOffsets[operand.Value]
                        : throw new InvalidOperationException($"data item {operand.Value} does not exist"),
                    IrOperandKind.Resource => throw new InvalidDataException(
                        $"{script.Name}: resource reference "
                        + (operand.Value < script.Resources.Count ? $"'{script.Resources[(int)operand.Value]}'" : $"#{operand.Value}")
                        + " is unresolved; link the mod before encoding"),
                    _ => throw new InvalidOperationException($"unknown operand kind {operand.Kind}"),
                });
            }
        }
        foreach (IrDataItem item in script.Data) body.AddRange(item.Dwords);

        IReadOnlyList<uint> t1 = script.Tables.ReadMessageSites ?? Sites(script, instructionOffsets, 0x71);
        IReadOnlyList<uint> t2 = script.Tables.ScriptCallSites ?? Sites(script, instructionOffsets, 0x03);
        IReadOnlyList<uint> t3 = script.Tables.LocalCallSites ?? Sites(script, instructionOffsets, 0x8F);
        body.AddRange(t1);
        body.AddRange(t2);
        body.AddRange(t3);

        var fields = new uint[13];
        for (int k = 0; k < IrScript.LocalBankCount; k++) fields[k] = (uint)script.LocalCounts[k];
        fields[6] = script.Field6;
        fields[7] = (uint)t1.Count;
        fields[8] = (uint)dataEnd;
        fields[9] = (uint)t2.Count;
        fields[10] = (uint)(dataEnd + t1.Count);
        fields[11] = (uint)t3.Count;
        fields[12] = (uint)(dataEnd + t1.Count + t2.Count);

        var bytes = new byte[IrScript.HeaderSize + body.Count * 4];
        script.Magic.CopyTo(bytes, 0);
        for (int k = 0; k < 13; k++) BitConverter.GetBytes(fields[k]).CopyTo(bytes, 8 + k * 4);
        for (int k = 0; k < body.Count; k++) BitConverter.GetBytes(body[k]).CopyTo(bytes, IrScript.HeaderSize + k * 4);
        return bytes;
    }

    private static uint[] Sites(IrScript script, int[] offsets, int opcode)
    {
        var sites = new List<uint>();
        for (int k = 0; k < script.Code.Count; k++)
            if (script.Code[k].Opcode == opcode) sites.Add((uint)offsets[k]);
        return sites.ToArray();
    }
}
