using Lua;
using Ome.Engine.Modding;

namespace Ome.Scripting.Lua;

// The userdata Lua sees (spec §5.2, §7.1, §8.1): every engine value is copied, or wrapped in one of these, so no .NET
// object reaches Lua. Each is a userdata, not a table, so rawset cannot reach past its metatable.

/// <summary>One session's metatables; a view's functions find their target through the userdata they are given.</summary>
internal sealed class Metatables
{
    public Metatables(LuaModSession session)
    {
        View = ReadOnlyViews.BuildMetatable(session);
        Vm = VmView.BuildMetatable(session);
        Bank = BankView.BuildMetatable(session);
        Hook = HookView.BuildMetatable(session);
        Args = ArgsView.BuildMetatable(session);
        Arg = ArgView.BuildMetatable(session);
        Session = SessionView.BuildMetatable(session);
        Names = NamesView.BuildMetatable(session);
        Globals = GlobalsView.BuildMetatable(session);
        Table = TableView.BuildMetatable(session);
        Row = RowView.BuildMetatable(session);
    }

    public LuaTable View { get; }
    public LuaTable Vm { get; }
    public LuaTable Bank { get; }
    public LuaTable Hook { get; }
    public LuaTable Args { get; }
    public LuaTable Arg { get; }
    public LuaTable Session { get; }
    public LuaTable Names { get; }
    public LuaTable Globals { get; }
    public LuaTable Table { get; }
    public LuaTable Row { get; }

    public const string Locked = "locked";

    internal static LuaRuntimeException ReadOnly(LuaFunctionExecutionContext context, string what)
        => new(context.State, $"{what} is read-only", 1);

    internal static T Self<T>(LuaFunctionExecutionContext context) where T : class, ILuaUserData
        => context.GetArgument(0).TryRead(out ILuaUserData data) && data is T self
            ? self
            : throw new LuaRuntimeException(context.State, $"expected a {typeof(T).Name}", 1);
}

/// <summary>
/// The read-only views of what another mod exports (spec §5.2): a requiring mod reads and calls through them, and
/// cannot write into the export or a table inside it. What a function returns is not wrapped.
/// </summary>
internal sealed class ReadOnlyViews(Metatables metatables)
{
    private readonly Dictionary<LuaTable, View> _views = new(ReferenceEqualityComparer.Instance);

    internal sealed class View(LuaTable target, ReadOnlyViews owner) : ILuaUserData
    {
        public LuaTable Target { get; } = target;
        public ReadOnlyViews Owner { get; } = owner;
        public LuaTable? Metatable { get => Owner._metatables.View; set { } }
        public Span<LuaValue> UserValues => Span<LuaValue>.Empty;
    }

    private readonly Metatables _metatables = metatables;

    public LuaValue Wrap(LuaValue value)
    {
        if (value.Type != LuaValueType.Table) return value;
        LuaTable table = value.Read<LuaTable>();
        if (!_views.TryGetValue(table, out View? view)) _views[table] = view = new View(table, this);
        return LuaValue.FromUserData(view);
    }

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        LuaFunction next = session.Fn("next", context =>
        {
            var view = Metatables.Self<View>(context);
            return view.Target.TryGetNext(context.GetArgument(1), out var pair)
                ? context.Return(pair.Key, view.Owner.Wrap(pair.Value))
                : context.Return(LuaValue.Nil);
        });
        meta["__index"] = session.Fn("__index", context =>
        {
            var view = Metatables.Self<View>(context);
            view.Target.TryGetValue(context.GetArgument(1), out LuaValue value);
            return context.Return(view.Owner.Wrap(value));
        });
        meta["__newindex"] = session.Fn("__newindex", context => throw Metatables.ReadOnly(context, "a table another mod exports"));
        meta["__len"] = session.Fn("__len", context => context.Return(Metatables.Self<View>(context).Target.ArrayLength));
        meta["__pairs"] = session.Fn("__pairs", context => context.Return(next, context.GetArgument(0), LuaValue.Nil));
        LuaFunction step = session.Fn("ipairs_step", context =>
        {
            var view = Metatables.Self<View>(context);
            double index = context.GetArgument<double>(1) + 1;
            view.Target.TryGetValue(index, out LuaValue value);
            return value.Type == LuaValueType.Nil ? context.Return(LuaValue.Nil) : context.Return(index, view.Owner.Wrap(value));
        });
        meta["__ipairs"] = session.Fn("__ipairs", context => context.Return(step, context.GetArgument(0), 0));
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}

/// <summary><c>maid.vm</c> (spec §8.1).</summary>
internal sealed class VmView(IVmAccess vm, LuaModSession session) : ILuaUserData
{
    public IVmAccess Vm { get; } = vm;
    private readonly Dictionary<string, LuaValue> _banks = new(StringComparer.Ordinal);
    public LuaTable? Metatable { get => session.Metatables.Vm; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    private static readonly string[] BankNames = ["g", "gf", "gs", "gp", "gsp", "l", "lf", "ls"];

    public LuaValue Bank(string name)
    {
        if (!_banks.TryGetValue(name, out LuaValue bank))
            _banks[name] = bank = LuaValue.FromUserData(new BankView(Vm, name, session));
        return bank;
    }

    // vm.frames(), vm.script_id(NAME) and vm.call(NAME), built once for this view and bound to it: spec §8.1 writes them
    // with a dot, so the view is not their first argument. A colon call passes the view first, which they skip.
    private LuaFunction? _frames, _scriptId, _call;

    /// <summary>The index of the first real argument: 1 when the call was <c>vm:name(…)</c>, else 0.</summary>
    private int First(LuaFunctionExecutionContext context)
        => context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out ILuaUserData data) && ReferenceEquals(data, this) ? 1 : 0;

    public LuaFunction Frames => _frames ??= session.Fn("frames", context =>
    {
        var list = new LuaTable();
        IReadOnlyList<ScriptFrameInfo> all = Vm.Frames();
        for (int i = 0; i < all.Count; i++) list[i + 1] = FrameTable(all[i]);
        return context.Return(list);
    });

    public LuaFunction ScriptId => _scriptId ??= session.Fn("script_id", context =>
    {
        int first = First(context);
        if (context.ArgumentCount <= first || !context.GetArgument(first).TryRead(out string name))
            throw new LuaRuntimeException(context.State, "bad argument #1 to 'script_id' (a script name expected)", 1);
        return Vm.ScriptId(name) is double id ? context.Return(id) : context.Return(LuaValue.Nil);
    });

    public LuaFunction Call => _call ??= session.Fn("call", context =>
        throw new LuaRuntimeException(context.State, "maid.vm.call is not available in this version", 1));

    public static LuaTable FrameTable(ScriptFrameInfo frame)
    {
        var table = new LuaTable { ["script"] = frame.Script, ["offset"] = frame.Offset, ["depth"] = frame.Depth };
        if (frame.Site is int site) table["site"] = site;
        if (frame.ChangedBy.Count > 0)
        {
            var mods = new LuaTable();
            for (int i = 0; i < frame.ChangedBy.Count; i++) mods[i + 1] = frame.ChangedBy[i];
            table["changed_by"] = mods;
        }
        return table;
    }

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<VmView>(context);
            if (!context.GetArgument(1).TryRead(out string key)) return context.Return(LuaValue.Nil);
            if (Array.IndexOf(BankNames, key) >= 0) return context.Return(self.Bank(key));
            return key switch
            {
                "frame" => self.Vm.Frame is { } frame ? context.Return(FrameTable(frame)) : context.Return(LuaValue.Nil),
                "frames" => context.Return(self.Frames),
                "script_id" => context.Return(self.ScriptId),
                "call" => context.Return(self.Call),
                _ => context.Return(LuaValue.Nil),
            };
        });
        meta["__newindex"] = session.Fn("__newindex", context => throw Metatables.ReadOnly(context, "maid.vm"));
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}

/// <summary>A bank of cells: <c>vm.g</c>, <c>vm.gf</c>, <c>vm.gs</c>, the pointer banks <c>vm.gp</c> and
/// <c>vm.gsp</c>, and the frame's locals <c>vm.l</c>, <c>vm.lf</c>, <c>vm.ls</c> (spec §8.1).</summary>
internal sealed class BankView(IVmAccess vm, string name, LuaModSession session) : ILuaUserData
{
    public IVmAccess Vm { get; } = vm;
    public string Name { get; } = name;
    public LuaTable? Metatable { get => session.Metatables.Bank; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    public LuaValue Read(double index) => Name switch
    {
        "g" => Vm.ReadInteger(index),
        "gf" => Vm.ReadFloat(index),
        "gs" => Vm.ReadString(index),
        "gp" => Vm.ReadPointer(index),
        "gsp" => Vm.ReadStringPointer(index),
        "l" => Vm.ReadLocalInteger(index),
        "lf" => Vm.ReadLocalFloat(index),
        _ => Vm.ReadLocalString(index),
    };

    public void Write(LuaFunctionExecutionContext context, double index, LuaValue value)
    {
        bool text = Name is "gs" or "ls";
        if (Name is "gp" or "gsp")
            throw new LuaRuntimeException(context.State, $"vm.{Name}: pointer cells are not writable in this version", 1);
        if (text ? value.Type != LuaValueType.String : value.Type != LuaValueType.Number)
            throw new LuaRuntimeException(context.State,
                $"vm.{Name}[{index}] takes {(text ? "a string" : "a number")}, not {value.TypeToString()}", 1);
        switch (Name)
        {
            case "g": Vm.WriteInteger(index, value.Read<double>()); break;
            case "gf": Vm.WriteFloat(index, value.Read<double>()); break;
            case "gs": Vm.WriteString(index, value.Read<string>()); break;
            case "l": Vm.WriteLocalInteger(index, value.Read<double>()); break;
            case "lf": Vm.WriteLocalFloat(index, value.Read<double>()); break;
            default: Vm.WriteLocalString(index, value.Read<string>()); break;
        }
    }

    /// <summary>The most cells one <c>range</c> call reads.</summary>
    public const int MaxRange = 1 << 16;

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        LuaFunction range = session.Fn("range", context =>
        {
            var self = Metatables.Self<BankView>(context);
            double start = context.GetArgument<double>(1), count = context.GetArgument<double>(2);
            if (count != Math.Floor(count) || count < 0 || count > MaxRange)
                throw new LuaRuntimeException(context.State, $"vm.{self.Name}:range: the count must be a whole number from 0 to {MaxRange}", 1);
            var cells = new LuaTable((int)count, 0);
            for (int i = 0; i < (int)count; i++) cells[i + 1] = self.Read(start + i);
            return context.Return(cells);
        });
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<BankView>(context);
            LuaValue key = context.GetArgument(1);
            if (key.Type == LuaValueType.Number) return context.Return(self.Read(key.Read<double>()));
            return key.TryRead(out string name) && name == "range" ? context.Return(range) : context.Return(LuaValue.Nil);
        });
        meta["__newindex"] = session.Fn("__newindex", context =>
        {
            var self = Metatables.Self<BankView>(context);
            LuaValue key = context.GetArgument(1);
            if (key.Type != LuaValueType.Number)
                throw new LuaRuntimeException(context.State, $"vm.{self.Name} is indexed by address", 1);
            self.Write(context, key.Read<double>(), context.GetArgument(2));
            return context.Return();
        });
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}

/// <summary>A handler's context (spec §7.1, §7.7): the instruction and its operands for a point, the fields for an
/// engine event.</summary>
internal sealed class HookView(HookContext context, LuaModSession session) : ILuaUserData
{
    public HookContext Context { get; } = context;
    private LuaValue _args = LuaValue.Nil;
    public LuaTable? Metatable { get => session.Metatables.Hook; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    public LuaValue Args => _args.Type == LuaValueType.Nil
        ? _args = LuaValue.FromUserData(new ArgsView(Context, session))
        : _args;

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        LuaFunction skip = session.Fn("skip", context =>
            throw new LuaRuntimeException(context.State, "ctx:skip(): this hook did not declare skip = true", 1));
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<HookView>(context);
            HookContext hook = self.Context;
            if (!context.GetArgument(1).TryRead(out string key)) return context.Return(LuaValue.Nil);
            if (hook.Kind == HookKind.EngineEvent)
            {
                if (key == "event") return context.Return(hook.Name ?? "");
                foreach (var (name, value) in hook.Fields)
                    if (name == key) return context.Return(value.Kind == CellKind.String ? value.Text ?? "" : value.Number);
                return context.Return(LuaValue.Nil);
            }
            return key switch
            {
                "script" => context.Return(hook.Script ?? ""),
                "site" => context.Return(hook.Site),
                "offset" => context.Return(hook.Offset),
                "point" => hook.Name is { } point ? context.Return(point) : context.Return(LuaValue.Nil),
                "opcode" => context.Return(hook.Opcode),
                "args" => context.Return(self.Args),
                "skip" => context.Return(skip),
                _ => context.Return(LuaValue.Nil),
            };
        });
        meta["__newindex"] = session.Fn("__newindex", context =>
            throw new LuaRuntimeException(context.State, "the context is read-only; write an operand through ctx.args[i].value", 1));
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}

/// <summary><c>ctx.args</c>: the instruction's operands, 1-based.</summary>
internal sealed class ArgsView(HookContext context, LuaModSession session) : ILuaUserData
{
    public HookContext Context { get; } = context;
    public LuaTable? Metatable { get => session.Metatables.Args; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<ArgsView>(context);
            LuaValue key = context.GetArgument(1);
            if (key.Type != LuaValueType.Number) return context.Return(LuaValue.Nil);
            double index = key.Read<double>();
            if (index != Math.Floor(index) || index < 1 || index > self.Context.OperandCount) return context.Return(LuaValue.Nil);
            return context.Return(LuaValue.FromUserData(new ArgView(self.Context, (int)index - 1, session)));
        });
        meta["__len"] = session.Fn("__len", context => context.Return(Metatables.Self<ArgsView>(context).Context.OperandCount));
        meta["__newindex"] = session.Fn("__newindex", context => throw Metatables.ReadOnly(context, "ctx.args"));
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}

/// <summary><c>ctx.args[i]</c>: <c>value</c> reads the operand and, for a cell, writes it; <c>kind</c> and
/// <c>writable</c> describe it.</summary>
internal sealed class ArgView(HookContext context, int index, LuaModSession session) : ILuaUserData
{
    public HookContext Context { get; } = context;
    public int Index { get; } = index;
    public LuaTable? Metatable { get => session.Metatables.Arg; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<ArgView>(context);
            if (!context.GetArgument(1).TryRead(out string key)) return context.Return(LuaValue.Nil);
            switch (key)
            {
                case "value":
                    CellValue value = self.Context.ReadOperand(self.Index);
                    return value.Kind == CellKind.String ? context.Return(value.Text ?? "") : context.Return(value.Number);
                case "kind":
                    return context.Return(self.Context.OperandKindOf(self.Index).ToString().ToLowerInvariant());
                case "writable":
                    return context.Return(self.Context.OperandWritable(self.Index));
                default:
                    return context.Return(LuaValue.Nil);
            }
        });
        meta["__newindex"] = session.Fn("__newindex", context =>
        {
            var self = Metatables.Self<ArgView>(context);
            if (!context.GetArgument(1).TryRead(out string key) || key != "value")
                throw new LuaRuntimeException(context.State, "an operand's only writable field is value", 1);
            LuaValue value = context.GetArgument(2);
            CellValue cell = value.Type switch
            {
                LuaValueType.Number => CellValue.Integer(value.Read<double>()),
                LuaValueType.String => CellValue.String(value.Read<string>()),
                _ => throw new LuaRuntimeException(context.State, $"an operand takes a number or a string, not {value.TypeToString()}", 1),
            };
            self.Context.WriteOperand(self.Index, cell);
            return context.Return();
        });
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}

/// <summary><c>maid.session</c> (spec §8.4): <c>reason</c> and <c>restoring</c>.</summary>
internal sealed class SessionView(LuaModSession session) : ILuaUserData
{
    public LuaModSession Session { get; } = session;
    public LuaTable? Metatable { get => Session.Metatables.Session; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<SessionView>(context);
            if (!context.GetArgument(1).TryRead(out string key)) return context.Return(LuaValue.Nil);
            return key switch
            {
                "reason" => context.Return(self.Session.Reason.Name()),
                "restoring" => context.Return(self.Session.Access.Restoring),
                _ => context.Return(LuaValue.Nil),
            };
        });
        meta["__newindex"] = session.Fn("__newindex", context => throw Metatables.ReadOnly(context, "maid.session"));
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}
