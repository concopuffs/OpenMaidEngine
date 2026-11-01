using Age.Script.Compile;
using Age.Script.Ir;

namespace Age.Script.Patch;

/// <summary>
/// Inert plans for gate 7 (spec §12): they change no behavior, so a patched script must stay VM-trace-equivalent to
/// its base once the inserted steps are filtered out. A probe plan uses every operation kind:
/// <list type="bullet">
/// <item>a <c>note</c> before every transfer target and every <paramref name="stride"/>-th instruction;</item>
/// <item>a <c>note</c> after every call and every <paramref name="stride"/>-th instruction that falls through;</item>
/// <item>every <paramref name="stride"/>-th instruction (another phase) replaced by a copy of itself, whose code
/// targets become base sites and whose data becomes plan data;</item>
/// <item>a literal operand replaced by the same value at a further phase.</item>
/// </list>
/// </summary>
public static class OmsPatchProbe
{
    public const string NoteText = "oms patch probe";

    public static OmsPatchPlan Build(IrScript script, string fingerprint, string abi, int stride = 5)
    {
        if (stride < 4) throw new ArgumentOutOfRangeException(nameof(stride), "the probe phases need a stride of at least 4");
        List<IrInstruction> code = script.Code;
        int n = code.Count;
        var offsets = new int[n + 1];
        for (int k = 0; k < n; k++) offsets[k + 1] = offsets[k] + code[k].SizeInDwords;
        var targets = new HashSet<int>();
        foreach (IrInstruction instruction in code)
            foreach (IrOperand operand in instruction.Operands)
                if (operand.Kind == IrOperandKind.CodeLabel) targets.Add((int)operand.Value);

        var plan = new OmsPatchPlan { Script = script.Name, Fingerprint = fingerprint, Abi = abi };
        plan.Data.Add(OmsPatchDataItem.From(IrDataItem.FromText(NoteText)));
        OmsPatchInstruction note = new(OmsCompiler.Comment, [OmsPatchOperand.Data(2, 0)]);
        int NewBlock(List<OmsPatchInstruction> block)
        {
            plan.Blocks.Add(block);
            return plan.Blocks.Count - 1;
        }

        for (int k = 0; k < n; k++)
        {
            IrInstruction instruction = code[k];
            // A copy must be expressible: every code target a base instruction (not the end of the code).
            bool copyable = instruction.Operands.All(operand => operand.Kind != IrOperandKind.CodeLabel || operand.Value < n);
            if (k % stride == 3 && copyable)
            {
                var copy = instruction.Operands.Select(operand => operand.Kind switch
                {
                    IrOperandKind.CodeLabel => OmsPatchOperand.Site(operand.Type, offsets[operand.Value]),
                    IrOperandKind.DataRef => CopyData(plan, script.Data[(int)operand.Value], operand.Type),
                    _ => OmsPatchOperand.Literal(operand.Type, operand.Value),
                }).ToArray();
                plan.Operations.Add(new OmsPatchOperation
                {
                    Kind = OmsPatchOperationKind.Replace, Site = offsets[k], Block = NewBlock([new(instruction.Opcode, copy)]),
                });
                continue;
            }
            if (targets.Contains(k) || k % stride == 0)
                plan.Operations.Add(new OmsPatchOperation
                {
                    Kind = OmsPatchOperationKind.Before, Site = offsets[k], Block = NewBlock([note]),
                });
            int literal = instruction.Operands.ToList().FindIndex(operand => operand.Kind == IrOperandKind.Literal);
            if (k % stride == 1 && literal >= 0)
                plan.Operations.Add(new OmsPatchOperation
                {
                    Kind = OmsPatchOperationKind.Operand, Site = offsets[k], Operand = literal + 1,
                    Value = OmsPatchOperand.Literal(instruction.Operands[literal].Type, instruction.Operands[literal].Value),
                });
            bool call = instruction.Opcode is OmsCompiler.Call or OmsCompiler.CallScript;
            if ((call || k % stride == 2) && OmsPatchApplier.CanFallThrough(instruction))
                plan.Operations.Add(new OmsPatchOperation
                {
                    Kind = OmsPatchOperationKind.After, Site = offsets[k], Block = NewBlock([note]),
                });
        }
        return plan;
    }

    private static OmsPatchOperand CopyData(OmsPatchPlan plan, IrDataItem item, int type)
    {
        plan.Data.Add(OmsPatchDataItem.From(item));
        return OmsPatchOperand.Data(type, plan.Data.Count - 1);
    }
}
