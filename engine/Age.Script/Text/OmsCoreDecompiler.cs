using System.Text;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Modules;
using Age.Script.Syntax;

namespace Age.Script.Text;

/// <summary>
/// Core-level decompiler (spec §11 steps 3 and 7). Stage C1 structures control flow; stage C2 folds compiler
/// temporaries back into expressions. A fold is kept only when the compiler's own <see cref="ExpressionLowering"/>
/// reproduces the consumed instructions exactly; where the original temporary slots differ from the canonical plan
/// the sidecar records them, so the result still recompiles byte-identically. Else-if chains are a single node so
/// that recursion depth follows real nesting, not chain length.
/// </summary>
/// <summary>One accepted fold, for allocator diagnostics: the statement's site, its shape, the same shape with the
/// original temporary slots (<c>#k</c> integer temporaries relative to the pool base, <c>@k</c> reference slots), and
/// whether the canonical plan reproduced it.</summary>
public sealed record OmsFoldRecord(int Site, string Shape, string Slots, bool Canonical);

public static class OmsCoreDecompiler
{
    private const uint NoTarget = 0xFFFFFFFF;
    private static readonly Dictionary<int, string> BinaryOperators =
        OmsCompiler.BinaryOpcodes.ToDictionary(pair => pair.Value, pair => pair.Key);

    /// <param name="modules">Game modules whose names replace registry cells and script ids (decompiler step 6);
    /// the source begins with <c>use</c> of each module it relies on.</param>
    /// <param name="idioms">Idiomatic level (spec §11 steps 4 and 5): library subs and template uses of the source
    /// modules are recognized and lifted. The result is kept only if it compiles back to the original, with the
    /// temporary pins re-derived for the lifted code; otherwise the core source comes back with the reason.</param>
    public static OmsDecompilation Decompile(IrScript script, string abiId, IntrinsicCatalog intrinsics, bool fold = true,
                                             Action<OmsFoldRecord>? observe = null, StringBuilder? dump = null,
                                             IReadOnlyList<GameModule>? modules = null, OmsIdioms? idioms = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (idioms == null || idioms.IsEmpty) return DecompileAt(script, abiId, intrinsics, fold, observe, dump, modules, null);
        OmsDecompilation lifted = DecompileAt(script, abiId, intrinsics, fold, observe, dump, modules, idioms);
        if (lifted.LibrarySubs + lifted.TemplateLifts == 0) return lifted;
        string? failure = Verify(lifted, script, abiId, intrinsics, modules ?? [], out SortedDictionary<int, OmsTempOverride>? temps);
        if (failure == null)
        {
            lifted.Layout.Temps = temps!.Count > 0 ? temps : null;
            return lifted;
        }
        return DecompileAt(script, abiId, intrinsics, fold, null, null, modules, null) with { LiftFailure = failure };
    }

    /// <summary>Compiles lifted source in fit mode against the original: null when it reproduces the original exactly,
    /// otherwise why not.</summary>
    private static string? Verify(OmsDecompilation lifted, IrScript script, string abiId, IntrinsicCatalog intrinsics,
                                  IReadOnlyList<GameModule> modules, out SortedDictionary<int, OmsTempOverride>? temps)
    {
        temps = null;
        try
        {
            OmsLayout layout = OmsLayout.FromJson(lifted.Layout.ToJson());
            layout.Temps = null;
            IrScript fitted = OmsCompiler.Fit(Parser.ParseScript(lifted.Source, script.Name), abiId, null, layout, intrinsics,
                name => modules.FirstOrDefault(module => module.Name == name), script, out SortedDictionary<int, OmsTempOverride> pins);
            if (!IrEncoder.Encode(fitted).AsSpan().SequenceEqual(IrEncoder.Encode(script)))
                return "the lifted source compiles to different bytes";
            temps = pins;
            return null;
        }
        catch (Exception error) when (error is OmsCompileException or OmsSyntaxException or InvalidDataException)
        {
            return error.Message;
        }
    }

    private static OmsDecompilation DecompileAt(IrScript script, string abiId, IntrinsicCatalog intrinsics, bool fold,
                                                Action<OmsFoldRecord>? observe, StringBuilder? dump,
                                                IReadOnlyList<GameModule>? modules, OmsIdioms? idioms)
    {
        var folding = new Folding(script.Code, intrinsics, fold, observe);
        var context = new Context(script, intrinsics, folding, modules ?? [], idioms);
        List<Node> top = context.CarveSubs(context.Structure(0, script.Code.Count, null));
        context.RecognizeLibrary(top);
        var body = new StringBuilder();
        context.CollectLabels(top, null);
        context.DeclareLocalArrays();
        context.Print(body, top, script.Code.Count, ownsEnd: true, depth: 1, null, skipLabel: -1);
        if (dump != null) context.Dump(dump, top, "");
        var text = new StringBuilder();
        text.Append("oms 1\n");
        text.Append("script ").Append(OmsRawDecompiler.Quote(script.Name)).Append('\n');
        foreach (GameModule module in modules ?? [])
            if (context.UsedModules.Contains(module)) text.Append("use ").Append(module.Name).Append('\n');
        foreach (LocalArray array in context.LocalArrays)
            text.Append($"local {array.TypeName} {array.Name}[0x{array.Length:x}] @ 0x{array.Base:x}\n");
        if (script.Code.Count > 0) text.Append('\n');
        text.Append(body);
        OmsLayout layout = OmsRawDecompiler.BuildLayout(script, abiId);
        // The compiler derives the pool base and header counts from the named cells and the declared arrays.
        var declared = context.LocalArrays.GroupBy(array => array.Type)
            .ToDictionary(group => group.Key, group => group.Max(array => (long)array.Base + array.Length - 1));
        long derivedBase = Math.Max(context.MaxExplicitLocal, declared.GetValueOrDefault(0x9, -1)) + 1;
        if (folding.UsesIntTemps && derivedBase != folding.PoolBase) layout.TempBase = folding.PoolBase;
        layout.LocalCounts = script.LocalCounts.SequenceEqual(OmsCanonical.LocalCounts(script.Code, declared))
            ? null : script.LocalCounts.ToArray();
        if (folding.RefSwap) layout.RefSwap = true;
        if (folding.Overrides.Count > 0) layout.Temps = folding.Overrides;
        if (context.LibrarySubs > 0)
            layout.Library = context.LibraryPlacement();
        return new OmsDecompilation(text.ToString(), layout, folding.FoldedInstructions, folding.Rejections,
            context.LibrarySubs, context.TemplateLifts, LiftedInstructions: context.Lifted);
    }

    // ============================================================================================== folding

    /// <summary>
    /// Which instructions fold into which statements. A temporary is a local cell that is never live on entry to a
    /// basic block; integer temporaries form the pool at the top of the integer bank, and reference temporaries are
    /// any such reference cell. A temporary definition folds into its single in-block use when the cell is dead
    /// afterwards; each resulting statement is accepted only if lowering it reproduces its instructions.
    /// </summary>
    private sealed class Folding
    {
        private static readonly string[] Write = ["write"], Read = ["read"], Bind = ["bind"], Address = ["address"];
        private static readonly HashSet<string> ReadKinds = new(StringComparer.Ordinal) { "read", "read-float", "read-string" };
        private static readonly HashSet<string> WriteKinds = new(StringComparer.Ordinal)
            { "write", "write-float", "write-string", "cell", "string-cell" };

        private readonly List<IrInstruction> _code;
        private readonly IntrinsicCatalog _intrinsics;
        private readonly bool[] _leader;
        private readonly HashSet<uint> _intTemps = new();
        private readonly HashSet<(int Type, uint Slot)> _refTemps = new();
        private readonly int[] _consumer;
        private readonly Dictionary<int, List<int>> _children = new();

        public readonly int[] RootOf;
        public readonly int[] UnitStart;
        public readonly LStatement?[] Statements;
        public uint PoolBase { get; private set; }
        public bool IsPoolSlot(uint slot) => _intTemps.Contains(slot);
        public bool UsesIntTemps { get; private set; }
        public int FoldedInstructions { get; private set; }
        public int Rejections { get; private set; }
        public SortedDictionary<int, OmsTempOverride> Overrides { get; } = new();

        private readonly Action<OmsFoldRecord>? _observe;

        public Folding(List<IrInstruction> code, IntrinsicCatalog intrinsics, bool fold, Action<OmsFoldRecord>? observe)
        {
            _observe = observe;
            _code = code;
            _intrinsics = intrinsics;
            int count = code.Count;
            _leader = Leaders(code);
            _consumer = Enumerable.Repeat(-1, count).ToArray();
            RootOf = Enumerable.Range(0, count).ToArray();
            UnitStart = Enumerable.Range(0, count).ToArray();
            Statements = new LStatement?[count];
            PoolBase = FindTemporaries();
            if (fold) LinkDefinitions();
            for (int root = count - 1; root >= 0; root--)
                if (_consumer[root] < 0) Accept(root);
            _accepted.Reverse();
            PlanTemporaries();
        }

        private static bool[] Leaders(List<IrInstruction> code)
        {
            var leader = new bool[code.Count + 1];
            leader[0] = true;
            leader[code.Count] = true;
            for (int k = 0; k < code.Count; k++)
            {
                foreach (IrOperand operand in code[k].Operands)
                    if (operand.Kind == IrOperandKind.CodeLabel && operand.Value <= code.Count) leader[operand.Value] = true;
                if (code[k].Opcode is OmsCompiler.Jmp or OmsCompiler.Jcc or OmsCompiler.Ret or OmsCompiler.Exit
                    or 0x01 or 0x7C or 0xA1 or 0xA2 or 0xA3)
                    leader[k + 1] = true;
            }
            return leader;
        }

        /// <summary>Native access of a one-based operand. The language-level opcodes are fixed by the VM; intrinsics
        /// come from the registry contracts.</summary>
        public IReadOnlyList<string>? Access(int opcode, int operand) => opcode switch
        {
            >= 0x50 and <= 0x5F => operand == 1 ? Write : Read,
            ExpressionLowering.LookupArray or ExpressionLowering.LookupArray2D =>
                operand == 1 ? Bind : operand == 2 ? Address : Read,
            OmsCompiler.Jcc or OmsCompiler.CallScript or OmsCompiler.Call => Read,
            _ => _intrinsics.Contracts.Access(opcode, operand, _intrinsics.AbiId),
        };

        private static bool IsPointer(int type) => type is 0xC or 0xD or 0xE;

        private enum Touch { None, Def, Use, Other }

        /// <summary>How an instruction touches one temporary candidate cell (reads before writes).</summary>
        private Touch TouchOf(int index, int type, uint slot, out int position)
        {
            position = -1;
            Touch result = Touch.None;
            IReadOnlyList<IrOperand> operands = _code[index].Operands;
            for (int p = 0; p < operands.Count; p++)
            {
                IrOperand operand = operands[p];
                if (operand.Kind != IrOperandKind.Literal || operand.Type != type || operand.Value != slot) continue;
                Touch touch = Classify(_code[index].Opcode, p + 1, type);
                if (result != Touch.None || touch != Touch.Def && touch != Touch.Use) return Touch.Other;
                result = touch;
                position = p;
            }
            return result;
        }

        private Touch Classify(int opcode, int operand, int type)
        {
            IReadOnlyList<string>? kinds = Access(opcode, operand);
            if (kinds == null || kinds.Count == 0) return Touch.Other;
            if (IsPointer(type))
            {
                if (kinds.Count == 1 && kinds[0] == "bind") return Touch.Def;
                return kinds.All(k => ReadKinds.Contains(k) || WriteKinds.Contains(k)) ? Touch.Use : Touch.Other;
            }
            if (kinds.Count == 1 && kinds[0] == "write") return Touch.Def;
            return kinds.All(ReadKinds.Contains) ? Touch.Use : Touch.Other;
        }

        /// <summary>Marks local cells that are never live into a block, and returns the integer pool base: the
        /// lowest slot of the run of such cells at the top of the integer bank.</summary>
        private uint FindTemporaries()
        {
            var candidates = new HashSet<(int Type, uint Slot)>();
            foreach (IrInstruction instruction in _code)
                foreach (IrOperand operand in instruction.Operands)
                    if (operand.Kind == IrOperandKind.Literal && (operand.Type == 0x9 || IsPointer(operand.Type)))
                        candidates.Add((operand.Type, operand.Value));
            var notTemp = new HashSet<(int, uint)>();
            // Uses since each cell's last in-block definition: a compiler temporary is read exactly once.
            var uses = new Dictionary<(int, uint), int>();
            void Settle()
            {
                foreach (var (key, count) in uses)
                    if (count != 1 && key.Item1 == 0x9) notTemp.Add(key);
                uses.Clear();
            }
            for (int k = 0; k < _code.Count; k++)
            {
                if (_leader[k]) Settle();
                IReadOnlyList<IrOperand> operands = _code[k].Operands;
                var defs = new List<(int, uint)>();
                for (int p = 0; p < operands.Count; p++)
                {
                    IrOperand operand = operands[p];
                    if (operand.Kind != IrOperandKind.Literal || !(operand.Type == 0x9 || IsPointer(operand.Type))) continue;
                    var key = (operand.Type, operand.Value);
                    Touch touch = Classify(_code[k].Opcode, p + 1, operand.Type);
                    if (touch == Touch.Def)
                    {
                        defs.Add(key);
                        // Integer temporaries are only ever results of binary operations; a cell set by mov or by an
                        // intrinsic is a named local.
                        if (operand.Type == 0x9 && !(_code[k].Opcode is >= 0x50 and <= 0x5F and not OmsCompiler.Mov))
                            notTemp.Add(key);
                    }
                    else if (touch == Touch.Other || !uses.ContainsKey(key)) notTemp.Add(key);
                    else uses[key]++;
                }
                foreach (var key in defs)
                {
                    if (uses.TryGetValue(key, out int count) && count != 1 && key.Item1 == 0x9) notTemp.Add(key);
                    uses[key] = 0;
                }
            }
            Settle();
            foreach (var key in candidates)
                if (!notTemp.Contains(key) && IsPointer(key.Type)) _refTemps.Add(key);
            var ints = candidates.Where(key => key.Type == 0x9).Select(key => key.Slot).ToHashSet();
            if (ints.Count == 0) return 0;
            uint top = ints.Max();
            uint pool = top + 1;
            for (long slot = top; slot >= 0; slot--)
            {
                if (!ints.Contains((uint)slot)) continue;
                if (notTemp.Contains((0x9, (uint)slot))) break;
                pool = (uint)slot;
                _intTemps.Add((uint)slot);
            }
            return pool;
        }

        private static int PointerTypeFor(int baseType) => baseType switch
        {
            0x3 or 0x9 => 0xC,
            0x4 or 0xA => 0xD,
            0x5 or 0xB => 0xE,
            _ => -1,
        };

        private bool IsTempDefinition(int index)
        {
            IrInstruction instruction = _code[index];
            if (instruction.Operands.Count == 0) return false;
            IrOperand dst = instruction.Operands[0];
            if (dst.Kind != IrOperandKind.Literal) return false;
            if (instruction.Opcode is >= 0x50 and <= 0x5F and not OmsCompiler.Mov && instruction.Operands.Count == 3)
                return dst.Type == 0x9 && _intTemps.Contains(dst.Value);
            if (instruction.Opcode is ExpressionLowering.LookupArray or ExpressionLowering.LookupArray2D
                && instruction.Operands.Count == (instruction.Opcode == ExpressionLowering.LookupArray ? 3 : 5))
            {
                IrOperand @base = instruction.Operands[1];
                return _refTemps.Contains((dst.Type, dst.Value)) && @base.Kind == IrOperandKind.Literal
                       && PointerTypeFor(@base.Type) == dst.Type;
            }
            return false;
        }

        private static bool IsStorage(IrOperand operand)
            => operand.Kind == IrOperandKind.Literal && StorageBanks.BankByType.ContainsKey(operand.Type);

        /// <summary>Whether a folded expression may stand at a 0-based operand position of the instruction.</summary>
        private bool CanFoldInto(int index, int position, bool pointer)
        {
            IrInstruction instruction = _code[index];
            int opcode = instruction.Opcode;
            switch (opcode)
            {
                case >= 0x50 and <= 0x5F:
                    if (instruction.Operands.Count != (opcode == OmsCompiler.Mov ? 2 : 3)) return false;
                    if (position == 0) return pointer;
                    if (!IsStorage(instruction.Operands[0]) && !IsTempDefinition(index)
                        && !(IsPointer(instruction.Operands[0].Type) && instruction.Operands[0].Kind == IrOperandKind.Literal))
                        return false;
                    return opcode != OmsCompiler.Mov || pointer;
                case ExpressionLowering.LookupArray:
                    return position == 2;
                case ExpressionLowering.LookupArray2D:
                    return position is 2 or 4;
                case OmsCompiler.Jcc:
                case OmsCompiler.CallScript:
                    return position == 0;
                case OmsCompiler.Call:
                    return position == 0 && instruction.Operands.Count == 1;
                case OmsCompiler.Comment or OmsCompiler.Jmp or OmsCompiler.BlockBegin or OmsCompiler.BlockEnd:
                    return false;
            }
            if (!_intrinsics.TryGet(opcode, out Intrinsic intrinsic) || !intrinsic.HasContract
                || intrinsic.Argc != instruction.Operands.Count || IntrinsicCatalog.Keywords.Contains(intrinsic.Name))
                return false;
            IReadOnlyList<string>? kinds = Access(opcode, position + 1);
            if (kinds == null || kinds.Count == 0) return false;
            return pointer
                ? kinds.All(k => ReadKinds.Contains(k) || WriteKinds.Contains(k))
                : kinds.All(ReadKinds.Contains);
        }

        /// <summary>Links each temporary definition to its single in-block use when the cell is dead after it.</summary>
        private void LinkDefinitions()
        {
            for (int d = 0; d < _code.Count; d++)
            {
                if (!IsTempDefinition(d)) continue;
                IrOperand dst = _code[d].Operands[0];
                int use = -1, position = -1;
                for (int u = d + 1; u < _code.Count && !_leader[u]; u++)
                {
                    Touch touch = TouchOf(u, dst.Type, dst.Value, out int p);
                    if (touch == Touch.None) continue;
                    if (touch == Touch.Use) { use = u; position = p; }
                    break;
                }
                if (use < 0 || !CanFoldInto(use, position, IsPointer(dst.Type))) continue;
                bool dead = true;
                for (int u = use + 1; u < _code.Count && !_leader[u]; u++)
                {
                    Touch touch = TouchOf(u, dst.Type, dst.Value, out _);
                    if (touch == Touch.None) continue;
                    dead = touch == Touch.Def;
                    break;
                }
                if (!dead) continue;
                _consumer[d] = use;
                if (!_children.TryGetValue(use, out List<int>? list)) _children[use] = list = new List<int>();
                list.Add(d);
            }
        }

        // ------------------------------------------------------------------------------------- statements

        private LExpr Operand(int index, int position)
        {
            IrOperand operand = _code[index].Operands[position];
            if (_children.TryGetValue(index, out List<int>? children))
                foreach (int child in children)
                    if (_code[child].Operands[0] == operand) return Node(child);
            return new LLeaf(operand);
        }

        private LExpr Node(int index)
        {
            IrInstruction instruction = _code[index];
            if (instruction.Opcode is ExpressionLowering.LookupArray or ExpressionLowering.LookupArray2D)
            {
                var indices = new LExpr[instruction.Operands.Count - 2];
                for (int k = 0; k < indices.Length; k++) indices[k] = Operand(index, k + 2);
                return new LElement(instruction.Operands[1], indices, instruction.Operands[0].Type) { Origin = index };
            }
            return new LBinary(instruction.Opcode, Operand(index, 1), Operand(index, 2)) { Origin = index };
        }

        private LStatement Statement(int index, bool compound)
        {
            IrInstruction instruction = _code[index];
            int opcode = instruction.Opcode;
            bool assignable = instruction.Operands.Count > 0
                              && (IsStorage(instruction.Operands[0]) || _children.ContainsKey(index)
                                  && _children[index].Any(c => _code[c].Operands[0] == instruction.Operands[0]));
            if (opcode == OmsCompiler.Mov && instruction.Operands.Count == 2 && assignable)
                return new LAssign(Operand(index, 0), Operand(index, 1), false);
            if (opcode is >= 0x50 and <= 0x5F && BinaryOperators.ContainsKey(opcode) && instruction.Operands.Count == 3
                && assignable)
                return new LAssign(Operand(index, 0), new LBinary(opcode, Operand(index, 1), Operand(index, 2)) { Origin = index },
                    compound);
            return new LInstruction(opcode, instruction.Operands.Select((_, p) => Operand(index, p)).ToArray());
        }

        private static bool Same(LExpr a, LExpr b) => (a, b) switch
        {
            (LLeaf x, LLeaf y) => x.Operand == y.Operand,
            (LBinary x, LBinary y) => x.Opcode == y.Opcode && Same(x.Left, y.Left) && Same(x.Right, y.Right),
            (LElement x, LElement y) => x.Base == y.Base && x.PointerType == y.PointerType
                                        && x.Indices.Count == y.Indices.Count
                                        && x.Indices.Zip(y.Indices).All(pair => Same(pair.First, pair.Second)),
            _ => false,
        };

        private static IEnumerable<IrOperand> Leaves(LStatement statement)
        {
            var pending = new Stack<LExpr>(statement switch
            {
                LAssign assign => [assign.Target, assign.Value],
                LInstruction instruction => instruction.Operands,
                _ => [],
            });
            while (pending.Count > 0)
                switch (pending.Pop())
                {
                    case LLeaf leaf: yield return leaf.Operand; break;
                    case LBinary binary: pending.Push(binary.Left); pending.Push(binary.Right); break;
                    case LElement element:
                        yield return element.Base;
                        foreach (LExpr index in element.Indices) pending.Push(index);
                        break;
                }
        }

        private static void Origins(LExpr expression, List<int> origins)
        {
            switch (expression)
            {
                case LBinary binary:
                    if (binary.Origin >= 0) origins.Add(binary.Origin);
                    Origins(binary.Left, origins);
                    Origins(binary.Right, origins);
                    break;
                case LElement element:
                    origins.Add(element.Origin);
                    foreach (LExpr index in element.Indices) Origins(index, origins);
                    break;
            }
        }

        private static List<int> Origins(LStatement statement, int root)
        {
            var origins = new List<int>();
            switch (statement)
            {
                case LAssign assign:
                    Origins(assign.Target, origins);
                    Origins(assign.Value, origins);
                    break;
                case LInstruction instruction:
                    foreach (LExpr operand in instruction.Operands) Origins(operand, origins);
                    break;
            }
            origins.Remove(root);
            return origins;
        }

        private string Slot(LExpr node)
        {
            IrOperand dst = _code[node.Origin].Operands[0];
            return node is LElement ? $"@{dst.Value}" : $"#{(long)dst.Value - PoolBase}";
        }

        /// <summary>A compact s-expression of a statement for diagnostics: leaves by bank, temporaries annotated.</summary>
        private string Describe(LStatement statement, Func<LExpr, string>? annotate)
        {
            string Leaf(IrOperand operand) => operand.Kind switch
            {
                IrOperandKind.CodeLabel => "c",
                IrOperandKind.DataRef => "d",
                _ => operand.Type == 0 ? "i" : StorageBanks.BankByType.GetValueOrDefault(operand.Type, $"t{operand.Type:x}"),
            };
            string Expr(LExpr expression) => expression switch
            {
                LLeaf leaf => Leaf(leaf.Operand),
                LBinary binary => $"({BinaryOperators.GetValueOrDefault(binary.Opcode, $"op{binary.Opcode:x}")} "
                                  + $"{Expr(binary.Left)} {Expr(binary.Right)})"
                                  + (annotate != null && binary.Origin >= 0 ? annotate(binary) : ""),
                LElement element => $"[{Leaf(element.Base)} {string.Join(" ", element.Indices.Select(Expr))}]"
                                    + (annotate != null ? annotate(element) : ""),
                _ => "?",
            };
            return statement switch
            {
                LAssign assign => $"({(assign.Compound ? "op=" : "=")} {Expr(assign.Target)} {Expr(assign.Value)})",
                LInstruction instruction => $"(0x{instruction.Opcode:x} {string.Join(" ", instruction.Operands.Select(Expr))})",
                _ => "?",
            };
        }

        private bool Reproduces(List<IrInstruction> emitted, int start, int root)
        {
            if (emitted.Count != root - start + 1) return false;
            for (int k = 0; k < emitted.Count; k++)
                if (emitted[k].Opcode != _code[start + k].Opcode
                    || !emitted[k].Operands.SequenceEqual(_code[start + k].Operands))
                    return false;
            return true;
        }

        private readonly List<(int Start, int Root, LStatement Statement, List<(int Type, uint Slot)> Slots)> _accepted = new();

        /// <summary>Whether the script starts its integer-reference registers in the order LP1, LP0.</summary>
        public bool RefSwap { get; private set; }

        private bool Canonical(int start, int root, LStatement statement, bool swap)
            => Canonical(start, root, statement, PoolBase, swap);

        private bool Canonical(int start, int root, LStatement statement, uint poolBase, bool swap)
            => Reproduces(ExpressionLowering.Instructions(statement, new CanonicalTemps(statement, poolBase, swap).Allocate),
                start, root);

        /// <summary>Chooses the script's reference order by which fits more statements, then records the original
        /// slots of every statement the canonical plan does not reproduce.</summary>
        private void PlanTemporaries()
        {
            // The numbering base is the pool's first slot as the compiler derives it (just above the named locals) or the
            // lowest temporary actually used (canonical numbering can skip a label that went to a reference); keep
            // whichever, with the reference order, reproduces the most statements, preferring the derived base.
            uint derived = (uint)(_code.SelectMany(instruction => instruction.Operands)
                .Where(operand => operand is { Kind: IrOperandKind.Literal, Type: 0x9 } && !_intTemps.Contains(operand.Value))
                .Select(operand => (long)operand.Value).DefaultIfEmpty(-1).Max() + 1);
            // The base the compiler will derive from the printed source: above every local integer left explicit.
            uint printed = (uint)(Enumerable.Range(0, _code.Count).Where(root => Statements[root] != null)
                .SelectMany(root => Leaves(Statements[root]!))
                .Where(leaf => leaf is { Kind: IrOperandKind.Literal, Type: 0x9 })
                .Select(leaf => (long)leaf.Value).DefaultIfEmpty(-1).Max() + 1);
            var best = (Base: derived, Swap: false, Fits: -1);
            foreach (uint poolBase in new[] { derived, printed, PoolBase }.Distinct())
                foreach (bool swap in new[] { false, true })
                {
                    int fits = _accepted.Count(a => Canonical(a.Start, a.Root, a.Statement, poolBase, swap));
                    if (fits > best.Fits) best = (poolBase, swap, fits);
                }
            PoolBase = best.Base;
            RefSwap = best.Swap;
            foreach (var (start, root, statement, slots) in _accepted)
            {
                bool canonical = Canonical(start, root, statement, RefSwap);
                if (!canonical)
                {
                    uint[] ints = slots.Where(a => a.Type == ExpressionLowering.IntTemp).Select(a => a.Slot).ToArray();
                    uint[] refs = slots.Where(a => a.Type != ExpressionLowering.IntTemp).Select(a => a.Slot).ToArray();
                    var expectedInts = new List<uint>();
                    var expectedRefs = new List<uint>();
                    var plan = new CanonicalTemps(statement, PoolBase, RefSwap);
                    ExpressionLowering.Instructions(statement, (node, type) =>
                    {
                        uint slot = plan.Allocate(node, type);
                        (type == ExpressionLowering.IntTemp ? expectedInts : expectedRefs).Add(slot);
                        return slot;
                    });
                    var entry = new OmsTempOverride
                    {
                        Ints = ints.SequenceEqual(expectedInts) ? null : ints,
                        Refs = refs.SequenceEqual(expectedRefs) ? null : refs,
                    };
                    var replay = new CanonicalTemps(statement, PoolBase, RefSwap, entry.Ints, entry.Refs);
                    if (!Reproduces(ExpressionLowering.Instructions(statement, replay.Allocate), start, root))
                        throw new InvalidOperationException($"temporary override does not reproduce statement at {start}");
                    Overrides[start] = entry;
                }
                if (_observe != null)
                    _observe(new OmsFoldRecord(start, Describe(statement, null),
                        Describe(statement, node => node.Origin == root ? "" : Slot(node)), canonical));
            }
        }

        /// <summary>Accepts the statement rooted at <paramref name="root"/>: verifies its fold (cutting the folds
        /// into the root when lowering does not reproduce the original), then records any temporary override.</summary>
        private void Accept(int root)
        {
            while (true)
            {
                if (TryAccept(root, compound: true) || TryAccept(root, compound: false)) return;
                Rejections++;
                foreach (int child in _children[root]) _consumer[child] = -1;
                _children.Remove(root);
            }
        }

        private bool TryAccept(int root, bool compound)
        {
            LStatement statement = Statement(root, compound);
            if (compound && !(statement is LAssign { Value: LBinary value } assign && Same(value.Left, assign.Target)))
                return false;
            List<int> origins = Origins(statement, root);
            int start = origins.Count == 0 ? root : Math.Min(origins.Min(), root);
            if (origins.Count != root - start || origins.Distinct().Count() != origins.Count) return false;
            var allocations = new List<(int Type, uint Slot)>();
            List<IrInstruction> actual = ExpressionLowering.Instructions(statement, (node, type) =>
            {
                uint slot = _code[node.Origin].Operands[0].Value;
                allocations.Add((type, slot));
                return slot;
            });
            if (!Reproduces(actual, start, root)) return false;
            if (allocations.Count > 0) _accepted.Add((start, root, statement, allocations));
            if (allocations.Any(a => a.Type == ExpressionLowering.IntTemp)) UsesIntTemps = true;
            Statements[root] = statement;
            for (int k = start; k <= root; k++) RootOf[k] = root;
            UnitStart[root] = start;
            FoldedInstructions += root - start;
            return true;
        }
    }

    // ============================================================================================== structure

    private abstract record Node(int Start);
    private sealed record SimpleNode(int Start, int Root) : Node(Start);

    /// <summary>One <c>if</c>/<c>else if</c> arm: its condition unit and jcc, then-body, and the index the body ends
    /// at. When <see cref="JumpsToEnd"/>, the body is followed by the arm's <c>jmp end</c> at <see cref="ThenEnd"/>.</summary>
    private sealed record Arm(int Start, int Jcc, List<Node> Then, int ThenEnd, bool JumpsToEnd);

    private sealed record IfChainNode(List<Arm> Arms, List<Node>? Else, int End) : Node(Arms[0].Start);
    private sealed record LoopNode(int Head, List<Node> Body, int BackJump, Loop Loop) : Node(Head);
    private sealed record DoWhileNode(int Head, List<Node> Body, int TestStart, int Test, Loop Loop) : Node(Head);
    private sealed record AtomicNode(int Begin, List<Node> Body, int End) : Node(Begin);

    /// <summary>A placed <c>sub</c> or <c>coroutine</c>: top-level code entered only by calls, callbacks or coroutine
    /// registrations, closed under its own jumps, and not reachable by falling through.</summary>
    private sealed record SubNode(int Entry, List<Node> Body, int End, bool Coroutine) : Node(Entry);

    /// <summary><c>while c { … }</c>: a loop whose head unit is <c>if !c break</c>.</summary>
    private sealed record WhileNode(int Head, int Test, List<Node> Body, int BackJump, Loop Loop) : Node(Head);

    /// <summary><c>for init; c; step { … }</c>: <c>init; jmp cond; loop { step; cond: if !c break; … }</c>. The parts
    /// are kept so the loop can print in its plain form when its step needs a label.</summary>
    private sealed record ForNode(int Start, SimpleNode? Init, SimpleNode Jump, SimpleNode Step, SimpleNode Test,
                                  List<Node> Body, LoopNode Original) : Node(Start);

    /// <summary>An index-addressed local array the decompiler declares (bank operand type, first slot, length).</summary>
    private sealed record LocalArray(int Type, uint Base, uint Length)
    {
        public string Name => Type switch { 0xA => "float_array_", 0xB => "str_array_", _ => "array_" } + Base.ToString("x");
        public string TypeName => Type switch { 0xA => "float", 0xB => "str", _ => "int" };
    }

    /// <summary>A loop's break and continue destinations (instruction indices).</summary>
    private sealed record Loop(int Break, int Continue, Loop? Outer);

    private sealed class Context(IrScript script, IntrinsicCatalog intrinsics, Folding folding, IReadOnlyList<GameModule> modules,
                                 OmsIdioms? idioms)
    {
        private readonly List<IrInstruction> _code = script.Code;
        private readonly HashSet<int> _labels = new();
        private readonly int[] _offsets = Offsets(script.Code);
        private readonly Dictionary<int, List<int>> _incoming = References(script.Code, transfersOnly: true);
        private readonly Dictionary<int, List<int>> _references = References(script.Code, transfersOnly: false);

        /// <summary>Highest local integer cell printed explicitly (the compiler derives the pool base from it).</summary>
        public long MaxExplicitLocal { get; private set; } = -1;

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

        private string LabelName(int index) => _libraryNames.TryGetValue(index, out string? library) ? library
            : index >= _code.Count ? "label_end" : $"label_{_code[index].OriginalOffset ?? _offsets[index]:x}";

        // ------------------------------------------------------------------------------------------ idioms

        private readonly Dictionary<int, (OmsLibraryPattern Pattern, int End)> _libraryAt = new();
        private readonly Dictionary<int, string> _libraryNames = new();
        private static readonly Dictionary<int, string> NoEntries = new();

        public int LibrarySubs => _libraryAt.Count;
        public int TemplateLifts { get; private set; }
        /// <summary>Instructions inside accepted library and template lifts, by module.</summary>
        public Dictionary<string, int> Lifted { get; } = new(StringComparer.Ordinal);

        private void CountLift(GameModule module, int instructions)
            => Lifted[module.Name] = Lifted.GetValueOrDefault(module.Name) + instructions;

        /// <summary>Matches a fragment against the code at <paramref name="at"/>: same opcodes and operands, except
        /// that temporaries match any slot of their bank, internal labels match by offset, references to library labels
        /// bind script indices to names (<paramref name="references"/>), and placeholders bind arguments.</summary>
        private bool Match(int at, OmsFragment fragment, Dictionary<int, string> references, Dictionary<int, IrOperand>? arguments)
        {
            if (at + fragment.Length > _code.Count) return false;
            for (int k = 0; k < fragment.Length; k++)
            {
                IrInstruction mine = _code[at + k], theirs = fragment.Image.Code[fragment.Start + k];
                if (mine.Opcode != theirs.Opcode || mine.Operands.Count != theirs.Operands.Count) return false;
                for (int p = 0; p < theirs.Operands.Count; p++)
                {
                    IrOperand t = theirs.Operands[p], m = mine.Operands[p];
                    if (arguments != null && OmsIdioms.ProbeOf(t, fragment.Image) is int parameter)
                    {
                        bool fits = t.Kind == IrOperandKind.DataRef
                            ? m.Kind == IrOperandKind.DataRef && script.Data[(int)m.Value].Text != null
                            : t.Type == 0x3 && t.Value >= OmsIdioms.CellProbe
                                ? m is { Kind: IrOperandKind.Literal, Type: 0x3 or 0x4 or 0x5 or 0x9 or 0xA or 0xB }
                                : t.Value >= OmsIdioms.ExprProbe ? m.Kind != IrOperandKind.CodeLabel
                                : m is { Kind: IrOperandKind.Literal, Type: 0 };
                        if (!fits || arguments.TryGetValue(parameter, out IrOperand bound) && bound != m) return false;
                        arguments[parameter] = m;
                        continue;
                    }
                    switch (t.Kind)
                    {
                        case IrOperandKind.CodeLabel:
                        {
                            if (m.Kind != IrOperandKind.CodeLabel || m.Type != t.Type) return false;
                            // Internal: inside the fragment, or its end unless a library label sits there.
                            int relative = (int)t.Value - fragment.Start;
                            if (relative >= 0 && relative < fragment.Length
                                || relative == fragment.Length && !fragment.Names.ContainsKey((int)t.Value))
                            {
                                if ((int)m.Value - at != relative) return false;
                            }
                            else if (fragment.Names.TryGetValue((int)t.Value, out string? name))
                            {
                                if (references.TryGetValue((int)m.Value, out string? seen) && seen != name) return false;
                                references[(int)m.Value] = name;
                            }
                            else return false;
                            break;
                        }
                        case IrOperandKind.DataRef:
                            if (m.Kind != IrOperandKind.DataRef || m.Type != t.Type
                                || !script.Data[(int)m.Value].Dwords.SequenceEqual(fragment.Image.Data[(int)t.Value].Dwords))
                                return false;
                            break;
                        default:
                            bool temporary = t.Type == 0x9 && t.Value - OmsCompiler.TempProbe < 0x400 || t.Type is 0xC or 0xD or 0xE;
                            if (temporary ? m.Kind != IrOperandKind.Literal || m.Type != t.Type : m != t) return false;
                            break;
                    }
                }
            }
            return true;
        }

        /// <summary>Whether code outside [start, end) enters it only at its start or at the given exported offsets.</summary>
        private bool EntriesOnly(int start, int end, IReadOnlyDictionary<int, string> entries)
        {
            for (int q = start + 1; q < end; q++)
                if (_references.TryGetValue(q, out List<int>? sources) && sources.Any(source => source < start || source >= end)
                    && !entries.ContainsKey(q - start))
                    return false;
            return true;
        }

        /// <summary>Recognizes library subs among the carved subs (spec §11 step 4). A match may run past the carved sub
        /// over following top-level nodes (a library function's trailing <c>exit</c>) but must end on a node boundary;
        /// matches whose references to other library labels do not land on the matching names are dropped until the
        /// set is consistent.</summary>
        public void RecognizeLibrary(List<Node> top)
        {
            if (idioms == null || idioms.Library.Count == 0) return;
            var boundaries = new HashSet<int>(top.Select(node => node.Start)) { _code.Count };
            var matches = new Dictionary<int, (OmsLibraryPattern Pattern, int End, Dictionary<int, string> References)>();
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (SubNode sub in top.OfType<SubNode>())
                foreach (OmsLibraryPattern pattern in idioms.Library)
                {
                    // A guarded sub's match starts at its exit guard, the top-level node before the carved sub.
                    int start = sub.Entry - pattern.Entries.First(pair => pair.Value == pattern.Name).Key;
                    int end = start + pattern.Code.Length;
                    if (taken.Contains(pattern.Name) || start < 0 || !boundaries.Contains(start) || end < sub.End
                        || !boundaries.Contains(end) || pattern.Code.FirstOpcode != _code[start].Opcode
                        || start < sub.Entry && _references.ContainsKey(start))
                        continue;
                    var references = new Dictionary<int, string>();
                    if (!Match(start, pattern.Code, references, null) || !EntriesOnly(start, end, pattern.Entries)) continue;
                    matches[start] = (pattern, end, references);
                    taken.Add(pattern.Name);
                    break;
                }
            for (bool changed = true; changed;)
            {
                changed = false;
                var names = new Dictionary<int, string>();
                foreach (var (entry, match) in matches)
                    foreach (var (offset, label) in match.Pattern.Entries) names[entry + offset] = label;
                foreach (var (entry, match) in matches.ToList())
                    if (match.References.Any(pair => !names.TryGetValue(pair.Key, out string? name) || name != pair.Value))
                    {
                        matches.Remove(entry);
                        changed = true;
                    }
            }
            foreach (var (entry, match) in matches)
            {
                _libraryAt[entry] = (match.Pattern, match.End);
                foreach (var (offset, label) in match.Pattern.Entries) _libraryNames[entry + offset] = label;
                UsedModules.Add(match.Pattern.Module);
                CountLift(match.Pattern.Module, match.End - entry);
            }
        }

        /// <summary>Where the recognized library subs the compiler would not link there sit, for the sidecar (spec §6.3).
        /// The compiler links the subs the sidecar does not place after the script, breadth first in order of first
        /// reference; the longest tail of the script that is exactly that needs no placement.</summary>
        public SortedDictionary<string, int>? LibraryPlacement()
        {
            var ranges = _libraryAt.OrderBy(pair => pair.Key).ToList();
            var owner = new Dictionary<int, string>();
            foreach (var (entry, library) in ranges)
                foreach (int offset in library.Pattern.Entries.Keys) owner[entry + offset] = library.Pattern.Name;
            IEnumerable<string> References(int from, int to)
            {
                for (int k = from; k < to; k++)
                    foreach (IrOperand operand in _code[k].Operands)
                        if (operand.Kind == IrOperandKind.CodeLabel && owner.TryGetValue((int)operand.Value, out string? name))
                            yield return name;
            }
            int tail = ranges.Count;
            for (int expected = _code.Count; tail > 0 && ranges[tail - 1].Value.End == expected; tail--)
                expected = ranges[tail - 1].Key;
            for (; ; tail++)
            {
                int body = tail < ranges.Count ? ranges[tail].Key : _code.Count;
                var queued = ranges.Take(tail).Select(range => range.Value.Pattern.Name).ToHashSet(StringComparer.Ordinal);
                var queue = References(0, body).Where(queued.Add).ToList();
                for (int i = 0; i < queue.Count; i++)
                {
                    var (start, (_, end)) = ranges.First(range => range.Value.Pattern.Name == queue[i]);
                    queue.AddRange(References(start, end).Where(queued.Add));
                }
                if (queue.SequenceEqual(ranges.Skip(tail).Select(range => range.Value.Pattern.Name)))
                    return tail == 0 ? null
                        : new SortedDictionary<string, int>(ranges.Take(tail)
                            .ToDictionary(range => range.Value.Pattern.Name, range => range.Key), StringComparer.Ordinal);
            }
        }

        /// <summary>Lifts a template use starting at node <paramref name="n"/> (spec §11 step 5): the longest variant
        /// whose code matches and ends on a node boundary, enters only at its start, and calls only recognized
        /// library labels.</summary>
        private (string Text, int Last)? Lift(List<Node> nodes, int n, int end)
        {
            if (idioms == null || nodes[n] is SubNode) return null;
            int start = nodes[n].Start;
            foreach (OmsTemplatePattern pattern in idioms.Templates)
            {
                if (pattern.Code.FirstOpcode != _code[start].Opcode) continue;
                int stop = start + pattern.Code.Length, last = n;
                while (last + 1 < nodes.Count && nodes[last + 1].Start < stop && nodes[last + 1] is not SubNode) last++;
                if ((last + 1 < nodes.Count ? nodes[last + 1].Start : end) != stop) continue;
                var references = new Dictionary<int, string>();
                var arguments = new Dictionary<int, IrOperand>();
                if (!Match(start, pattern.Code, references, arguments)) continue;
                if (references.Any(pair => !_libraryNames.TryGetValue(pair.Key, out string? name) || name != pair.Value)) continue;
                if (!EntriesOnly(start, stop, NoEntries)) continue;
                TemplateLifts++;
                UsedModules.Add(pattern.Module);
                CountLift(pattern.Module, pattern.Code.Length);
                return (TemplateUse(pattern, arguments), last);
            }
            return null;
        }

        /// <summary>A lifted use: a dialogue line when the template is <c>@dialogue</c> and its speaker (if passed) has an
        /// enum name, otherwise a call with the leading required arguments positional and the rest named.</summary>
        private string TemplateUse(OmsTemplatePattern pattern, Dictionary<int, IrOperand> arguments)
        {
            TemplateStatement template = pattern.Template;
            string? Member(int k)
                => idioms!.Enums.TryGetValue(template.Parameters[k].Kind, out var members)
                   && arguments[k] is { Kind: IrOperandKind.Literal, Type: 0 } operand
                   && members.TryGetValue(operand.Value, out string? member) ? member : null;
            string Value(int k) => Member(k) ?? Atom(arguments[k]);
            int text = template.Parameters.ToList().FindIndex(parameter => parameter.Name == "text");
            int speaker = template.Parameters.ToList().FindIndex(parameter => parameter.Name == "speaker");
            if (template.Dialogue && text >= 0 && (speaker < 0 || pattern.Absent[speaker] || Member(speaker) != null))
            {
                var named = new List<string>();
                for (int k = 0; k < template.Parameters.Count; k++)
                    if (k != text && k != speaker && !pattern.Absent[k]) named.Add($"{template.Parameters[k].Name}: {Value(k)}");
                string who = speaker >= 0 && !pattern.Absent[speaker] ? Value(speaker) + " " : "";
                return who + Value(text) + (named.Count > 0 ? $" ({string.Join(", ", named)})" : "");
            }
            var parts = new List<string>();
            bool positional = true;
            for (int k = 0; k < template.Parameters.Count; k++)
            {
                if (pattern.Absent[k])
                {
                    positional = false;
                    continue;
                }
                positional &= template.Parameters[k].Default == null;
                parts.Add(positional ? Value(k) : $"{template.Parameters[k].Name}: {Value(k)}");
            }
            return $"{template.Name}({string.Join(", ", parts)})";
        }

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
                int root = folding.RootOf[k];
                int back = BackEdge(k, hi, out bool doWhile);
                if (back >= k)
                {
                    if (doWhile)
                    {
                        int testStart = folding.UnitStart[back];
                        var inner = new Loop(back + 1, testStart, loop);
                        nodes.Add(new DoWhileNode(k, Structure(k, testStart, inner), testStart, back, inner));
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
                    // Plan the arms first: (start, jcc, skip, end) per arm while the else region is exactly one more if.
                    var plan = new List<(int Start, int Jcc, int Skip, int End)> { (k, root, skip, end) };
                    while (plan[^1].End > plan[^1].Skip
                           && CanChain(plan[^1].Skip, plan[^1].End, out int nextSkip, out int nextEnd))
                        plan.Add((plan[^1].Skip, folding.RootOf[plan[^1].Skip], nextSkip, nextEnd));
                    int chainEnd = end;
                    // The join's own arrivals: each arm's jmp end, and the last arm's false branch when it has no else.
                    int armJumps = plan.Count(arm => arm.End > arm.Skip) + (plan.Count > 1 && plan[^1].End == plan[^1].Skip ? 1 : 0);
                    // Only forward jumps compete for the join; a backward jump to it belongs to a loop starting there.
                    bool privateJoin = end == skip
                        || (_incoming.TryGetValue(chainEnd, out List<int>? joins)
                            && joins.Count(source => source < chainEnd) == armJumps);
                    if (!privateJoin)
                    {
                        // The join label is shared with other code: the trailing jmp is a goto, not an else.
                        nodes.Add(new IfChainNode([new Arm(k, root, Structure(root + 1, skip, loop), skip, false)], null, skip));
                        k = skip;
                        continue;
                    }
                    var arms = new List<Arm>();
                    List<Node>? otherwise = null;
                    foreach (var (start, jcc, armSkip, armEnd) in plan)
                    {
                        if (armEnd > armSkip)
                            arms.Add(new Arm(start, jcc, Structure(jcc + 1, armSkip - 1, loop), armSkip - 1, true));
                        else
                            arms.Add(new Arm(start, jcc, Structure(jcc + 1, armSkip, loop), armSkip, false));
                    }
                    var (_, _, lastSkip, lastEnd) = plan[^1];
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
                nodes.Add(new SimpleNode(k, root));
                k = root + 1;
            }
            RecognizeLoops(nodes);
            return nodes;
        }

        // ------------------------------------------------------------------------------------------ subs

        /// <summary>Whether control can leave the node by falling off its end.</summary>
        private bool FallsThrough(Node node) => node switch
        {
            SimpleNode simple => !OmsCompiler.IsTerminator(_code[simple.Root].Opcode),
            SubNode => false,
            LoopNode => false,      // its only exit is a jump to the instruction after it
            _ => true,
        };

        /// <summary>How an instruction index is entered from elsewhere: <c>sub</c> (calls and callbacks),
        /// <c>coroutine</c> (coroutine registrations only), or null (unreferenced, jumped to, or mixed).</summary>
        private string? EntryKind(int index)
        {
            if (!_references.TryGetValue(index, out List<int>? sources)) return null;
            bool sub = false, coroutine = false;
            foreach (int source in sources)
            {
                int opcode = _code[source].Opcode;
                string? kind = opcode == OmsCompiler.Call ? "call"
                    : IsTransfer(_code[source]) ? "transfer" : intrinsics.Contracts.CodeTargetKind(opcode);
                switch (kind)
                {
                    case "call" or "callback": sub = true; break;
                    case "coroutine": coroutine = true; break;
                    default: return null;
                }
            }
            return sub == coroutine ? null : sub ? "sub" : "coroutine";
        }

        private bool IsTransferOperand(int index, IrOperand operand)
            => operand.Kind == IrOperandKind.CodeLabel
               && (IsTransfer(_code[index]) || intrinsics.Contracts.CodeTargetKind(_code[index].Opcode) == "transfer");

        /// <summary>Carves placed subs and coroutines out of the top-level nodes: an entry node after a node that does
        /// not fall through, extended to the first node boundary where no jump crosses in or out and the last
        /// instruction ends control flow (a coroutine's must be <c>resume</c>).</summary>
        public List<Node> CarveSubs(List<Node> nodes)
        {
            var result = new List<Node>();
            int i = 0;
            while (i < nodes.Count)
            {
                if (i > 0 && !FallsThrough(nodes[i - 1]) && EntryKind(nodes[i].Start) is { } kind
                    && Region(nodes, i, kind == "coroutine") is int last and >= 0)
                {
                    int end = last + 1 < nodes.Count ? nodes[last + 1].Start : _code.Count;
                    result.Add(new SubNode(nodes[i].Start, nodes.GetRange(i, last - i + 1), end, kind == "coroutine"));
                    i = last + 1;
                    continue;
                }
                result.Add(nodes[i]);
                i++;
            }
            return result;
        }

        private int Region(List<Node> nodes, int first, bool coroutine)
        {
            int entry = nodes[first].Start;
            int lowTarget = int.MaxValue, highTarget = -1, lowSource = int.MaxValue, highSource = -1;
            for (int j = first; j < nodes.Count; j++)
            {
                int from = nodes[j].Start, to = j + 1 < nodes.Count ? nodes[j + 1].Start : _code.Count;
                for (int k = from; k < to; k++)
                {
                    foreach (IrOperand operand in _code[k].Operands)
                        if (IsTransferOperand(k, operand))
                        {
                            lowTarget = Math.Min(lowTarget, (int)operand.Value);
                            highTarget = Math.Max(highTarget, (int)operand.Value);
                        }
                    if (k > entry && _incoming.TryGetValue(k, out List<int>? sources))
                        foreach (int source in sources)
                        {
                            lowSource = Math.Min(lowSource, source);
                            highSource = Math.Max(highSource, source);
                        }
                }
                if (FallsThrough(nodes[j])) continue;
                bool closed = (highTarget < 0 || lowTarget >= entry && highTarget < to)
                              && (highSource < 0 || lowSource >= entry && highSource < to);
                if (!closed) continue;
                if (coroutine && _code[to - 1].Opcode != OmsCompiler.Resume) return -1;
                return j;
            }
            return -1;
        }

        /// <summary>Recasts plain loops as <c>while</c> and <c>for</c> where their layout is exactly that lowering.</summary>
        private void RecognizeLoops(List<Node> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is not LoopNode loop) continue;
                bool IsExitTest(Node node)
                    => node is SimpleNode test && IsBranch(_code[test.Root], out bool whenTrue, out int target)
                       && !whenTrue && target == loop.Loop.Break;
                if (i > 0 && nodes[i - 1] is SimpleNode jump && jump.Start == jump.Root && IsJump(_code[jump.Root], out int test)
                    && !_references.ContainsKey(jump.Start)
                    && _references.TryGetValue(test, out List<int>? testRefs) && testRefs.Count == 1
                    && loop.Body.Count >= 2 && loop.Body[1].Start == test && IsExitTest(loop.Body[1])
                    && loop.Body[0] is SimpleNode step && IsClause(step.Root))
                {
                    SimpleNode? init = i > 1 && nodes[i - 2] is SimpleNode candidate && IsClause(candidate.Root)
                                       && SameTarget(candidate.Root, step.Root) ? candidate : null;
                    int first = init != null ? i - 2 : i - 1;
                    var node = new ForNode(init?.Start ?? jump.Start, init, jump, step, (SimpleNode)loop.Body[1],
                        loop.Body.Skip(2).ToList(), loop);
                    nodes.RemoveRange(first, i - first + 1);
                    nodes.Insert(first, node);
                    i = first;
                    continue;
                }
                if (loop.Body.Count > 0 && loop.Body[0].Start == loop.Head && IsExitTest(loop.Body[0]))
                    nodes[i] = new WhileNode(loop.Head, ((SimpleNode)loop.Body[0]).Root, loop.Body.Skip(1).ToList(),
                        loop.BackJump, loop.Loop);
            }
        }

        /// <summary>A statement that can stand as a <c>for</c> init or step clause: an assignment or an intrinsic call.</summary>
        private bool IsClause(int root)
            => Lowered(root) is LAssign
               || _code[root].Opcode is not (OmsCompiler.Jmp or OmsCompiler.Jcc or OmsCompiler.Call or OmsCompiler.Ret
                   or OmsCompiler.Exit or OmsCompiler.CallScript or OmsCompiler.Comment or OmsCompiler.BlockBegin
                   or OmsCompiler.BlockEnd)
               && intrinsics.TryGet(_code[root].Opcode, out Intrinsic intrinsic)
               && intrinsic.Argc == _code[root].Operands.Count && !IntrinsicCatalog.Keywords.Contains(intrinsic.Name);

        private bool SameTarget(int first, int second)
            => Lowered(first) is LAssign a && Lowered(second) is LAssign b && a.Target is LLeaf x && b.Target is LLeaf y
               && x.Operand == y.Operand;

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

        /// <summary>An if whose condition unit starts at k: branch-when-false to skip, with a non-empty then-body;
        /// end > skip when the body ends in <c>jmp end</c> and [skip, end) is an else region inside the region.</summary>
        private bool IfShape(int k, int hi, out int skip, out int end)
        {
            end = -1;
            int root = folding.RootOf[k];
            if (!IsBranch(_code[root], out bool whenTrue, out skip) || whenTrue || skip <= root + 1 || skip > hi) return false;
            end = skip;
            if (skip - 1 > root + 1 && IsJump(_code[skip - 1], out int after) && after > skip && after <= hi) end = after;
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

        /// <summary>Diagnostics: one JSON line per statement with temporaries, in code order, with its structural
        /// path and its expression tree annotated with the original slots.</summary>
        public void Dump(StringBuilder output, List<Node> nodes, string path)
        {
            foreach (Node node in nodes)
                switch (node)
                {
                    case SimpleNode simple:
                        DumpStatement(output, simple.Root, path);
                        break;
                    case IfChainNode chain:
                        for (int a = 0; a < chain.Arms.Count; a++)
                        {
                            Arm arm = chain.Arms[a];
                            string arms = $"{path}/if{chain.Start}.{a}";
                            DumpStatement(output, arm.Jcc, $"{path}/if{chain.Start}");
                            Dump(output, arm.Then, arms);
                        }
                        if (chain.Else != null) Dump(output, chain.Else, $"{path}/if{chain.Start}.else");
                        break;
                    case LoopNode loopNode:
                        Dump(output, loopNode.Body, $"{path}/loop{loopNode.Head}");
                        break;
                    case DoWhileNode doNode:
                        Dump(output, doNode.Body, $"{path}/do{doNode.Head}");
                        DumpStatement(output, doNode.Test, $"{path}/do{doNode.Head}");
                        break;
                    case AtomicNode atomic:
                        Dump(output, atomic.Body, $"{path}/atomic{atomic.Begin}");
                        break;
                    case SubNode sub:
                        Dump(output, sub.Body, $"{path}/sub{sub.Entry}");
                        break;
                    case WhileNode whileNode:
                        DumpStatement(output, whileNode.Test, $"{path}/loop{whileNode.Head}");
                        Dump(output, whileNode.Body, $"{path}/loop{whileNode.Head}");
                        break;
                    case ForNode forNode:
                        if (forNode.Init != null) DumpStatement(output, forNode.Init.Root, path);
                        DumpStatement(output, forNode.Step.Root, $"{path}/loop{forNode.Original.Head}");
                        DumpStatement(output, forNode.Test.Root, $"{path}/loop{forNode.Original.Head}");
                        Dump(output, forNode.Body, $"{path}/loop{forNode.Original.Head}");
                        break;
                }
        }

        private void DumpStatement(StringBuilder output, int root, string path)
        {
            LStatement statement = Lowered(root);
            int start = folding.UnitStart[root];
            string Node(LExpr expression) => expression switch
            {
                LLeaf leaf => "{\"k\":\"leaf\",\"t\":" + leaf.Operand.Type + ",\"v\":" + leaf.Operand.Value
                              + ",\"kind\":\"" + leaf.Operand.Kind + "\"}",
                LBinary binary => "{\"k\":\"bin\",\"op\":" + binary.Opcode + ",\"slot\":"
                                  + (binary.Origin >= 0 && binary.Origin != root ? _code[binary.Origin].Operands[0].Value : -1)
                                  + ",\"l\":" + Node(binary.Left) + ",\"r\":" + Node(binary.Right) + "}",
                LElement element => "{\"k\":\"elem\",\"pt\":" + element.PointerType + ",\"slot\":"
                                    + _code[element.Origin].Operands[0].Value + ",\"bt\":" + element.Base.Type
                                    + ",\"idx\":[" + string.Join(",", element.Indices.Select(Node)) + "]}",
                _ => "null",
            };
            string body = statement switch
            {
                LAssign assign => "\"kind\":\"" + (assign.Compound ? "compound" : "assign") + "\",\"target\":"
                                  + Node(assign.Target) + ",\"value\":" + Node(assign.Value),
                LInstruction instruction => "\"kind\":\"instr\",\"operands\":["
                                            + string.Join(",", instruction.Operands.Select(Node)) + "]",
                _ => "\"kind\":\"?\"",
            };
            output.Append("{\"site\":").Append(start).Append(",\"root\":").Append(root).Append(",\"op\":")
                .Append(_code[root].Opcode).Append(",\"path\":\"").Append(path).Append("\",").Append(body).Append("}\n");
        }

        public void CollectLabels(List<Node> nodes, Loop? loop)
        {
            foreach (Node node in nodes)
                switch (node)
                {
                    case SimpleNode simple:
                        CollectSimple(_code[simple.Root], loop);
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
                    case WhileNode whileNode:
                        CollectLabels(whileNode.Body, whileNode.Loop);
                        break;
                    case SubNode sub:
                        CollectLabels(sub.Body, null);
                        break;
                    case ForNode forNode:
                        if (forNode.Init != null) CollectSimple(_code[forNode.Init.Root], loop);
                        CollectSimple(_code[forNode.Step.Root], forNode.Original.Loop);
                        CollectLabels(forNode.Body, forNode.Original.Loop);
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
            for (int n = 0; n < nodes.Count; n++)
            {
                Node node = nodes[n];
                if (_libraryAt.TryGetValue(node.Start, out var library))
                {
                    // A linked library sub: its code comes from the module.
                    while (n + 1 < nodes.Count && nodes[n + 1].Start < library.End) n++;
                    continue;
                }
                if (_labels.Contains(node.Start) && node.Start != skipLabel && node is not SubNode)
                    PrintLabel(text, node.Start, depth);
                skipLabel = -1;
                if (Lift(nodes, n, end) is var (use, last))
                {
                    text.Append(pad).Append(use).Append('\n');
                    n = last;
                    continue;
                }
                switch (node)
                {
                    case SimpleNode simple:
                        text.Append(pad).Append(Statement(simple.Root, loop)).Append('\n');
                        break;
                    case IfChainNode chain:
                        for (int a = 0; a < chain.Arms.Count; a++)
                        {
                            Arm arm = chain.Arms[a];
                            if (a > 0 && _labels.Contains(arm.Start))
                                throw new InvalidOperationException($"else-if arm at {arm.Start} carries a label");
                            text.Append(pad).Append(a == 0 ? "if " : "} else if ").Append(Condition(arm.Jcc)).Append(" {\n");
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
                        Print(text, doNode.Body, doNode.TestStart, ownsEnd: true, depth + 1, doNode.Loop, doNode.Head);
                        text.Append(pad).Append("} while ").Append(Condition(doNode.Test)).Append('\n');
                        break;
                    case AtomicNode atomic:
                        text.Append(pad).Append("atomic {\n");
                        Print(text, atomic.Body, atomic.End, ownsEnd: true, depth + 1, loop, -1);
                        text.Append(pad).Append("}\n");
                        break;
                    case SubNode sub:
                        text.Append(new string(' ', (depth - 1) * 4)).Append(sub.Coroutine ? "coroutine " : "sub ")
                            .Append(LabelName(sub.Entry)).Append(" {\n");
                        Print(text, sub.Body, sub.End, ownsEnd: false, depth, null, sub.Entry);
                        text.Append(new string(' ', (depth - 1) * 4)).Append("}\n");
                        break;
                    case WhileNode whileNode:
                        text.Append(pad).Append("while ").Append(Condition(whileNode.Test)).Append(" {\n");
                        Print(text, whileNode.Body, whileNode.BackJump, ownsEnd: true, depth + 1, whileNode.Loop, -1);
                        text.Append(pad).Append("}\n");
                        break;
                    case ForNode forNode when _labels.Contains(forNode.Original.Head):
                    {
                        // The step is a jump target of its own: keep the plain loop and its entry goto.
                        _labels.Add(forNode.Test.Start);
                        var parts = new List<Node>();
                        if (forNode.Init != null) parts.Add(forNode.Init);
                        parts.Add(forNode.Jump);
                        parts.Add(forNode.Original);
                        Print(text, parts, forNode.Original.BackJump + 1, ownsEnd: false, depth, loop, node.Start);
                        break;
                    }
                    case ForNode forNode:
                        text.Append(pad).Append("for ")
                            .Append(forNode.Init != null ? Statement(forNode.Init.Root, loop) : "").Append("; ")
                            .Append(Condition(forNode.Test.Root)).Append("; ")
                            .Append(Statement(forNode.Step.Root, forNode.Original.Loop)).Append(" {\n");
                        Print(text, forNode.Body, forNode.Original.BackJump, ownsEnd: true, depth + 1,
                            forNode.Original.Loop, -1);
                        text.Append(pad).Append("}\n");
                        break;
                }
            }
            if (ownsEnd && _labels.Contains(end)) PrintLabel(text, end, depth);
        }

        /// <summary>Index-addressed local arrays, declared so the compiler keeps their slots. Each array runs to the
        /// next named cell of its bank; the topmost runs to where the original frame put the temporary pool (or the
        /// spare top slot), because its upper elements are reached only by computed index.</summary>
        public List<LocalArray> LocalArrays { get; } = new();
        private readonly Dictionary<(int Type, uint Slot), LocalArray> _arrayAt = new();

        public void DeclareLocalArrays()
        {
            var used = new Dictionary<int, SortedSet<uint>>();
            var bases = new Dictionary<int, SortedSet<uint>>();
            for (int k = 0; k < _code.Count; k++)
                for (int p = 0; p < _code[k].Operands.Count; p++)
                {
                    IrOperand operand = _code[k].Operands[p];
                    if (operand.Kind != IrOperandKind.Literal || operand.Type is not (0x9 or 0xA or 0xB)) continue;
                    if (operand.Type == 0x9 && folding.IsPoolSlot(operand.Value)) continue;
                    (used.TryGetValue(operand.Type, out var set) ? set : used[operand.Type] = new SortedSet<uint>()).Add(operand.Value);
                    if (folding.Access(_code[k].Opcode, p + 1) is { } kinds && kinds.Contains("address"))
                        (bases.TryGetValue(operand.Type, out var b) ? b : bases[operand.Type] = new SortedSet<uint>()).Add(operand.Value);
                }
            foreach (var (type, arrayBases) in bases)
            {
                int bank = Array.IndexOf(StorageBanks.LocalBankTypes, type);
                long top = type == 0x9 && folding.UsesIntTemps ? folding.PoolBase : script.LocalCounts[bank] - 1;
                foreach (uint start in arrayBases)
                {
                    uint? next = used[type].GetViewBetween(start + 1, uint.MaxValue).Cast<uint?>().FirstOrDefault();
                    long end = next ?? Math.Max(top, start + 1);
                    if (end <= start) continue;
                    var array = new LocalArray(type, start, (uint)(end - start));
                    LocalArrays.Add(array);
                    _arrayAt[(type, start)] = array;
                }
            }
        }

        /// <summary>The modules whose names were printed (the source then needs their <c>use</c>).</summary>
        public HashSet<GameModule> UsedModules { get; } = new();

        private string Atom(IrOperand operand)
            => Named(operand) is { } symbol ? symbol.Name
                : operand.Kind == IrOperandKind.Literal && _arrayAt.TryGetValue((operand.Type, operand.Value), out LocalArray? array)
                    ? array.Name : RawAtom(operand);

        /// <summary>The registry name of a global cell; marks its module used.</summary>
        private GlobalSymbol? Named(IrOperand operand)
        {
            if (operand.Kind != IrOperandKind.Literal || operand.Type is not (0x3 or 0x4 or 0x5)) return null;
            foreach (GameModule module in modules)
                if (module.TryGet(operand.Type, operand.Value, out GlobalSymbol symbol))
                {
                    UsedModules.Add(module);
                    return symbol;
                }
            return null;
        }

        /// <summary><c>NAME</c>, or <c>"NAME"</c> when the catalog name is not an identifier, for a script id.</summary>
        private string? ScriptName(IrOperand operand)
        {
            if (operand is not { Kind: IrOperandKind.Literal, Type: 0 }) return null;
            foreach (GameModule module in modules)
                if (module.TryGetScript(operand.Value, out string name))
                {
                    UsedModules.Add(module);
                    return System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")
                           && !IntrinsicCatalog.Keywords.Contains(name)
                        ? name : OmsRawDecompiler.Quote(name);
                }
            return null;
        }

        private string RawAtom(IrOperand operand)
        {
            if (operand is { Kind: IrOperandKind.Literal, Type: 0x9 } && operand.Value > MaxExplicitLocal)
                MaxExplicitLocal = operand.Value;
            return OmsRawDecompiler.Operand(operand, script, LabelName);
        }

        private string Target(int target, Loop? loop) => JumpWord(target, loop) ?? $"goto {LabelName(target)}";

        /// <summary>The lowered statement of a root (an all-leaf form for instructions the folding never touched).</summary>
        private LStatement Lowered(int root)
            => folding.Statements[root]
               ?? new LInstruction(_code[root].Opcode, _code[root].Operands.Select(o => (LExpr)new LLeaf(o)).ToArray());

        private LExpr ConditionOperand(int jcc) => ((LInstruction)Lowered(jcc)).Operands[0];

        private string Condition(int jcc) => Expression(ConditionOperand(jcc), 0);

        private string Negated(LExpr condition) => "!" + Expression(condition, UnaryPrecedence);

        private string Statement(int root, Loop? loop)
        {
            IrInstruction instruction = _code[root];
            IReadOnlyList<IrOperand> operands = instruction.Operands;
            LStatement lowered = Lowered(root);
            switch (instruction.Opcode)
            {
                case OmsCompiler.Jmp when IsJump(instruction, out int target):
                    return Target(target, loop);
                case OmsCompiler.Jcc when IsBranch(instruction, out bool whenTrue, out int target)
                                          && operands[0].Kind != IrOperandKind.CodeLabel:
                    return $"if {(whenTrue ? Condition(root) : Negated(ConditionOperand(root)))} {Target(target, loop)}";
                case OmsCompiler.Jcc when IsTwoTargetBranch(instruction):
                    return $"if {Condition(root)} {Target((int)operands[1].Value, loop)} else "
                           + Target((int)operands[2].Value, loop);
                case OmsCompiler.Call when operands.Count == 1 && operands[0] is { Kind: IrOperandKind.CodeLabel, Type: 0 }:
                    return $"{LabelName((int)operands[0].Value)}()";
                case OmsCompiler.Ret when operands.Count == 0:
                    return "return";
                case OmsCompiler.Exit when operands.Count == 0:
                    return "exit";
                case OmsCompiler.Resume when operands.Count == 0:
                    return "resume";
            }
            if (lowered is LAssign assign)
            {
                string target = Expression(assign.Target, 0);
                if (assign.Value is LBinary binary && IsCompoundForm(assign, binary))
                    return $"{target} {BinaryOperators[binary.Opcode]}= {Expression(binary.Right, 0)}";
                return $"{target} = {Expression(assign.Value, 0)}";
            }
            var arguments = ((LInstruction)lowered).Operands;
            switch (instruction.Opcode)
            {
                case OmsCompiler.CallScript when operands.Count == 1 && operands[0].Kind == IrOperandKind.Literal:
                    return $"run {(arguments[0] is LLeaf leaf ? ScriptName(leaf.Operand) : null) ?? Expression(arguments[0], 0)}";
                case OmsCompiler.Comment when operands.Count == 1 && operands[0].Kind != IrOperandKind.CodeLabel:
                    return $"note {Atom(operands[0])}";
                case OmsCompiler.Call when operands.Count == 1 && operands[0].Kind == IrOperandKind.Literal:
                    return $"call *{Expression(arguments[0], UnaryPrecedence)}";
            }
            if (instruction.Opcode is not (OmsCompiler.BlockBegin or OmsCompiler.BlockEnd or OmsCompiler.Jmp
                    or OmsCompiler.Jcc or OmsCompiler.Call or OmsCompiler.Comment)
                && intrinsics.TryGet(instruction.Opcode, out Intrinsic intrinsic) && intrinsic.Argc == operands.Count
                && !IntrinsicCatalog.Keywords.Contains(intrinsic.Name))
            {
                // x = f(args) when the only written operand is operand 1 (spec §6.3).
                if (intrinsic.HasContract && intrinsic.OutOperands.Count == 1 && intrinsic.OutOperands.Contains(1)
                    && (arguments[0] is LElement || arguments[0] is LLeaf { Operand.Kind: IrOperandKind.Literal } first
                        && StorageBanks.BankByType.ContainsKey(first.Operand.Type)))
                    return $"{Expression(arguments[0], 0)} = {intrinsic.Name}("
                           + string.Join(", ", arguments.Skip(1).Select(argument => Expression(argument, 0))) + ")";
                var text = arguments.Select((argument, k) =>
                    (intrinsic.HasContract && intrinsic.OutOperands.Contains(k + 1) ? "out " : "") + Expression(argument, 0));
                return $"{intrinsic.Name}({string.Join(", ", text)})";
            }
            return "@op(0x" + instruction.Opcode.ToString("x") + string.Concat(operands.Select(o => ", " + Atom(o))) + ")";
        }

        /// <summary>Compound spelling: always for an element target (its order differs from plain assignment, and
        /// the folding chose compound order), and as a shorthand for a plain cell updated from itself.</summary>
        private static bool IsCompoundForm(LAssign assign, LBinary binary)
            => assign.Compound && (assign.Target is LElement || binary.Left is LLeaf left && assign.Target is LLeaf target
                                       && left.Operand == target.Operand);

        // ------------------------------------------------------------------------------------------ expressions

        private const int UnaryPrecedence = 10, PostfixPrecedence = 11;

        private static int Precedence(string symbol) => symbol switch
        {
            "||" => 0,
            "&&" => 1,
            "|" => 2,
            "^" => 3,
            "&" => 4,
            "==" or "!=" => 5,
            "<" or "<=" or ">" or ">=" => 6,
            "<<" or ">>" => 7,
            "+" or "-" => 8,
            _ => 9,
        };

        private string? NamedElementOf(LElement element)
        {
            if (element.Base.Kind == IrOperandKind.Literal && element.Indices.Count == 1
                && _arrayAt.TryGetValue((element.Base.Type, element.Base.Value), out LocalArray? local))
                return $"{local.Name}[{Expression(element.Indices[0], 0)}]";
            if (element.Base.Kind != IrOperandKind.Literal || element.Base.Type is not (0x3 or 0x4 or 0x5)) return null;
            foreach (GameModule module in modules)
                if (module.TryGet(element.Base.Type, element.Base.Value, out GlobalSymbol symbol))
                {
                    if (NamedElement(element, symbol) is not { } named) return null;
                    UsedModules.Add(module);
                    return named;
                }
            return null;
        }

        /// <summary>The named form of an element, when the symbol's declared shape lowers to exactly this lookup:
        /// <c>name[i]</c> for a one-dimensional array, <c>name[r][c]</c> or <c>name[r].column</c> for a table whose row
        /// width is the lookup's immediate stride.</summary>
        private string? NamedElement(LElement element, GlobalSymbol symbol)
        {
            IReadOnlyList<int> shape = symbol.Dimensions;
            if (element.Indices.Count == 1)
                return shape.Count == 1 ? $"{symbol.Name}[{Expression(element.Indices[0], 0)}]" : null;
            if (shape.Count != 2 || element.Indices[1] is not LLeaf { Operand: { Kind: IrOperandKind.Literal, Type: 0 } stride }
                || stride.Value != (uint)shape[1])
                return null;
            string row = Expression(element.Indices[0], 0);
            if (element.Indices[2] is LLeaf { Operand: { Kind: IrOperandKind.Literal, Type: 0 } column }
                && column.Value < int.MaxValue && symbol.ColumnName((int)column.Value) is { } columnName)
                return $"{symbol.Name}[{row}].{columnName}";
            return $"{symbol.Name}[{row}][{Expression(element.Indices[2], 0)}]";
        }

        private static bool IsComparison(int opcode) => opcode is >= 0x5A and <= 0x5F;

        /// <summary>A node whose value is always 0 or 1: comparisons, and and/or of such nodes.</summary>
        private static bool IsBoolean(LExpr expression)
            => expression is LBinary binary
               && (IsComparison(binary.Opcode)
                   || binary.Opcode is 0x56 or 0x57 && IsBoolean(binary.Left) && IsBoolean(binary.Right));

        private string Expression(LExpr expression, int context, bool rightOperand = false)
        {
            switch (expression)
            {
                case LLeaf leaf:
                    return Atom(leaf.Operand);
                case LElement element:
                {
                    if (NamedElementOf(element) is { } named) return named;
                    string @base = RawAtom(element.Base);
                    if (element.Indices.Count == 1) return $"{@base}[{Expression(element.Indices[0], 0)}]";
                    string stride = Expression(element.Indices[1], 0);
                    return $"{@base[..^1]}:{stride}][{Expression(element.Indices[0], 0)}][{Expression(element.Indices[2], 0)}]";
                }
                case LBinary { Opcode: 0x51, Left: LLeaf { Operand: { Kind: IrOperandKind.Literal, Type: 0, Value: 0 } } } negation:
                {
                    string operand = negation.Right is LLeaf { Operand.Type: 0 } immediate
                        ? $"({Atom(immediate.Operand)})" : Expression(negation.Right, UnaryPrecedence);
                    string text = "-" + operand;
                    return context > UnaryPrecedence ? $"({text})" : text;
                }
                case LBinary binary:
                {
                    string symbol = BinaryOperators[binary.Opcode];
                    if (binary.Opcode is 0x56 or 0x57 && IsBoolean(binary.Left) && IsBoolean(binary.Right))
                        symbol = binary.Opcode == 0x56 ? "&&" : "||";
                    int precedence = Precedence(symbol);
                    string text = $"{Expression(binary.Left, precedence)} {symbol} {Expression(binary.Right, precedence, true)}";
                    return precedence < context || precedence == context && rightOperand ? $"({text})" : text;
                }
                default:
                    throw new InvalidOperationException($"unknown expression {expression.GetType().Name}");
            }
        }
    }
}
