using Age.Engine.Model;
using Age.Script.Abi;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Syntax;

namespace Age.Script.Compile;

public sealed class OmsCompileException(SourceSpan span, string message) : Exception($"{span}: {message}")
{
    public SourceSpan Span { get; } = span;
}

/// <summary>
/// Compiles a parsed script unit to the intermediate form using the canonical lowerings of spec §7. Data items
/// are created per data-operand occurrence in emission order (spec §7.5); an optional layout sidecar restores
/// any non-canonical original layout. This stage lowers raw instructions and three-address core statements;
/// nested expressions arrive with the temporary allocator.
/// </summary>
public static class OmsCompiler
{
    public static readonly IReadOnlyDictionary<string, int> BinaryOpcodes = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["+"] = 0x50, ["-"] = 0x51, ["*"] = 0x52, ["/"] = 0x53, ["%"] = 0x54, ["&"] = 0x56, ["|"] = 0x57,
        [">>"] = 0x58, ["<<"] = 0x59, ["=="] = 0x5A, ["!="] = 0x5B, ["<"] = 0x5C, ["<="] = 0x5D, [">"] = 0x5E,
        [">="] = 0x5F,
    };

    public const int Mov = 0x55, Jmp = 0x8C, Jcc = 0xA0, Call = 0x8F, Ret = 0x05, Exit = 0x02, CallScript = 0x03;
    public const int BlockBegin = 0x1F4, BlockEnd = 0x1F5, Comment = 0x1A7;
    private const uint NoTarget = 0xFFFFFFFF;

    public static IrScript Compile(ScriptUnit unit, string abiId, OpcodeTable? table = null, OmsLayout? layout = null,
                                   IntrinsicCatalog? intrinsics = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var generator = new CodeGenerator(table, intrinsics);
        generator.EmitAll(unit.Body);
        generator.ResolveLabels();
        return generator.Build(unit.Name, abiId, layout);
    }

    private sealed record Occurrence(IrDataItem Item, SourceSpan Span);

    private sealed class CodeGenerator(OpcodeTable? table, IntrinsicCatalog? intrinsics)
    {
        private readonly List<IrInstruction> _code = new();
        private readonly List<Occurrence> _occurrences = new();
        private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
        private readonly HashSet<string> _userLabels = new(StringComparer.Ordinal);
        private readonly List<(string Key, SourceSpan Span)> _pendingLabels = new();
        private const uint PendingTag = 0x80000000;
        private readonly Stack<(string Break, string Continue)> _loops = new();
        private int _internal;

        private string NewLabel() => $"#{_internal++}";

        private void Define(string key, SourceSpan span)
        {
            if (!_labels.TryAdd(key, _code.Count))
                throw new OmsCompileException(span, $"label '{key}' is defined twice");
        }

        public void EmitAll(IReadOnlyList<Statement> statements)
        {
            CollectUserLabels(statements);
            foreach (Statement statement in statements) Emit(statement);
        }

        private void CollectUserLabels(IReadOnlyList<Statement> statements)
        {
            foreach (Statement statement in statements)
                switch (statement)
                {
                    case LabelStatement label: _userLabels.Add(label.Name); break;
                    case IfStatement s:
                        IfStatement? arm = s;
                        while (arm != null)
                        {
                            CollectUserLabels(arm.Then);
                            if (arm.Else is [IfStatement next]) { arm = next; continue; }
                            if (arm.Else != null) CollectUserLabels(arm.Else);
                            arm = null;
                        }
                        break;
                    case LoopStatement s: CollectUserLabels(s.Body); break;
                    case WhileStatement s: CollectUserLabels(s.Body); break;
                    case DoWhileStatement s: CollectUserLabels(s.Body); break;
                    case AtomicStatement s: CollectUserLabels(s.Body); break;
                }
        }

        private void Emit(Statement statement)
        {
            switch (statement)
            {
                case LabelStatement label:
                    Define(label.Name, label.Span);
                    break;
                case RawOpStatement raw:
                    CheckArgc(raw.Opcode, raw.Operands.Count, raw.Span);
                    Add(raw.Opcode, raw.Operands.Select(o => Operand(o)).ToArray());
                    break;
                case AssignStatement assign:
                    EmitAssign(assign);
                    break;
                case CallStatement call:
                    EmitCall(call);
                    break;
                case JumpStatement jump:
                    Add(Jmp, [LabelOperand(Resolve(jump.Target, jump.Span), jump.Span)]);
                    break;
                case ConditionalJumpStatement jump:
                {
                    IrOperand condition = Atom(jump.Condition, "a condition");
                    IrOperand target = LabelOperand(Resolve(jump.Target, jump.Span), jump.Span);
                    IrOperand otherwise = jump.Else != null
                        ? LabelOperand(Resolve(jump.Else, jump.Span), jump.Span) : IrOperand.Literal(0, NoTarget);
                    Add(Jcc, jump.Negated ? [condition, otherwise, target] : [condition, target, otherwise]);
                    break;
                }
                case IfStatement branch:
                    EmitIf(branch);
                    break;
                case LoopStatement loop:
                {
                    string head = NewLabel(), end = NewLabel();
                    Define(head, loop.Span);
                    EmitLoopBody(loop.Body, end, head);
                    Add(Jmp, [LabelOperand(head, loop.Span)]);
                    Define(end, loop.Span);
                    break;
                }
                case WhileStatement loop:
                {
                    string head = NewLabel(), end = NewLabel();
                    Define(head, loop.Span);
                    EmitBranchUnless(loop.Condition, end, loop.Span);
                    EmitLoopBody(loop.Body, end, head);
                    Add(Jmp, [LabelOperand(head, loop.Span)]);
                    Define(end, loop.Span);
                    break;
                }
                case DoWhileStatement loop:
                {
                    string head = NewLabel(), condition = NewLabel(), end = NewLabel();
                    Define(head, loop.Span);
                    EmitLoopBody(loop.Body, end, condition);
                    Define(condition, loop.Span);
                    (IrOperand test, bool negated) = Condition(loop.Condition);
                    IrOperand back = LabelOperand(head, loop.Span), none = IrOperand.Literal(0, NoTarget);
                    Add(Jcc, negated ? [test, none, back] : [test, back, none]);
                    Define(end, loop.Span);
                    break;
                }
                case AtomicStatement atomic:
                    Add(BlockBegin, []);
                    foreach (Statement inner in atomic.Body) Emit(inner);
                    Add(BlockEnd, []);
                    break;
                case ReturnStatement:
                    Add(Ret, []);
                    break;
                case ExitStatement:
                    Add(Exit, []);
                    break;
                case RunStatement run:
                    Add(CallScript, [Atom(run.Script, "a script id")]);
                    break;
                case NoteStatement note:
                    Add(Comment, [Atom(note.Value, "a note")]);
                    break;
                case IndirectCallStatement call:
                    Add(Call, [Atom(call.Target, "an indirect call target")]);
                    break;
                default:
                    throw new OmsCompileException(statement.Span, $"{statement.GetType().Name} is not supported yet");
            }
        }

        private void EmitLoopBody(IReadOnlyList<Statement> body, string breakKey, string continueKey)
        {
            _loops.Push((breakKey, continueKey));
            foreach (Statement inner in body) Emit(inner);
            _loops.Pop();
        }

        /// <summary>if / else-if chains (an else holding exactly one if) are emitted iteratively; every arm's
        /// <c>jmp end</c> lands after the last arm, exactly as nested if-else would.</summary>
        private void EmitIf(IfStatement first)
        {
            var ends = new List<string>();
            IfStatement branch = first;
            while (true)
            {
                string skip = NewLabel();
                EmitBranchUnless(branch.Condition, skip, branch.Span);
                foreach (Statement inner in branch.Then) Emit(inner);
                if (branch.Else == null)
                {
                    Define(skip, branch.Span);
                    break;
                }
                string end = NewLabel();
                ends.Add(end);
                Add(Jmp, [LabelOperand(end, branch.Span)]);
                Define(skip, branch.Span);
                if (branch.Else is [IfStatement elseIf])
                {
                    branch = elseIf;
                    continue;
                }
                foreach (Statement inner in branch.Else) Emit(inner);
                break;
            }
            foreach (string end in ends) Define(end, first.Span);
        }

        /// <summary>Jump to <paramref name="target"/> when the condition is false (spec §7.1: jcc c, -1, target);
        /// a negated condition flips the jcc sense instead of synthesizing a test.</summary>
        private void EmitBranchUnless(Expr condition, string target, SourceSpan span)
        {
            (IrOperand test, bool negated) = Condition(condition);
            IrOperand label = LabelOperand(target, span), none = IrOperand.Literal(0, NoTarget);
            Add(Jcc, negated ? [test, label, none] : [test, none, label]);
        }

        private (IrOperand Test, bool Negated) Condition(Expr condition)
            => condition is UnaryExpr { Op: "!" } not
                ? (Atom(not.Operand, "a condition"), true)
                : (Atom(condition, "a condition"), false);

        private void EmitAssign(AssignStatement assign)
        {
            IrOperand target = Operand(assign.Target);
            switch (assign.Value)
            {
                case OperandExpr atom:
                    Add(Mov, [target, Operand(atom.Operand)]);
                    break;
                case BinaryExpr { Left: OperandExpr left, Right: OperandExpr right } binary
                    when BinaryOpcodes.TryGetValue(binary.Op, out int opcode):
                    Add(opcode, [target, Operand(left.Operand), Operand(right.Operand)]);
                    break;
                default:
                    throw new OmsCompileException(assign.Value.Span,
                        "only single-operation assignments are supported at this stage");
            }
        }

        private void EmitCall(CallStatement call)
        {
            if (_userLabels.Contains(call.Name))
            {
                if (call.Arguments.Count != 0)
                    throw new OmsCompileException(call.Span, $"'{call.Name}' is a label; calls to it take no arguments");
                Add(Call, [LabelOperand(call.Name, call.Span)]);
                return;
            }
            if (intrinsics == null || !intrinsics.TryGet(call.Name, out Intrinsic intrinsic))
                throw new OmsCompileException(call.Span, $"unknown intrinsic or label '{call.Name}'");
            if (intrinsic.Argc != call.Arguments.Count)
                throw new OmsCompileException(call.Span,
                    $"{call.Name} takes {intrinsic.Argc} operands, not {call.Arguments.Count}");
            var operands = new IrOperand[call.Arguments.Count];
            for (int k = 0; k < operands.Length; k++)
            {
                CallArgument argument = call.Arguments[k];
                if (intrinsic.HasContract && argument.Out != intrinsic.OutOperands.Contains(k + 1))
                    throw new OmsCompileException(argument.Value.Span, argument.Out
                        ? $"operand {k + 1} of {call.Name} is not written; remove 'out'"
                        : $"operand {k + 1} of {call.Name} is written; mark it 'out'");
                operands[k] = Atom(argument.Value, $"operand {k + 1} of {call.Name}");
            }
            Add(intrinsic.Opcode, operands);
        }

        private IrOperand Atom(Expr expression, string what)
            => expression is OperandExpr atom
                ? Operand(atom.Operand)
                : throw new OmsCompileException(expression.Span,
                    $"{what} must be a single operand at this stage");

        private string Resolve(JumpTarget target, SourceSpan span)
        {
            if (target.Kind == JumpKind.Label) return target.Label!;
            if (_loops.Count == 0)
                throw new OmsCompileException(span, $"'{target.Kind.ToString().ToLowerInvariant()}' outside a loop");
            return target.Kind == JumpKind.Break ? _loops.Peek().Break : _loops.Peek().Continue;
        }

        /// <summary>A label operand placeholder, tagged with the index of its pending reference.</summary>
        private IrOperand LabelOperand(string key, SourceSpan span)
        {
            _pendingLabels.Add((key, span));
            return new IrOperand(0, IrOperandKind.CodeLabel, PendingTag | (uint)(_pendingLabels.Count - 1));
        }

        private void CheckArgc(int opcode, int count, SourceSpan span)
        {
            if (table == null) return;
            int argc = table.Argc(opcode);
            if (argc < 0)
                throw new OmsCompileException(span, $"opcode 0x{opcode:x} is not in ABI {table.AbiId}");
            if (argc != count)
                throw new OmsCompileException(span, $"opcode 0x{opcode:x} takes {argc} operands, not {count}");
        }

        private void Add(int opcode, IrOperand[] operands) => _code.Add(new IrInstruction(opcode, operands));

        private IrOperand Operand(RawOperand operand)
        {
            switch (operand)
            {
                case RawInteger integer:
                    return IrOperand.Literal(0, integer.Value);
                case RawFloat number:
                    return IrOperand.Literal(1, BitConverter.SingleToUInt32Bits(number.Value));
                case RawStorage storage:
                    return IrOperand.Literal(StorageBanks.TypeByBank[storage.Bank], storage.Index);
                case RawLabelRef label:
                    return LabelOperand(label.Name, label.Span);
                case RawString text:
                    _occurrences.Add(new Occurrence(EncodeText(text.Text, text.Span), text.Span));
                    return IrOperand.Data(2, _occurrences.Count - 1);
                case RawArray array:
                    _occurrences.Add(new Occurrence(IrDataItem.FromInts(array.Values), array.Span));
                    return IrOperand.Data(0, _occurrences.Count - 1);
                case RawDataBlock block:
                    _occurrences.Add(new Occurrence(new IrDataItem(IrDataKind.Raw, block.Dwords.ToArray()), block.Span));
                    return IrOperand.Data(0, _occurrences.Count - 1);
                case RawTyped typed:
                    IrOperand inner = Operand(typed.Inner);
                    return inner with { Type = typed.Type };
                default:
                    throw new OmsCompileException(operand.Span, $"unsupported operand {operand.GetType().Name}");
            }
        }

        public void ResolveLabels()
        {
            for (int k = 0; k < _code.Count; k++)
            {
                IrInstruction instruction = _code[k];
                if (!instruction.Operands.Any(IsPending)) continue;
                _code[k] = instruction with
                {
                    Operands = instruction.Operands.Select(operand =>
                    {
                        if (!IsPending(operand)) return operand;
                        var (key, span) = _pendingLabels[(int)(operand.Value & ~PendingTag)];
                        return _labels.TryGetValue(key, out int target)
                            ? IrOperand.Label(operand.Type, target)
                            : throw new OmsCompileException(span, $"unknown label '{key}'");
                    }).ToArray(),
                };
            }
        }

        private static bool IsPending(IrOperand operand)
            => operand.Kind == IrOperandKind.CodeLabel && (operand.Value & PendingTag) != 0;

        public IrScript Build(string name, string abiId, OmsLayout? layout)
        {
            // Data: canonical is one item per occurrence in code order; the sidecar may reorder, share or add raw slots.
            var slotOfOccurrence = new int[_occurrences.Count];
            var data = new List<IrDataItem>();
            IrDataItem Content(int occurrence)
                => layout?.ItemDwords != null && layout.ItemDwords.TryGetValue(occurrence, out uint[]? exact)
                    ? _occurrences[occurrence].Item with { Dwords = exact }
                    : _occurrences[occurrence].Item;
            if (layout?.Data == null)
            {
                for (int k = 0; k < _occurrences.Count; k++)
                {
                    slotOfOccurrence[k] = k;
                    data.Add(Content(k));
                }
            }
            else
            {
                var seen = new bool[_occurrences.Count];
                foreach (OmsDataSlot slot in layout.Data)
                {
                    if (slot.Refs is { Length: > 0 } refs)
                    {
                        foreach (int r in refs)
                        {
                            if (r < 0 || r >= _occurrences.Count || seen[r])
                                throw new InvalidDataException($"layout data slot names invalid or repeated occurrence {r}");
                            seen[r] = true;
                            slotOfOccurrence[r] = data.Count;
                        }
                        data.Add(Content(refs[0]));
                    }
                    else if (slot.Raw != null)
                        data.Add(new IrDataItem(IrDataKind.Raw, slot.Raw));
                    else
                        throw new InvalidDataException("layout data slot has neither refs nor raw dwords");
                }
                int missing = Array.IndexOf(seen, false);
                if (missing >= 0) throw new InvalidDataException($"layout data omits occurrence {missing}");
            }
            for (int k = 0; k < _code.Count; k++)
            {
                IrInstruction instruction = _code[k];
                if (!instruction.Operands.Any(o => o.Kind == IrOperandKind.DataRef)) continue;
                _code[k] = instruction with
                {
                    Operands = instruction.Operands.Select(o => o.Kind == IrOperandKind.DataRef
                        ? IrOperand.Data(o.Type, slotOfOccurrence[o.Value]) : o).ToArray(),
                };
            }

            uint[][]? tables = layout?.Tables;
            return new IrScript
            {
                Name = name,
                Magic = layout?.Magic != null
                    ? System.Text.Encoding.ASCII.GetBytes(layout.Magic) : OmsCanonical.MagicFor(abiId),
                LocalCounts = layout?.LocalCounts ?? OmsCanonical.LocalCounts(_code),
                Field6 = layout?.Field6 ?? OmsCanonical.Field6,
                Code = _code,
                Data = data,
                Tables = tables == null ? IrTables.Derived : new IrTables(tables[0], tables[1], tables[2]),
            };
        }
    }

    private static IrDataItem EncodeText(string text, SourceSpan span)
    {
        try
        {
            return IrDataItem.FromText(text);
        }
        catch (System.Text.EncoderFallbackException error)
        {
            throw new OmsCompileException(span, $"text is not encodable in CP932: {error.Message}");
        }
    }
}
