using Ome.Engine.Model;

namespace Ome.Engine.Modding;

public enum HookKind
{
    /// <summary>A point (spec §7.1): the instruction, with its frame and operands.</summary>
    Point,
    /// <summary>An engine event (spec §7.7): named fields, no frame.</summary>
    EngineEvent,
    /// <summary>A game event (spec §9.2): its declared fields, at its point or raised from Lua.</summary>
    GameEvent,
    /// <summary>A script's entry or exit (spec §7.2): the frame, and the cause it was entered for.</summary>
    Script,
    /// <summary>A write to a watched cell (spec §7.3): the cell, its old and new value, and what wrote it; no frame.</summary>
    Watch,
}

/// <summary>What wrote a watched cell (spec §7.3): an instruction, a mod's Lua (the innermost running handler's mod,
/// spec §12), the console, or the engine outside any instruction.</summary>
public enum CellWriter { Script, Mod, Console, Engine }

/// <summary>Whether a pass over a hooked instruction is skipped (spec §7.1): one per pass, shared by the contexts of its
/// before list, set by the first <c>ctx:skip()</c> and never cleared.</summary>
public sealed class InstructionSkip
{
    public bool Skipped { get; private set; }

    internal void Set() => Skipped = true;
}

/// <summary>An operand as <c>ctx.args[i]</c> gives it (spec §7.1): what it holds, and whether a write may change it.
/// An immediate, a float literal and a string literal are read-only; every cell operand is writable.</summary>
public enum OperandKind { Integer, Float, String }

/// <summary>
/// What a handler is given (spec §7.1, §7.7). The scripting native builds one per call, and it is live only for that
/// call: a context a handler keeps and touches later is refused. The operand rules are the engine's.
/// </summary>
public sealed class HookContext
{
    private readonly IInterpreterAccess? _interpreter;
    private readonly IReadOnlyList<Operand> _operands;
    private readonly int _codePage;

    private HookContext(HookKind kind, string? name, IReadOnlyList<KeyValuePair<string, CellValue>> fields,
                        IInterpreterAccess? interpreter, IReadOnlyList<Operand> operands, int codePage)
    {
        Kind = kind;
        Name = name;
        Fields = fields;
        _interpreter = interpreter;
        _operands = operands;
        _codePage = codePage;
    }

    /// <summary>An instruction's context: a point's, or with <paramref name="when"/> a site hook's (<c>before</c> or
    /// <c>after</c>). Before it, <paramref name="skip"/> is the pass's skip state when a handler there may skip, and
    /// <paramref name="maySkip"/> says whether this one declared it.</summary>
    public static HookContext ForPoint(HookSite site, Instruction instruction, IInterpreterAccess interpreter, int codePage,
                                       string? when = null, InstructionSkip? skip = null, bool maySkip = false)
        => new(HookKind.Point, site.Planned.Point, [], interpreter, instruction.Args, codePage)
        {
            Script = site.Script,
            Offset = site.Planned.Offset,
            Site = site.Planned.Site,
            Opcode = instruction.Opcode,
            When = when,
            SkipState = skip,
            MaySkip = maySkip,
        };

    /// <summary>A script hook's context (spec §7.2): <paramref name="when"/> is <c>enter</c> or <c>exit</c>,
    /// <paramref name="cause"/> why the frame was entered (<c>call</c>, <c>root</c>, <c>title</c>, <c>restore</c>).</summary>
    public static HookContext ForScript(string script, string when, string cause)
        => new(HookKind.Script, script, [], null, [], 0) { Script = script, When = when, Cause = cause };

    /// <summary>A watch's context (spec §7.3): the cell <paramref name="cell"/> names at <paramref name="address"/>,
    /// the global it belongs to with its row and column (-1 for none), the values before and after the write, and the
    /// writer: for an instruction its script, linked offset and original offset (-1 when a patch inserted it), for Lua
    /// the mod.</summary>
    public static HookContext ForWatch(string watch, string cell, int address, string? global, int index, int column,
                                       CellValue old, CellValue @new, CellWriter writer, string? writerMod = null,
                                       string? script = null, int offset = -1, int site = -1)
        => new(HookKind.Watch, watch, [], null, [], 0)
        {
            Cell = cell,
            Address = address,
            Global = global,
            Index = index,
            Column = column,
            Old = old,
            New = @new,
            Writer = writer,
            WriterMod = writerMod,
            Script = script,
            Offset = offset,
            Site = site,
        };

    /// <summary>An engine event's context, with its fields.</summary>
    public static HookContext ForEngineEvent(string name, params KeyValuePair<string, CellValue>[] fields)
        => new(HookKind.EngineEvent, name, fields, null, [], 0);

    /// <summary>A game event's context: its declaration and its fields, and for an event at a point the instruction
    /// it is raised at, which an error names.</summary>
    public static HookContext ForGameEvent(GameEvent gameEvent, IEventFields fields, HookSite? site = null,
                                           Instruction? instruction = null)
        => new(HookKind.GameEvent, gameEvent.Name, [], null, [], 0)
        {
            Event = gameEvent,
            EventFields = fields,
            Script = site?.Script,
            Offset = site?.Planned.Offset ?? -1,
            Site = site?.Planned.Site ?? -1,
            Opcode = instruction?.Opcode ?? -1,
        };

    public HookKind Kind { get; }

    /// <summary>The point's name, or the engine event's or the game event's, or the watch as its manifest names it.</summary>
    public string? Name { get; }

    public string? Script { get; private init; }
    /// <summary>A site hook's <c>before</c> or <c>after</c>, a script hook's <c>enter</c> or <c>exit</c>; null for a
    /// point hook and an event.</summary>
    public string? When { get; private init; }
    /// <summary>A script hook's cause: why its frame was entered.</summary>
    public string? Cause { get; private init; }
    public int Offset { get; private init; } = -1;
    public int Site { get; private init; } = -1;
    public int Opcode { get; private init; } = -1;

    /// <summary>A watch's cell, as the trace writes it (<c>G[0x5003]</c>), and its address.</summary>
    public string? Cell { get; private init; }
    public int Address { get; private init; } = -1;
    /// <summary>A watch's global, with the cell's row in a table and its column in a 2-D one (-1 for none).</summary>
    public string? Global { get; private init; }
    public int Index { get; private init; } = -1;
    public int Column { get; private init; } = -1;
    /// <summary>A watched cell's value before the write and after it.</summary>
    public CellValue Old { get; private init; }
    public CellValue New { get; private init; }
    /// <summary>What wrote a watched cell; for a mod, <see cref="WriterMod"/>, and for an instruction
    /// <see cref="Script"/>, <see cref="Offset"/> and <see cref="Site"/>.</summary>
    public CellWriter Writer { get; private init; }
    public string? WriterMod { get; private init; }

    /// <summary>An engine event's fields (<c>reason</c>).</summary>
    public IReadOnlyList<KeyValuePair<string, CellValue>> Fields { get; }

    /// <summary>Whether this handler declared <c>skip = true</c> (spec §7.1).</summary>
    public bool MaySkip { get; private init; }
    private InstructionSkip? SkipState { get; init; }

    /// <summary><c>ctx.skipped</c>: whether this handler or one before it skipped the instruction.</summary>
    public bool Skipped
    {
        get
        {
            if (!Live) throw new ScriptAccessException("this context's call has returned; a context is live only during it");
            return SkipState?.Skipped == true;
        }
    }

    /// <summary>Whether this call skipped the instruction, which the trace names.</summary>
    public bool SkippedHere { get; private set; }

    /// <summary><c>ctx:skip()</c>: the VM passes over the instruction once its before list has run (spec §7.1). Only a
    /// handler before it that declared <c>skip = true</c> may; a second skip changes nothing.</summary>
    public void Skip()
    {
        if (!Live) throw new ScriptAccessException("this context's call has returned; a context is live only during it");
        if (Kind != HookKind.Point) throw new ScriptAccessException($"{Name} has no instruction to skip");
        if (When == "after")
            throw new ScriptAccessException("ctx:skip(): an after hook runs once its instruction has run; only a hook before "
                                            + "it may skip it");
        if (!MaySkip || SkipState == null)
            throw new ScriptAccessException("ctx:skip(): this hook did not declare skip = true");
        if (SkipState.Skipped) return;
        SkipState.Set();
        SkippedHere = true;
    }

    /// <summary>A game event's declaration.</summary>
    public GameEvent? Event { get; private init; }
    private IEventFields? EventFields { get; init; }

    /// <summary>A game event's field, read live: a cell is read when the handler asks (spec §9.2).</summary>
    public CellValue ReadField(GameEventField field)
    {
        LiveEvent();
        return EventFields!.Read(field);
    }

    /// <summary>Write a game event's field: an <c>rw</c> field, with the value its type takes. The next handler sees the
    /// change.</summary>
    public void WriteField(GameEventField field, CellValue value)
    {
        LiveEvent();
        if (!field.Writable)
            throw new ScriptAccessException($"field {field.Name} of {Name} is read-only (access = \"r\")");
        EventFields!.Write(field, EventFieldRules.Check(field, value));
    }

    private void LiveEvent()
    {
        if (!Live) throw new ScriptAccessException("this context's call has returned; a context is live only during it");
        if (Kind != HookKind.GameEvent) throw new ScriptAccessException($"{Name} has no declared fields");
    }

    /// <summary>False once the call it was built for has returned.</summary>
    public bool Live { get; internal set; } = true;

    /// <summary>Told of each write through <c>ctx.args</c> under <c>--lua-trace</c> (spec §12): the operand's index, the
    /// operand, and its value before and after. Null otherwise, so a write reads nothing more.</summary>
    internal Action<int, Operand, CellValue, CellValue>? OperandWritten { get; set; }

    public int OperandCount => _operands.Count;

    public OperandKind OperandKindOf(int index) => KindOf(OperandAt(index).Type);

    /// <summary>Whether a write to the operand changes a cell.</summary>
    public bool OperandWritable(int index) => IsWritable(OperandAt(index).Type);

    /// <summary>Whether an operand of this type is a cell a write changes: not an immediate or a literal.</summary>
    internal static bool IsWritable(int type) => type is not (OperandTypes.Immediate or OperandTypes.Float
                                                             or OperandTypes.StringLiteral);

    /// <summary>The kind of value an operand of this type holds.</summary>
    internal static CellKind CellKindOf(int type) => KindOf(type) switch
    {
        OperandKind.Float => CellKind.Float,
        OperandKind.String => CellKind.String,
        _ => CellKind.Integer,
    };

    public CellValue ReadOperand(int index)
    {
        Operand operand = OperandAt(index);
        return _interpreter!.ReadOperand(operand);
    }

    public void WriteOperand(int index, CellValue value)
    {
        Operand operand = OperandAt(index);
        if (!OperandWritable(index))
            throw new ScriptAccessException($"ctx.args[{index + 1}] is {Describe(operand.Type)}, which a write cannot change");
        OperandKind kind = KindOf(operand.Type);
        CellValue checkedValue = kind switch
        {
            OperandKind.Integer when value.Kind != CellKind.String => CellValue.Integer(CellRules.Integer(value.Number)),
            OperandKind.Float when value.Kind != CellKind.String => CellValue.Float(value.Number),
            OperandKind.String when value.Kind == CellKind.String => CellValue.String(CellRules.Text(value.Text ?? "", _codePage)),
            _ => throw new ScriptAccessException(
                $"ctx.args[{index + 1}] is {Describe(operand.Type)}; it takes {(kind == OperandKind.String ? "a string" : "a number")}"),
        };
        CellValue? before = OperandWritten != null ? _interpreter!.ReadOperand(operand) : null;
        _interpreter!.WriteOperand(operand, checkedValue);
        if (before is { } previous) OperandWritten!(index, operand, previous, _interpreter.ReadOperand(operand));
    }

    private Operand OperandAt(int index)
    {
        if (!Live) throw new ScriptAccessException("this context's call has returned; a context is live only during it");
        if (Kind != HookKind.Point) throw new ScriptAccessException($"{Name} has no operands");
        if ((uint)index >= (uint)_operands.Count)
            throw new ScriptAccessException($"the instruction has {_operands.Count} operands; there is no ctx.args[{index + 1}]");
        return _operands[index];
    }

    private static OperandKind KindOf(int type) => type switch
    {
        OperandTypes.Float or OperandTypes.GlobalFloat or OperandTypes.LocalFloat => OperandKind.Float,
        OperandTypes.StringLiteral or OperandTypes.GlobalString or OperandTypes.GlobalStringPointer or OperandTypes.LocalString
            or OperandTypes.LocalStringPointer => OperandKind.String,
        _ => OperandKind.Integer,
    };

    internal static string Describe(int type) => type switch
    {
        OperandTypes.Immediate => "an immediate",
        OperandTypes.Float => "a float literal",
        OperandTypes.StringLiteral => "a string literal",
        _ => "a cell",
    };
}

/// <summary>The operand types of the SYS4 encoding (docs/sys4-format-notes.md), as the hook context reads them.</summary>
public static class OperandTypes
{
    public const int Immediate = 0, Float = 1, StringLiteral = 2, GlobalInteger = 3, GlobalFloat = 4, GlobalString = 5,
                     GlobalPointer = 6, GlobalStringPointer = 8, LocalInteger = 9, LocalFloat = 10, LocalString = 11,
                     LocalPointer = 12, LocalStringPointer = 14;
}
