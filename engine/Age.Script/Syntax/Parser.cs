namespace Age.Script.Syntax;

/// <summary>
/// Recursive-descent parser for Open Maid Script. This stage accepts the unit header, labels and the raw
/// <c>@op</c> statement; structured statements are added on the same token stream as the core level lands.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _position;

    private Parser(List<Token> tokens) => _tokens = tokens;

    public static ScriptUnit ParseScript(string source, string file = "<source>")
        => new Parser(Lexer.Tokenize(source, file)).ParseUnit();

    private Token Peek(int ahead = 0) => _tokens[Math.Min(_position + ahead, _tokens.Count - 1)];
    private Token Next() => _tokens[Math.Min(_position++, _tokens.Count - 1)];

    private bool Accept(TokenKind kind, string text)
    {
        if (!Peek().Is(kind, text)) return false;
        _position++;
        return true;
    }

    private Token Expect(TokenKind kind, string? text = null)
    {
        Token token = Next();
        if (token.Kind != kind || (text != null && token.Text != text))
            throw new OmsSyntaxException(token.Span, $"expected {text ?? kind.ToString()}, found {token}");
        return token;
    }

    private void SkipNewlines()
    {
        while (Peek().Kind == TokenKind.Newline || Peek().Is(TokenKind.Punct, ";")) _position++;
    }

    private void EndStatement()
    {
        Token token = Peek();
        if (token.Kind is TokenKind.Newline or TokenKind.End || token.Is(TokenKind.Punct, ";")) { SkipNewlines(); return; }
        throw new OmsSyntaxException(token.Span, $"expected end of statement, found {token}");
    }

    private ScriptUnit ParseUnit()
    {
        SkipNewlines();
        int version = 1;
        if (Peek().Is(TokenKind.Identifier, "oms"))
        {
            Next();
            version = (int)Expect(TokenKind.Integer).Integer;
            if (version != 1) throw new OmsSyntaxException(Peek().Span, $"unsupported language version {version}");
            EndStatement();
        }
        Token head = Expect(TokenKind.Identifier, "script");
        string name = Expect(TokenKind.String).Text;
        string? abi = null;
        if (Accept(TokenKind.Identifier, "abi")) abi = Expect(TokenKind.Identifier).Text;
        EndStatement();
        var body = new List<Statement>();
        while (Peek().Kind != TokenKind.End)
        {
            body.Add(ParseStatement());
        }
        return new ScriptUnit(head.Span, version, name, abi, body);
    }

    private Statement ParseStatement()
    {
        Token token = Peek();
        if (token.Kind == TokenKind.Identifier && Peek(1).Is(TokenKind.Punct, ":"))
        {
            _position += 2;
            SkipNewlines();
            return new LabelStatement(token.Span, token.Text);
        }
        if (token.Is(TokenKind.Punct, "@") && Peek(1).Is(TokenKind.Identifier, "op"))
        {
            Statement raw = ParseRawOp();
            EndStatement();
            return raw;
        }
        throw new OmsSyntaxException(token.Span, $"unexpected {token}");
    }

    private RawOpStatement ParseRawOp()
    {
        Token at = Expect(TokenKind.Punct, "@");
        Expect(TokenKind.Identifier, "op");
        Expect(TokenKind.Punct, "(");
        int opcode = (int)Expect(TokenKind.Integer).Integer;
        var operands = new List<RawOperand>();
        while (Accept(TokenKind.Punct, ","))
            operands.Add(ParseRawOperand());
        Expect(TokenKind.Punct, ")");
        return new RawOpStatement(at.Span, opcode, operands);
    }

    private RawOperand ParseRawOperand()
    {
        Token token = Peek();
        if (token.Is(TokenKind.Punct, "-"))
        {
            Next();
            Token number = Expect(TokenKind.Integer);
            return new RawInteger(token.Span, unchecked((uint)-(long)number.Integer));
        }
        switch (token.Kind)
        {
            case TokenKind.Integer:
                Next();
                return new RawInteger(token.Span, token.Integer);
            case TokenKind.String:
                Next();
                return new RawString(token.Span, token.Text);
        }
        if (token.Is(TokenKind.Punct, "["))
        {
            Next();
            var values = new List<int>();
            if (!Peek().Is(TokenKind.Punct, "]"))
                do values.Add(unchecked((int)ParseSignedInteger())); while (Accept(TokenKind.Punct, ","));
            Expect(TokenKind.Punct, "]");
            return new RawArray(token.Span, values);
        }
        if (token.Kind == TokenKind.Identifier)
        {
            if (token.Text == "float")
            {
                Next();
                bool negative = Accept(TokenKind.Punct, "-");
                Token value = Next();
                float number = value.Kind switch
                {
                    TokenKind.Float => value.Float,
                    TokenKind.Integer => value.Integer,
                    _ => throw new OmsSyntaxException(value.Span, "expected a float literal"),
                };
                return new RawFloat(token.Span, negative ? -number : number);
            }
            if (token.Text == "type" && Peek(1).Is(TokenKind.Punct, "("))
            {
                _position += 2;
                int type = (int)Expect(TokenKind.Integer).Integer;
                Expect(TokenKind.Punct, ",");
                RawOperand inner = ParseRawOperand();
                Expect(TokenKind.Punct, ")");
                return new RawTyped(token.Span, type, inner);
            }
            if (token.Text == "data" && Peek(1).Is(TokenKind.Punct, "("))
            {
                _position += 2;
                var dwords = new List<uint>();
                if (!Peek().Is(TokenKind.Punct, ")"))
                    do dwords.Add(Expect(TokenKind.Integer).Integer); while (Accept(TokenKind.Punct, ","));
                Expect(TokenKind.Punct, ")");
                return new RawDataBlock(token.Span, dwords);
            }
            if (StorageBanks.TypeByBank.ContainsKey(token.Text) && Peek(1).Is(TokenKind.Punct, "["))
            {
                _position += 2;
                uint index = Expect(TokenKind.Integer).Integer;
                Expect(TokenKind.Punct, "]");
                return new RawStorage(token.Span, token.Text, index);
            }
            Next();
            return new RawLabelRef(token.Span, token.Text);
        }
        throw new OmsSyntaxException(token.Span, $"expected an operand, found {token}");
    }

    private uint ParseSignedInteger()
    {
        bool negative = Accept(TokenKind.Punct, "-");
        uint value = Expect(TokenKind.Integer).Integer;
        return negative ? unchecked((uint)-(long)value) : value;
    }
}
