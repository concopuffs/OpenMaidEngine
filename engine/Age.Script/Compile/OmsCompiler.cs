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
/// Compiles a parsed script unit to the intermediate form using the canonical lowerings of spec §7. Expressions
/// lower through <see cref="ExpressionLowering"/> with the canonical temporary plan: integer temporaries from the
/// pool above the highest local integer the source names, reference temporaries from slot 0. Data items are one per
/// data-operand occurrence in code order (spec §7.5). An optional layout sidecar restores any non-canonical original
/// layout, including the pool base and per-statement temporary slots.
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
        var generator = new CodeGenerator(table, intrinsics, layout);
        generator.EmitAll(unit.Body);
        generator.ResolveLabels();
        return generator.Build(unit.Name, abiId);
    }

    private sealed record Occurrence(IrDataItem Item, SourceSpan Span);

    private sealed class CodeGenerator(OpcodeTable? table, IntrinsicCatalog? intrinsics, OmsLayout? layout)
    {
        private readonly List<IrInstruction> _code = new();
        private readonly List<Occurrence> _occurrences = new();
        private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
        private readonly HashSet<string> _userLabels = new(StringComparer.Ordinal);
        private readonly List<(string Key, SourceSpan Span)> _pendingLabels = new();
        private const uint PendingTag = 0x80000000;
        private readonly Stack<(string Break, string Continue)> _loops = new();
        private int _internal;
        private uint _tempBase;

        private string NewLabel() => $"#{_internal++}";

        private void Define(string key, SourceSpan span)
        {
            if (!_labels.TryAdd(key, _code.Count))
                throw new OmsCompileException(span, $"label '{key}' is defined twice");
        }

        public void EmitAll(IReadOnlyList<Statement> statements)
        {
            CollectUserLabels(statements);
            long highest = -1;
            foreach (RawOperand operand in OperandsOf(statements))
                if (operand is RawStorage { Bank: "L" } local) highest = Math.Max(highest, local.Index);
            _tempBase = layout?.TempBase ?? (uint)(highest + 1);
            foreach (Statement statement in statements) Emit(statement);
        }

        /// <summary>Every raw operand written in the statements, for the canonical pool base.</summary>
        private static IEnumerable<RawOperand> OperandsOf(IEnumerable<Statement> statements)
        {
            var pending = new Stack<object>(statements.Reverse());
            while (pending.Count > 0)
            {
                switch (pending.Pop())
                {
                    case RawOperand raw:
                        yield return raw;
                        if (raw is RawTyped typed) pending.Push(typed.Inner);
                        break;
                    case OperandExpr atom: pending.Push(atom.Operand); break;
                    case ElementExpr element:
                        pending.Push(element.Base);
                        if (element.Stride != null) pending.Push(element.Stride);
                        foreach (Expr index in element.Indices) pending.Push(index);
                        break;
                    case BinaryExpr binary: pending.Push(binary.Left); pending.Push(binary.Right); break;
                    case UnaryExpr unary: pending.Push(unary.Operand); break;
                    case RawOpStatement raw: foreach (RawOperand operand in raw.Operands) pending.Push(operand); break;
                    case AssignStatement assign: pending.Push(assign.Target); pending.Push(assign.Value); break;
                    case CallStatement call: foreach (CallArgument argument in call.Arguments) pending.Push(argument.Value); break;
                    case ConditionalJumpStatement jump: pending.Push(jump.Condition); break;
                    case IfStatement branch:
                        pending.Push(branch.Condition);
                        foreach (Statement inner in branch.Then) pending.Push(inner);
                        if (branch.Else != null) foreach (Statement inner in branch.Else) pending.Push(inner);
                        break;
                    case LoopStatement loop: foreach (Statement inner in loop.Body) pending.Push(inner); break;
                    case WhileStatement loop:
                        pending.Push(loop.Condition);
                        foreach (Statement inner in loop.Body) pending.Push(inner);
                        break;
                    case DoWhileStatement loop:
                        pending.Push(loop.Condition);
                        foreach (Statement inner in loop.Body) pending.Push(inner);
                        break;
                    case ForStatement loop:
                        if (loop.Init != null) pending.Push(loop.Init);
                        if (loop.Condition != null) pending.Push(loop.Condition);
                        if (loop.Step != null) pending.Push(loop.Step);
                        foreach (Statement inner in loop.Body) pending.Push(inner);
                        break;
                    case AtomicStatement atomic: foreach (Statement inner in atomic.Body) pending.Push(inner); break;
                    case RunStatement run: pending.Push(run.Script); break;
                    case NoteStatement note: pending.Push(note.Value); break;
                    case IndirectCallStatement call: pending.Push(call.Target); break;
                }
            }
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
                    case ForStatement s: CollectUserLabels(s.Body); break;
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
                    IrOperand target = LabelOperand(Resolve(jump.Target, jump.Span), jump.Span);
                    IrOperand otherwise = jump.Else != null
                        ? LabelOperand(Resolve(jump.Else, jump.Span), jump.Span) : IrOperand.Literal(0, NoTarget);
                    if (jump.Negated) EmitJcc(jump.Condition, otherwise, target, jump.Span);
                    else EmitJcc(jump.Condition, target, otherwise, jump.Span);
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
                    EmitJcc(loop.Condition, LabelOperand(head, loop.Span), IrOperand.Literal(0, NoTarget), loop.Span);
                    Define(end, loop.Span);
                    break;
                }
                case ForStatement loop:
                {
                    // init; jmp cond; inc: step; cond: test; body; jmp inc; end:  (spec §7.1)
                    if (loop.Init != null) Emit(loop.Init);
                    string increment = NewLabel(), condition = NewLabel(), end = NewLabel();
                    Add(Jmp, [LabelOperand(condition, loop.Span)]);
                    Define(increment, loop.Span);
                    if (loop.Step != null) Emit(loop.Step);
                    Define(condition, loop.Span);
                    if (loop.Condition != null) EmitBranchUnless(loop.Condition, end, loop.Span);
                    EmitLoopBody(loop.Body, end, increment);
                    Add(Jmp, [LabelOperand(increment, loop.Span)]);
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
                    Lower(new LInstruction(CallScript, [ToL(run.Script)]), run.Span);
                    break;
                case NoteStatement note:
                    Add(Comment, [Atom(note.Value, "a note")]);
                    break;
                case IndirectCallStatement call:
                    Lower(new LInstruction(Call, [ToL(call.Target)]), call.Span);
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

        /// <summary>Jump to <paramref name="target"/> when the condition is false (spec §7.1: jcc c, -1, target).</summary>
        private void EmitBranchUnless(Expr condition, string target, SourceSpan span)
            => EmitJcc(condition, IrOperand.Literal(0, NoTarget), LabelOperand(target, span), span);

        /// <summary>Evaluates the condition and branches; a top-level <c>!</c> swaps the jcc targets instead of
        /// synthesizing a test.</summary>
        private void EmitJcc(Expr condition, IrOperand whenTrue, IrOperand whenFalse, SourceSpan span)
        {
            if (condition is UnaryExpr { Op: "!" } not)
            {
                condition = not.Operand;
                (whenTrue, whenFalse) = (whenFalse, whenTrue);
            }
            Lower(new LInstruction(Jcc, [ToL(condition), new LLeaf(whenTrue), new LLeaf(whenFalse)]), span);
        }

        private void EmitAssign(AssignStatement assign)
        {
            LExpr target = LValue(assign.Target);
            if (assign.CompoundOp == null)
            {
                Lower(new LAssign(target, ToL(assign.Value), false), assign.Span);
                return;
            }
            if (!BinaryOpcodes.TryGetValue(assign.CompoundOp, out int opcode))
                throw new OmsCompileException(assign.Span, $"unsupported compound assignment {assign.CompoundOp}=");
            Lower(new LAssign(target, new LBinary(opcode, LValue(assign.Target), ToL(assign.Value)), true), assign.Span);
        }

        /// <summary>Lowers one statement with the canonical temporary plan, or the sidecar's override for the
        /// statement starting at this instruction.</summary>
        private void Lower(LStatement statement, SourceSpan span)
        {
            int site = _code.Count;
            OmsTempOverride? entry = null;
            layout?.Temps?.TryGetValue(site, out entry);
            var temps = new CanonicalTemps(statement, _tempBase, layout?.RefSwap == true, entry?.Ints, entry?.Refs);
            try
            {
                ExpressionLowering.Lower(statement, temps.Allocate, _code.Add);
            }
            catch (InvalidDataException error)
            {
                throw new OmsCompileException(span, error.Message);
            }
            if (entry != null && (entry.Ints != null && entry.Ints.Length != temps.IntCount
                                  || entry.Refs != null && entry.Refs.Length != temps.RefCount))
                throw new OmsCompileException(span, $"the sidecar's temporaries for the statement at {site} do not fit it");
        }

        private static readonly LLeaf Zero = new(IrOperand.Literal(0, 0));

        /// <summary>An expression in lowered form (spec §6.1): operators map to their opcodes; unary minus, <c>!</c>,
        /// <c>~</c> and <c>^</c> are synthesized; <c>&amp;&amp;</c> and <c>||</c> combine normalized booleans.</summary>
        private LExpr ToL(Expr expression)
        {
            switch (expression)
            {
                case OperandExpr atom:
                    return new LLeaf(Operand(atom.Operand));
                case ElementExpr element:
                {
                    IrOperand cell = Operand(element.Base);
                    int pointer = cell.Type switch
                    {
                        0x3 or 0x9 => 0xC,
                        0x4 or 0xA => 0xD,
                        0x5 or 0xB => 0xE,
                        _ => throw new OmsCompileException(element.Span,
                            "element access needs an integer, float or string cell as its base"),
                    };
                    LExpr[] indices = element.Stride == null
                        ? [ToL(element.Indices[0])]
                        : [ToL(element.Indices[0]), new LLeaf(Operand(element.Stride)), ToL(element.Indices[1])];
                    return new LElement(cell, indices, pointer);
                }
                case BinaryExpr { Op: "&&" or "||" } logical:
                    return new LBinary(logical.Op == "&&" ? 0x56 : 0x57, Boolean(logical.Left), Boolean(logical.Right));
                case BinaryExpr { Op: "^" } xor:
                    return new LBinary(0x51, new LBinary(0x57, ToL(xor.Left), ToL(xor.Right)),
                        new LBinary(0x56, ToL(xor.Left), ToL(xor.Right)));
                case BinaryExpr binary when BinaryOpcodes.TryGetValue(binary.Op, out int opcode):
                    return new LBinary(opcode, ToL(binary.Left), ToL(binary.Right));
                case UnaryExpr { Op: "-" } negation:
                    return new LBinary(0x51, Zero, ToL(negation.Operand));
                case UnaryExpr { Op: "!" } not:
                    return new LBinary(0x5A, ToL(not.Operand), Zero);
                case UnaryExpr { Op: "~" } complement:
                    return new LBinary(0x51, new LLeaf(IrOperand.Literal(0, NoTarget)), ToL(complement.Operand));
                default:
                    throw new OmsCompileException(expression.Span, $"unsupported expression {expression.GetType().Name}");
            }
        }

        private static bool IsBoolean(Expr expression)
            => expression is BinaryExpr { Op: "==" or "!=" or "<" or "<=" or ">" or ">=" or "&&" or "||" }
                   or UnaryExpr { Op: "!" };

        private LExpr Boolean(Expr expression)
            => IsBoolean(expression) ? ToL(expression) : new LBinary(0x5B, ToL(expression), Zero);

        /// <summary>An assignable operand: a storage cell or an element.</summary>
        private LExpr LValue(Expr expression)
            => expression is OperandExpr { Operand: RawStorage } or ElementExpr
                ? ToL(expression)
                : throw new OmsCompileException(expression.Span, "only a storage cell or an element can be assigned");

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
            var operands = new LExpr[call.Arguments.Count];
            for (int k = 0; k < operands.Length; k++)
            {
                CallArgument argument = call.Arguments[k];
                if (intrinsic.HasContract && argument.Out != intrinsic.OutOperands.Contains(k + 1))
                    throw new OmsCompileException(argument.Value.Span, argument.Out
                        ? $"operand {k + 1} of {call.Name} is not written; remove 'out'"
                        : $"operand {k + 1} of {call.Name} is written; mark it 'out'");
                operands[k] = argument.Out ? LValue(argument.Value) : ToL(argument.Value);
            }
            Lower(new LInstruction(intrinsic.Opcode, operands), call.Span);
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

        public IrScript Build(string name, string abiId)
        {
            // Occurrences are numbered in code order (expressions may create them in another order).
            var createdAt = new List<int>();
            foreach (IrInstruction instruction in _code)
                foreach (IrOperand operand in instruction.Operands)
                    if (operand.Kind == IrOperandKind.DataRef) createdAt.Add((int)operand.Value);
            if (createdAt.Count != _occurrences.Count || createdAt.Distinct().Count() != createdAt.Count)
                throw new InvalidOperationException("each data occurrence must be emitted exactly once");
            var codeOrder = new int[_occurrences.Count];
            for (int k = 0; k < createdAt.Count; k++) codeOrder[createdAt[k]] = k;
            var occurrences = createdAt.Select(created => _occurrences[created]).ToList();

            // Data: canonical is one item per occurrence in code order; the sidecar may reorder, share or add raw slots.
            var slotOfOccurrence = new int[occurrences.Count];
            var data = new List<IrDataItem>();
            IrDataItem Content(int occurrence)
                => layout?.ItemDwords != null && layout.ItemDwords.TryGetValue(occurrence, out uint[]? exact)
                    ? occurrences[occurrence].Item with { Dwords = exact }
                    : occurrences[occurrence].Item;
            if (layout?.Data == null)
            {
                for (int k = 0; k < occurrences.Count; k++)
                {
                    slotOfOccurrence[k] = k;
                    data.Add(Content(k));
                }
            }
            else
            {
                var seen = new bool[occurrences.Count];
                foreach (OmsDataSlot slot in layout.Data)
                {
                    if (slot.Refs is { Length: > 0 } refs)
                    {
                        foreach (int r in refs)
                        {
                            if (r < 0 || r >= occurrences.Count || seen[r])
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
                        ? IrOperand.Data(o.Type, slotOfOccurrence[codeOrder[o.Value]]) : o).ToArray(),
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
