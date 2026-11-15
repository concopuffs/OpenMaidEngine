using System.Text;
using Lua;
using Lua.Runtime;

namespace Ome.Scripting.Lua;

/// <summary>
/// Source text as Lua-CSharp is given it. Lua-CSharp 0.5.8's lexer refuses a hexadecimal integer numeral that an
/// operator or bracket follows directly: <c>t[0x100]</c>, <c>0x10+1</c> and <c>0x10==16</c> fail with "malformed
/// number", while <c>t[ 0x100 ]</c>, <c>(0x100)</c>, <c>{0x100}</c>, decimal numerals and hex numerals with a fraction
/// or an exponent read as Lua reads them (plan §4.1). Every chunk therefore gets a space after such a numeral before it
/// is parsed or loaded; strings, comments and identifiers are left as they are, and so is a numeral a letter, digit or
/// point follows (Lua reads that as one malformed numeral, and so should we). Lines do not move, so an error's line is
/// the mod's. The lexing helpers serve <see cref="LuaSourceScan"/> too.
/// </summary>
internal static class LuaSource
{
    /// <summary>Load a chunk (compiles it, runs nothing).</summary>
    public static LuaClosure Load(LuaState state, string text, string chunkName, LuaTable environment)
        => state.Load(Prepare(text).AsSpan(), chunkName, environment);

    /// <summary>The text with a space after each hex integer numeral an operator or bracket follows directly.</summary>
    public static string Prepare(string text)
    {
        StringBuilder? output = null;
        int copied = 0;
        int i = 0, n = text.Length;

        while (i < n)
        {
            char c = text[i];
            if (c == '-' && i + 1 < n && text[i + 1] == '-')
            {
                i += 2;
                int level = LongBracketLevel(text, i);
                i = level >= 0 ? SkipLongBracket(text, i, level) : SkipToLineEnd(text, i);
            }
            else if (c == '[' && LongBracketLevel(text, i) is var level and >= 0) i = SkipLongBracket(text, i, level);
            else if (c is '"' or '\'') i = SkipShortString(text, i);
            else if (char.IsAsciiLetter(c) || c == '_')
                while (i < n && IsNameChar(text[i])) i++;
            else if (c == '.' && i + 1 < n && text[i + 1] == '.')
                while (i < n && text[i] == '.') i++;   // .. and ...: a numeral after them stands alone
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < n && char.IsAsciiDigit(text[i + 1])))
            {
                int end = NumeralEnd(text, i, out bool hexInteger);
                if (hexInteger && end < n && !char.IsWhiteSpace(text[end]))
                {
                    output ??= new StringBuilder(text.Length + 16);
                    output.Append(text, copied, end - copied).Append(' ');
                    copied = end;
                }
                i = end;
            }
            else i++;
        }
        if (output == null) return text;
        return output.Append(text, copied, n - copied).ToString();
    }

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>Where a numeral ends, read as Lua 5.2's lexer reads one: digits, letters, points, and a sign after
    /// an exponent mark (e/E, or p/P after 0x).</summary>
    internal static int NumeralEnd(string text, int start, out bool hexInteger)
    {
        int n = text.Length;
        bool hex = text[start] == '0' && start + 1 < n && (text[start + 1] | 0x20) == 'x';
        int i = hex ? start + 2 : start;
        bool fraction = false, exponent = false;
        while (i < n)
        {
            char c = text[i];
            if ((hex && (c | 0x20) == 'p') || (!hex && (c | 0x20) == 'e'))
            {
                exponent = true;
                i++;
                if (i < n && text[i] is '+' or '-') i++;
            }
            else if (IsNameChar(c)) i++;
            else if (c == '.')
            {
                fraction = true;
                i++;
            }
            else break;
        }
        hexInteger = hex && !fraction && !exponent;
        return i;
    }

    /// <summary>The level of a long bracket opening at <paramref name="at"/> (<c>[[</c> is 0, <c>[==[</c> is 2), or
    /// -1.</summary>
    internal static int LongBracketLevel(string text, int at)
    {
        if (at >= text.Length || text[at] != '[') return -1;
        int i = at + 1;
        while (i < text.Length && text[i] == '=') i++;
        return i < text.Length && text[i] == '[' ? i - at - 1 : -1;
    }

    internal static int SkipLongBracket(string text, int at, int level)
    {
        string close = "]" + new string('=', level) + "]";
        int end = text.IndexOf(close, at + level + 2, StringComparison.Ordinal);
        return end < 0 ? text.Length : end + close.Length;
    }

    internal static int SkipToLineEnd(string text, int at)
    {
        int end = text.IndexOf('\n', at);
        return end < 0 ? text.Length : end;
    }

    internal static int SkipShortString(string text, int at)
    {
        char quote = text[at];
        int i = at + 1;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == quote) return i + 1;
            if (c == '\n') return i;          // unterminated: the lexer reports it
            if (c != '\\') i++;
            else if (i + 1 < text.Length && text[i + 1] == 'z')
            {
                i += 2;                          // \z skips the white space after it, line ends too
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            }
            else i += 2;
        }
        return text.Length;
    }
}
