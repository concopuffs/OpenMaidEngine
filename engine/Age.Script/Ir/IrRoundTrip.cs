using Age.Engine.Model;
using Age.Script.Abi;

namespace Age.Script.Ir;

public sealed record IrRoundTripFailure(string Name, long PackedId, string Error);

/// <summary>Gate-1 result: decode every script to the intermediate form, re-encode it and compare bytes.</summary>
public sealed record IrRoundTripReport(
    int Scripts,
    int Identical,
    long Instructions,
    long LabelOperands,
    long LiteralCodeTargets,
    long DataRefOperands,
    IReadOnlyDictionary<string, long> DataItems,
    long ReferencedRawItems,
    long UnreferencedRawDwords,
    int ExplicitTableScripts,
    IReadOnlyList<IrRoundTripFailure> Failures)
{
    public bool Passed => Failures.Count == 0 && Identical == Scripts;
}

public static class IrRoundTrip
{
    public static IrRoundTripReport Run(
        IEnumerable<(string Name, long PackedId, Func<byte[]> Read)> scripts,
        OpcodeTable table,
        OpcodeContracts contracts)
    {
        int count = 0, identical = 0, explicitTables = 0;
        long instructions = 0, labels = 0, literalTargets = 0, dataRefs = 0, referencedRaw = 0, unreferencedRaw = 0;
        var items = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var failures = new List<IrRoundTripFailure>();
        foreach (var (name, packedId, read) in scripts)
        {
            count++;
            try
            {
                byte[] original = read();
                IrScript ir = IrDecoder.Decode(original, table, contracts, name);
                byte[] encoded = IrEncoder.Encode(ir);
                if (encoded.AsSpan().SequenceEqual(original)) identical++;
                else failures.Add(new IrRoundTripFailure(name, packedId, FirstDifference(original, encoded)));

                instructions += ir.Code.Count;
                var referenced = new HashSet<uint>();
                foreach (IrInstruction instruction in ir.Code)
                    for (int a = 0; a < instruction.Operands.Count; a++)
                    {
                        IrOperand operand = instruction.Operands[a];
                        if (operand.Kind == IrOperandKind.CodeLabel) labels++;
                        else if (operand.Kind == IrOperandKind.DataRef) { dataRefs++; referenced.Add(operand.Value); }
                        else if (contracts.IsCodeTarget(instruction.Opcode, a + 1) && operand.Value != 0xFFFFFFFF)
                            literalTargets++;
                    }
                for (int k = 0; k < ir.Data.Count; k++)
                {
                    IrDataItem item = ir.Data[k];
                    items[item.Kind.ToString()] = items.GetValueOrDefault(item.Kind.ToString()) + 1;
                    if (item.Kind != IrDataKind.Raw) continue;
                    if (referenced.Contains((uint)k)) referencedRaw++;
                    else unreferencedRaw += item.SizeInDwords;
                }
                if (!ir.Tables.IsDerived) explicitTables++;
            }
            catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException)
            {
                failures.Add(new IrRoundTripFailure(name, packedId, error.Message));
            }
        }
        return new IrRoundTripReport(count, identical, instructions, labels, literalTargets, dataRefs, items,
            referencedRaw, unreferencedRaw, explicitTables, failures);
    }

    private static string FirstDifference(byte[] original, byte[] encoded)
    {
        int limit = Math.Min(original.Length, encoded.Length);
        for (int k = 0; k < limit; k++)
            if (original[k] != encoded[k])
                return $"first difference at byte 0x{k:x} (original {original.Length} bytes, encoded {encoded.Length})";
        return $"length differs: original {original.Length} bytes, encoded {encoded.Length}";
    }
}
