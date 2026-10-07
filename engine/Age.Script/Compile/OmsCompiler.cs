using Age.Engine.Model;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Syntax;

namespace Age.Script.Compile;

public sealed class OmsCompileException(SourceSpan span, string message) : Exception($"{span}: {message}")
{
    public SourceSpan Span { get; } = span;
}

/// <summary>
/// Compiles a parsed script unit to the intermediate form. Data items are created per data-operand occurrence
/// in code order (spec §7.5); an optional layout sidecar restores any non-canonical original layout.
/// </summary>
public static class OmsCompiler
{
    private sealed record Occurrence(IrDataItem Item, SourceSpan Span);

    public static IrScript Compile(ScriptUnit unit, string abiId, OpcodeTable? table = null, OmsLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        int count = 0;
        foreach (Statement statement in unit.Body)
            switch (statement)
            {
                case LabelStatement label:
                    if (!labels.TryAdd(label.Name, count))
                        throw new OmsCompileException(label.Span, $"label '{label.Name}' is defined twice");
                    break;
                case RawOpStatement:
                    count++;
                    break;
                default:
                    throw new OmsCompileException(statement.Span, $"{statement.GetType().Name} is not supported yet");
            }

        var code = new List<IrInstruction>(count);
        var occurrences = new List<Occurrence>();
        foreach (RawOpStatement op in unit.Body.OfType<RawOpStatement>())
        {
            if (table != null)
            {
                int argc = table.Argc(op.Opcode);
                if (argc < 0)
                    throw new OmsCompileException(op.Span, $"opcode 0x{op.Opcode:x} is not in ABI {table.AbiId}");
                if (argc != op.Operands.Count)
                    throw new OmsCompileException(op.Span,
                        $"opcode 0x{op.Opcode:x} takes {argc} operands, not {op.Operands.Count}");
            }
            code.Add(new IrInstruction(op.Opcode, op.Operands.Select(o => Operand(o, labels, occurrences)).ToArray()));
        }

        // Data: canonical is one item per occurrence in code order; the sidecar may reorder, share or add raw slots.
        var slotOfOccurrence = new int[occurrences.Count];
        var data = new List<IrDataItem>();
        IrDataItem Content(int occurrence)
            => layout?.ItemDwords != null && layout.ItemDwords.TryGetValue(occurrence, out uint[]? exact)
                ? occurrences[occurrence].Item with { Dwords = exact }
                : occurrences[occurrence].Item;
        if (layout?.Data == null)
        {
            for (int k = 0; k < occurrences.Count; k++)
            {
                slotOfOccurrence[k] = k;
                data.Add(Content(k));
            }
        }
        else
        {
            var seen = new bool[occurrences.Count];
            foreach (OmsDataSlot slot in layout.Data)
            {
                if (slot.Refs is { Length: > 0 } refs)
                {
                    foreach (int r in refs)
                    {
                        if (r < 0 || r >= occurrences.Count || seen[r])
                            throw new InvalidDataException($"layout data slot names invalid or repeated occurrence {r}");
                        seen[r] = true;
                        slotOfOccurrence[r] = data.Count;
                    }
                    data.Add(Content(refs[0]));
                }
                else if (slot.Raw != null)
                    data.Add(new IrDataItem(IrDataKind.Raw, slot.Raw));
                else
                    throw new InvalidDataException("layout data slot has neither refs nor raw dwords");
            }
            int missing = Array.IndexOf(seen, false);
            if (missing >= 0)
                throw new InvalidDataException($"layout data omits occurrence {missing}");
        }
        for (int k = 0; k < code.Count; k++)
        {
            IrInstruction instruction = code[k];
            if (!instruction.Operands.Any(o => o.Kind == IrOperandKind.DataRef)) continue;
            code[k] = instruction with
            {
                Operands = instruction.Operands.Select(o => o.Kind == IrOperandKind.DataRef
                    ? IrOperand.Data(o.Type, slotOfOccurrence[o.Value]) : o).ToArray(),
            };
        }

        uint[][]? tables = layout?.Tables;
        return new IrScript
        {
            Name = unit.Name,
            Magic = layout?.Magic != null ? System.Text.Encoding.ASCII.GetBytes(layout.Magic) : OmsCanonical.MagicFor(abiId),
            LocalCounts = layout?.LocalCounts ?? OmsCanonical.LocalCounts(code),
            Field6 = layout?.Field6 ?? OmsCanonical.Field6,
            Code = code,
            Data = data,
            Tables = tables == null ? IrTables.Derived : new IrTables(tables[0], tables[1], tables[2]),
        };
    }

    private static IrOperand Operand(RawOperand operand, Dictionary<string, int> labels, List<Occurrence> occurrences)
    {
        switch (operand)
        {
            case RawInteger integer:
                return IrOperand.Literal(0, integer.Value);
            case RawFloat number:
                return IrOperand.Literal(1, BitConverter.SingleToUInt32Bits(number.Value));
            case RawStorage storage:
                return IrOperand.Literal(StorageBanks.TypeByBank[storage.Bank], storage.Index);
            case RawLabelRef label:
                return labels.TryGetValue(label.Name, out int target)
                    ? IrOperand.Label(0, target)
                    : throw new OmsCompileException(label.Span, $"unknown label '{label.Name}'");
            case RawString text:
                occurrences.Add(new Occurrence(EncodeText(text), text.Span));
                return IrOperand.Data(2, occurrences.Count - 1);
            case RawArray array:
                occurrences.Add(new Occurrence(IrDataItem.FromInts(array.Values), array.Span));
                return IrOperand.Data(0, occurrences.Count - 1);
            case RawDataBlock block:
                occurrences.Add(new Occurrence(new IrDataItem(IrDataKind.Raw, block.Dwords.ToArray()), block.Span));
                return IrOperand.Data(0, occurrences.Count - 1);
            case RawTyped typed:
                IrOperand inner = Operand(typed.Inner, labels, occurrences);
                return inner with { Type = typed.Type };
            default:
                throw new OmsCompileException(operand.Span, $"unsupported operand {operand.GetType().Name}");
        }
    }

    private static IrDataItem EncodeText(RawString text)
    {
        try
        {
            return IrDataItem.FromText(text.Text);
        }
        catch (System.Text.EncoderFallbackException error)
        {
            throw new OmsCompileException(text.Span, $"text is not encodable in CP932: {error.Message}");
        }
    }
}
