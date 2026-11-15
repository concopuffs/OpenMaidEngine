using Lua;
using Ome.Engine.Modding;

namespace Ome.Scripting.Lua;

// The accessors' Lua side (spec §9.3): userdata over the engine's INamedAccess, which holds every rule (the names, the
// index bounds, the kinds, the write list). Nothing here decides what a mod may write.

/// <summary>
/// What <c>require("&lt;profile&gt;-base")</c> gives a mod that requires the base mod, and what <c>maid.names</c> holds
/// (spec §5.4, §8.4): <c>g</c> and <c>table</c> over the base mod's layout for that mod, beside a read-only view of what
/// the base mod's <c>init.lua</c> returned.
/// </summary>
internal sealed class NamesView(INamedAccess names, LuaValue exports, ReadOnlyViews views, LuaModSession session)
    : ILuaUserData
{
    private readonly Dictionary<LayoutGlobal, LuaValue> _tables = new(ReferenceEqualityComparer.Instance);
    private LuaValue _globals = LuaValue.Nil;
    private LuaFunction? _table;

    public INamedAccess Names { get; } = names;
    public LuaTable? Metatable { get => session.Metatables.Names; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    /// <summary>The names an export may not use: the accessors take them (spec §5.4).</summary>
    public static readonly string[] AccessorNames = ["g", "table"];

    public LuaValue Globals => _globals.Type == LuaValueType.Nil
        ? _globals = LuaValue.FromUserData(new GlobalsView(Names, session))
        : _globals;

    /// <summary><c>table(NAME)</c>, bound to this view: a dot call or a colon call.</summary>
    public LuaFunction Table => _table ??= session.Fn("table", context =>
    {
        int first = context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out ILuaUserData data)
                    && ReferenceEquals(data, this) ? 1 : 0;
        if (context.ArgumentCount <= first || !context.GetArgument(first).TryRead(out string name))
            throw new LuaRuntimeException(context.State, "bad argument #1 to 'table' (a global's name expected)", 1);
        LayoutGlobal global = Names.Global(name);
        if (global.IsScalar)
            throw new LuaRuntimeException(context.State,
                $"{global.Name} is no table: read it as .g.{global.Name}", 1);
        if (!_tables.TryGetValue(global, out LuaValue view))
            _tables[global] = view = LuaValue.FromUserData(new TableView(Names, global, session));
        return context.Return(view);
    });

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<NamesView>(context);
            LuaValue key = context.GetArgument(1);
            if (key.TryRead(out string name))
            {
                if (name == "g") return context.Return(self.Globals);
                if (name == "table") return context.Return(self.Table);
            }
            if (exportsOf(self) is not { } table) return context.Return(LuaValue.Nil);
            table.TryGetValue(key, out LuaValue value);
            return context.Return(self._views.Wrap(value));
        });
        meta["__newindex"] = session.Fn("__newindex", context =>
            throw Metatables.ReadOnly(context, $"what {Metatables.Self<NamesView>(context).Names.BaseModId} gives"));
        meta["__metatable"] = Metatables.Locked;
        return meta;

        static LuaTable? exportsOf(NamesView view)
            => view._exports.Type == LuaValueType.Table ? view._exports.Read<LuaTable>() : null;
    }

    private readonly LuaValue _exports = exports;
    private readonly ReadOnlyViews _views = views;

    /// <summary>A cell as Lua reads it.</summary>
    internal static LuaValue Value(CellValue cell) => cell.Kind == CellKind.String ? cell.Text ?? "" : cell.Number;

    /// <summary>A value a mod writes: a number or a string. The face decides whether the global takes it.</summary>
    internal static CellValue Cell(LuaFunctionExecutionContext context, LayoutGlobal global, LuaValue value)
        => value.Type switch
        {
            LuaValueType.Number => CellValue.Integer(value.Read<double>()),
            LuaValueType.String => CellValue.String(value.Read<string>()),
            _ => throw new LuaRuntimeException(context.State,
                $"{global.Name} takes {(global.Kind == CellKind.String ? "a string" : "a number")}, not {value.TypeToString()}", 1),
        };
}

/// <summary><c>g</c>: the base mod's scalar globals by name (spec §9.3).</summary>
internal sealed class GlobalsView(INamedAccess names, LuaModSession session) : ILuaUserData
{
    public INamedAccess Names { get; } = names;
    public LuaTable? Metatable { get => session.Metatables.Globals; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    private LayoutGlobal Scalar(LuaFunctionExecutionContext context)
    {
        if (!context.GetArgument(1).TryRead(out string name))
            throw new LuaRuntimeException(context.State, ".g is indexed by a global's name", 1);
        LayoutGlobal global = Names.Global(name);
        if (!global.IsScalar)
            throw new LuaRuntimeException(context.State, $"{global.Name} is a table: use .table(\"{global.Name}\")", 1);
        return global;
    }

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<GlobalsView>(context);
            return context.Return(NamesView.Value(self.Names.Read(self.Scalar(context))));
        });
        meta["__newindex"] = session.Fn("__newindex", context =>
        {
            var self = Metatables.Self<GlobalsView>(context);
            LayoutGlobal global = self.Scalar(context);
            self.Names.Write(global, -1, -1, NamesView.Cell(context, global, context.GetArgument(2)));
            return context.Return();
        });
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}

/// <summary><c>table(NAME)</c>: a named array (spec §9.3). A 1-D array's index gives its element, and its column
/// names name elements; a 2-D array's index gives a row (<see cref="RowView"/>). Indices are the game's, from 0.</summary>
internal sealed class TableView(INamedAccess names, LayoutGlobal global, LuaModSession session) : ILuaUserData
{
    public INamedAccess Names { get; } = names;
    public LayoutGlobal Global { get; } = global;
    public LuaTable? Metatable { get => session.Metatables.Table; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    private bool Rows => Global.Dimensions.Count == 2;

    /// <summary>The element a key names in a 1-D array: an index or a column's name.</summary>
    private int Element(LuaFunctionExecutionContext context, LuaValue key)
    {
        if (key.Type == LuaValueType.Number) return Names.Row(Global, key.Read<double>());
        if (key.TryRead(out string column)) return Names.Named(Global, column);
        throw new LuaRuntimeException(context.State, $"{Global.Name} is indexed by a number or a column's name", 1);
    }

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<TableView>(context);
            LuaValue key = context.GetArgument(1);
            if (!self.Rows) return context.Return(NamesView.Value(self.Names.Read(self.Global, self.Element(context, key))));
            if (key.Type != LuaValueType.Number)
                throw new LuaRuntimeException(context.State,
                    $"{self.Global.Name} is a table of rows: index a row first ({self.Global.Name}[row].column)", 1);
            int row = self.Names.Row(self.Global, key.Read<double>());
            return context.Return(LuaValue.FromUserData(new RowView(self.Names, self.Global, row, session)));
        });
        meta["__newindex"] = session.Fn("__newindex", context =>
        {
            var self = Metatables.Self<TableView>(context);
            if (self.Rows)
                throw new LuaRuntimeException(context.State,
                    $"{self.Global.Name} is a table of rows: write a cell of a row ({self.Global.Name}[row][column] = …)", 1);
            int element = self.Element(context, context.GetArgument(1));
            self.Names.Write(self.Global, element, -1, NamesView.Cell(context, self.Global, context.GetArgument(2)));
            return context.Return();
        });
        meta["__len"] = session.Fn("__len", context =>
        {
            var self = Metatables.Self<TableView>(context);
            return self.Global.Rows is int rows
                ? context.Return(rows)
                : throw new LuaRuntimeException(context.State, $"{self.Global.Name}'s row count is not known", 1);
        });
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}

/// <summary>A row of a 2-D array: indexed by a column's number or its name.</summary>
internal sealed class RowView(INamedAccess names, LayoutGlobal global, int row, LuaModSession session) : ILuaUserData
{
    public INamedAccess Names { get; } = names;
    public LayoutGlobal Global { get; } = global;
    public int Row { get; } = row;
    public LuaTable? Metatable { get => session.Metatables.Row; set { } }
    public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

    private int Column(LuaFunctionExecutionContext context, LuaValue key)
    {
        if (key.Type == LuaValueType.Number) return Names.Column(Global, key.Read<double>());
        if (key.TryRead(out string column)) return Names.Named(Global, column);
        throw new LuaRuntimeException(context.State, $"a row of {Global.Name} is indexed by a number or a column's name", 1);
    }

    public static LuaTable BuildMetatable(LuaModSession session)
    {
        var meta = new LuaTable();
        meta["__index"] = session.Fn("__index", context =>
        {
            var self = Metatables.Self<RowView>(context);
            int column = self.Column(context, context.GetArgument(1));
            return context.Return(NamesView.Value(self.Names.Read(self.Global, self.Row, column)));
        });
        meta["__newindex"] = session.Fn("__newindex", context =>
        {
            var self = Metatables.Self<RowView>(context);
            int column = self.Column(context, context.GetArgument(1));
            self.Names.Write(self.Global, self.Row, column, NamesView.Cell(context, self.Global, context.GetArgument(2)));
            return context.Return();
        });
        meta["__len"] = session.Fn("__len", context => context.Return(Metatables.Self<RowView>(context).Global.RowWidth));
        meta["__metatable"] = Metatables.Locked;
        return meta;
    }
}
