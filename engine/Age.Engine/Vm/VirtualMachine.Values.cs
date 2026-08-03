using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepValue(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "add": Write(a[0], Read(a[1]) + Read(a[2])); return pc + 1;
            case "sub": Write(a[0], Read(a[1]) - Read(a[2])); return pc + 1;
            case "mul": Write(a[0], Read(a[1]) * Read(a[2])); return pc + 1;
            case "div": Write(a[0], PyDiv(Read(a[1]), Read(a[2]))); return pc + 1;
            case "mod": Write(a[0], PyMod(Read(a[1]), Read(a[2]))); return pc + 1;
            case "and": Write(a[0], Read(a[1]) & Read(a[2])); return pc + 1;
            case "or":  Write(a[0], Read(a[1]) | Read(a[2])); return pc + 1;
            case "sar": Write(a[0], Read(a[1]) >> (int)(Read(a[2]) & 31)); return pc + 1;
            case "shl": Write(a[0], Read(a[1]) << (int)(Read(a[2]) & 31)); return pc + 1;
            case "eq":  Write(a[0], Read(a[1]) == Read(a[2]) ? 1 : 0); return pc + 1;
            case "ne":  Write(a[0], Read(a[1]) != Read(a[2]) ? 1 : 0); return pc + 1;
            case "string-equals":
                Write(a[0], string.Equals(ReadStr(a[1]), ReadStr(a[2]), StringComparison.Ordinal) ? 1 : 0);
                return pc + 1;
            case "string-not-equals":
                Write(a[0], string.Equals(ReadStr(a[1]), ReadStr(a[2]), StringComparison.Ordinal) ? 0 : 1);
                return pc + 1;
            case "concat":
            {
                string left = ReadStr(a[1]);
                string right = ReadStr(a[2]);
                WriteStr(a[0], left + right);
                return pc + 1;
            }
            case "toString":
                WriteStr(a[0], unchecked((int)Read(a[1])).ToString(System.Globalization.CultureInfo.InvariantCulture));
                return pc + 1;
            case "absolute-value":
            {
                int value = unchecked((int)Read(a[1]));
                int sign = value >> 31;
                Write(a[0], unchecked((value ^ sign) - sign));
                return pc + 1;
            }
            case "lt":  Write(a[0], Read(a[1]) <  Read(a[2]) ? 1 : 0); return pc + 1;
            case "lte": Write(a[0], Read(a[1]) <= Read(a[2]) ? 1 : 0); return pc + 1;
            case "gr":  Write(a[0], Read(a[1]) >  Read(a[2]) ? 1 : 0); return pc + 1;
            case "gre": Write(a[0], Read(a[1]) >= Read(a[2]) ? 1 : 0); return pc + 1;
            case "mov":
            case "set-string":
                if (IsStr(a[0]) || IsStr(a[1])) WriteStr(a[0], ReadStr(a[1]));
                else Write(a[0], Read(a[1]));
                return pc + 1;
            case "halve-strlen": // 0x1a6: strlen(native encoded bytes) >> 1
                Write(a[0], NativeStringByteLength(ReadStr(a[1])) >> 1);
                return pc + 1;
            case "edit-fullwidth-string-dialog": // 0x144: blocking AGERc command-10 editor
            {
                string current = ReadStr(a[0]);
                string initial = ReadStr(a[1]);
                FullwidthTextEditResult result =
                    _host.EditFullwidthString(new(current, initial));
                if (result.Accepted) WriteStr(a[0], result.Text);
                return pc + 1;
            }
            case "cp932-character-length": // 0x2c6: Japanese-locale _mbstrlen
                Write(a[0], Cp932Text.CharacterLength(ReadStr(a[1]), _nativeStringEncoding));
                return pc + 1;
            case "cp932-substring": // 0x2c8: multibyte-character interval [start,start+count)
                WriteStr(a[0], Cp932Text.Substring(
                    ReadStr(a[1]),
                    unchecked((int)Read(a[2])),
                    unchecked((int)Read(a[3])),
                    _nativeStringEncoding));
                return pc + 1;
            default:
                throw new InvalidOperationException($"Non-value opcode routed to value handler: {label}");
        }
    }
}
