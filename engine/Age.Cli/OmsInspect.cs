using Age.Engine.Model;
using Age.Script.Abi;
using Age.Script.Ir;

/// <summary>Diagnostics for the OMS intermediate form: lists operands the decoder kept literal at code-target
/// positions and referenced data it could not type, so the residue of gate 1 can be explained.</summary>
internal static class OmsInspect
{
    public static void LiteralResidue(
        IEnumerable<(string Name, long PackedId, Func<byte[]> Read)> scripts,
        OpcodeTable table, OpcodeContracts contracts, int limit)
    {
        var byShape = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var samples = new List<string>();
        foreach (var (name, _, read) in scripts)
        {
            IrScript ir = IrDecoder.Decode(read(), table, contracts, name);
            var codeOffsets = ir.Code.Select(i => i.OriginalOffset ?? -1).ToHashSet();
            int codeLength = ir.Code.Sum(i => i.SizeInDwords);
            var referenced = new HashSet<uint>();
            foreach (IrInstruction instruction in ir.Code)
                for (int a = 0; a < instruction.Operands.Count; a++)
                {
                    IrOperand operand = instruction.Operands[a];
                    if (operand.Kind == IrOperandKind.DataRef) referenced.Add(operand.Value);
                    if (operand.Kind != IrOperandKind.Literal || operand.Value == 0xFFFFFFFF
                        || !contracts.IsCodeTarget(instruction.Opcode, a + 1))
                        continue;
                    string why = operand.Type != 0 ? $"type-0x{operand.Type:x}"
                        : operand.Value >= codeLength ? "past-code"
                        : codeOffsets.Contains((int)operand.Value) ? "?" : "mid-instruction";
                    string key = $"op 0x{instruction.Opcode:x} operand {a + 1}: {why}";
                    byShape[key] = byShape.GetValueOrDefault(key) + 1;
                    if (samples.Count < limit)
                        samples.Add($"{name}@0x{instruction.OriginalOffset:x} op 0x{instruction.Opcode:x} #{a + 1} "
                                    + $"type 0x{operand.Type:x} value 0x{operand.Value:x} (code length 0x{codeLength:x})");
                }
            for (int k = 0; k < ir.Data.Count; k++)
                if (ir.Data[k].Kind == IrDataKind.Raw && referenced.Contains((uint)k))
                    samples.Add($"{name}: referenced raw data item {k} at 0x{ir.Data[k].OriginalOffset:x}, "
                                + $"{ir.Data[k].SizeInDwords} dwords: "
                                + string.Join(" ", ir.Data[k].Dwords.Take(6).Select(d => $"{d:x8}")));
        }
        foreach (var (key, count) in byShape) Console.WriteLine($"{count,6}  {key}");
        foreach (string sample in samples) Console.WriteLine($"  {sample}");
    }
}
