using System.Reflection;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Scripting.Lua;

/// <summary>
/// The waits a Lua script call refuses (docs/superpowers/specs/2026-11-10-lua-scripting-design.md §10, "What counts as
/// a wait"): every member of <see cref="IInputAndWaits"/> is on <see cref="ModCallWaits.Waits"/> or
/// <see cref="ModCallWaits.NotWaits"/>, a member added to the interface fails here until it is sorted, and inside a call
/// each wait refuses while every other member reaches the inner waits.
/// </summary>
public sealed class ModCallWaitsTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);

    /// <summary>A member's name as the lists give it: a property by its own name.</summary>
    private static string NameOf(MethodInfo method)
        => method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal) ? method.Name[4..] : method.Name;

    [Fact]
    public void EveryMemberIsAWaitOrNotAndTheGuardImplementsEachItself()
    {
        string[] members = typeof(IInputAndWaits).GetMethods().Select(NameOf).Distinct().Order().ToArray();

        Assert.Empty(ModCallWaits.Waits.Intersect(ModCallWaits.NotWaits));
        Assert.Equal(members, ModCallWaits.Waits.Concat(ModCallWaits.NotWaits).Order().ToArray());
        // A member left to its default body would run the interface's code on the guard, not reach the inner waits.
        InterfaceMapping map = typeof(ModCallWaits).GetInterfaceMap(typeof(IInputAndWaits));
        Assert.All(map.TargetMethods, method => Assert.Equal(typeof(ModCallWaits), method.DeclaringType));
    }

    [Fact]
    public void InsideACallEachWaitRefusesAndEveryOtherMemberReachesTheInnerWaits()
    {
        // ROOT's first instruction is hooked; its handler calls SUB, and as SUB is entered the inner waits probe the
        // guard the interpreter calls.
        int mov = Table.ByLabel("mov")!.Value;
        Script root = ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
        {
            (mov, [new Operand(3, 0x300), new Operand(0, 1)]),
            (0x2, []),
        }, []);
        Script sub = ScriptAssembler.Assemble(Table, "SUB.BIN", new List<(int, Operand[])> { (0x5, []) }, []);
        var (stack, plan) = new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function caller(ctx) maid.vm.call("SUB") end
                """)
            .Calls("mod.a", "SUB")
            .Point(root, 0, "mod.a", "hooks.lua:caller")
            .Build();
        using LazyModRuntime runtime = LuaModRuntime.Lazy();
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup { Runtime = runtime, Stack = stack, Plan = plan });
        var inner = DispatchProxy.Create<IInputAndWaits, Probe>();
        var probe = (Probe)(object)inner;
        var vm = new VirtualMachine(root, Table, inner, provider: new MapProvider(new(),
            new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase) { ["SUB.BIN"] = sub }), scripting: native);
        probe.Guard = (IInputAndWaits)typeof(VirtualMachine)
            .GetField("_waits", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
        Assert.IsType<ModCallWaits>(probe.Guard);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.True(probe.Probed);
        Assert.Equal(ModCallWaits.Waits.Order(), probe.Refused.Distinct().Order());
        Assert.Equal(ModCallWaits.NotWaits.Order(), probe.Reached.Distinct().Order());
    }

    /// <summary>The inner waits: they record each member the guard passes on, and probe the guard as SUB is entered.</summary>
    public class Probe : DispatchProxy
    {
        public IInputAndWaits? Guard;
        public bool Probed;
        public readonly List<string> Refused = new();
        public readonly List<string> Reached = new();
        private bool _probing;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (_probing) Reached.Add(NameOf(method!));
            if (method!.Name == nameof(IInputAndWaits.EnterScriptContext) && args?[0] as string == "SUB.BIN" && !Probed)
                ProbeGuard();
            Type type = method.ReturnType;
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }

        private void ProbeGuard()
        {
            Probed = true;
            _probing = true;
            try
            {
                foreach (MethodInfo member in typeof(IInputAndWaits).GetMethods())
                {
                    object?[] defaults = member.GetParameters()
                        .Select(parameter => parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
                        .ToArray();
                    try
                    {
                        member.Invoke(Guard, defaults);
                    }
                    catch (TargetInvocationException error) when (error.InnerException?.GetType().Name == "ModCallRefusedException")
                    {
                        Refused.Add(NameOf(member));
                    }
                }
            }
            finally
            {
                _probing = false;
            }
        }
    }
}
