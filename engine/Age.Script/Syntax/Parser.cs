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
        if (token.Is(TokenKind.Punct, "}")) return;   // a block's closing brace also ends its last statement
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
        Statement statement = token.Kind == TokenKind.Identifier ? ParseKeywordOrSimple(token) : null!;
        if (statement == null) throw new OmsSyntaxException(token.Span, $"unexpected {token}");
        return statement;
    }

    private Statement ParseKeywordOrSimple(Token token)
    {
        switch (token.Text)
        {
            case "if":
                Next();
                return ParseIfTail(token);
            case "loop":
                Next();
                return Finish(new LoopStatement(token.Span, ParseBlock()));
            case "while":
            {
                Next();
                Expr condition = ParseExpression();
                return Finish(new WhileStatement(token.Span, condition, ParseBlock()));
            }
            case "do":
            {
                Next();
                IReadOnlyList<Statement> body = ParseBlock();
                SkipNewlines();
                Expect(TokenKind.Identifier, "while");
                return Finish(new DoWhileStatement(token.Span, body, ParseExpression()));
            }
            case "for":
            {
                Next();
                Statement? init = Peek().Is(TokenKind.Punct, ";") ? null : ParseSimple();
                Expect(TokenKind.Punct, ";");
                Expr? condition = Peek().Is(TokenKind.Punct, ";") ? null : ParseExpression();
                Expect(TokenKind.Punct, ";");
                Statement? step = Peek().Is(TokenKind.Punct, "{") ? null : ParseSimple();
                return Finish(new ForStatement(token.Span, init, condition, step, ParseBlock()));
            }
            case "atomic":
                Next();
                return Finish(new AtomicStatement(token.Span, ParseBlock()));
            case "goto":
                Next();
                return Finish(new JumpStatement(token.Span, new JumpTarget(JumpKind.Label, Expect(TokenKind.Identifier).Text)));
            case "break":
                Next();
                return Finish(new JumpStatement(token.Span, new JumpTarget(JumpKind.Break)));
            case "continue":
                Next();
                return Finish(new JumpStatement(token.Span, new JumpTarget(JumpKind.Continue)));
            case "return":
                Next();
                return Finish(new ReturnStatement(token.Span));
            case "exit":
                Next();
                return Finish(new ExitStatement(token.Span));
            case "run":
                Next();
                return Finish(new RunStatement(token.Span, ParseExpression()));
            case "note":
                Next();
                return Finish(new NoteStatement(token.Span, ParseExpression()));
            case "call" when Peek(1).Is(TokenKind.Punct, "*"):
                _position += 2;
                return Finish(new IndirectCallStatement(token.Span, ParseUnary()));
        }
        return Finish(ParseSimple());
    }

    /// <summary>An assignment or a call, without its terminator (also the init and step clauses of <c>for</c>).</summary>
    private Statement ParseSimple()
    {
        Token token = Peek();
        if (StorageBanks.TypeByBank.ContainsKey(token.Text) && Peek(1).Is(TokenKind.Punct, "["))
        {
            Expr target = ParseStorageOrElement();
            Token op = Next();
            if (op.Is(TokenKind.Punct, "="))
                return new AssignStatement(token.Span, target, ParseExpression());
            if (op.Kind == TokenKind.Punct && CompoundOperators.Contains(op.Text))
                return new AssignStatement(token.Span, target, ParseExpression(), op.Text[..^1]);
            throw new OmsSyntaxException(op.Span, $"expected = or a compound assignment, found {op}");
        }
        if (token.Kind == TokenKind.Identifier && Peek(1).Is(TokenKind.Punct, "("))
        {
            Next();
            Next();
            var arguments = new List<CallArgument>();
            if (!Peek().Is(TokenKind.Punct, ")"))
                do
                {
                    bool isOut = Peek().Is(TokenKind.Identifier, "out") && !Peek(1).Is(TokenKind.Punct, ",")
                                 && !Peek(1).Is(TokenKind.Punct, ")");
                    if (isOut) Next();
                    arguments.Add(new CallArgument(ParseExpression(), isOut));
                } while (Accept(TokenKind.Punct, ","));
            Expect(TokenKind.Punct, ")");
            return new CallStatement(token.Span, token.Text, arguments);
        }
        throw new OmsSyntaxException(token.Span, $"expected an assignment or a call, found {token}");
    }

    private Statement Finish(Statement statement)
    {
        EndStatement();
        return statement;
    }

    private Statement ParseIfTail(Token ifToken)
    {
        Expr condition = ParseExpression();
        if (Peek().Kind == TokenKind.Identifier && Peek().Text is "goto" or "break" or "continue")
        {
            bool negated = false;
            if (condition is UnaryExpr { Op: "!" } not) { negated = true; condition = not.Operand; }
            JumpTarget target = ParseJumpTarget();
            JumpTarget? elseTarget = null;
            if (Accept(TokenKind.Identifier, "else")) elseTarget = ParseJumpTarget();
            return Finish(new ConditionalJumpStatement(ifToken.Span, condition, negated, target, elseTarget));
        }
        // An if/else-if chain is parsed iteratively, then nested from the innermost arm outward, so chain length
        // never costs recursion depth.
        var arms = new List<(SourceSpan Span, Expr Condition, IReadOnlyList<Statement> Then)>
        {
            (ifToken.Span, condition, ParseBlock()),
        };
        IReadOnlyList<Statement>? otherwise = null;
        while (true)
        {
            int mark = _position;
            while (Peek().Kind == TokenKind.Newline) _position++;
            if (!Accept(TokenKind.Identifier, "else"))
            {
                _position = mark;
                break;
            }
            if (Peek().Is(TokenKind.Identifier, "if"))
            {
                Token nested = Next();
                arms.Add((nested.Span, ParseExpression(), ParseBlock()));
                continue;
            }
            otherwise = ParseBlock();
            break;
        }
        Statement statement = new IfStatement(arms[^1].Span, arms[^1].Condition, arms[^1].Then, otherwise);
        for (int a = arms.Count - 2; a >= 0; a--)
            statement = new IfStatement(arms[a].Span, arms[a].Condition, arms[a].Then, [statement]);
        return Finish(statement);
    }

    private JumpTarget ParseJumpTarget()
    {
        Token token = Expect(TokenKind.Identifier);
        return token.Text switch
        {
            "goto" => new JumpTarget(JumpKind.Label, Expect(TokenKind.Identifier).Text),
            "break" => new JumpTarget(JumpKind.Break),
            "continue" => new JumpTarget(JumpKind.Continue),
            _ => throw new OmsSyntaxException(token.Span, $"expected goto, break or continue, found {token}"),
        };
    }

    private IReadOnlyList<Statement> ParseBlock()
    {
        Expect(TokenKind.Punct, "{");
        SkipNewlines();
        var body = new List<Statement>();
        while (!Peek().Is(TokenKind.Punct, "}"))
        {
            if (Peek().Kind == TokenKind.End) throw new OmsSyntaxException(Peek().Span, "unterminated block");
            body.Add(ParseStatement());
        }
        Next();
        return body;
    }

    // ------------------------------------------------------------------------------------- expressions

    private static readonly string[][] BinaryLevels =
    [
        ["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<", "<=", ">", ">="], ["<<", ">>"], ["+", "-"],
        ["*", "/", "%"],
    ];

    private static readonly HashSet<string> CompoundOperators = new(StringComparer.Ordinal)
    {
        "+=", "-=", "*=", "/=", "%=", "&=", "|=", "<<=", ">>=",
    };

    public Expr ParseExpression() => ParseBinary(0);

    private Expr ParseBinary(int level)
    {
        if (level == BinaryLevels.Length) return ParseUnary();
        Expr left = ParseBinary(level + 1);
        while (Peek().Kind == TokenKind.Punct && BinaryLevels[level].Contains(Peek().Text))
        {
            Token op = Next();
            left = new BinaryExpr(op.Span, op.Text, left, ParseBinary(level + 1));
        }
        return left;
    }

    private Expr ParseUnary()
    {
        Token token = Peek();
        if (token.Is(TokenKind.Punct, "-") && Peek(1).Kind == TokenKind.Integer)
        {
            Next();
            Token number = Next();
            return new OperandExpr(token.Span, new RawInteger(token.Span, unchecked((uint)-(long)number.Integer)));
        }
        if (token.Kind == TokenKind.Punct && token.Text is "-" or "!" or "~")
        {
            Next();
            return new UnaryExpr(token.Span, token.Text, ParseUnary());
        }
        if (Accept(TokenKind.Punct, "("))
        {
            Expr inner = ParseExpression();
            Expect(TokenKind.Punct, ")");
            return inner;
        }
        if (token.Kind == TokenKind.Identifier && StorageBanks.TypeByBank.ContainsKey(token.Text)
            && Peek(1).Is(TokenKind.Punct, "["))
            return ParseStorageOrElement();
        return new OperandExpr(token.Span, ParseRawOperand());
    }

    /// <summary>A storage cell, optionally with a row stride (<c>G[0x341ab:0x35]</c>) and element indices.</summary>
    private Expr ParseStorageOrElement()
    {
        Token bank = Expect(TokenKind.Identifier);
        Expect(TokenKind.Punct, "[");
        uint index = Expect(TokenKind.Integer).Integer;
        RawOperand? stride = Accept(TokenKind.Punct, ":") ? ParseRawOperand() : null;
        Expect(TokenKind.Punct, "]");
        var cell = new RawStorage(bank.Span, bank.Text, index);
        var indices = new List<Expr>();
        while (Accept(TokenKind.Punct, "["))
        {
            indices.Add(ParseExpression());
            Expect(TokenKind.Punct, "]");
        }
        if (indices.Count == 0)
        {
            if (stride != null) throw new OmsSyntaxException(bank.Span, "a row stride needs [row][col] indices");
            return new OperandExpr(bank.Span, cell);
        }
        if (indices.Count != (stride == null ? 1 : 2))
            throw new OmsSyntaxException(bank.Span, stride == null
                ? "two-dimensional access needs a row stride: base:stride[row][col]"
                : "a row stride takes exactly [row][col]");
        return new ElementExpr(bank.Span, cell, stride, indices);
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
