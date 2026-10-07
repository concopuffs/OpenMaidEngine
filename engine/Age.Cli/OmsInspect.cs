using Age.Engine.Model;
using Age.Script.Abi;
using Age.Script.Ir;
using Age.Script.Text;

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
    /// <summary>Allocator residue of the core level: folded statement shapes ranked by how often the original
    /// temporary slots differ from the canonical plan, with the slot patterns seen for each shape.</summary>
    public static void TemporaryResidue(
        IEnumerable<(string Name, long PackedId, Func<byte[]> Read)> scripts,
        OpcodeTable table, OpcodeContracts contracts, int limit, string? match, string? dumpDirectory)
    {
        var intrinsics = new IntrinsicCatalog(table, contracts);
        if (dumpDirectory != null) Directory.CreateDirectory(dumpDirectory);
        var shapes = new Dictionary<string, (int Total, int Canonical, Dictionary<string, int> Slots, string Sample)>();
        int total = 0, canonical = 0;
        foreach (var (name, _, read) in scripts)
        {
            IrScript ir = IrDecoder.Decode(read(), table, contracts, name);
            var dump = dumpDirectory != null ? new System.Text.StringBuilder() : null;
            OmsCoreDecompiler.Decompile(ir, table.AbiId, intrinsics, dump: dump, observe: record =>
            {
                total++;
                if (record.Canonical) canonical++;
                if (match != null && !record.Shape.Contains(match, StringComparison.Ordinal)) return;
                if (!shapes.TryGetValue(record.Shape, out var entry))
                    entry = (0, 0, new Dictionary<string, int>(StringComparer.Ordinal), $"{name}@{record.Site}");
                entry.Total++;
                if (record.Canonical) entry.Canonical++;
                entry.Slots[record.Slots] = entry.Slots.GetValueOrDefault(record.Slots) + 1;
                shapes[record.Shape] = entry;
            });
            if (dump != null)
                File.WriteAllText(Path.Combine(dumpDirectory!, Path.ChangeExtension(name, ".jsonl")), dump.ToString());
        }
        Console.WriteLine($"oms-temps: folded statements with temporaries={total} canonical={canonical} "
                          + $"overridden={total - canonical}");
        foreach (var (shape, entry) in shapes.OrderByDescending(pair => pair.Value.Total - pair.Value.Canonical).Take(limit))
        {
            Console.WriteLine($"{entry.Total - entry.Canonical,7} of {entry.Total,7}  {shape}   e.g. {entry.Sample}");
            foreach (var (slots, count) in entry.Slots.OrderByDescending(pair => pair.Value).Take(6))
                Console.WriteLine($"{count,17}  {slots}");
        }
    }
}
