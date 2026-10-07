using System.Globalization;
using System.Text;

namespace Age.Script.Syntax;

public enum TokenKind
{
    Identifier, Integer, Float, String, Punct, Newline, End,
}

/// <summary>One token. <see cref="Text"/> is the identifier, punctuation or decoded string; <see cref="Integer"/>
/// holds an unsigned 32-bit literal value; <see cref="Float"/> a float literal.</summary>
public readonly record struct Token(TokenKind Kind, string Text, uint Integer, float Float, SourceSpan Span)
{
    public bool Is(TokenKind kind, string text) => Kind == kind && Text == text;
    public override string ToString() => Kind switch
    {
        TokenKind.Integer => $"0x{Integer:x}",
        TokenKind.Float => Float.ToString("R", CultureInfo.InvariantCulture),
        TokenKind.String => $"\"{Text}\"",
        TokenKind.Newline => "newline",
        TokenKind.End => "end of file",
        _ => Text,
    };
}

public readonly record struct SourceSpan(string File, int Line, int Column)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}

public sealed class OmsSyntaxException(SourceSpan span, string message) : Exception($"{span}: {message}")
{
    public SourceSpan Span { get; } = span;
}

/// <summary>
/// Tokenizes Open Maid Script source (spec §4). Newlines are significant except inside brackets; `//` and
/// `/* */` comments are dropped. Integers are decimal, `0x` hex or `0b` binary and must fit in 32 bits.
/// </summary>
public static class Lexer
{
    private static readonly string[] Punctuation =
    [
        "<<=", ">>=", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "->",
        "(", ")", "[", "]", "{", "}", ",", ":", ";", "=", "@", "*", "&", "+", "-", "/", "%", "<", ">",
        "!", "|", "^", "~", ".",
    ];

    public static List<Token> Tokenize(string source, string file = "<source>")
    {
        var tokens = new List<Token>();
        int i = 0, line = 1, column = 1, depth = 0;

        SourceSpan Here() => new(file, line, column);
        void Advance(int count)
        {
            for (int k = 0; k < count; k++)
            {
                if (source[i] == '\n') { line++; column = 1; }
                else column++;
                i++;
            }
        }

        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\r' || c == ' ' || c == '\t') { Advance(1); continue; }
            if (c == '\n')
            {
                if (depth == 0 && tokens.Count > 0 && tokens[^1].Kind != TokenKind.Newline)
                    tokens.Add(new Token(TokenKind.Newline, "\n", 0, 0, Here()));
                Advance(1);
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') Advance(1);
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                SourceSpan start = Here();
                Advance(2);
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) Advance(1);
                if (i + 1 >= source.Length) throw new OmsSyntaxException(start, "unterminated comment");
                Advance(2);
                continue;
            }
            SourceSpan span = Here();
            if (c == '"')
            {
                tokens.Add(new Token(TokenKind.String, ReadString(source, ref i, ref column, span), 0, 0, span));
                continue;
            }
            if (char.IsAsciiDigit(c))
            {
                int start = i;
                tokens.Add(ReadNumber(source, ref i, span));
                column += i - start;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Identifier, source[start..i], 0, 0, span));
                column += i - start;
                continue;
            }
            string? punct = Punctuation.FirstOrDefault(p => string.CompareOrdinal(source, i, p, 0, p.Length) == 0);
            if (punct == null) throw new OmsSyntaxException(span, $"unexpected character '{c}'");
            if (punct is "(" or "[") depth++;
            else if (punct is ")" or "]") depth = Math.Max(0, depth - 1);
            tokens.Add(new Token(TokenKind.Punct, punct, 0, 0, span));
            Advance(punct.Length);
        }
        if (tokens.Count > 0 && tokens[^1].Kind != TokenKind.Newline)
            tokens.Add(new Token(TokenKind.Newline, "\n", 0, 0, Here()));
        tokens.Add(new Token(TokenKind.End, "", 0, 0, Here()));
        return tokens;
    }

    private static string ReadString(string source, ref int i, ref int column, SourceSpan span)
    {
        var text = new StringBuilder();
        i++; column++;
        while (true)
        {
            if (i >= source.Length || source[i] == '\n') throw new OmsSyntaxException(span, "unterminated string");
            char c = source[i];
            if (c == '"') { i++; column++; return text.ToString(); }
            if (c == '\\')
            {
                if (i + 1 >= source.Length) throw new OmsSyntaxException(span, "unterminated escape");
                char e = source[i + 1];
                switch (e)
                {
                    case '"': text.Append('"'); i += 2; column += 2; break;
                    case '\\': text.Append('\\'); i += 2; column += 2; break;
                    case 'n': text.Append('\n'); i += 2; column += 2; break;
                    case 't': text.Append('\t'); i += 2; column += 2; break;
                    case 'u':
                        if (i + 6 > source.Length
                            || !ushort.TryParse(source.AsSpan(i + 2, 4), NumberStyles.HexNumber, null, out ushort code))
                            throw new OmsSyntaxException(span, "bad \\u escape");
                        text.Append((char)code); i += 6; column += 6; break;
                    default: throw new OmsSyntaxException(span, $"unknown escape \\{e}");
                }
                continue;
            }
            text.Append(c);
            i++; column++;
        }
    }

    private static Token ReadNumber(string source, ref int i, SourceSpan span)
    {
        int start = i;
        if (source[i] == '0' && i + 1 < source.Length && (source[i + 1] is 'x' or 'X' or 'b' or 'B'))
        {
            bool hex = source[i + 1] is 'x' or 'X';
            i += 2;
            int digits = i;
            while (i < source.Length && (hex ? char.IsAsciiHexDigit(source[i]) : source[i] is '0' or '1' or '_')) i++;
            string body = source[digits..i].Replace("_", "");
            if (body.Length == 0) throw new OmsSyntaxException(span, "missing digits");
            ulong value = hex ? ulong.Parse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                              : Convert.ToUInt64(body, 2);
            if (value > uint.MaxValue) throw new OmsSyntaxException(span, "integer does not fit in 32 bits");
            return new Token(TokenKind.Integer, source[start..i], (uint)value, 0, span);
        }
        while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
        bool isFloat = false;
        if (i + 1 < source.Length && source[i] == '.' && char.IsAsciiDigit(source[i + 1]))
        {
            isFloat = true;
            i++;
            while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
        }
        if (i < source.Length && source[i] is 'e' or 'E')
        {
            int mark = i;
            i++;
            if (i < source.Length && source[i] is '+' or '-') i++;
            if (i < source.Length && char.IsAsciiDigit(source[i]))
            {
                isFloat = true;
                while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
            }
            else i = mark;
        }
        string text = source[start..i];
        if (isFloat)
            return new Token(TokenKind.Float, text, 0, float.Parse(text, CultureInfo.InvariantCulture), span);
        ulong integer = ulong.Parse(text, CultureInfo.InvariantCulture);
        if (integer > uint.MaxValue) throw new OmsSyntaxException(span, "integer does not fit in 32 bits");
        return new Token(TokenKind.Integer, text, (uint)integer, 0, span);
    }
}
