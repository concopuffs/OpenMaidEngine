using Age.Script.Abi;

namespace Age.Script.Ir;

/// <summary>
/// Turns immediates that data flow proves to be code positions into labels, so they relocate with the code instead of
/// surviving as numbers that only the original layout makes true (spec §9.5). A code value comes from an operand the
/// registry marks <c>value = "code-address"</c> (op 0x140's returned PC). Within a basic block, an immediate compared
/// with a code value by <c>eq</c>/<c>ne</c>, directly or through a cell a <c>mov</c> loaded, is a code position too.
/// Every ADV scene's loop exit test is this shape (<c>G[0x6c3] = &amp;loop_head; if G[0x6c3] == G[0x6be]</c>).
/// </summary>
public static class IrCodeValues
{
    private const int Mov = 0x55, Eq = 0x5A, Ne = 0x5B, Call = 0x8F, CallScript = 0x03;
    private const int ThrowExit = 0x01, Exit = 0x02, Ret = 0x05, Resume = 0x7C;

    /// <summary>What a cell holds at a point in a block: a code value, or the immediate a <c>mov</c> loaded.</summary>
    private readonly record struct Fact(bool Code, int Instruction, int Operand);

    /// <summary>Relabels the proven code-position immediates in place. Returns how many proven code values are not
    /// an instruction start and stay literal.</summary>
    public static int Resolve(List<IrInstruction> code, IReadOnlyDictionary<int, int> indexByOffset,
                              OpcodeContracts contracts, string abiId)
    {
        var leaders = new bool[code.Count + 1];
        for (int k = 0; k < code.Count; k++)
            foreach (IrOperand operand in code[k].Operands)
                if (operand.Kind == IrOperandKind.CodeLabel) leaders[(int)operand.Value] = true;

        var marked = new HashSet<(int Instruction, int Operand)>();
        var state = new Dictionary<(int Type, uint Index), Fact>();
        for (int k = 0; k < code.Count; k++)
        {
            if (leaders[k]) state.Clear();
            IrInstruction instruction = code[k];
            IReadOnlyList<IrOperand> operands = instruction.Operands;
            switch (instruction.Opcode)
            {
                case Mov when operands.Count == 2 && Cell(operands[0]) is { } target:
                    if (operands[1] is { Kind: IrOperandKind.Literal, Type: 0 })
                        state[target] = new Fact(false, k, 1);
                    else if (Cell(operands[1]) is { } source && state.TryGetValue(source, out Fact copied))
                        state[target] = copied;
                    else
                        state.Remove(target);
                    break;
                case Eq or Ne when operands.Count == 3:
                    for (int side = 1; side <= 2; side++)
                    {
                        IrOperand value = operands[side], other = operands[3 - side];
                        if (Cell(value) is not { } codeCell || !state.TryGetValue(codeCell, out Fact fact) || !fact.Code)
                            continue;
                        if (other is { Kind: IrOperandKind.Literal, Type: 0 })
                            marked.Add((k, 3 - side));
                        else if (Cell(other) is { } otherCell && state.TryGetValue(otherCell, out Fact loaded) && !loaded.Code)
                            marked.Add((loaded.Instruction, loaded.Operand));
                    }
                    if (Cell(operands[0]) is { } result) state.Remove(result);
                    else state.Clear();
                    break;
                default:
                    if (!Apply(instruction, contracts, abiId, state)) state.Clear();
                    break;
            }
            // Control may leave, and code elsewhere may write any cell before control comes back.
            if (instruction.Opcode is Call or CallScript or ThrowExit or Exit or Ret or Resume
                || operands.Any(operand => operand.Kind == IrOperandKind.CodeLabel))
            {
                state.Clear();
                leaders[k + 1] = true;
            }
        }

        int literal = 0;
        foreach (var (k, p) in marked)
        {
            IrOperand operand = code[k].Operands[p];
            if (operand.Kind != IrOperandKind.Literal) continue;
            if (!indexByOffset.TryGetValue((int)operand.Value, out int target))
            {
                literal++;
                continue;
            }
            var relabeled = code[k].Operands.ToArray();
            relabeled[p] = IrOperand.Label(operand.Type, target);
            code[k] = code[k] with { Operands = relabeled };
        }
        return literal;
    }

    /// <summary>A storage cell operand (global or local, any value bank) as a state key; references and immediates
    /// are not cells.</summary>
    private static (int, uint)? Cell(IrOperand operand)
        => operand is { Kind: IrOperandKind.Literal, Type: 0x3 or 0x4 or 0x5 or 0x9 or 0xA or 0xB }
            ? (operand.Type, operand.Value) : null;

    /// <summary>Applies an instruction's writes to the state from its access contract: a code-address operand makes
    /// its cell a code value, any other write forgets the cell. False when the writes are unknown (no contract, or a
    /// write through a reference that may alias any cell).</summary>
    private static bool Apply(IrInstruction instruction, OpcodeContracts contracts, string abiId,
                              Dictionary<(int, uint), Fact> state)
    {
        for (int p = 0; p < instruction.Operands.Count; p++)
        {
            IReadOnlyList<string>? access = contracts.Access(instruction.Opcode, p + 1, abiId);
            if (access == null) return false;
            if (!access.Any(kind => kind.StartsWith("write", StringComparison.Ordinal) || kind == "bind")) continue;
            IrOperand operand = instruction.Operands[p];
            if (Cell(operand) is not { } cell) return false;
            if (contracts.IsCodeValue(instruction.Opcode, p + 1)) state[cell] = new Fact(true, -1, -1);
            else state.Remove(cell);
        }
        return true;
    }
}
