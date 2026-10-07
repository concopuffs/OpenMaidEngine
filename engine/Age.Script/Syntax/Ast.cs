namespace Age.Script.Syntax;

/// <summary>A parsed script unit (spec §3.1).</summary>
public sealed record ScriptUnit(SourceSpan Span, int Version, string Name, string? Abi, IReadOnlyList<Statement> Body);

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
