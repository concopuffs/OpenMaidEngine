using System.Text;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Syntax;

namespace Age.Script.Text;

/// <summary>
/// Core-level decompiler, stage C1 (spec §11 steps 3 and 7): structured control flow and named statements over
/// explicit three-address operands. Every construct lowers back to exactly the instructions it groups, so the
/// result recompiles byte-identically with the same layout sidecar as the raw level. Else-if chains are a single
/// node so that recursion depth follows real nesting, not chain length.
/// </summary>
public static class OmsCoreDecompiler
{
    private const uint NoTarget = 0xFFFFFFFF;
    private static readonly Dictionary<int, string> BinaryOperators =
        OmsCompiler.BinaryOpcodes.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static OmsDecompilation Decompile(IrScript script, string abiId, IntrinsicCatalog intrinsics)
    {
        ArgumentNullException.ThrowIfNull(script);
        var context = new Context(script, intrinsics);
        List<Node> top = context.Structure(0, script.Code.Count, null);
        var text = new StringBuilder();
        text.Append("oms 1\n");
        text.Append("script ").Append(OmsRawDecompiler.Quote(script.Name)).Append('\n');
        if (script.Code.Count > 0) text.Append('\n');
        context.CollectLabels(top, null);
        context.Print(text, top, script.Code.Count, ownsEnd: true, depth: 1, null, skipLabel: -1);
        return new OmsDecompilation(text.ToString(), OmsRawDecompiler.BuildLayout(script, abiId));
    }

    private abstract record Node(int Start);
    private sealed record SimpleNode(int Index) : Node(Index);

    /// <summary>One <c>if</c>/<c>else if</c> arm: its jcc, then-body, and the index the body ends at. When
    /// <see cref="JumpsToEnd"/>, the body is followed by the arm's <c>jmp end</c> at <see cref="ThenEnd"/>.</summary>
    private sealed record Arm(int Jcc, List<Node> Then, int ThenEnd, bool JumpsToEnd);

    private sealed record IfChainNode(List<Arm> Arms, List<Node>? Else, int End) : Node(Arms[0].Jcc);
    private sealed record LoopNode(int Head, List<Node> Body, int BackJump, Loop Loop) : Node(Head);
    private sealed record DoWhileNode(int Head, List<Node> Body, int Test, Loop Loop) : Node(Head);
    private sealed record AtomicNode(int Begin, List<Node> Body, int End) : Node(Begin);

    /// <summary>A loop's break and continue destinations (instruction indices).</summary>
    private sealed record Loop(int Break, int Continue, Loop? Outer);

    private sealed class Context(IrScript script, IntrinsicCatalog intrinsics)
    {
        private readonly List<IrInstruction> _code = script.Code;
        private readonly HashSet<int> _labels = new();
        private readonly int[] _offsets = Offsets(script.Code);
        private readonly Dictionary<int, List<int>> _incoming = References(script.Code, transfersOnly: true);
        private readonly Dictionary<int, List<int>> _references = References(script.Code, transfersOnly: false);

        private static int[] Offsets(List<IrInstruction> code)
        {
            var offsets = new int[code.Count + 1];
            for (int k = 0; k < code.Count; k++) offsets[k + 1] = offsets[k] + code[k].SizeInDwords;
            return offsets;
        }

        private static Dictionary<int, List<int>> References(List<IrInstruction> code, bool transfersOnly)
        {
            var incoming = new Dictionary<int, List<int>>();
            for (int s = 0; s < code.Count; s++)
            {
                if (transfersOnly && !IsTransfer(code[s])) continue;
                foreach (IrOperand operand in code[s].Operands)
                    if (operand.Kind == IrOperandKind.CodeLabel)
                    {
                        if (!incoming.TryGetValue((int)operand.Value, out List<int>? sources))
                            incoming[(int)operand.Value] = sources = new List<int>();
                        sources.Add(s);
                    }
            }
            return incoming;
        }

        private string LabelName(int index) => index >= _code.Count
            ? "label_end" : $"label_{_code[index].OriginalOffset ?? _offsets[index]:x}";

        private static bool IsTransfer(IrInstruction instruction)
            => instruction.Opcode is OmsCompiler.Jmp or OmsCompiler.Jcc or 0xA2 or 0xA3;

        private static bool IsJump(IrInstruction instruction, out int target)
        {
            target = -1;
            if (instruction.Opcode != OmsCompiler.Jmp || instruction.Operands.Count != 1) return false;
            IrOperand operand = instruction.Operands[0];
            if (operand.Kind != IrOperandKind.CodeLabel || operand.Type != 0) return false;
            target = (int)operand.Value;
            return true;
        }

        /// <summary>jcc c, -1, L (branch when false) or jcc c, L, -1 (branch when true) with plain label operands.</summary>
        private static bool IsBranch(IrInstruction instruction, out bool whenTrue, out int target)
        {
            whenTrue = false;
            target = -1;
            if (instruction.Opcode != OmsCompiler.Jcc || instruction.Operands.Count != 3) return false;
            IrOperand onTrue = instruction.Operands[1], onFalse = instruction.Operands[2];
            if (onTrue == IrOperand.Literal(0, NoTarget) && onFalse.Kind == IrOperandKind.CodeLabel && onFalse.Type == 0)
            {
                target = (int)onFalse.Value;
                return true;
            }
            if (onFalse == IrOperand.Literal(0, NoTarget) && onTrue.Kind == IrOperandKind.CodeLabel && onTrue.Type == 0)
            {
                whenTrue = true;
                target = (int)onTrue.Value;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------ structure

        public List<Node> Structure(int lo, int hi, Loop? loop)
        {
            var nodes = new List<Node>();
            int k = lo;
            while (k < hi)
            {
                int back = BackEdge(k, hi, out bool doWhile);
                if (back >= k)
                {
                    if (doWhile)
                    {
                        var inner = new Loop(back + 1, back, loop);
                        nodes.Add(new DoWhileNode(k, Structure(k, back, inner), back, inner));
                    }
                    else
                    {
                        var inner = new Loop(back + 1, k, loop);
                        nodes.Add(new LoopNode(k, Structure(k, back, inner), back, inner));
                    }
                    k = back + 1;
                    continue;
                }
                if (IfShape(k, hi, out int skip, out int end))
                {
                    // Plan the arms first: (jcc, skip, end) per arm while the else region is exactly one more if.
                    var plan = new List<(int Jcc, int Skip, int End)> { (k, skip, end) };
                    while (plan[^1].End > plan[^1].Skip
                           && CanChain(plan[^1].Skip, plan[^1].End, out int nextSkip, out int nextEnd))
                        plan.Add((plan[^1].Skip, nextSkip, nextEnd));
                    int chainEnd = end;
                    int armJumps = plan.Count(arm => arm.End > arm.Skip);
                    // Only forward jumps compete for the join; a backward jump to it belongs to a loop starting there.
                    bool privateJoin = end == skip
                        || (_incoming.TryGetValue(chainEnd, out List<int>? joins)
                            && joins.Count(source => source < chainEnd) == armJumps);
                    if (!privateJoin)
                    {
                        // The join label is shared with other code: the trailing jmp is a goto, not an else.
                        nodes.Add(new IfChainNode([new Arm(k, Structure(k + 1, skip, loop), skip, false)], null, skip));
                        k = skip;
                        continue;
                    }
                    var arms = new List<Arm>();
                    List<Node>? otherwise = null;
                    foreach (var (jcc, armSkip, armEnd) in plan)
                    {
                        if (armEnd > armSkip)
                            arms.Add(new Arm(jcc, Structure(jcc + 1, armSkip - 1, loop), armSkip - 1, true));
                        else
                            arms.Add(new Arm(jcc, Structure(jcc + 1, armSkip, loop), armSkip, false));
                    }
                    var (_, lastSkip, lastEnd) = plan[^1];
                    if (lastEnd > lastSkip) otherwise = Structure(lastSkip, lastEnd, loop);
                    nodes.Add(new IfChainNode(arms, otherwise, chainEnd));
                    k = chainEnd;
                    continue;
                }
                if (_code[k].Opcode == OmsCompiler.BlockBegin && MatchingBlockEnd(k, hi) is int close and >= 0)
                {
                    nodes.Add(new AtomicNode(k, Structure(k + 1, close, loop), close));
                    k = close + 1;
                    continue;
                }
                nodes.Add(new SimpleNode(k));
                k++;
            }
            return nodes;
        }

        /// <summary>The outermost back edge to k inside [k, hi): a jmp (loop) or a branch-when-true (do-while).</summary>
        private int BackEdge(int k, int hi, out bool doWhile)
        {
            doWhile = false;
            if (!_incoming.TryGetValue(k, out List<int>? sources)) return -1;
            int best = -1;
            foreach (int j in sources)
            {
                if (j < k || j >= hi || j <= best) continue;
                if (IsJump(_code[j], out int t) && t == k) { best = j; doWhile = false; }
                else if (IsBranch(_code[j], out bool whenTrue, out int b) && whenTrue && b == k) { best = j; doWhile = true; }
            }
            return best;
        }

        /// <summary>An if at k: branch-when-false to skip, with a non-empty then-body; end > skip when the body
        /// ends in <c>jmp end</c> and [skip, end) is an else region inside the current region.</summary>
        private bool IfShape(int k, int hi, out int skip, out int end)
        {
            end = -1;
            if (!IsBranch(_code[k], out bool whenTrue, out skip) || whenTrue || skip <= k + 1 || skip > hi) return false;
            end = skip;
            if (skip - 1 > k + 1 && IsJump(_code[skip - 1], out int after) && after > skip && after <= hi) end = after;
            return true;
        }

        /// <summary>Whether [s, e) is exactly one if/if-else (so it can print as <c>else if</c>), not a loop head,
        /// and referenced only by the previous arm's branch (no label is needed between else and if).</summary>
        private bool CanChain(int s, int e, out int skip, out int end)
        {
            skip = end = -1;
            if (_references.TryGetValue(s, out List<int>? refs) && refs.Count > 1) return false;
            if (BackEdge(s, e, out _) >= s) return false;
            if (!IfShape(s, e, out skip, out end)) return false;
            return end == e;
        }

        /// <summary>The 0x1f5 closing the block opened at k, when no control transfer crosses the block.</summary>
        private int MatchingBlockEnd(int k, int hi)
        {
            int depth = 0;
            for (int m = k; m < hi; m++)
            {
                if (_code[m].Opcode == OmsCompiler.BlockBegin) depth++;
                else if (_code[m].Opcode == OmsCompiler.BlockEnd && --depth == 0)
                {
                    for (int s = k + 1; s < m; s++)
                        if (IsTransfer(_code[s]))
                            foreach (IrOperand operand in _code[s].Operands)
                                if (operand.Kind == IrOperandKind.CodeLabel && !(operand.Value > k && operand.Value <= m))
                                    return -1;
                    for (int t = k + 1; t <= m; t++)
                        if (_incoming.TryGetValue(t, out List<int>? sources) && sources.Any(s => s <= k || s >= m))
                            return -1;
                    if (_incoming.TryGetValue(k, out List<int>? back) && back.Any(s => s > k && s < m)) return -1;
                    return m;
                }
            }
            return -1;
        }

        // ------------------------------------------------------------------------------------------ labels

        public void CollectLabels(List<Node> nodes, Loop? loop)
        {
            foreach (Node node in nodes)
                switch (node)
                {
                    case SimpleNode simple:
                        CollectSimple(_code[simple.Index], loop);
                        break;
                    case IfChainNode chain:
                        foreach (Arm arm in chain.Arms) CollectLabels(arm.Then, loop);
                        if (chain.Else != null) CollectLabels(chain.Else, loop);
                        break;
                    case LoopNode loopNode:
                        CollectLabels(loopNode.Body, loopNode.Loop);
                        break;
                    case DoWhileNode doNode:
                        CollectLabels(doNode.Body, doNode.Loop);
                        break;
                    case AtomicNode atomic:
                        CollectLabels(atomic.Body, loop);
                        break;
                }
        }

        private void CollectSimple(IrInstruction instruction, Loop? loop)
        {
            if (IsJump(instruction, out int target))
            {
                if (JumpWord(target, loop) == null) _labels.Add(target);
                return;
            }
            if (IsBranch(instruction, out _, out int branch))
            {
                if (JumpWord(branch, loop) == null) _labels.Add(branch);
                return;
            }
            if (IsTwoTargetBranch(instruction))
            {
                foreach (IrOperand operand in instruction.Operands.Skip(1))
                    if (JumpWord((int)operand.Value, loop) == null) _labels.Add((int)operand.Value);
                return;
            }
            foreach (IrOperand operand in instruction.Operands)
                if (operand.Kind == IrOperandKind.CodeLabel) _labels.Add((int)operand.Value);
        }

        private static bool IsTwoTargetBranch(IrInstruction instruction)
            => instruction.Opcode == OmsCompiler.Jcc && instruction.Operands.Count == 3
               && instruction.Operands[0].Kind != IrOperandKind.CodeLabel
               && instruction.Operands.Skip(1).All(o => o is { Kind: IrOperandKind.CodeLabel, Type: 0 });

        private static string? JumpWord(int target, Loop? loop)
            => loop == null ? null : target == loop.Break ? "break" : target == loop.Continue ? "continue" : null;

        // ------------------------------------------------------------------------------------------ printing

        private void PrintLabel(StringBuilder text, int index, int depth)
            => text.Append(new string(' ', (depth - 1) * 4)).Append(LabelName(index)).Append(":\n");

        public void Print(StringBuilder text, List<Node> nodes, int end, bool ownsEnd, int depth, Loop? loop, int skipLabel)
        {
            string pad = new(' ', depth * 4);
            foreach (Node node in nodes)
            {
                if (_labels.Contains(node.Start) && node.Start != skipLabel) PrintLabel(text, node.Start, depth);
                skipLabel = -1;
                switch (node)
                {
                    case SimpleNode simple:
                        text.Append(pad).Append(Statement(simple.Index, loop)).Append('\n');
                        break;
                    case IfChainNode chain:
                        for (int a = 0; a < chain.Arms.Count; a++)
                        {
                            Arm arm = chain.Arms[a];
                            text.Append(pad).Append(a == 0 ? "if " : "} else if ")
                                .Append(Atom(_code[arm.Jcc].Operands[0])).Append(" {\n");
                            Print(text, arm.Then, arm.ThenEnd, ownsEnd: arm.JumpsToEnd, depth + 1, loop, -1);
                        }
                        if (chain.Else != null)
                        {
                            text.Append(pad).Append("} else {\n");
                            Print(text, chain.Else, chain.End, ownsEnd: false, depth + 1, loop, -1);
                        }
                        text.Append(pad).Append("}\n");
                        break;
                    case LoopNode loopNode:
                        text.Append(pad).Append("loop {\n");
                        Print(text, loopNode.Body, loopNode.BackJump, ownsEnd: true, depth + 1, loopNode.Loop, loopNode.Head);
                        text.Append(pad).Append("}\n");
                        break;
                    case DoWhileNode doNode:
                        text.Append(pad).Append("do {\n");
                        Print(text, doNode.Body, doNode.Test, ownsEnd: true, depth + 1, doNode.Loop, doNode.Head);
                        text.Append(pad).Append("} while ").Append(Atom(_code[doNode.Test].Operands[0])).Append('\n');
                        break;
                    case AtomicNode atomic:
                        text.Append(pad).Append("atomic {\n");
                        Print(text, atomic.Body, atomic.End, ownsEnd: true, depth + 1, loop, -1);
                        text.Append(pad).Append("}\n");
                        break;
                }
            }
            if (ownsEnd && _labels.Contains(end)) PrintLabel(text, end, depth);
        }

        private string Atom(IrOperand operand) => OmsRawDecompiler.Operand(operand, script, LabelName);

        private static bool IsStorage(IrOperand operand)
            => operand.Kind == IrOperandKind.Literal && StorageBanks.BankByType.ContainsKey(operand.Type);

        private string Target(int target, Loop? loop) => JumpWord(target, loop) ?? $"goto {LabelName(target)}";

        private string Statement(int index, Loop? loop)
        {
            IrInstruction instruction = _code[index];
            IReadOnlyList<IrOperand> operands = instruction.Operands;
            switch (instruction.Opcode)
            {
                case OmsCompiler.Mov when operands.Count == 2 && IsStorage(operands[0]):
                    return $"{Atom(operands[0])} = {Atom(operands[1])}";
                case int op when BinaryOperators.TryGetValue(op, out string? symbol) && operands.Count == 3
                                 && IsStorage(operands[0]):
                    return $"{Atom(operands[0])} = {Atom(operands[1])} {symbol} {Atom(operands[2])}";
                case OmsCompiler.Jmp when IsJump(instruction, out int target):
                    return Target(target, loop);
                case OmsCompiler.Jcc when IsBranch(instruction, out bool whenTrue, out int target)
                                          && operands[0].Kind != IrOperandKind.CodeLabel:
                    return $"if {(whenTrue ? "" : "!")}{Atom(operands[0])} {Target(target, loop)}";
                case OmsCompiler.Jcc when IsTwoTargetBranch(instruction):
                    return $"if {Atom(operands[0])} {Target((int)operands[1].Value, loop)} else "
                           + Target((int)operands[2].Value, loop);
                case OmsCompiler.Call when operands.Count == 1 && operands[0] is { Kind: IrOperandKind.CodeLabel, Type: 0 }:
                    return $"{LabelName((int)operands[0].Value)}()";
                case OmsCompiler.Ret when operands.Count == 0:
                    return "return";
                case OmsCompiler.Exit when operands.Count == 0:
                    return "exit";
                case OmsCompiler.CallScript when operands.Count == 1 && operands[0].Kind == IrOperandKind.Literal:
                    return $"run {Atom(operands[0])}";
                case OmsCompiler.Comment when operands.Count == 1 && operands[0].Kind != IrOperandKind.CodeLabel:
                    return $"note {Atom(operands[0])}";
                case OmsCompiler.Call when operands.Count == 1 && operands[0].Kind == IrOperandKind.Literal:
                    return $"call *{Atom(operands[0])}";
            }
            if (instruction.Opcode is not (OmsCompiler.BlockBegin or OmsCompiler.BlockEnd or OmsCompiler.Jmp
                    or OmsCompiler.Jcc or OmsCompiler.Call or OmsCompiler.Comment)
                && intrinsics.TryGet(instruction.Opcode, out Intrinsic intrinsic) && intrinsic.Argc == operands.Count
                && !IntrinsicCatalog.Keywords.Contains(intrinsic.Name))
            {
                var arguments = operands.Select((operand, k) =>
                    (intrinsic.HasContract && intrinsic.OutOperands.Contains(k + 1) ? "out " : "") + Atom(operand));
                return $"{intrinsic.Name}({string.Join(", ", arguments)})";
            }
            return "@op(0x" + instruction.Opcode.ToString("x") + string.Concat(operands.Select(o => ", " + Atom(o))) + ")";
        }
    }
}
