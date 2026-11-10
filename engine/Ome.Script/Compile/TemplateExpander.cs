using Ome.Script.Syntax;

namespace Ome.Script.Compile;

/// <summary>
/// Expands one use of an <c>inline</c> template (spec §6.3, §11.1): binds arguments to parameters, resolves
/// <c>static if</c> against the constant parameters, substitutes every parameter reference and renames the labels the
/// template defines so each expansion has its own. The result is ordinary source that compiles like any other.
/// </summary>
public sealed class TemplateExpander
{
    private readonly TemplateStatement _template;
    private readonly Dictionary<string, Expr> _arguments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long?> _constants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

    private TemplateExpander(TemplateStatement template) => _template = template;

    /// <param name="constant">Evaluates a constant argument: an integer, or null for <c>none</c>.</param>
    /// <param name="hygiene">A suffix unique to this expansion, appended to the template's own labels.</param>
    public static IReadOnlyList<Statement> Expand(TemplateStatement template, IReadOnlyList<CallArgument> arguments,
                                                  SourceSpan span, Func<Expr, long?> constant, string hygiene)
    {
        var expander = new TemplateExpander(template);
        expander.Bind(arguments, span, constant);
        foreach (string label in DefinedLabels(template.Body)) expander._labels[label] = label + hygiene;
        return expander.Statements(template.Body);
    }



    private void Bind(IReadOnlyList<CallArgument> arguments, SourceSpan span, Func<Expr, long?> constant)
    {
        var bound = new Dictionary<string, Expr>(StringComparer.Ordinal);
        int position = 0;
        foreach (CallArgument argument in arguments)
        {
            if (argument.Out)
                throw new OmsCompileException(argument.Value.Span, $"template '{_template.Name}' takes no 'out' arguments");
            string name;
            if (argument.Name != null)
            {
                if (_template.Parameters.All(parameter => parameter.Name != argument.Name))
                    throw new OmsCompileException(argument.Value.Span, $"template '{_template.Name}' has no parameter '{argument.Name}'");
                name = argument.Name;
            }
            else
            {
                if (position >= _template.Parameters.Count)
                    throw new OmsCompileException(argument.Value.Span, $"template '{_template.Name}' takes {_template.Parameters.Count} arguments");
                name = _template.Parameters[position++].Name;
            }
            if (!bound.TryAdd(name, argument.Value))
                throw new OmsCompileException(argument.Value.Span, $"argument '{name}' of '{_template.Name}' is given twice");
        }
        foreach (TemplateParameter parameter in _template.Parameters)
        {
            Expr value = bound.TryGetValue(parameter.Name, out Expr? given) ? given
                : parameter.Default ?? throw new OmsCompileException(span, $"'{_template.Name}' needs argument '{parameter.Name}'");
            switch (parameter.Kind)
            {
                case "text":
                    if (value is not OperandExpr { Operand: RawString })
                        throw new OmsCompileException(value.Span, $"argument '{parameter.Name}' of '{_template.Name}' must be a string literal");
                    break;
                case "cell":
                    if (value is not (OperandExpr { Operand: RawStorage or RawLabelRef } or ElementExpr or NamedElementExpr))
                        throw new OmsCompileException(value.Span, $"argument '{parameter.Name}' of '{_template.Name}' must be a cell or element");
                    break;
                case "expr":
                    break;
                default:
                    // const, or an enum name: a constant that static if can test.
                    long? number = constant(value);
                    _constants[parameter.Name] = number;
                    value = number is long known ? new OperandExpr(value.Span, new RawInteger(value.Span, unchecked((uint)known)))
                        : new NoneExpr(value.Span);
                    break;
            }
            _arguments[parameter.Name] = value;
        }
    }

    /// <summary>The labels a body defines (a template's own, renamed per expansion).</summary>
    public static IEnumerable<string> DefinedLabels(IReadOnlyList<Statement> body)
    {
        foreach (Statement statement in body)
            switch (statement)
            {
                case LabelStatement label: yield return label.Name; break;
                case IfStatement branch:
                    foreach (string name in DefinedLabels(branch.Then)) yield return name;
                    if (branch.Else != null) foreach (string name in DefinedLabels(branch.Else)) yield return name;
                    break;
                case StaticIfStatement branch:
                    foreach (string name in DefinedLabels(branch.Then)) yield return name;
                    if (branch.Else != null) foreach (string name in DefinedLabels(branch.Else)) yield return name;
                    break;
                case LoopStatement loop: foreach (string name in DefinedLabels(loop.Body)) yield return name; break;
                case WhileStatement loop: foreach (string name in DefinedLabels(loop.Body)) yield return name; break;
                case DoWhileStatement loop: foreach (string name in DefinedLabels(loop.Body)) yield return name; break;
                case ForStatement loop: foreach (string name in DefinedLabels(loop.Body)) yield return name; break;
                case AtomicStatement atomic: foreach (string name in DefinedLabels(atomic.Body)) yield return name; break;
                case WithStatement with: foreach (string name in DefinedLabels(with.Body)) yield return name; break;
            }
    }

    // ------------------------------------------------------------------------------------------ static if

    private bool Holds(Expr condition) => Evaluate(condition) is long value && value != 0;

    /// <summary>Evaluates a static condition over constant parameters, integers and <c>none</c>.</summary>
    private long? Evaluate(Expr expression)
    {
        switch (expression)
        {
            case NoneExpr: return null;
            case OperandExpr { Operand: RawInteger integer }: return unchecked((int)integer.Value);
            case OperandExpr { Operand: RawLabelRef name } when _constants.TryGetValue(name.Name, out long? value): return value;
            case UnaryExpr { Op: "!" } not: return Evaluate(not.Operand) is long v && v != 0 ? 0 : 1;
            case BinaryExpr { Op: "==" or "!=" } equality:
            {
                bool same = Evaluate(equality.Left) == Evaluate(equality.Right);
                return (equality.Op == "==") == same ? 1 : 0;
            }
            case BinaryExpr { Op: "&&" } and: return Holds(and.Left) && Holds(and.Right) ? 1 : 0;
            case BinaryExpr { Op: "||" } or: return Holds(or.Left) || Holds(or.Right) ? 1 : 0;
            case BinaryExpr { Op: "<" or "<=" or ">" or ">=" } order
                when Evaluate(order.Left) is long left && Evaluate(order.Right) is long right:
                return order.Op switch { "<" => left < right, "<=" => left <= right, ">" => left > right, _ => left >= right }
                    ? 1 : 0;
        }
        throw new OmsCompileException(expression.Span, "a static if condition may use only constant parameters, integers and none");
    }

    // ------------------------------------------------------------------------------------------ substitution

    private List<Statement> Statements(IReadOnlyList<Statement> body)
    {
        var result = new List<Statement>();
        foreach (Statement statement in body)
        {
            if (statement is StaticIfStatement branch)
            {
                IReadOnlyList<Statement>? chosen = Holds(branch.Condition) ? branch.Then : branch.Else;
                if (chosen != null) result.AddRange(Statements(chosen));
                continue;
            }
            result.Add(Statement(statement));
        }
        return result;
    }

    private Statement Statement(Statement statement) => statement switch
    {
        LabelStatement label => label with { Name = Label(label.Name) },
        RawOpStatement raw => raw with { Operands = raw.Operands.Select(RawOperandOf).ToArray() },
        AssignStatement assign => assign with { Target = Expression(assign.Target), Value = Expression(assign.Value) },
        CallStatement call => call with
        {
            Name = Label(call.Name),
            Arguments = call.Arguments.Select(argument => argument with { Value = Expression(argument.Value) }).ToArray(),
        },
        CallAssignStatement call => call with { Target = Expression(call.Target), Call = (CallStatement)Statement(call.Call) },
        JumpStatement jump => jump with { Target = Target(jump.Target) },
        ConditionalJumpStatement jump => jump with
        {
            Condition = Expression(jump.Condition), Target = Target(jump.Target),
            Else = jump.Else == null ? null : Target(jump.Else),
        },
        IfStatement branch => branch with
        {
            Condition = Expression(branch.Condition), Then = Statements(branch.Then),
            Else = branch.Else == null ? null : Statements(branch.Else),
        },
        LoopStatement loop => loop with { Body = Statements(loop.Body) },
        WhileStatement loop => loop with { Condition = Expression(loop.Condition), Body = Statements(loop.Body) },
        DoWhileStatement loop => loop with { Body = Statements(loop.Body), Condition = Expression(loop.Condition) },
        ForStatement loop => loop with
        {
            Init = loop.Init == null ? null : Statement(loop.Init),
            Condition = loop.Condition == null ? null : Expression(loop.Condition),
            Step = loop.Step == null ? null : Statement(loop.Step),
            Body = Statements(loop.Body),
        },
        AtomicStatement atomic => atomic with { Body = Statements(atomic.Body) },
        WithStatement with => with with { Record = (NamedElementExpr)Expression(with.Record), Body = Statements(with.Body) },
        RunStatement run => run with { Script = Expression(run.Script) },
        NoteStatement note => note with { Value = Expression(note.Value) },
        IndirectCallStatement call => call with { Target = Expression(call.Target) },
        DialogueStatement dialogue => dialogue with
        {
            Arguments = dialogue.Arguments.Select(argument => argument with { Value = Expression(argument.Value) }).ToArray(),
        },
        ReturnStatement or ExitStatement or ResumeStatement => statement,
        // A declaration names one slot or value for the whole script, and a template may expand many times.
        UseStatement or ConstStatement or StorageDeclaration or EnumStatement or TableStatement or TemplateStatement
            => throw new OmsCompileException(statement.Span,
                "a declaration (use, const, enum, table, global, local or inline) cannot appear in a template body; declare it at the top level of the script"),
        _ => throw new OmsCompileException(statement.Span, $"{statement.GetType().Name} cannot appear in a template"),
    };

    private JumpTarget Target(JumpTarget target)
        => target.Kind == JumpKind.Label ? target with { Label = Label(target.Label!) } : target;

    private string Label(string name) => _labels.TryGetValue(name, out string? renamed) ? renamed : name;

    private Expr Expression(Expr expression) => expression switch
    {
        OperandExpr { Operand: RawLabelRef name } when _arguments.TryGetValue(name.Name, out Expr? argument) => argument,
        OperandExpr { Operand: RawLabelRef name } when _labels.ContainsKey(name.Name)
            => new OperandExpr(expression.Span, name with { Name = Label(name.Name) }),
        OperandExpr => expression,
        NamedElementExpr named when _arguments.TryGetValue(named.Name, out Expr? argument) => Indexed(named, argument),
        NamedElementExpr named => named with
        {
            Indices = named.Indices.Select(Expression).ToArray(),
            FieldIndices = named.FieldIndices?.Select(Expression).ToArray(),
        },
        ElementExpr element => element with { Indices = element.Indices.Select(Expression).ToArray() },
        BinaryExpr binary => binary with { Left = Expression(binary.Left), Right = Expression(binary.Right) },
        UnaryExpr unary => unary with { Operand = Expression(unary.Operand) },
        _ => expression,
    };

    /// <summary><c>p[i]</c> where <c>p</c> is a parameter: the element of the array or cell its argument names. A
    /// declared name keeps the element form (rows, columns and table fields included); a raw cell takes one index,
    /// as <c>G[…][i]</c> does.</summary>
    private Expr Indexed(NamedElementExpr named, Expr argument)
    {
        Expr[] indices = named.Indices.Select(Expression).ToArray();
        switch (argument)
        {
            case OperandExpr { Operand: RawLabelRef name }:
                return named with
                {
                    Name = name.Name,
                    Indices = indices,
                    FieldIndices = named.FieldIndices?.Select(Expression).ToArray(),
                };
            case OperandExpr { Operand: RawStorage cell } when indices.Length == 1 && named.Column == null
                                                             && named.FieldIndices == null:
                return new ElementExpr(named.Span, cell, null, indices);
            default:
                throw new OmsCompileException(named.Span,
                    $"'{named.Name}' of '{_template.Name}' cannot be indexed that way: its argument is not a named array or a single raw cell");
        }
    }

    /// <summary>A parameter in a raw <c>@op</c> operand must stand for a raw operand form.</summary>
    private RawOperand RawOperandOf(RawOperand operand)
    {
        if (operand is RawLabelRef name && _arguments.TryGetValue(name.Name, out Expr? argument))
            return argument is OperandExpr atom ? atom.Operand
                : throw new OmsCompileException(argument.Span, $"'{name.Name}' must be a single operand in @op");
        if (operand is RawLabelRef label && _labels.ContainsKey(label.Name)) return label with { Name = Label(label.Name) };
        return operand;
    }
}
