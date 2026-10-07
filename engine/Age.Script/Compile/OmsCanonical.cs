using Age.Script.Ir;
using Age.Script.Syntax;

namespace Age.Script.Compile;

/// <summary>The canonical layout rules shared by the compiler and the decompiler (spec §7.3, §7.5).</summary>
public static class OmsCanonical
{
    public const uint Field6 = 0x1C;

    /// <summary>The script magic implied by an engine ABI id: "SYS4422" → "SYS4422 ".</summary>
    public static byte[] MagicFor(string abiId)
    {
        string text = abiId.Length >= 8 ? abiId[..8] : abiId.PadRight(8);
        return System.Text.Encoding.ASCII.GetBytes(text);
    }

    /// <summary>Header F0..F5: highest slot used + 2 per local bank, or 1 when the bank is unused.</summary>
    public static int[] LocalCounts(IEnumerable<IrInstruction> code)
    {
        var highest = new long[StorageBanks.LocalBankTypes.Length];
        Array.Fill(highest, -1);
        foreach (IrInstruction instruction in code)
            foreach (IrOperand operand in instruction.Operands)
            {
                if (operand.Kind != IrOperandKind.Literal) continue;
                int bank = Array.IndexOf(StorageBanks.LocalBankTypes, operand.Type);
                if (bank >= 0) highest[bank] = Math.Max(highest[bank], operand.Value);
            }
        return highest.Select(h => h < 0 ? 1 : checked((int)h + 2)).ToArray();
    }
}
