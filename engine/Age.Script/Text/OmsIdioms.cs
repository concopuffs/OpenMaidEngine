using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;
using Age.Script.Layout;
using Age.Script.Modules;
using Age.Script.Syntax;

namespace Age.Script.Text;

/// <summary>Compiled code of one library sub or template variant: <see cref="Length"/> instructions of
/// <see cref="Image"/> from <see cref="Start"/>. Temporaries use the probe slots from
/// <see cref="OmsCompiler.TempProbe"/>; <see cref="Names"/> names the library labels the image links, by
/// instruction index, so references out of the fragment match by name.</summary>
public sealed record OmsFragment(IrScript Image, int Start, int Length, IReadOnlyDictionary<int, string> Names)
{
    public int FirstOpcode => Image.Code[Start].Opcode;
}

/// <summary>A library sub the decompiler recognizes (spec §11 step 4). <see cref="Entries"/> maps each exported
/// label's offset in the sub to its name (offset 0 is the sub itself).</summary>
public sealed record OmsLibraryPattern(string Name, GameModule Module, OmsFragment Code, IReadOnlyDictionary<int, string> Entries);

/// <summary>One <c>static if</c> variant of a template (spec §11 step 5): the optional constant parameters marked
/// <see cref="Absent"/> were passed as <c>none</c>, every other parameter as a placeholder.</summary>
public sealed record OmsTemplatePattern(TemplateStatement Template, GameModule Module, IReadOnlyList<bool> Absent, OmsFragment Code);

/// <summary>
/// The idiomatic level's patterns, compiled once from the source modules: every library sub, and every template in
/// each <c>static if</c> variant over its optional constant parameters. Placeholder arguments are integers from
/// <see cref="ConstProbe"/> and <see cref="ExprProbe"/>, cells from <see cref="CellProbe"/> and strings starting with
/// <see cref="TextProbe"/>; a variant in which a passed parameter leaves no trace cannot be lifted and is dropped.
/// </summary>
public sealed class OmsIdioms
{
    public const uint ConstProbe = 0x40100000, ExprProbe = 0x40200000, CellProbe = 0x40300000, ProbeSpan = 0x100;
    public const string TextProbe = "\u0001oms";

    private readonly Dictionary<string, GameModule> _modules;

    public IReadOnlyList<OmsLibraryPattern> Library { get; }
    /// <summary>Template variants, longest first, then in declaration order.</summary>
    public IReadOnlyList<OmsTemplatePattern> Templates { get; }
    /// <summary>Enum member names by enum and value (the first member wins a shared value).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<uint, string>> Enums { get; }

    private OmsIdioms(Dictionary<string, GameModule> modules, List<OmsLibraryPattern> library,
                      List<OmsTemplatePattern> templates, Dictionary<string, IReadOnlyDictionary<uint, string>> enums)
    {
        _modules = modules;
        Library = library;
        Templates = templates;
        Enums = enums;
    }

    public bool IsEmpty => Library.Count == 0 && Templates.Count == 0;

    public GameModule? Module(string name) => _modules.GetValueOrDefault(name);

    public static OmsIdioms Build(IReadOnlyList<GameModule> modules, string abiId, IntrinsicCatalog intrinsics)
    {
        var byName = modules.ToDictionary(module => module.Name, StringComparer.Ordinal);
        var library = new List<OmsLibraryPattern>();
        var templates = new List<OmsTemplatePattern>();
        var enums = new Dictionary<string, IReadOnlyDictionary<uint, string>>(StringComparer.Ordinal);
        var probe = new OmsLayout { TempBase = OmsCompiler.TempProbe };
        foreach (GameModule module in modules)
        {
            if (module.Source is not { } source) continue;
            SourceSpan span = source.Span;
            ScriptUnit Unit(params Statement[] body) => new(span, 1, module.Name, null, [new UseStatement(span, module.Name), .. body]);

            OmsImage image = OmsCompiler.CompileImage(Unit(new ExitStatement(span)), abiId, null, probe, intrinsics,
                byName.GetValueOrDefault, linkAll: true);
            foreach (OmsLinkedSub sub in image.Library.Where(sub => sub.Module == module.Name))
            {
                var entries = sub.Labels.ToDictionary(label => image.Labels[label] - sub.Start, label => label);
                library.Add(new OmsLibraryPattern(sub.Name, module,
                    new OmsFragment(image.Script, sub.Start, sub.End - sub.Start, LibraryNames(image)), entries));
            }

            foreach (EnumStatement enumeration in source.Body.OfType<EnumStatement>())
            {
                var members = new Dictionary<uint, string>();
                foreach (EnumMember member in enumeration.Members) members.TryAdd(member.Value, member.Name);
                enums[enumeration.Name] = members;
            }

            foreach (TemplateStatement template in source.Body.OfType<TemplateStatement>())
            {
                int[] optional = Enumerable.Range(0, template.Parameters.Count)
                    .Where(k => IsConstKind(template.Parameters[k].Kind) && template.Parameters[k].Default is NoneExpr).ToArray();
                for (int mask = 0; mask < 1 << optional.Length; mask++)
                {
                    var absent = new bool[template.Parameters.Count];
                    for (int bit = 0; bit < optional.Length; bit++) absent[optional[bit]] = (mask >> bit & 1) != 0;
                    var arguments = new List<CallArgument>();
                    for (int k = 0; k < template.Parameters.Count; k++)
                    {
                        TemplateParameter parameter = template.Parameters[k];
                        Expr value = absent[k] ? new NoneExpr(span) : new OperandExpr(span, parameter.Kind switch
                        {
                            "text" => new RawString(span, TextProbe + k),
                            "cell" => new RawStorage(span, "G", CellProbe + (uint)k),
                            "expr" => new RawInteger(span, ExprProbe + (uint)k),
                            _ => new RawInteger(span, ConstProbe + (uint)k),
                        });
                        arguments.Add(new CallArgument(value, false, parameter.Name));
                    }
                    OmsImage expansion;
                    try
                    {
                        expansion = OmsCompiler.CompileImage(Unit(new CallStatement(span, template.Name, arguments),
                            new ExitStatement(span)), abiId, null, probe, intrinsics, byName.GetValueOrDefault, linkAll: false);
                    }
                    catch (OmsCompileException)
                    {
                        continue;   // a variant the template does not allow
                    }
                    int length = (expansion.Library.Count > 0 ? expansion.Library.Min(sub => sub.Start) : expansion.Script.Code.Count) - 1;
                    if (length <= 0 || !Traced(expansion.Script, length, template, absent)) continue;
                    templates.Add(new OmsTemplatePattern(template, module, absent,
                        new OmsFragment(expansion.Script, 0, length, LibraryNames(expansion))));
                }
            }
        }
        // Longest first, then declaration order (a stable sort keeps the latter).
        List<OmsTemplatePattern> ordered = templates.OrderByDescending(pattern => pattern.Code.Length).ToList();
        return new OmsIdioms(byName, library, ordered, enums);
    }

    public static bool IsConstKind(string kind) => kind is not ("text" or "expr" or "cell");

    private static Dictionary<int, string> LibraryNames(OmsImage image)
    {
        var names = new Dictionary<int, string>();
        foreach (OmsLinkedSub sub in image.Library)
            foreach (string label in sub.Labels)
                names.TryAdd(image.Labels[label], label);
        return names;
    }

    /// <summary>Whether every parameter passed in this variant shows up in the expansion, so lifting can recover it. A
    /// parameter with a default other than <c>none</c> may leave no trace: a lifted use then leaves it out and gets the
    /// default. (A <c>none</c> default marks an optional parameter, whose absent variant is enumerated already.)</summary>
    private static bool Traced(IrScript image, int length, TemplateStatement template, bool[] absent)
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < length; i++)
            foreach (IrOperand operand in image.Code[i].Operands)
                if (ProbeOf(operand, image) is int k) seen.Add(k);
        return Enumerable.Range(0, template.Parameters.Count)
            .All(k => absent[k] || seen.Contains(k) || template.Parameters[k].Default is not (null or NoneExpr));
    }

    /// <summary>The parameter a placeholder operand stands for, if it is one.</summary>
    public static int? ProbeOf(IrOperand operand, IrScript image)
    {
        switch (operand.Kind)
        {
            case IrOperandKind.Literal when operand.Type == 0 && operand.Value - ConstProbe < ProbeSpan:
                return (int)(operand.Value - ConstProbe);
            case IrOperandKind.Literal when operand.Type == 0 && operand.Value - ExprProbe < ProbeSpan:
                return (int)(operand.Value - ExprProbe);
            case IrOperandKind.Literal when operand.Type == 0x3 && operand.Value - CellProbe < ProbeSpan:
                return (int)(operand.Value - CellProbe);
            case IrOperandKind.DataRef when image.Data[(int)operand.Value].Text is { } text && text.StartsWith(TextProbe, StringComparison.Ordinal):
                return int.Parse(text.AsSpan(TextProbe.Length));
            default:
                return null;
        }
    }
}
