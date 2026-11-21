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

    // 0x2d0-0x2d3, 0x2d5, 0x2d7: float arithmetic (AGE 4.33 and later; 創刻のアテリアル's scripts are the first to use
    // it). Native fetches each operand through the float accessor, which leaves an integer exact, and computes on an
    // x87 that Direct3D set to single precision, so each result is rounded to a float once (SinglePrecision).
    private StepResult OpAddFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        WriteFloat(a[0], SinglePrecision.Add(ReadFloatExact(a[1]), ReadFloatExact(a[2]))); return StepResult.Next;
    }

    private StepResult OpSubFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        WriteFloat(a[0], SinglePrecision.Subtract(ReadFloatExact(a[1]), ReadFloatExact(a[2]))); return StepResult.Next;
    }

    private StepResult OpMulFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        WriteFloat(a[0], SinglePrecision.Multiply(ReadFloatExact(a[1]), ReadFloatExact(a[2]))); return StepResult.Next;
    }

    // Native does not test the divisor: a zero gives the IEEE infinity or NaN.
    private StepResult OpDivFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        WriteFloat(a[0], SinglePrecision.Divide(ReadFloatExact(a[1]), ReadFloatExact(a[2]))); return StepResult.Next;
    }

    private StepResult OpMovFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        WriteFloat(a[0], (float)ReadFloatExact(a[1])); return StepResult.Next;
    }

    // __ftol2_sse on the fetched float, then the integer accessor's store.
    private StepResult OpFloatToInt(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], NativeTruncate(ReadFloatExact(a[1]))); return StepResult.Next;
    }

    // 0x2df to 0x2e4 (AGE 4.38D): (out)(a)(b), 1 when the two floats compare so and 0 otherwise. Both come through
    // the float accessor. A NaN is equal to nothing and neither less nor greater.
    private StepResult OpEqFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], ReadFloatExact(a[1]) == ReadFloatExact(a[2]) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpNeFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], ReadFloatExact(a[1]) != ReadFloatExact(a[2]) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpLtFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], ReadFloatExact(a[1]) < ReadFloatExact(a[2]) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpLteFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], ReadFloatExact(a[1]) <= ReadFloatExact(a[2]) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpGrFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], ReadFloatExact(a[1]) > ReadFloatExact(a[2]) ? 1 : 0); return StepResult.Next;
    }

    private StepResult OpGreFloat(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], ReadFloatExact(a[1]) >= ReadFloatExact(a[2]) ? 1 : 0); return StepResult.Next;
    }

    // 0x1cd: (out)(x), the C runtime's sqrt of x; a negative x gives a NaN. Each operand goes through the accessor
    // its ABI's contract names.
    private StepResult OpSqrt(Instruction ins, int pc)
    {
        WriteNumber(ins, 0, Math.Sqrt(ReadNumber(ins, 1))); return StepResult.Next;
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
            _waits.EditFullwidthString(new(current, initial));
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

    // 0x2c7, native's SBSubstr (AGE 4.38D): a byte interval of the string, kept from splitting a double-byte
    // character; native traces a warning when it moves an end. Older builds take the bytes plainly
    // (TextRules.SubstringKeepsDoubleByteCharacters).
    private StepResult OpByteSubstring(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (!_rules.Text.SubstringKeepsDoubleByteCharacters) return OpPlainByteSubstring(ins);
        string result = Cp932Text.ByteSubstring(
            ReadStr(a[1]), unchecked((int)Read(a[2])), unchecked((int)Read(a[3])), _memory.Encoding,
            out bool startMoved, out bool endMoved);
        if (startMoved)
            _waits.ReportWarning($"{At(ins)}: the substring starts inside a double-byte character; its first byte is "
                                 + "dropped, as AGE drops it with the trace 「関数：SBSubstr 警告：文字列が全角文字の途中から"
                                 + "切り出されるため、先頭バイトを切り捨てました。」");
        if (endMoved)
            _waits.ReportWarning($"{At(ins)}: the substring ends inside a double-byte character; its last byte is "
                                 + "dropped, as AGE drops it with the trace 「関数：SBSubstr 警告：切り出した文字列が全角文字の"
                                 + "途中で終了するため、最終バイトを切り捨てました。」");
        WriteStr(a[0], result);
        return StepResult.Next;
    }

    // 0x2c7 in AGE 4.24J and 4.33G: std::string's substr of the bytes. A start past the end throws out_of_range, which
    // neither the main tick's catches (AGE's own five exception types) nor the window's catch-all (around a command's
    // delete) take, so the program ends; the run halts there.
    private StepResult OpPlainByteSubstring(Instruction ins)
    {
        var a = ins.Args;
        string source = ReadStr(a[1]);
        int start = unchecked((int)Read(a[2]));
        string? result = Cp932Text.PlainByteSubstring(source, start, unchecked((int)Read(a[3])), _memory.Encoding);
        if (result is null)
        {
            HaltReason ??= $"string-position: {At(ins)}: the substring starts at byte {unchecked((uint)start)}, past the "
                           + "string's end, where AGE's std::string throws out_of_range (invalid string position) and "
                           + "the program ends";
            return StepResult.Halt;
        }
        WriteStr(a[0], result);
        return StepResult.Next;
    }

    // 0x2eb (AGE 4.38D): the game's version text, set:GameVersion
    private StepResult OpGetGameVersion(Instruction ins, int pc)
    {
        WriteStr(ins.Args[0], _o.GameVersion); return StepResult.Next;
    }

    // 0x2e9 (AGE 4.38D): the characters Auto's per-character delay leaves out of the page's count
    private StepResult OpSetAutoMessageCountOffset(Instruction ins, int pc)
    {
        _skip.SetAutoMessageCountOffset(unchecked((int)Read(ins.Args[0]))); return StepResult.Next;
    }

    // 0x2e6 (AGE 4.38D): (selector 0=after a voice, 1=unvoiced, out) Auto's per-character delay
    private StepResult OpGetAutoMessagePitch(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (_skip.AutoMessagePitch(unchecked((int)Read(a[0]))) is { } pitch) Write(a[1], pitch);
        else ReportAutoPitchSelector(ins);
        return StepResult.Next;
    }

    // 0x2e7 (AGE 4.38D): (selector, milliseconds)
    private StepResult OpSetAutoMessagePitch(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (!_skip.SetAutoMessagePitch(unchecked((int)Read(a[0])), unchecked((int)Read(a[1]))))
            ReportAutoPitchSelector(ins);
        return StepResult.Next;
    }

    // Both handlers trace the same message for a selector that is neither 0 nor 1, and do nothing else.
    private void ReportAutoPitchSelector(Instruction ins)
        => _waits.ReportWarning($"{At(ins)}: the Auto pitch selector is neither 0 nor 1; nothing is read or written, "
                                + "as AGE does with the trace 「GetAutoMesPiの引数が不正です．」");

    // 0x2e8 (AGE 4.38D): (value) message:AutoMessageOption
    private StepResult OpSetAutoMessageOption(Instruction ins, int pc)
    {
        _skip.AutoMessageOption = unchecked((int)Read(ins.Args[0])); return StepResult.Next;
    }

    // 0x2ea (AGE 4.38D): (out) message:AutoMessageOption
    private StepResult OpGetAutoMessageOption(Instruction ins, int pc)
    {
        Write(ins.Args[0], _skip.AutoMessageOption); return StepResult.Next;
    }

    // 0x2ec (AGE 4.38D): a string as an integer, by atol
    private StepResult OpStringToInt(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], NativeAtol(ReadStr(a[1]))); return StepResult.Next;
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
