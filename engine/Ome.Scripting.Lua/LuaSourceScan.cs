namespace Ome.Scripting.Lua;

/// <summary>
/// What the link's check reads from a file without running it (spec §6.2, §9.3): the functions it declares at its top
/// level as globals of the mod, and the base mod's names it writes out as <c>x.g.NAME = …</c> or
/// <c>x.table("NAME")[…] = …</c>.
/// <para>
/// A token scan of our own, not Lua-CSharp's syntax tree: that parser (<c>LuaSyntaxTree.Parse</c>) is a separate
/// implementation from the compiler, and in 0.5.8 it refuses comments, <c>\z</c> in strings and a bare <c>return</c>
/// at the end of a chunk, all of which the compiler accepts (plan §4.2). The check compiles the file first, so the
/// scan only ever reads text the compiler accepted, and it needs only Lua 5.2's tokens and its block keywords.
/// </para>
/// </summary>
internal static class LuaSourceScan
{
    private enum Kind { Name, Keyword, String, Number, Symbol }

    private readonly record struct Token(Kind Kind, string Text);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in", "local", "nil",
        "not", "or", "repeat", "return", "then", "true", "until", "while",
    };

    private static readonly string[] LongSymbols = ["...", "..", "==", "~=", "<=", ">=", "::"];

    public static (HashSet<string> Functions, List<string> Writes) Scan(string text)
    {
        List<Token> tokens = Tokens(text);
        var functions = new HashSet<string>(StringComparer.Ordinal);
        var writes = new List<string>();
        int depth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Kind == Kind.Keyword)
            {
                switch (token.Text)
                {
                    case "function":
                        // function NAME( at the top level, not local: a handler. function M.f( and function M:f( are
                        // fields of a table, and function( is an expression.
                        if (depth == 0 && !Is(tokens, i - 1, Kind.Keyword, "local") && At(tokens, i + 1).Kind == Kind.Name
                            && Is(tokens, i + 2, Kind.Symbol, "("))
                            functions.Add(tokens[i + 1].Text);
                        depth++;
                        break;
                    case "do" or "if" or "repeat":   // while and for open their block with do; elseif and else open none
                        depth++;
                        break;
                    case "end" or "until":
                        depth--;
                        break;
                }
                continue;
            }
            if (!Is(tokens, i, Kind.Symbol, ".")) continue;
            // x.g.NAME = …
            if (Is(tokens, i + 1, Kind.Name, "g") && Is(tokens, i + 2, Kind.Symbol, ".") && At(tokens, i + 3).Kind == Kind.Name
                && IsAssignedTo(tokens, i + 4))
                writes.Add(tokens[i + 3].Text);
            // x.table("NAME")[…] = … and x.table "NAME" […] = …
            if (Is(tokens, i + 1, Kind.Name, "table"))
            {
                int at = i + 2;
                string? name = null;
                if (Is(tokens, at, Kind.Symbol, "(") && At(tokens, at + 1).Kind == Kind.String && Is(tokens, at + 2, Kind.Symbol, ")"))
                {
                    name = tokens[at + 1].Text;
                    at += 3;
                }
                else if (At(tokens, at).Kind == Kind.String)
                {
                    name = tokens[at].Text;
                    at += 1;
                }
                if (name != null && Is(tokens, at, Kind.Symbol, "[") && Close(tokens, at) is int close && IsAssignedTo(tokens, close + 1))
                    writes.Add(name);
            }
        }
        return (functions, writes);
    }

    /// <summary>Whether the target that ends before <paramref name="at"/> is assigned to: what follows is <c>=</c>, or
    /// more targets (each a prefix expression after a comma) and then <c>=</c>.</summary>
    private static bool IsAssignedTo(List<Token> tokens, int at)
    {
        while (true)
        {
            if (Is(tokens, at, Kind.Symbol, "=")) return true;
            if (!Is(tokens, at, Kind.Symbol, ",")) return false;
            at++;
            // A prefix expression: a name or a parenthesized expression, then fields, indexes and calls.
            if (At(tokens, at).Kind == Kind.Name) at++;
            else if (Is(tokens, at, Kind.Symbol, "(") && Close(tokens, at) is int closed) at = closed + 1;
            else return false;
            while (true)
            {
                if ((Is(tokens, at, Kind.Symbol, ".") || Is(tokens, at, Kind.Symbol, ":")) && At(tokens, at + 1).Kind == Kind.Name)
                    at += 2;
                else if ((Is(tokens, at, Kind.Symbol, "[") || Is(tokens, at, Kind.Symbol, "(") || Is(tokens, at, Kind.Symbol, "{"))
                         && Close(tokens, at) is int end)
                    at = end + 1;
                else if (At(tokens, at).Kind == Kind.String) at++;
                else break;
            }
        }
    }

    /// <summary>The index of the bracket that closes the one at <paramref name="open"/>, or null.</summary>
    private static int? Close(List<Token> tokens, int open)
    {
        int depth = 0;
        for (int i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != Kind.Symbol) continue;
            switch (tokens[i].Text)
            {
                case "(" or "[" or "{": depth++; break;
                case ")" or "]" or "}":
                    if (--depth == 0) return i;
                    break;
            }
        }
        return null;
    }

    private static Token At(List<Token> tokens, int index)
        => (uint)index < (uint)tokens.Count ? tokens[index] : new Token(Kind.Symbol, "");

    private static bool Is(List<Token> tokens, int index, Kind kind, string text)
    {
        Token token = At(tokens, index);
        return token.Kind == kind && token.Text == text;
    }

    /// <summary>Lua 5.2's tokens, without comments. A short string's token holds its text between the quotes as
    /// written, escapes included; a long string's, its text between the brackets.</summary>
    private static List<Token> Tokens(string text)
    {
        var tokens = new List<Token>();
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) i++;
            else if (c == '-' && i + 1 < n && text[i + 1] == '-')
            {
                int level = LuaSource.LongBracketLevel(text, i + 2);
                i = level >= 0 ? LuaSource.SkipLongBracket(text, i + 2, level) : LuaSource.SkipToLineEnd(text, i + 2);
            }
            else if (c == '[' && LuaSource.LongBracketLevel(text, i) is var level and >= 0)
            {
                int end = LuaSource.SkipLongBracket(text, i, level), start = i + level + 2;
                tokens.Add(new Token(Kind.String, text[start..Math.Max(start, end - level - 2)]));
                i = end;
            }
            else if (c is '"' or '\'')
            {
                int end = LuaSource.SkipShortString(text, i);
                tokens.Add(new Token(Kind.String, text.Substring(i + 1, Math.Max(0, end - i - 2))));
                i = end;
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                string word = text[start..i];
                tokens.Add(new Token(Keywords.Contains(word) ? Kind.Keyword : Kind.Name, word));
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < n && char.IsAsciiDigit(text[i + 1])))
            {
                int end = LuaSource.NumeralEnd(text, i, out _);
                tokens.Add(new Token(Kind.Number, text[i..end]));
                i = end;
            }
            else
            {
                string symbol = LongSymbols.FirstOrDefault(s => string.CompareOrdinal(text, i, s, 0, s.Length) == 0) ?? c.ToString();
                tokens.Add(new Token(Kind.Symbol, symbol));
                i += symbol.Length;
            }
        }
        return tokens;
    }
}
