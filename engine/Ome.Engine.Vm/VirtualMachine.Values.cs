using Ome.Engine.Hosting;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    /// <summary>A native integer cell's value: the low 32 bits, sign-extended. Native arithmetic wraps at 32 bits and
    /// compares signed 32-bit values; the port's cells are wider, and an immediate or a loaded save can hold either
    /// extension of the same 32 bits, so arithmetic results and compare operands go through this.</summary>
    private static long I32(long value) => unchecked((int)value);

    private StepResult OpAdd(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1]) + Read(a[2]))); return StepResult.Next;
    }

    private StepResult OpSub(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1]) - Read(a[2]))); return StepResult.Next;
    }

    private StepResult OpMul(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1]) * Read(a[2]))); return StepResult.Next;
    }

    private StepResult OpDiv(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(NativeDiv(I32(Read(a[1])), I32(Read(a[2]))))); return StepResult.Next;
    }

    private StepResult OpMod(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(NativeMod(I32(Read(a[1])), I32(Read(a[2]))))); return StepResult.Next;
    }

    private StepResult OpAnd(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1]) & Read(a[2]))); return StepResult.Next;
    }

    private StepResult OpOr(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1]) | Read(a[2]))); return StepResult.Next;
    }

    private StepResult OpSar(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1])) >> (int)(Read(a[2]) & 31)); return StepResult.Next;
    }

    private StepResult OpShl(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1]) << (int)(Read(a[2]) & 31))); return StepResult.Next;
    }

    private StepResult OpEq(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1])) == I32(Read(a[2])) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpNe(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1])) != I32(Read(a[2])) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpStringEquals(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], string.Equals(ReadStr(a[1]), ReadStr(a[2]), StringComparison.Ordinal) ? 1 : 0);
        return StepResult.Next;
    }

    private StepResult OpStringNotEquals(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], string.Equals(ReadStr(a[1]), ReadStr(a[2]), StringComparison.Ordinal) ? 0 : 1);
        return StepResult.Next;
    }

    private StepResult OpConcat(Instruction ins, int pc)
    {
        var a = ins.Args;
        string left = ReadStr(a[1]);
        string right = ReadStr(a[2]);
        WriteStr(a[0], left + right);
        RecordStringStore(a[0], pc);
        return StepResult.Next;
    }

    private StepResult OpToString(Instruction ins, int pc)
    {
        var a = ins.Args;
        WriteStr(a[0], unchecked((int)Read(a[1])).ToString(System.Globalization.CultureInfo.InvariantCulture));
        RecordStringStore(a[0], pc);
        return StepResult.Next;
    }

    private StepResult OpAbsoluteValue(Instruction ins, int pc)
    {
        var a = ins.Args;
        int value = unchecked((int)Read(a[1]));
        int sign = value >> 31;
        Write(a[0], unchecked((value ^ sign) - sign));
        return StepResult.Next;
    }

    private StepResult OpLt(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1])) <  I32(Read(a[2])) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpLte(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1])) <= I32(Read(a[2])) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpGr(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1])) >  I32(Read(a[2])) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpGre(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], I32(Read(a[1])) >= I32(Read(a[2])) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpMov(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (IsStr(a[0]) || IsStr(a[1]))
        {
            WriteStr(a[0], ReadStr(a[1]));
            RecordStringStore(a[0], pc, copiedFrom: a[1]);
        }
        else Write(a[0], Read(a[1]));
        return StepResult.Next;
    }

    // 0x1a6: strlen(native encoded bytes) >> 1
    private StepResult OpHalfByteStringLength(Instruction ins, int pc)
    {
        var a = ins.Args;
        string text = ReadStr(a[1]);
        Write(a[0], PresentedLength(a[1], text, pc, TextLengthKind.Halved)
                    ?? NativeStringByteLength(text) >> 1);
        return StepResult.Next;
    }

    // 0x144: blocking AGERc command-10 editor
    private StepResult OpEditFullwidthStringDialog(Instruction ins, int pc)
    {
        var a = ins.Args;
        string current = ReadStr(a[0]);
        string initial = ReadStr(a[1]);
        FullwidthTextEditResult result =
            _host.EditFullwidthString(new(current, initial));
        if (result.Accepted) WriteStr(a[0], result.Text);
        return StepResult.Next;
    }

    // 0x2c6: Japanese-locale _mbstrlen
    private StepResult OpCp932CharacterLength(Instruction ins, int pc)
    {
        var a = ins.Args;
        string text = ReadStr(a[1]);
        Write(a[0], PresentedLength(a[1], text, pc, TextLengthKind.Characters)
                    ?? Cp932Text.CharacterLength(text, _memory.Encoding));
        return StepResult.Next;
    }

    // 0x2c8: multibyte-character interval [start,start+count)
    private StepResult OpCp932Substring(Instruction ins, int pc)
    {
        var a = ins.Args;
        WriteStr(a[0], Cp932Text.Substring(
            ReadStr(a[1]),
            unchecked((int)Read(a[2])),
            unchecked((int)Read(a[3])),
            _memory.Encoding));
        return StepResult.Next;
    }
}
