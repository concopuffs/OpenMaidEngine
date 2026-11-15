using Ome.Engine.Model;
using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

// Game events (spec §8.2, §9.2): an event at a point gives its subscribers live views of the frame's cells; an event
// raised from Lua gives them the raiser's values. Both run their subscribers through Call, so the error policy, the
// budget and the nesting cap are the handlers'.
public sealed partial class Scripting
{
    // A subscriber of an event raised from Lua failed under --lua-errors halt: the outermost call returns it, so the
    // instruction it all ran at halts. Null between calls.
    private string? _nestedHalt;

    /// <summary>The context of a subscriber of a game event at a point.</summary>
    private HookContext GameEventContext(string name, HookSite site, Instruction instruction, IInterpreterAccess interpreter)
    {
        GameEvent gameEvent = _setup.Stack.Event(name)
                              ?? throw new InvalidOperationException($"the plan names {name}, which the stack does not declare");
        return HookContext.ForGameEvent(gameEvent, new PointFields(this, interpreter, instruction), site, instruction);
    }

    /// <summary><c>maid.events.emit</c> (spec §8.2): the owner raises an event declared with <c>raised_by = "lua"</c>;
    /// its subscribers run before it returns, and the values come back as they left them.</summary>
    private IReadOnlyDictionary<string, CellValue> Emit(string modId, string name, IReadOnlyDictionary<string, CellValue> given)
    {
        GameEvent gameEvent = _setup.Stack.Event(name)
                              ?? throw new ScriptAccessException($"no mod of the stack declares the event '{name}'");
        if (!gameEvent.RaisedByLua)
            throw new ScriptAccessException($"{name} is raised at its point ({gameEvent.Point}), not from Lua");
        if (gameEvent.Owner != modId)
            throw new ScriptAccessException($"{name} is raised by {gameEvent.Owner}, which declares it; {modId} cannot raise it");
        IModSession session = _scene.Session
                              ?? throw new ScriptAccessException("an event is raised from a handler, not while the session is built");
        var values = new EventValues(gameEvent, given);
        IReadOnlyList<PlannedHandler> subscribers = _setup.Plan.SubscribersOf(name);
        for (int i = 0; i < subscribers.Count; i++)
        {
            if (!IsOn(session, subscribers[i].Handler)) continue;
            if (Call(session, subscribers[i].Handler, HookContext.ForGameEvent(gameEvent, values), frameAccess: false) is { } halt)
            {
                _nestedHalt ??= halt;
                break;
            }
        }
        return values.Values;
    }

    /// <summary>A cell of script memory as a value of its kind.</summary>
    private CellValue ReadCell(CellKind kind, int address) => kind switch
    {
        CellKind.Integer => CellValue.Integer(CellRules.ReadInteger(_memory.Integer(address))),
        CellKind.Float => CellValue.Float(CellRules.ReadFloat(_memory.Float(address))),
        _ => CellValue.String(_memory.String(address)),
    };

    /// <summary>Write a cell of script memory: the value must be of its kind and pass <see cref="CellRules"/>.</summary>
    private void WriteCell(CellKind kind, int address, CellValue value, string what)
    {
        bool text = kind == CellKind.String;
        if (text != (value.Kind == CellKind.String))
            throw new ScriptAccessException($"{what} holds {(text ? "strings" : "numbers")}, not "
                                            + (value.Kind == CellKind.String ? "a string" : "a number"));
        switch (kind)
        {
            case CellKind.Integer: _memory.SetInteger(address, CellRules.Integer(value.Number)); break;
            case CellKind.Float: _memory.SetFloat(address, CellRules.FloatBits(value.Number)); break;
            default: _memory.SetString(address, CellRules.Text(value.Text ?? "", _memory.Encoding.CodePage)); break;
        }
    }

    /// <summary>The fields of an event at a point (spec §9.2): each a live view of its location, read and written
    /// when the handler asks, so the next handler sees a change.</summary>
    private sealed class PointFields(Scripting owner, IInterpreterAccess interpreter, Instruction instruction) : IEventFields
    {
        public CellValue Read(GameEventField field)
        {
            CellValue value = ReadAt(field.Location!, field);
            if ((value.Kind == CellKind.String) != (field.Type == EventFieldType.String)
                || field.Type == EventFieldType.Float != (value.Kind == CellKind.Float))
                throw new ScriptAccessException($"field {field.Name}: {field.At} holds {Kind(value.Kind)}, and the field's type "
                                                + $"is {field.Type.Name()}");
            return value;
        }

        public void Write(GameEventField field, CellValue value)
        {
            switch (field.Location)
            {
                case FieldLocation.Local local:
                    switch (local.Cell)
                    {
                        case CellKind.Integer: interpreter.SetLocalInteger(local.Index, CellRules.Integer(value.Number)); break;
                        case CellKind.Float: interpreter.SetLocalFloat(local.Index, CellRules.FloatBits(value.Number)); break;
                        default:
                            interpreter.SetLocalString(local.Index, CellRules.Text(value.Text ?? "", owner._memory.Encoding.CodePage));
                            break;
                    }
                    break;
                case FieldLocation.Global global:
                    owner.WriteCell(global.Cell, global.Address, value, $"field {field.Name}");
                    break;
                case FieldLocation.Element element:
                    owner.WriteCell(element.Table.Kind, Address(element, field), value, $"field {field.Name}");
                    break;
                case FieldLocation.Operand operand:
                    Operand cell = Arg(operand.Index, field);
                    if (!HookContext.IsWritable(cell.Type))
                        throw new ScriptAccessException($"field {field.Name}: {field.At} is {HookContext.Describe(cell.Type)}, "
                                                        + "which a write cannot change");
                    if (HookContext.CellKindOf(cell.Type) != field.Type.Cell())
                        throw new ScriptAccessException($"field {field.Name}: {field.At} is not a {field.Type.Name()} cell");
                    CellValue checkedValue = field.Type switch
                    {
                        EventFieldType.Float => CellValue.Float(value.Number),
                        EventFieldType.String => CellValue.String(CellRules.Text(value.Text ?? "", owner._memory.Encoding.CodePage)),
                        _ => CellValue.Integer(CellRules.Integer(value.Number)),
                    };
                    interpreter.WriteOperand(cell, checkedValue);
                    break;
                default:
                    throw new ScriptAccessException($"field {field.Name}: {field.At} is not a cell");
            }
        }

        private CellValue ReadAt(FieldLocation location, GameEventField field) => location switch
        {
            FieldLocation.Number number => CellValue.Integer(number.Value),
            FieldLocation.Local { Cell: CellKind.Integer } local
                => CellValue.Integer(CellRules.ReadInteger(interpreter.LocalInteger(local.Index))),
            FieldLocation.Local { Cell: CellKind.Float } local
                => CellValue.Float(CellRules.ReadFloat(interpreter.LocalFloat(local.Index))),
            FieldLocation.Local local => CellValue.String(interpreter.LocalString(local.Index)),
            FieldLocation.Global global => owner.ReadCell(global.Cell, global.Address),
            FieldLocation.Operand operand => interpreter.ReadOperand(Arg(operand.Index, field)),
            FieldLocation.Element element => owner.ReadCell(element.Table.Kind, Address(element, field)),
            _ => throw new ScriptAccessException($"field {field.Name}: {field.At} is no location"),
        };

        /// <summary>An element's address: its indices read now, then checked against the table's shape.</summary>
        private int Address(FieldLocation.Element element, GameEventField field)
        {
            int row = LayoutCells.Row(element.Table, Index(element.Row, field));
            int column = element.Column == null ? -1 : LayoutCells.Column(element.Table, Index(element.Column, field));
            return LayoutCells.Address(element.Table, row, column);
        }

        private double Index(FieldLocation location, GameEventField field)
        {
            CellValue value = ReadAt(location, field);
            return value.Kind == CellKind.Integer
                ? value.Number
                : throw new ScriptAccessException($"field {field.Name}: an index of {field.At} holds {Kind(value.Kind)}");
        }

        private Operand Arg(int index, GameEventField field)
            => (uint)index < (uint)instruction.Args.Count
                ? instruction.Args[index]
                : throw new ScriptAccessException($"field {field.Name}: the instruction at the point has {instruction.Args.Count} "
                                                  + $"operands; there is no arg[{index + 1}]");

        private static string Kind(CellKind kind) => kind switch
        {
            CellKind.Float => "a float",
            CellKind.String => "a string",
            _ => "an integer",
        };
    }
}
