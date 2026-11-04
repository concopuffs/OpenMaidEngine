namespace Age.Script.Syntax;

/// <summary>
/// Recursive-descent parser for Open Maid Script. This stage accepts the unit header, labels and the raw
/// <c>@op</c> statement; structured statements are added on the same token stream as the core level lands.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _position;

    /// <summary>The records of the enclosing <c>with</c> blocks; the innermost binds <c>.field</c>.</summary>
    private readonly Stack<NamedElementExpr> _records = new();

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
        bool module = Peek().Is(TokenKind.Identifier, "module");
        bool patch = Peek().Is(TokenKind.Identifier, "patch");
        Token head = Expect(TokenKind.Identifier, module ? "module" : patch ? "patch" : "script");
        string name = module ? ParseDottedName() : Expect(TokenKind.String).Text;
        string? fingerprint = null;
        if (patch)
        {
            Expect(TokenKind.Identifier, "fingerprint");
            Token value = Expect(TokenKind.String);
            fingerprint = value.Text;
            if (!System.Text.RegularExpressions.Regex.IsMatch(fingerprint, "^sha256:[0-9a-fA-F]{64}$"))
                throw new OmsSyntaxException(value.Span, "a fingerprint is \"sha256:\" and 64 hex digits");
        }
        string? abi = null;
        if (Accept(TokenKind.Identifier, "abi")) abi = Expect(TokenKind.Identifier).Text;
        EndStatement();
        var body = new List<Statement>();
        while (Peek().Kind != TokenKind.End)
        {
            if (StartsSiteOperation())
            {
                if (!patch) throw new OmsSyntaxException(Peek().Span, "site operations belong in a patch unit (spec §10)");
                body.Add(ParseSiteOperation());
                continue;
            }
            body.Add(ParseStatement());
        }
        return new ScriptUnit(head.Span, version, name, abi, body, module, fingerprint);
    }

    /// <summary><c>before site</c>, <c>after site</c> or <c>replace site</c>: the words are reserved only in that
    /// order, so neither is a keyword elsewhere.</summary>
    private bool StartsSiteOperation()
        => Peek().Kind == TokenKind.Identifier && Peek().Text is "before" or "after" or "replace"
           && Peek(1).Is(TokenKind.Identifier, "site");

    /// <summary>A fragment's site operation (spec §10.2).</summary>
    private Statement ParseSiteOperation()
    {
        Token keyword = Next();
        Expect(TokenKind.Identifier, "site");
        SiteRef site = ParseSiteRef();
        if (keyword.Text == "replace" && Accept(TokenKind.Identifier, "operand"))
        {
            Token number = Expect(TokenKind.Integer);
            if (number.Integer is < 1 or > 64)
                throw new OmsSyntaxException(number.Span, "operands count from 1");
            Expect(TokenKind.Identifier, "with");
            Expr value = ParseExpression();
            EndStatement();
            return new OperandPatchStatement(keyword.Span, site, (int)number.Integer, value);
        }
        SiteRef? through = keyword.Text == "replace" && Accept(TokenKind.Identifier, "through") ? ParseSiteRef() : null;
        IReadOnlyList<Statement> body = ParseBlock();
        SkipNewlines();
        var operation = keyword.Text switch
        {
            "before" => SiteOperation.Before,
            "after" => SiteOperation.After,
            _ => SiteOperation.Replace,
        };
        return new SiteStatement(keyword.Span, operation, site, through, body);
    }

    private SiteRef ParseSiteRef()
    {
        Token token = Next();
        return token.Kind switch
        {
            TokenKind.Integer => new SiteRef(token.Span, token.Integer, null),
            TokenKind.Identifier => new SiteRef(token.Span, null, DottedSiteName(token.Text)),
            _ => throw new OmsSyntaxException(token.Span, $"expected a site (an offset, a base label or a patch point), found {token}"),
        };
    }

    /// <summary>A dotted site name is a named patch point (mod loader spec §5); a base label has no dot.</summary>
    private string DottedSiteName(string first)
    {
        string name = first;
        while (Accept(TokenKind.Punct, ".")) name += "." + Expect(TokenKind.Identifier).Text;
        return name;
    }

    private string ParseDottedName()
    {
        var path = new List<string> { Expect(TokenKind.Identifier).Text };
        while (Accept(TokenKind.Punct, ".")) path.Add(Expect(TokenKind.Identifier).Text);
        return string.Join(".", path);
    }

    private Statement ParseStatement()
    {
        Token token = Peek();
        if (StartsSiteOperation())
            throw new OmsSyntaxException(token.Span, "site operations belong at the top level of a patch unit (spec §10)");
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
        if (token.Is(TokenKind.Punct, "@") && Peek(1).Is(TokenKind.Identifier, "guard"))
        {
            _position += 2;
            SkipNewlines();
            Token keyword = Expect(TokenKind.Identifier, "sub");
            return Finish(((SubStatement)ParseSub(keyword, coroutine: false)) with { Guarded = true });
        }
        if (token.Is(TokenKind.Punct, "@") && Peek(1).Is(TokenKind.Identifier, "dialogue"))
        {
            _position += 2;
            SkipNewlines();
            Token inline = Expect(TokenKind.Identifier, "inline");
            return Finish(ParseTemplate(inline, dialogue: true));
        }
        if (token.Kind == TokenKind.String)
            return ParseDialogue(token, null);
        if (token.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.String
            && !Abi.IntrinsicCatalog.Keywords.Contains(token.Text))
        {
            Next();
            return ParseDialogue(token, token.Text);
        }
        Statement statement = token.Kind == TokenKind.Identifier ? ParseKeywordOrSimple(token)
            : StartsRecordField() ? Finish(ParseSimple()) : null!;
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
            case "sub" or "coroutine" when Peek(1).Kind == TokenKind.Identifier:
                Next();
                return Finish(ParseSub(token, token.Text == "coroutine"));
            case "resume":
                Next();
                return Finish(new ResumeStatement(token.Span));
            case "use":
                Next();
                return Finish(new UseStatement(token.Span, ParseDottedName()));
            case "inline" when Peek(1).Kind == TokenKind.Identifier:
                Next();
                return Finish(ParseTemplate(token, dialogue: false));
            case "static" when Peek(1).Is(TokenKind.Identifier, "if"):
                _position += 2;
                return Finish(ParseStaticIf(token));
            case "const":
            {
                Next();
                string name = Expect(TokenKind.Identifier).Text;
                Expect(TokenKind.Punct, "=");
                return Finish(new ConstStatement(token.Span, name, ParseSignedInteger()));
            }
            case "enum" when Peek(1).Kind == TokenKind.Identifier:
                Next();
                return Finish(ParseEnum(token));
            case "global" or "local" when Peek(1).Kind == TokenKind.Identifier:
                Next();
                return Finish(ParseDeclaration(token));
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
            case "table" when Peek(1).Kind == TokenKind.Identifier:
                Next();
                return Finish(ParseTable(token));
            case "with" when Peek(1).Kind == TokenKind.Identifier:
            {
                Next();
                if (ParseNamed() is not NamedElementExpr { Indices.Count: 1, Column: null } record)
                    throw new OmsSyntaxException(token.Span, "with takes one table record: with unit[i] { … }");
                _records.Push(record);
                try
                {
                    return Finish(new WithStatement(token.Span, record, ParseBlock()));
                }
                finally
                {
                    _records.Pop();
                }
            }
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

    /// <summary><c>sub name(p: cell, …) -> cell { … }</c> or <c>coroutine name { … }</c>, after the keyword.</summary>
    private Statement ParseSub(Token keyword, bool coroutine)
    {
        Token name = Expect(TokenKind.Identifier);
        var parameters = new List<SubParameter>();
        RawStorage? result = null;
        if (!coroutine && Accept(TokenKind.Punct, "("))
        {
            if (!Peek().Is(TokenKind.Punct, ")"))
                do
                {
                    Token parameter = Expect(TokenKind.Identifier);
                    Expect(TokenKind.Punct, ":");
                    parameters.Add(new SubParameter(parameter.Span, parameter.Text, ParseCell()));
                } while (Accept(TokenKind.Punct, ","));
            Expect(TokenKind.Punct, ")");
            if (Accept(TokenKind.Punct, "->")) result = ParseCell();
        }
        return new SubStatement(keyword.Span, name.Text, parameters, result, ParseBlock(), coroutine);
    }

    /// <summary><c>enum name { member = value, … }</c> after <c>enum</c>; members are separated by commas or
    /// newlines.</summary>
    private Statement ParseEnum(Token keyword)
    {
        string name = Expect(TokenKind.Identifier).Text;
        Expect(TokenKind.Punct, "{");
        var members = new List<EnumMember>();
        while (true)
        {
            while (Peek().Kind == TokenKind.Newline || Peek().Is(TokenKind.Punct, ",")) _position++;
            if (Accept(TokenKind.Punct, "}")) break;
            Token member = Expect(TokenKind.Identifier);
            Expect(TokenKind.Punct, "=");
            members.Add(new EnumMember(member.Span, member.Text, ParseSignedInteger()));
        }
        return new EnumStatement(keyword.Span, name, members);
    }

    /// <summary><c>table name[records] { field = array, … }</c> after <c>table</c>; fields are separated by commas or
    /// newlines.</summary>
    private Statement ParseTable(Token keyword)
    {
        string name = Expect(TokenKind.Identifier).Text;
        Expect(TokenKind.Punct, "[");
        Token records = Expect(TokenKind.Integer);
        Expect(TokenKind.Punct, "]");
        if (records.Integer is 0 or > int.MaxValue)
            throw new OmsSyntaxException(records.Span, "a table needs a record count");
        Expect(TokenKind.Punct, "{");
        var fields = new List<TableField>();
        while (true)
        {
            while (Peek().Kind == TokenKind.Newline || Peek().Is(TokenKind.Punct, ",")) _position++;
            if (Accept(TokenKind.Punct, "}")) break;
            Token field = Expect(TokenKind.Identifier);
            Expect(TokenKind.Punct, "=");
            Token array = Expect(TokenKind.Identifier);
            if (fields.Any(other => other.Name == field.Text))
                throw new OmsSyntaxException(field.Span, $"field '{field.Text}' is named twice");
            fields.Add(new TableField(field.Span, field.Text, array.Text));
        }
        return new TableStatement(keyword.Span, name, (int)records.Integer, fields);
    }

    /// <summary><c>inline name(p: kind [= default], …) { … }</c> after <c>inline</c>; the kind is const, text, expr,
    /// cell or an enum name.</summary>
    private Statement ParseTemplate(Token keyword, bool dialogue)
    {
        string name = Expect(TokenKind.Identifier).Text;
        var parameters = new List<TemplateParameter>();
        Expect(TokenKind.Punct, "(");
        if (!Peek().Is(TokenKind.Punct, ")"))
            do
            {
                Token parameter = Expect(TokenKind.Identifier);
                Expect(TokenKind.Punct, ":");
                Token kind = Expect(TokenKind.Identifier);
                Expr? fallback = Accept(TokenKind.Punct, "=") ? ParseExpression() : null;
                parameters.Add(new TemplateParameter(parameter.Span, parameter.Text, kind.Text, fallback));
            } while (Accept(TokenKind.Punct, ","));
        Expect(TokenKind.Punct, ")");
        return new TemplateStatement(keyword.Span, name, parameters, ParseBlock(), dialogue);
    }

    /// <summary><c>static if c { … } [else { … } | else static if …]</c> after <c>static if</c>.</summary>
    private Statement ParseStaticIf(Token keyword)
    {
        Expr condition = ParseExpression();
        IReadOnlyList<Statement> then = ParseBlock();
        int mark = _position;
        while (Peek().Kind == TokenKind.Newline) _position++;
        if (!Accept(TokenKind.Identifier, "else"))
        {
            _position = mark;
            return new StaticIfStatement(keyword.Span, condition, then, null);
        }
        if (Peek().Is(TokenKind.Identifier, "static") && Peek(1).Is(TokenKind.Identifier, "if"))
        {
            Token nested = Next();
            Next();
            return new StaticIfStatement(keyword.Span, condition, then, [ParseStaticIf(nested)]);
        }
        return new StaticIfStatement(keyword.Span, condition, then, ParseBlock());
    }

    /// <summary><c>"text" [(name: value, …)]</c> with an optional speaker name already consumed.</summary>
    private Statement ParseDialogue(Token start, string? speaker)
    {
        string text = Expect(TokenKind.String).Text;
        var arguments = new List<CallArgument>();
        if (Accept(TokenKind.Punct, "("))
        {
            do
            {
                Token name = Expect(TokenKind.Identifier);
                Expect(TokenKind.Punct, ":");
                arguments.Add(new CallArgument(ParseExpression(), false, name.Text));
            } while (Accept(TokenKind.Punct, ","));
            Expect(TokenKind.Punct, ")");
        }
        return Finish(new DialogueStatement(start.Span, speaker, text, arguments));
    }

    /// <summary><c>type name[dims] [@ address] [{ column: index, … }]</c> after <c>global</c> or <c>local</c>.</summary>
    private Statement ParseDeclaration(Token keyword)
    {
        Token type = Expect(TokenKind.Identifier);
        if (type.Text is not ("int" or "str" or "float"))
            throw new OmsSyntaxException(type.Span, $"expected int, str or float, found {type}");
        string name = Expect(TokenKind.Identifier).Text;
        var dimensions = new List<int>();
        while (Accept(TokenKind.Punct, "["))
        {
            // Only the leading dimension may be open (name[][27]): an array of unknown length.
            if (dimensions.Count == 0 && Accept(TokenKind.Punct, "]")) { dimensions.Add(0); continue; }
            dimensions.Add((int)Expect(TokenKind.Integer).Integer);
            Expect(TokenKind.Punct, "]");
        }
        uint? address = Accept(TokenKind.Punct, "@") ? Expect(TokenKind.Integer).Integer : null;
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        if (Accept(TokenKind.Punct, "{"))
        {
            SkipNewlines();
            while (!Accept(TokenKind.Punct, "}"))
            {
                Token column = Expect(TokenKind.Identifier);
                Expect(TokenKind.Punct, ":");
                if (!columns.TryAdd(column.Text, (int)Expect(TokenKind.Integer).Integer))
                    throw new OmsSyntaxException(column.Span, $"column '{column.Text}' is named twice");
                Accept(TokenKind.Punct, ",");
                SkipNewlines();
            }
            if (dimensions.Count != 2) throw new OmsSyntaxException(keyword.Span, "named columns need a [rows][columns] table");
        }
        if (keyword.Text == "global" && address == null)
            throw new OmsSyntaxException(keyword.Span, "a global needs its cell: @ address");
        return new StorageDeclaration(keyword.Span, keyword.Text == "local", type.Text, name, dimensions, address, columns);
    }

    /// <summary>A named operand: a bare name, or a named element <c>name[i]</c>, <c>name[r][c]</c>, <c>name[r].col</c>,
    /// or a table field <c>t[i].field</c> with the field's own index or column after it.</summary>
    private Expr ParseNamed()
    {
        Token name = Expect(TokenKind.Identifier);
        if (!Peek().Is(TokenKind.Punct, "[")) return new OperandExpr(name.Span, new RawLabelRef(name.Span, name.Text));
        List<Expr> indices = ParseIndices();
        if (!Accept(TokenKind.Punct, ".")) return new NamedElementExpr(name.Span, name.Text, indices, null);
        return ParseFieldRest(new NamedElementExpr(name.Span, name.Text, indices, Expect(TokenKind.Identifier).Text));
    }

    private List<Expr> ParseIndices()
    {
        var indices = new List<Expr>();
        while (Accept(TokenKind.Punct, "["))
        {
            indices.Add(ParseExpression());
            Expect(TokenKind.Punct, "]");
        }
        return indices;
    }

    /// <summary>A table field's own row index or column, after <c>.field</c>.</summary>
    private NamedElementExpr ParseFieldRest(NamedElementExpr element)
    {
        List<Expr>? indices = Peek().Is(TokenKind.Punct, "[") ? ParseIndices() : null;
        string? column = Accept(TokenKind.Punct, ".") ? Expect(TokenKind.Identifier).Text : null;
        return element with { FieldIndices = indices, FieldColumn = column };
    }

    private bool StartsRecordField() => Peek().Is(TokenKind.Punct, ".") && Peek(1).Kind == TokenKind.Identifier;

    /// <summary><c>.field…</c> inside a <c>with</c> block: the innermost record's field (spec §5.3).</summary>
    private NamedElementExpr ParseRecordField()
    {
        Token dot = Expect(TokenKind.Punct, ".");
        if (_records.Count == 0) throw new OmsSyntaxException(dot.Span, "'.field' needs an enclosing with block");
        NamedElementExpr record = _records.Peek();
        Token field = Expect(TokenKind.Identifier);
        return ParseFieldRest(new NamedElementExpr(dot.Span, record.Name, record.Indices, field.Text));
    }

    private bool StartsNamed()
        => Peek().Kind == TokenKind.Identifier && !StorageBanks.TypeByBank.ContainsKey(Peek().Text)
           && Peek().Text is not ("type" or "data" or "float") && Peek(1).Is(TokenKind.Punct, "[");

    private RawStorage ParseCell()
        => ParseRawOperand() as RawStorage ?? throw new OmsSyntaxException(Peek().Span, "expected a storage cell");

    /// <summary>An assignment or a call, without its terminator (also the init and step clauses of <c>for</c>).</summary>
    private Statement ParseSimple()
    {
        Token token = Peek();
        bool storage = StorageBanks.TypeByBank.ContainsKey(token.Text) && Peek(1).Is(TokenKind.Punct, "[");
        bool named = token.Kind == TokenKind.Identifier && !storage && Peek(1).Kind == TokenKind.Punct
                     && (Peek(1).Text is "=" or "[" || CompoundOperators.Contains(Peek(1).Text));
        bool record = StartsRecordField();
        if (storage || named || record)
        {
            Expr target = record ? ParseRecordField() : storage ? ParseStorageOrElement() : ParseNamed();
            Token op = Next();
            if (op.Is(TokenKind.Punct, "="))
            {
                if (Peek().Kind == TokenKind.Identifier && Peek(1).Is(TokenKind.Punct, "(")
                    && !StorageBanks.TypeByBank.ContainsKey(Peek().Text) && Peek().Text is not ("type" or "data"))
                    return new CallAssignStatement(token.Span, target, (CallStatement)ParseSimple());
                return new AssignStatement(token.Span, target, ParseExpression());
            }
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
                    string? argumentName = null;
                    if (Peek().Kind == TokenKind.Identifier && Peek(1).Is(TokenKind.Punct, ":"))
                    {
                        argumentName = Next().Text;
                        Next();
                    }
                    bool isOut = Peek().Is(TokenKind.Identifier, "out") && !Peek(1).Is(TokenKind.Punct, ",")
                                 && !Peek(1).Is(TokenKind.Punct, ")");
                    if (isOut) Next();
                    arguments.Add(new CallArgument(ParseExpression(), isOut, argumentName));
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
        if (token.Is(TokenKind.Identifier, "none"))
        {
            Next();
            return new NoneExpr(token.Span);
        }
        if (StartsNamed()) return ParseNamed();
        if (StartsRecordField()) return ParseRecordField();
        if (token.Is(TokenKind.Punct, "&") && Peek(1).Kind == TokenKind.Identifier)
        {
            Next();
            Token name = Next();
            return new OperandExpr(token.Span, new RawLabelRef(name.Span, name.Text));
        }
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
