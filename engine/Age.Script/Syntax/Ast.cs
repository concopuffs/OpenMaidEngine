namespace Age.Script.Syntax;

/// <summary>A parsed unit (spec §3.1): a script (<c>script "X.BIN"</c>) or, when <see cref="IsModule"/>, a module
/// (<c>module himegari.adv</c>) holding declarations, templates and library subroutines.</summary>
public sealed record ScriptUnit(SourceSpan Span, int Version, string Name, string? Abi, IReadOnlyList<Statement> Body,
                                bool IsModule = false);

public abstract record Statement(SourceSpan Span);

/// <summary><c>name:</c> — names the next instruction.</summary>
public sealed record LabelStatement(SourceSpan Span, string Name) : Statement(Span);

/// <summary><c>@op(opcode, operands…)</c> — the raw instruction escape (spec §6.2).</summary>
public sealed record RawOpStatement(SourceSpan Span, int Opcode, IReadOnlyList<RawOperand> Operands) : Statement(Span);

/// <summary>An explicit operand form of <c>@op</c>. Each form determines the SYS4 type tag and value.</summary>
public abstract record RawOperand(SourceSpan Span);

/// <summary>An integer immediate (type 0). Negative literals are their two's-complement value.</summary>
public sealed record RawInteger(SourceSpan Span, uint Value) : RawOperand(Span);

/// <summary>A float immediate (type 1), written <c>float 1.5</c>.</summary>
public sealed record RawFloat(SourceSpan Span, float Value) : RawOperand(Span);

/// <summary>A string literal: an inline data item referenced with type 2.</summary>
public sealed record RawString(SourceSpan Span, string Text) : RawOperand(Span);

/// <summary>An integer array literal: an inline op-0x64 data item referenced with type 0.</summary>
public sealed record RawArray(SourceSpan Span, IReadOnlyList<int> Values) : RawOperand(Span);

/// <summary><c>data(dword, …)</c>: a referenced data item with no recognized shape, stored exactly.</summary>
public sealed record RawDataBlock(SourceSpan Span, IReadOnlyList<uint> Dwords) : RawOperand(Span);

/// <summary>A storage cell such as <c>G[0x152616]</c> or <c>L[3]</c>; the bank selects the type tag.</summary>
public sealed record RawStorage(SourceSpan Span, string Bank, uint Index) : RawOperand(Span);

/// <summary>A code reference to a label (type 0).</summary>
public sealed record RawLabelRef(SourceSpan Span, string Name) : RawOperand(Span);

/// <summary><c>type(tag, operand)</c>: any other type tag around an integer, label or data operand.</summary>
public sealed record RawTyped(SourceSpan Span, int Type, RawOperand Inner) : RawOperand(Span);

// ---------------------------------------------------------------------------------------------- expressions

public abstract record Expr(SourceSpan Span);

/// <summary>An operand atom: literal, storage cell, string/array literal, label or typed form.</summary>
public sealed record OperandExpr(SourceSpan Span, RawOperand Operand) : Expr(Span);

/// <summary><c>left op right</c> with a C-style binary operator.</summary>
public sealed record BinaryExpr(SourceSpan Span, string Op, Expr Left, Expr Right) : Expr(Span);

/// <summary><c>op operand</c> with <c>-</c>, <c>!</c> or <c>~</c>.</summary>
public sealed record UnaryExpr(SourceSpan Span, string Op, Expr Operand) : Expr(Span);

/// <summary>Element access: <c>base[index]</c>, or <c>base:stride[row][col]</c> written <c>G[0x341ab:0x35][r][c]</c>
/// (spec §6.1). It lowers to a lookup into a reference temporary.</summary>
public sealed record ElementExpr(SourceSpan Span, RawStorage Base, RawOperand? Stride, IReadOnlyList<Expr> Indices)
    : Expr(Span);

// ---------------------------------------------------------------------------------------------- statements

/// <summary><c>target = value</c>, or <c>target op= value</c> when <see cref="CompoundOp"/> is set; the target is a
/// storage cell or an element.</summary>
public sealed record AssignStatement(SourceSpan Span, Expr Target, Expr Value, string? CompoundOp = null) : Statement(Span);

/// <summary>An intrinsic argument; <see cref="Out"/> marks an operand the intrinsic writes (spec §6.3).</summary>
public sealed record CallArgument(Expr Value, bool Out, string? Name = null);

/// <summary><c>name(args)</c>: an intrinsic, or a local subroutine/label called with no arguments.</summary>
public sealed record CallStatement(SourceSpan Span, string Name, IReadOnlyList<CallArgument> Arguments) : Statement(Span);

public enum JumpKind { Label, Break, Continue }

/// <summary>A jump destination: a named label or the innermost loop's exit/continue point.</summary>
public sealed record JumpTarget(JumpKind Kind, string? Label = null)
{
    public override string ToString() => Kind switch
    {
        JumpKind.Break => "break",
        JumpKind.Continue => "continue",
        _ => $"goto {Label}",
    };
}

/// <summary><c>goto L</c>, <c>break</c> or <c>continue</c>.</summary>
public sealed record JumpStatement(SourceSpan Span, JumpTarget Target) : Statement(Span);

/// <summary><c>if [!]c goto/break/continue [else goto …]</c>: one jcc (spec §7.1).</summary>
public sealed record ConditionalJumpStatement(
    SourceSpan Span, Expr Condition, bool Negated, JumpTarget Target, JumpTarget? Else) : Statement(Span);

public sealed record IfStatement(SourceSpan Span, Expr Condition, IReadOnlyList<Statement> Then,
                                 IReadOnlyList<Statement>? Else) : Statement(Span);

/// <summary><c>loop { … }</c>: an unconditional loop closed by a backward jmp.</summary>
public sealed record LoopStatement(SourceSpan Span, IReadOnlyList<Statement> Body) : Statement(Span);

public sealed record WhileStatement(SourceSpan Span, Expr Condition, IReadOnlyList<Statement> Body) : Statement(Span);

public sealed record DoWhileStatement(SourceSpan Span, IReadOnlyList<Statement> Body, Expr Condition) : Statement(Span);

/// <summary><c>for init; condition; step { … }</c> (spec §7.1); each clause may be empty.</summary>
public sealed record ForStatement(SourceSpan Span, Statement? Init, Expr? Condition, Statement? Step,
                                  IReadOnlyList<Statement> Body) : Statement(Span);

public sealed record AtomicStatement(SourceSpan Span, IReadOnlyList<Statement> Body) : Statement(Span);

public sealed record ReturnStatement(SourceSpan Span) : Statement(Span);

public sealed record ExitStatement(SourceSpan Span) : Statement(Span);

/// <summary><c>run script</c>: call-script with a catalog id or an integer variable.</summary>
public sealed record RunStatement(SourceSpan Span, Expr Script) : Statement(Span);

/// <summary><c>note "text"</c>: the compiled comment, op 0x1a7 (any operand form; the engine ignores it).</summary>
public sealed record NoteStatement(SourceSpan Span, Expr Value) : Statement(Span);

/// <summary><c>call *v</c>: an indirect local call through an integer value (spec §6.3).</summary>
public sealed record IndirectCallStatement(SourceSpan Span, Expr Target) : Statement(Span);

/// <summary><c>target = name(args)</c>: a sub with a result cell, or an intrinsic whose only written operand is
/// operand 1 (spec §6.3).</summary>
public sealed record CallAssignStatement(SourceSpan Span, Expr Target, CallStatement Call) : Statement(Span);

/// <summary>A bound parameter: <paramref name="Name"/> stands for <paramref name="Cell"/> inside the sub, and a
/// call assigns the argument to the cell before calling.</summary>
public sealed record SubParameter(SourceSpan Span, string Name, RawStorage Cell);

/// <summary><c>sub name(params) -> result { … }</c> or <c>coroutine name { … }</c>: a placed local subroutine
/// (spec §6.3). Control may not fall into it; a sub that can fall off its end returns implicitly, and a coroutine
/// must end with <c>resume</c>. <c>@guard sub</c> is placed behind an <c>exit</c> guard instead, as the original
/// compiler placed library functions.</summary>
public sealed record SubStatement(SourceSpan Span, string Name, IReadOnlyList<SubParameter> Parameters,
                                  RawStorage? Result, IReadOnlyList<Statement> Body, bool Coroutine,
                                  bool Guarded = false) : Statement(Span);

/// <summary>A template parameter (spec §6.3, §11.1): <c>name: kind [= default]</c>. Kinds: <c>const</c> (an integer
/// constant, which <c>static if</c> can test), <c>text</c> (a string literal), <c>expr</c> (any expression) and
/// <c>cell</c> (an assignable cell or element).</summary>
public sealed record TemplateParameter(SourceSpan Span, string Name, string Kind, Expr? Default);

/// <summary><c>inline name(params) { … }</c>: a template, expanded at each use; <c>@dialogue</c> marks the one that
/// dialogue statements expand through (spec §11.2).</summary>
public sealed record TemplateStatement(SourceSpan Span, string Name, IReadOnlyList<TemplateParameter> Parameters,
                                       IReadOnlyList<Statement> Body, bool Dialogue) : Statement(Span);

/// <summary><c>static if c { … } else { … }</c>: chosen at template expansion from constant parameters.</summary>
public sealed record StaticIfStatement(SourceSpan Span, Expr Condition, IReadOnlyList<Statement> Then,
                                       IReadOnlyList<Statement>? Else) : Statement(Span);

/// <summary>A dialogue line (spec §11.2): <c>"text"</c> or <c>speaker "text" (voice: 0x34e)</c>.</summary>
public sealed record DialogueStatement(SourceSpan Span, string? Speaker, string Text, IReadOnlyList<CallArgument> Arguments)
    : Statement(Span);

/// <summary>One member of an <c>enum</c>: a constant.</summary>
public sealed record EnumMember(SourceSpan Span, string Name, uint Value);

/// <summary><c>enum speaker { エミリオ = 0x1, … }</c>: named constants that a template parameter can take as its kind,
/// so decompiled arguments print by member name (spec §11 step 6).</summary>
public sealed record EnumStatement(SourceSpan Span, string Name, IReadOnlyList<EnumMember> Members) : Statement(Span);

/// <summary><c>none</c>: the absent value of an optional constant parameter.</summary>
public sealed record NoneExpr(SourceSpan Span) : Expr(Span);

/// <summary><c>resume</c>: the coroutine resume point, op 0x7c.</summary>
public sealed record ResumeStatement(SourceSpan Span) : Statement(Span);

/// <summary><c>use himegari.globals</c>: import a module's names (spec §3.2).</summary>
public sealed record UseStatement(SourceSpan Span, string Module) : Statement(Span);

/// <summary><c>const NAME = value</c>: an integer substituted wherever the name appears (spec §5.2).</summary>
public sealed record ConstStatement(SourceSpan Span, string Name, uint Value) : Statement(Span);

/// <summary><c>global int name[r][c] @ 0x6f70 { column: index }</c> or <c>local int name @ 0x1</c> (spec §5.2): a name
/// for a storage cell or array. A local without <c>@</c> gets a slot above every local the source names.</summary>
public sealed record StorageDeclaration(SourceSpan Span, bool Local, string Type, string Name, IReadOnlyList<int> Dimensions,
                                        uint? Address, IReadOnlyDictionary<string, int> Columns) : Statement(Span);

/// <summary>A named array element: <c>name[i]</c>, <c>name[r][c]</c> or the row-table column <c>name[r].column</c>; or
/// a table field (spec §5.3), where <see cref="Column"/> is the field and the field's own row index or column follows:
/// <c>t[i].field</c>, <c>t[i].field[k]</c>, <c>t[i].field.column</c>.</summary>
public sealed record NamedElementExpr(SourceSpan Span, string Name, IReadOnlyList<Expr> Indices, string? Column,
                                      IReadOnlyList<Expr>? FieldIndices = null, string? FieldColumn = null)
    : Expr(Span);

/// <summary>One field of a <c>table</c>: its name and the array that holds it.</summary>
public sealed record TableField(SourceSpan Span, string Name, string Array);

/// <summary><c>table unit[1000] { full_name = unit_full_names, … }</c> (spec §5.3): a compile-time view naming the
/// parallel arrays that hold one kind of record. It allocates and emits nothing.</summary>
public sealed record TableStatement(SourceSpan Span, string Name, int Records, IReadOnlyList<TableField> Fields)
    : Statement(Span);

/// <summary><c>with unit[i] { .field = v }</c> (spec §5.3): the parser has already written each <c>.field</c> in the
/// body as <c>unit[i].field</c>, so the block emits only its statements.</summary>
public sealed record WithStatement(SourceSpan Span, NamedElementExpr Record, IReadOnlyList<Statement> Body) : Statement(Span);
