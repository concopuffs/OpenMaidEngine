using Age.Engine.Model;
using Age.Script.Abi;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Modules;
using Age.Script.Syntax;

namespace Age.Script.Compile;

public sealed class OmsCompileException(SourceSpan span, string message) : Exception($"{span}: {message}")
{
    public SourceSpan Span { get; } = span;
}

/// <summary>A compiler warning (spec §8.2): the code compiles, but probably not as meant.</summary>
public sealed record OmsWarning(SourceSpan Span, string Message)
{
    public override string ToString() => $"{Span}: warning: {Message}";
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
    public const int BlockBegin = 0x1F4, BlockEnd = 0x1F5, Comment = 0x1A7, Resume = 0x7C, ThrowExit = 0x01;

    /// <summary>Instructions after which control cannot fall through.</summary>
    public static bool IsTerminator(int opcode) => opcode is Ret or Exit or Jmp or Resume or ThrowExit;
    private const uint NoTarget = 0xFFFFFFFF;

    /// <param name="modules">Resolves <c>use</c> module names, such as <c>himegari.globals</c>.</param>
    /// <param name="warnings">Receives §8.2 warnings: recursion with bound parameters, division by a constant zero,
    /// and opcodes no game corpus of the ABI executes.</param>
    public static IrScript Compile(ScriptUnit unit, string abiId, OpcodeTable? table = null, OmsLayout? layout = null,
                                   IntrinsicCatalog? intrinsics = null, Func<string, GameModule?>? modules = null,
                                   ICollection<OmsWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var generator = new CodeGenerator(table, intrinsics, layout, modules, warnings, null, false);
        generator.EmitAll(unit.Body);
        generator.ResolveLabels();
        return generator.Build(unit.Name, abiId);
    }

    /// <summary>Compiles a unit and reports where every label and linked library sub landed, for building decompiler
    /// patterns (spec §11 steps 4 and 5). <paramref name="linkAll"/> links every library sub of the used modules,
    /// referenced or not.</summary>
    public static OmsImage CompileImage(ScriptUnit unit, string abiId, OpcodeTable? table, OmsLayout? layout,
                                        IntrinsicCatalog? intrinsics, Func<string, GameModule?>? modules, bool linkAll)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var generator = new CodeGenerator(table, intrinsics, layout, modules, null, null, linkAll);
        generator.EmitAll(unit.Body);
        generator.ResolveLabels();
        IrScript script = generator.Build(unit.Name, abiId);
        return new OmsImage(script, generator.Labels, generator.Linked);
    }

    /// <summary>
    /// Compiles source decompiled from <paramref name="reference"/> with every statement's temporaries pinned to the
    /// slots the reference uses there (the layout's own temporary pins are ignored). The pins that differ from the
    /// canonical plan come back in <paramref name="temps"/>, ready for the sidecar. A statement whose instructions
    /// differ from the reference other than in temporary slots is an error; the caller compares the whole result.
    /// </summary>
    public static IrScript Fit(ScriptUnit unit, string abiId, OpcodeTable? table, OmsLayout? layout,
                               IntrinsicCatalog? intrinsics, Func<string, GameModule?>? modules, IrScript reference,
                               out SortedDictionary<int, OmsTempOverride> temps)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(reference);
        var generator = new CodeGenerator(table, intrinsics, layout, modules, null, reference, false);
        generator.EmitAll(unit.Body);
        generator.ResolveLabels();
        temps = generator.Fitted;
        return generator.Build(unit.Name, abiId);
    }

    /// <summary>First slot of the probe range <see cref="Fit"/> and pattern builders use for temporaries.</summary>
    public const uint TempProbe = 0x40000000;

    private sealed record Occurrence(IrDataItem Item, SourceSpan Span);

    /// <summary>A declared name for a storage cell or array, from <c>global</c>/<c>local</c> or a used module.</summary>
    private sealed record Declared(RawStorage Cell, IReadOnlyList<int> Dimensions, IReadOnlyDictionary<string, int> Columns);

    private static readonly Dictionary<(string Type, bool Local), string> BankOfDeclaration = new()
    {
        [("int", false)] = "G", [("float", false)] = "GF", [("str", false)] = "GS",
        [("int", true)] = "L", [("float", true)] = "LF", [("str", true)] = "LS",
    };

    private sealed record LibrarySub(SubStatement Sub, string Module, IReadOnlyList<string> Labels);

    private sealed class CodeGenerator(OpcodeTable? table, IntrinsicCatalog? intrinsics, OmsLayout? layout,
                                       Func<string, GameModule?>? modules, ICollection<OmsWarning>? warnings,
                                       IrScript? reference, bool linkAll)
    {
        private string? _currentSub;
        private readonly HashSet<int> _warnedOpcodes = new();

        private void Warn(SourceSpan span, string message) => warnings?.Add(new OmsWarning(span, message));

        /// <summary>Warns once per opcode that no game corpus of the target ABI executes.</summary>
        private void CheckObserved(int opcode, SourceSpan span)
        {
            if (intrinsics == null || intrinsics.Contracts.IsObserved(opcode, intrinsics.AbiId) || !_warnedOpcodes.Add(opcode))
                return;
            Warn(span, $"opcode 0x{opcode:x} is installed in {intrinsics.AbiId} but no game corpus executes it");
        }
        private readonly Dictionary<string, Declared> _names = new(StringComparer.Ordinal);
        private readonly Dictionary<string, uint> _constants = new(StringComparer.Ordinal);
        private readonly List<GameModule> _used = new();
        private readonly Dictionary<int, long> _declaredLocals = new();
        private readonly List<IrInstruction> _code = new();
        private readonly List<Occurrence> _occurrences = new();
        private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
        private readonly HashSet<string> _userLabels = new(StringComparer.Ordinal);
        private readonly List<(string Key, SourceSpan Span)> _pendingLabels = new();
        private const uint PendingTag = 0x80000000;
        private readonly Stack<(string Break, string Continue)> _loops = new();
        private int _internal;
        private uint _tempBase;
        private readonly Dictionary<string, SubStatement> _subs = new(StringComparer.Ordinal);
        private Dictionary<string, RawStorage>? _parameters;
        private readonly HashSet<string> _referenced = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TemplateStatement> _templates = new(StringComparer.Ordinal);
        /// <summary>Library subroutines of the used source modules, in declaration order.</summary>
        private readonly List<LibrarySub> _library = new();
        private readonly List<OmsLinkedSub> _linkedRanges = new();
        /// <summary>Label keys in order of first reference (code order), for the library link order.</summary>
        private readonly List<string> _referenceOrder = new();
        private readonly HashSet<string> _enums = new(StringComparer.Ordinal);

        /// <summary>Temporary pins <see cref="OmsCompiler.Fit"/> derived, by statement site.</summary>
        public SortedDictionary<int, OmsTempOverride> Fitted { get; } = new();

        /// <summary>Instruction index of every user, module and sub label (after <see cref="ResolveLabels"/>).</summary>
        public IReadOnlyDictionary<string, int> Labels
            => _labels.Where(pair => !pair.Key.Contains('#')).ToDictionary(pair => pair.Key, pair => pair.Value);

        public IReadOnlyList<OmsLinkedSub> Linked => _linkedRanges;
        private readonly HashSet<string> _linked = new(StringComparer.Ordinal);
        private readonly HashSet<string> _placed = new(StringComparer.Ordinal);
        /// <summary>Names a used source module declares, with the module; a script may not redeclare them.</summary>
        private readonly Dictionary<string, string> _moduleNames = new(StringComparer.Ordinal);
        private readonly Stack<string> _expanding = new();
        private int _expansions;

        /// <summary>Whether control can arrive at <paramref name="position"/> through a label: a user label, or an
        /// internal label its construct has already jumped to.</summary>
        private bool LabelAt(int position)
            => _labels.Any(pair => pair.Value == position && (_userLabels.Contains(pair.Key) || _referenced.Contains(pair.Key)));

        private string NewLabel() => $"#{_internal++}";

        private void Define(string key, SourceSpan span)
        {
            if (!_labels.TryAdd(key, _code.Count))
                throw new OmsCompileException(span, $"label '{key}' is defined twice");
        }

        public void EmitAll(IReadOnlyList<Statement> statements)
        {
            CollectUserLabels(statements);
            List<Statement> declarations = DeclarationsIn(statements);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (UseStatement use in declarations.OfType<UseStatement>()) Use(use, visited);
            // Locals a used module names count as named slots in every script that uses it.
            var highest = new Dictionary<string, long>(StringComparer.Ordinal);
            IEnumerable<Statement> scanned = statements.Concat(_library.Select(library => library.Sub))
                .Concat(_templates.Values.Where(template => _moduleNames.ContainsKey(template.Name)));
            foreach (RawOperand operand in OperandsOf(scanned))
                if (operand is RawStorage { Bank: "L" or "LF" or "LS" } local)
                    highest[local.Bank] = Math.Max(highest.GetValueOrDefault(local.Bank, -1), local.Index);
            Declare(declarations, highest);
            foreach (var (bank, slot) in highest) _declaredLocals[StorageBanks.TypeByBank[bank]] = slot;
            _tempBase = layout?.TempBase ?? (uint)(highest.GetValueOrDefault("L", -1) + 1);
            foreach (Statement statement in statements)
            {
                PlaceLibrary();
                Emit(statement);
            }
            PlaceLibrary();
            LinkLibrary();
            if (layout?.Library?.Keys.FirstOrDefault(name => !_placed.Contains(name)) is { } unplaced)
                throw new OmsCompileException(default, $"the sidecar places library sub '{unplaced}', which is not linked at that index");
        }

        /// <summary>Declarations anywhere outside templates: <c>use</c>, <c>const</c>, <c>global</c>, <c>local</c> and
        /// the script's own <c>inline</c> templates.</summary>
        private static List<Statement> DeclarationsIn(IReadOnlyList<Statement> statements)
        {
            var declarations = new List<Statement>();
            var pending = new Stack<IReadOnlyList<Statement>>();
            pending.Push(statements);
            while (pending.Count > 0)
                foreach (Statement statement in pending.Pop())
                    switch (statement)
                    {
                        case UseStatement or ConstStatement or StorageDeclaration or TemplateStatement or EnumStatement:
                            declarations.Add(statement);
                            break;
                        case SubStatement sub: pending.Push(sub.Body); break;
                        case AtomicStatement atomic: pending.Push(atomic.Body); break;
                    }
            return declarations;
        }

        /// <summary>Imports a module (spec §3.2): its global names and, for a source module, its constants, templates
        /// and library subroutines; modules it uses are imported first.</summary>
        private void Use(UseStatement use, HashSet<string> visited)
        {
            if (!visited.Add(use.Module)) return;
            GameModule module = modules?.Invoke(use.Module)
                ?? throw new OmsCompileException(use.Span, $"unknown module '{use.Module}'");
            ScriptUnit? unit = module.Source;
            foreach (UseStatement inner in unit?.Body.OfType<UseStatement>() ?? []) Use(inner, visited);
            _used.Add(module);
            foreach (GlobalSymbol symbol in module.Symbols)
            {
                _names[symbol.Name] = new Declared(
                    new RawStorage(use.Span, StorageBanks.BankByType[symbol.Type], symbol.Address),
                    symbol.Dimensions, symbol.Columns);
                if (unit != null) Shared(symbol.Name, use.Span, unit.Name);
            }
            if (unit == null) return;
            foreach (Statement statement in unit.Body)
                switch (statement)
                {
                    case UseStatement or StorageDeclaration { Local: false, Address: not null }:
                        break;
                    case ConstStatement constant:
                        Shared(constant.Name, constant.Span, unit.Name);
                        _constants[constant.Name] = constant.Value;
                        _names.Remove(constant.Name);
                        break;
                    case EnumStatement enumeration:
                        Shared(enumeration.Name, enumeration.Span, unit.Name);
                        _enums.Add(enumeration.Name);
                        foreach (EnumMember member in enumeration.Members)
                        {
                            Shared(member.Name, member.Span, unit.Name);
                            _constants[member.Name] = member.Value;
                            _names.Remove(member.Name);
                        }
                        break;
                    case TemplateStatement template:
                        Shared(template.Name, template.Span, unit.Name);
                        _templates[template.Name] = template;
                        break;
                    case SubStatement sub:
                    {
                        // A library sub's labels are module names: other subs may call into them (a sub with several
                        // entry points), and a script may not reuse them.
                        string[] labels = [sub.Name, .. TemplateExpander.DefinedLabels(sub.Body)];
                        foreach (string label in labels)
                        {
                            Shared(label, sub.Span, unit.Name);
                            if (_userLabels.Contains(label))
                                throw new OmsCompileException(sub.Span, $"'{label}' is defined by the script and by module {unit.Name}");
                        }
                        if (!_subs.TryAdd(sub.Name, sub))
                            throw new OmsCompileException(sub.Span, $"'{sub.Name}' is defined by the script and by module {unit.Name}");
                        foreach (string label in labels) _userLabels.Add(label);
                        _library.Add(new LibrarySub(sub, unit.Name, labels));
                        break;
                    }
                    default:
                        throw new OmsCompileException(statement.Span,
                            $"module {unit.Name} may hold only use, const, enum, pinned global, inline and sub declarations");
                }
        }

        /// <summary>Records a name a source module declares; two modules may not declare the same name.</summary>
        private void Shared(string name, SourceSpan span, string module)
        {
            if (_moduleNames.TryGetValue(name, out string? other) && other != module)
                throw new OmsCompileException(span, $"'{name}' is declared by modules {other} and {module}");
            _moduleNames[name] = module;
        }

        /// <summary>Links library subs the sidecar places at the current instruction index.</summary>
        private void PlaceLibrary()
        {
            if (layout?.Library == null) return;
            for (bool placed = true; placed;)
            {
                placed = false;
                foreach (LibrarySub library in _library)
                    if (!_linked.Contains(library.Sub.Name) && layout.Library.TryGetValue(library.Sub.Name, out int index)
                        && index == _code.Count)
                    {
                        _placed.Add(library.Sub.Name);
                        Link(library);
                        placed = true;
                        break;
                    }
            }
        }

        /// <summary>Links the library subs the code needs after the script (spec §6.3), breadth first in order of first
        /// reference: the subs the code references, in code order, then the subs each linked sub references in turn.
        /// This is the original compiler's order for its library functions.</summary>
        private void LinkLibrary()
        {
            if (_library.All(library => _linked.Contains(library.Sub.Name))) return;
            if (linkAll)
            {
                foreach (LibrarySub library in _library.Where(library => !_linked.Contains(library.Sub.Name)).ToList())
                    Link(library);
                return;
            }
            var owner = new Dictionary<string, LibrarySub>(StringComparer.Ordinal);
            foreach (LibrarySub library in _library)
                foreach (string label in library.Labels) owner[label] = library;
            var queue = new List<LibrarySub>();
            var queued = new HashSet<string>(_linked, StringComparer.Ordinal);
            int scanned = 0;
            void Scan()
            {
                for (; scanned < _referenceOrder.Count; scanned++)
                    if (owner.TryGetValue(_referenceOrder[scanned], out LibrarySub? library) && queued.Add(library.Sub.Name))
                        queue.Add(library);
            }
            Scan();
            for (int i = 0; i < queue.Count; i++)
            {
                Link(queue[i]);
                Scan();
            }
        }

        private void Link(LibrarySub library)
        {
            _linked.Add(library.Sub.Name);
            int start = _code.Count;
            EmitSub(library.Sub);
            _linkedRanges.Add(new OmsLinkedSub(library.Sub.Name, library.Module, start, _code.Count, library.Labels,
                library.Sub.Coroutine));
        }

        /// <summary>Collects <c>const</c>, <c>global</c>, <c>local</c> and <c>inline</c> declarations (spec §5.2). Pinned
        /// locals count as named slots; unpinned locals take slots above every local the source names, so the
        /// temporary pool stays above them.</summary>
        private void Declare(List<Statement> declarations, Dictionary<string, long> highest)
        {
            var user = new HashSet<string>(StringComparer.Ordinal);
            void Claim(string name, SourceSpan span)
            {
                if (_moduleNames.TryGetValue(name, out string? module))
                    throw new OmsCompileException(span, $"'{name}' is already declared by module {module}");
                if (!user.Add(name) || _userLabels.Contains(name))
                    throw new OmsCompileException(span, $"'{name}' is declared twice");
            }
            foreach (Statement statement in declarations)
            {
                switch (statement)
                {
                    case TemplateStatement template:
                        Claim(template.Name, template.Span);
                        _templates[template.Name] = template;
                        break;
                    case EnumStatement enumeration:
                        Claim(enumeration.Name, enumeration.Span);
                        _enums.Add(enumeration.Name);
                        foreach (EnumMember member in enumeration.Members)
                        {
                            Claim(member.Name, member.Span);
                            _constants[member.Name] = member.Value;
                            _names.Remove(member.Name);
                        }
                        break;
                    case ConstStatement constant:
                        Claim(constant.Name, constant.Span);
                        _constants[constant.Name] = constant.Value;
                        _names.Remove(constant.Name);
                        break;
                    case StorageDeclaration declaration:
                    {
                        Claim(declaration.Name, declaration.Span);
                        string bank = BankOfDeclaration[(declaration.Type, declaration.Local)];
                        long size = declaration.Dimensions.Aggregate(1L, (product, dimension) => product * dimension);
                        uint index;
                        if (declaration.Address is uint pinned)
                        {
                            index = pinned;
                            if (declaration.Local)
                                highest[bank] = Math.Max(highest.GetValueOrDefault(bank, -1), pinned + Math.Max(size, 1) - 1);
                        }
                        else
                        {
                            if (declaration.Dimensions.Contains(0))
                                throw new OmsCompileException(declaration.Span, $"local '{declaration.Name}' needs a known size or an @ slot");
                            long next = highest.GetValueOrDefault(bank, -1) + 1;
                            index = (uint)next;
                            highest[bank] = next + size - 1;
                        }
                        _names[declaration.Name] = new Declared(new RawStorage(declaration.Span, bank, index),
                            declaration.Dimensions, declaration.Columns);
                        break;
                    }
                }
            }
        }

        private Declared? Name(string name) => _names.TryGetValue(name, out Declared? declared) ? declared : null;

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
                    case NamedElementExpr named:
                        foreach (Expr index in named.Indices) pending.Push(index);
                        break;
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
                    case CallAssignStatement call:
                        pending.Push(call.Target);
                        pending.Push(call.Call);
                        break;
                    case SubStatement sub:
                        foreach (SubParameter parameter in sub.Parameters) pending.Push(parameter.Cell);
                        if (sub.Result != null) pending.Push(sub.Result);
                        foreach (Statement inner in sub.Body) pending.Push(inner);
                        break;
                    case TemplateStatement template: foreach (Statement inner in template.Body) pending.Push(inner); break;
                    case StaticIfStatement branch:
                        foreach (Statement inner in branch.Then) pending.Push(inner);
                        if (branch.Else != null) foreach (Statement inner in branch.Else) pending.Push(inner);
                        break;
                    case DialogueStatement line: foreach (CallArgument argument in line.Arguments) pending.Push(argument.Value); break;
                }
            }
        }

        private void CollectUserLabels(IReadOnlyList<Statement> statements)
        {
            foreach (Statement statement in statements)
                switch (statement)
                {
                    case LabelStatement label: _userLabels.Add(label.Name); break;
                    case SubStatement sub:
                        if (!_userLabels.Add(sub.Name) || !_subs.TryAdd(sub.Name, sub))
                            throw new OmsCompileException(sub.Span, $"'{sub.Name}' is defined twice");
                        CollectUserLabels(sub.Body);
                        break;
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
                    CheckObserved(raw.Opcode, raw.Span);
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
                    Lower(new LInstruction(CallScript, [ScriptOperand(run.Script) ?? ToL(run.Script)]), run.Span);
                    break;
                case NoteStatement note:
                    Add(Comment, [Atom(note.Value, "a note")]);
                    break;
                case IndirectCallStatement call:
                    Lower(new LInstruction(Call, [ToL(call.Target)]), call.Span);
                    break;
                case CallAssignStatement call:
                    EmitCallAssign(call);
                    break;
                case SubStatement sub:
                    EmitSub(sub);
                    break;
                case ResumeStatement:
                    Add(Resume, []);
                    break;
                case DialogueStatement line:
                    EmitDialogue(line);
                    break;
                case StaticIfStatement branch:
                    throw new OmsCompileException(branch.Span, "static if belongs in a template body");
                case UseStatement or ConstStatement or StorageDeclaration or TemplateStatement or EnumStatement:
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
            if (assign.CompoundOp is "/" or "%" && assign.Value is OperandExpr { Operand: RawInteger { Value: 0 } })
                Warn(assign.Span, $"'{assign.CompoundOp}=' by a constant zero (the VM does not guard it)");
            Lower(new LAssign(target, new LBinary(opcode, LValue(assign.Target), ToL(assign.Value)), true), assign.Span);
        }

        /// <summary>Lowers one statement with the canonical temporary plan, or the sidecar's override for the
        /// statement starting at this instruction.</summary>
        private static readonly uint[] Probes = Enumerable.Range(0, 0x400).Select(k => TempProbe + (uint)k).ToArray();

        private static bool IsReferenceType(int type) => type is 0xC or 0xD or 0xE;

        /// <summary>Fit mode: lowers the statement with probe slots, reads each temporary's slot off the reference at
        /// the same instructions, and emits it with those slots, recording them where they are not canonical.</summary>
        private void LowerFitted(LStatement statement, SourceSpan span, int site)
        {
            bool swap = layout?.RefSwap == true;
            var probe = new CanonicalTemps(statement, _tempBase, swap, Probes, Probes);
            var probed = new List<IrInstruction>();
            var plan = new CanonicalTemps(statement, _tempBase, swap);
            var canonical = new List<IrInstruction>();
            try
            {
                ExpressionLowering.Lower(statement, probe.Allocate, probed.Add);
                ExpressionLowering.Lower(statement, plan.Allocate, canonical.Add);
            }
            catch (InvalidDataException error)
            {
                throw new OmsCompileException(span, error.Message);
            }
            var ints = new uint?[probe.IntCount];
            var refs = new uint?[probe.RefCount];
            var canonicalInts = new uint[probe.IntCount];
            var canonicalRefs = new uint[probe.RefCount];
            for (int i = 0; i < probed.Count; i++)
            {
                IrInstruction mine = probed[i];
                IrInstruction? original = site + i < reference!.Code.Count ? reference.Code[site + i] : null;
                if (original == null || original.Opcode != mine.Opcode || original.Operands.Count != mine.Operands.Count)
                    throw new OmsCompileException(span, $"the statement at instruction {site} does not fit the original");
                for (int p = 0; p < mine.Operands.Count; p++)
                {
                    IrOperand m = mine.Operands[p], o = original.Operands[p];
                    bool probeSlot = m.Kind == IrOperandKind.Literal && m.Value - TempProbe < (uint)Probes.Length;
                    if (probeSlot && m.Type == 0x9 || probeSlot && IsReferenceType(m.Type))
                    {
                        int k = (int)(m.Value - TempProbe);
                        uint?[] slots = m.Type == 0x9 ? ints : refs;
                        if (o.Kind != IrOperandKind.Literal || o.Type != m.Type || slots[k] is uint seen && seen != o.Value)
                            throw new OmsCompileException(span, $"the temporaries at instruction {site + i} do not fit the original");
                        slots[k] = o.Value;
                        (m.Type == 0x9 ? canonicalInts : canonicalRefs)[k] = canonical[i].Operands[p].Value;
                    }
                    else if (m.Kind == IrOperandKind.Literal && m != o)
                        throw new OmsCompileException(span, $"operand {p + 1} at instruction {site + i} does not fit the original");
                }
            }
            uint[] fittedInts = ints.Select(slot => slot ?? 0).ToArray(), fittedRefs = refs.Select(slot => slot ?? 0).ToArray();
            uint[]? intPins = fittedInts.SequenceEqual(canonicalInts) ? null : fittedInts;
            uint[]? refPins = fittedRefs.SequenceEqual(canonicalRefs) ? null : fittedRefs;
            if (intPins != null || refPins != null) Fitted[site] = new OmsTempOverride { Ints = intPins, Refs = refPins };
            ExpressionLowering.Lower(statement, new CanonicalTemps(statement, _tempBase, swap, intPins, refPins).Allocate, _code.Add);
        }

        private void Lower(LStatement statement, SourceSpan span)
        {
            int site = _code.Count;
            if (reference != null)
            {
                LowerFitted(statement, span, site);
                return;
            }
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
                case NamedElementExpr named:
                    return NamedElement(named);
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
                    if (binary.Op is "/" or "%" && binary.Right is OperandExpr { Operand: RawInteger { Value: 0 } })
                        Warn(binary.Span, $"'{binary.Op}' by a constant zero (the VM does not guard it)");
                    return new LBinary(opcode, ToL(binary.Left), ToL(binary.Right));
                case UnaryExpr { Op: "-" } negation:
                    return new LBinary(0x51, Zero, ToL(negation.Operand));
                case UnaryExpr { Op: "!" } not:
                    return new LBinary(0x5A, ToL(not.Operand), Zero);
                case UnaryExpr { Op: "~" } complement:
                    return new LBinary(0x51, new LLeaf(IrOperand.Literal(0, NoTarget)), ToL(complement.Operand));
                case NoneExpr:
                    throw new OmsCompileException(expression.Span, "'none' has no value here; test for it with static if");
                default:
                    throw new OmsCompileException(expression.Span, $"unsupported expression {expression.GetType().Name}");
            }
        }

        /// <summary><c>run NAME</c> or <c>run "NAME"</c>: a catalog script from a used scripts module.</summary>
        private LLeaf? ScriptOperand(Expr script)
        {
            string? name = script switch
            {
                OperandExpr { Operand: RawLabelRef label } when !(_parameters?.ContainsKey(label.Name) ?? false) => label.Name,
                OperandExpr { Operand: RawString text } => text.Text,
                _ => null,
            };
            if (name == null) return null;
            foreach (GameModule module in _used)
                if (module.TryGetScript(name, out uint id)) return new LLeaf(IrOperand.Literal(0, id));
            if (script is OperandExpr { Operand: RawString })
                throw new OmsCompileException(script.Span, $"unknown script \"{name}\"; use the game's scripts module");
            return null;
        }

        /// <summary><c>name[i]</c>, <c>name[r][c]</c> or <c>name[r].column</c> against the name's declared shape.</summary>
        private LExpr NamedElement(NamedElementExpr named)
        {
            Declared declared = Name(named.Name)
                ?? throw new OmsCompileException(named.Span, $"'{named.Name}' is not a declared array");
            IrOperand cell = Operand(declared.Cell);
            int pointer = cell.Type switch { 0x3 or 0x9 => 0xC, 0x4 or 0xA => 0xD, _ => 0xE };
            IReadOnlyList<int> shape = declared.Dimensions;
            LLeaf Immediate(int value) => new(IrOperand.Literal(0, (uint)value));
            if (named.Column != null)
            {
                if (shape.Count != 2 || named.Indices.Count != 1)
                    throw new OmsCompileException(named.Span, $"'{named.Name}[row].{named.Column}' needs a [rows][columns] table and one index");
                if (!declared.Columns.TryGetValue(named.Column, out int column))
                    throw new OmsCompileException(named.Span, $"'{named.Name}' has no column '{named.Column}'");
                return new LElement(cell, [ToL(named.Indices[0]), Immediate(shape[1]), Immediate(column)], pointer);
            }
            if (shape.Count == 1 && named.Indices.Count == 1)
                return new LElement(cell, [ToL(named.Indices[0])], pointer);
            if (shape.Count == 2 && named.Indices.Count == 2)
                return new LElement(cell, [ToL(named.Indices[0]), Immediate(shape[1]), ToL(named.Indices[1])], pointer);
            throw new OmsCompileException(named.Span,
                $"'{named.Name}' is declared with {shape.Count} dimensions, not {named.Indices.Count}");
        }

        private static bool IsBoolean(Expr expression)
            => expression is BinaryExpr { Op: "==" or "!=" or "<" or "<=" or ">" or ">=" or "&&" or "||" }
                   or UnaryExpr { Op: "!" };

        private LExpr Boolean(Expr expression)
            => IsBoolean(expression) ? ToL(expression) : new LBinary(0x5B, ToL(expression), Zero);

        /// <summary>An assignable operand: a storage cell or an element.</summary>
        private LExpr LValue(Expr expression)
            => expression is OperandExpr { Operand: RawStorage } or ElementExpr or NamedElementExpr
               || expression is OperandExpr { Operand: RawLabelRef name }
                  && (_parameters?.ContainsKey(name.Name) == true || Name(name.Name) != null)
                ? ToL(expression)
                : throw new OmsCompileException(expression.Span, "only a storage cell, a parameter or an element can be assigned");

        /// <summary>A placed sub or coroutine (spec §6.3): control may not fall into it; a sub that can fall off its
        /// end returns implicitly, and a coroutine must end with <c>resume</c>. Parameter names stand for their cells
        /// inside the body, and break/continue cannot leave it.</summary>
        private void EmitSub(SubStatement sub)
        {
            if (sub.Guarded)
                Add(Exit, []);
            else if (_code.Count > 0 && (!IsTerminator(_code[^1].Opcode) || LabelAt(_code.Count)))
                throw new OmsCompileException(sub.Span,
                    $"control falls into {(sub.Coroutine ? "coroutine" : "sub")} '{sub.Name}'; end the code before it with goto, return or exit");
            var parameters = new Dictionary<string, RawStorage>(StringComparer.Ordinal);
            foreach (SubParameter parameter in sub.Parameters)
                if (!parameters.TryAdd(parameter.Name, parameter.Cell) || _userLabels.Contains(parameter.Name))
                    throw new OmsCompileException(parameter.Span, $"parameter '{parameter.Name}' clashes with another name");
            int start = _code.Count;
            Define(sub.Name, sub.Span);
            var outerLoops = _loops.ToArray();
            _loops.Clear();
            Dictionary<string, RawStorage>? outerParameters = _parameters;
            string? outerSub = _currentSub;
            _parameters = parameters;
            _currentSub = sub.Name;
            foreach (Statement inner in sub.Body) Emit(inner);
            _parameters = outerParameters;
            _currentSub = outerSub;
            foreach (var loop in outerLoops.Reverse()) _loops.Push(loop);
            bool fallsOff = _code.Count == start || !IsTerminator(_code[^1].Opcode) || LabelAt(_code.Count);
            if (sub.Coroutine)
            {
                if (fallsOff || _code[^1].Opcode != Resume)
                    throw new OmsCompileException(sub.Span, $"coroutine '{sub.Name}' must end with resume");
            }
            else if (fallsOff)
                Add(Ret, []);
        }

        /// <summary>Calls a sub with bound parameters: each argument is assigned to its cell, then <c>call</c>.</summary>
        private void EmitSubCall(SubStatement sub, CallStatement call)
        {
            if (sub.Coroutine)
                throw new OmsCompileException(call.Span, $"coroutine '{sub.Name}' cannot be called; register it as a handler");
            if (call.Arguments.Count != sub.Parameters.Count)
                throw new OmsCompileException(call.Span,
                    $"{sub.Name} takes {sub.Parameters.Count} arguments, not {call.Arguments.Count}");
            if (sub.Name == _currentSub && sub.Parameters.Count > 0)
                Warn(call.Span, $"the recursive call to '{sub.Name}' overwrites its own bound parameters");
            for (int k = 0; k < sub.Parameters.Count; k++)
            {
                if (call.Arguments[k].Out)
                    throw new OmsCompileException(call.Arguments[k].Value.Span, "sub arguments are assigned to their cells; remove 'out'");
                Lower(new LAssign(new LLeaf(Operand(sub.Parameters[k].Cell)), ToL(call.Arguments[k].Value), false), call.Span);
            }
            Add(Call, [LabelOperand(sub.Name, call.Span)]);
        }

        /// <summary>Expands a template use (spec §6.3) and emits the result in place. The expansion's labels are its
        /// own, and break/continue inside it cannot leave it.</summary>
        private void EmitTemplate(TemplateStatement template, IReadOnlyList<CallArgument> arguments, SourceSpan span)
        {
            if (_expanding.Contains(template.Name))
                throw new OmsCompileException(span, $"template '{template.Name}' expands itself");
            foreach (TemplateParameter parameter in template.Parameters)
                if (parameter.Kind is not ("const" or "text" or "expr" or "cell") && !_enums.Contains(parameter.Kind))
                    throw new OmsCompileException(parameter.Span,
                        $"parameter '{parameter.Name}' of '{template.Name}' has unknown kind '{parameter.Kind}'");
            IReadOnlyList<Statement> body = TemplateExpander.Expand(template, arguments, span, ConstantOf,
                $"#{template.Name}{_expansions++}");
            CollectUserLabels(body);
            var outerLoops = _loops.ToArray();
            _loops.Clear();
            _expanding.Push(template.Name);
            foreach (Statement statement in body) Emit(statement);
            _expanding.Pop();
            foreach (var loop in outerLoops.Reverse()) _loops.Push(loop);
        }

        /// <summary>A dialogue line (spec §11.2) expands through the one <c>@dialogue</c> template of the used modules:
        /// the text binds its <c>text</c> parameter, the speaker (a constant) its <c>speaker</c> parameter.</summary>
        private void EmitDialogue(DialogueStatement line)
        {
            TemplateStatement[] candidates = _templates.Values.Where(template => template.Dialogue).ToArray();
            if (candidates.Length != 1)
                throw new OmsCompileException(line.Span, candidates.Length == 0
                    ? "a dialogue line needs an @dialogue template; use the game's dialogue module"
                    : $"dialogue lines are ambiguous between templates {string.Join(", ", candidates.Select(t => t.Name))}");
            var arguments = new List<CallArgument>
            {
                new(new OperandExpr(line.Span, new RawString(line.Span, line.Text)), false, "text"),
            };
            if (line.Speaker != null)
            {
                if (!_constants.ContainsKey(line.Speaker))
                    throw new OmsCompileException(line.Span, $"unknown speaker '{line.Speaker}'");
                arguments.Add(new CallArgument(new OperandExpr(line.Span, new RawLabelRef(line.Span, line.Speaker)), false, "speaker"));
            }
            arguments.AddRange(line.Arguments);
            EmitTemplate(candidates[0], arguments, line.Span);
        }

        /// <summary>The value of a <c>const</c> template argument: an integer, a constant, or null for <c>none</c>.</summary>
        private long? ConstantOf(Expr expression) => expression switch
        {
            NoneExpr => null,
            OperandExpr { Operand: RawInteger integer } => unchecked((int)integer.Value),
            OperandExpr { Operand: RawLabelRef name } when _constants.TryGetValue(name.Name, out uint value) => unchecked((int)value),
            UnaryExpr { Op: "-" } negation when ConstantOf(negation.Operand) is long value => -value,
            _ => throw new OmsCompileException(expression.Span, "a const argument must be an integer, a constant or none"),
        };

        /// <summary><c>x = f(args)</c>: a sub's result cell copied after the call, or an intrinsic whose only written
        /// operand is operand 1.</summary>
        private void EmitCallAssign(CallAssignStatement assign)
        {
            CallStatement call = assign.Call;
            if (_templates.ContainsKey(call.Name))
                throw new OmsCompileException(call.Span, $"template '{call.Name}' has no result");
            if (_subs.TryGetValue(call.Name, out SubStatement? sub))
            {
                if (sub.Result == null)
                    throw new OmsCompileException(call.Span, $"sub '{sub.Name}' has no result cell (-> cell)");
                LExpr target = LValue(assign.Target);
                EmitSubCall(sub, call);
                Lower(new LAssign(target, new LLeaf(Operand(sub.Result)), false), assign.Span);
                return;
            }
            if (intrinsics == null || !intrinsics.TryGet(call.Name, out Intrinsic intrinsic))
                throw new OmsCompileException(call.Span, $"unknown sub or intrinsic '{call.Name}'");
            if (!intrinsic.HasContract || intrinsic.OutOperands.Count != 1 || !intrinsic.OutOperands.Contains(1))
                throw new OmsCompileException(call.Span,
                    $"{call.Name} does not write exactly operand 1; pass its written operands with 'out'");
            var arguments = new List<CallArgument> { new(assign.Target, true) };
            arguments.AddRange(call.Arguments);
            EmitCall(call with { Arguments = arguments });
        }

        private void EmitCall(CallStatement call)
        {
            if (_templates.TryGetValue(call.Name, out TemplateStatement? template))
            {
                EmitTemplate(template, call.Arguments, call.Span);
                return;
            }
            if (_subs.TryGetValue(call.Name, out SubStatement? sub))
            {
                EmitSubCall(sub, call);
                return;
            }
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
                CheckCodeReference(intrinsic, k + 1, argument.Value, call.Name);
                operands[k] = argument.Out ? LValue(argument.Value) : ToL(argument.Value);
            }
            CheckObserved(intrinsic.Opcode, call.Span);
            Lower(new LInstruction(intrinsic.Opcode, operands), call.Span);
        }

        /// <summary>Code-target operands take a sub, coroutine or label of the kind the registry names
        /// (<c>code_target_kind</c>): coroutine handlers need a coroutine, callbacks and calls a sub.</summary>
        private void CheckCodeReference(Intrinsic intrinsic, int operand, Expr argument, string name)
        {
            if (intrinsics == null || !intrinsics.Contracts.IsCodeTarget(intrinsic.Opcode, operand)) return;
            if (argument is not OperandExpr { Operand: RawLabelRef label } || !_subs.TryGetValue(label.Name, out SubStatement? sub))
                return;
            string? kind = intrinsics.Contracts.CodeTargetKind(intrinsic.Opcode);
            if (kind == "coroutine" && !sub.Coroutine)
                throw new OmsCompileException(argument.Span, $"operand {operand} of {name} takes a coroutine; '{sub.Name}' is a sub");
            if (kind != "coroutine" && sub.Coroutine)
                throw new OmsCompileException(argument.Span, $"operand {operand} of {name} takes a {kind ?? "code"} target; '{sub.Name}' is a coroutine");
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
            if (_referenced.Add(key)) _referenceOrder.Add(key);
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
                case RawLabelRef label when _parameters != null && _parameters.TryGetValue(label.Name, out RawStorage? cell):
                    return Operand(cell);
                case RawLabelRef label when _constants.TryGetValue(label.Name, out uint value):
                    return IrOperand.Literal(0, value);
                case RawLabelRef label when Name(label.Name) is { } declared:
                    return Operand(declared.Cell);
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
                LocalCounts = layout?.LocalCounts ?? OmsCanonical.LocalCounts(_code, _declaredLocals),
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

/// <summary>A library sub as linked into a compiled unit: its instruction range and the labels it exports (its name
/// first, then its inner entry points).</summary>
public sealed record OmsLinkedSub(string Name, string Module, int Start, int End, IReadOnlyList<string> Labels, bool Coroutine);

/// <summary>A compiled unit with the instruction index of every named label and each linked library sub.</summary>
public sealed record OmsImage(IrScript Script, IReadOnlyDictionary<string, int> Labels, IReadOnlyList<OmsLinkedSub> Library);
