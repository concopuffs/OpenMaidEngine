using System.Text.RegularExpressions;
using Age.Engine.Model;

namespace Age.Script.Abi;

/// <summary>One intrinsic: an opcode callable by name with its operand count and `out` positions.</summary>
public sealed record Intrinsic(int Opcode, string Name, int Argc, IReadOnlySet<int> OutOperands, bool HasContract);

/// <summary>
/// The intrinsics of one engine ABI (spec §3.2, generated ABI module): every opcode the ABI's table frames is
/// callable as <c>op_0x…</c>, and also by its registry semantic name in snake case when that name is a unique,
/// well-formed identifier that is not a statement keyword. Operands whose native access writes or rebinds
/// (write, write-string, write-float, bind) must be passed with <c>out</c>.
/// </summary>
public sealed class IntrinsicCatalog
{
    private static readonly Regex Identifier = new("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> OutKinds = new(StringComparer.Ordinal)
    {
        "write", "write-string", "write-float", "bind",
    };
    public static readonly IReadOnlySet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "if", "else", "loop", "while", "do", "for", "switch", "case", "default", "atomic", "goto", "break",
        "continue", "return", "exit", "run", "note", "out", "script", "oms", "abi", "const", "global", "local",
        "saved", "ref", "sub", "coroutine", "inline", "library", "use", "module", "patch", "resume", "type",
        "data", "float", "int", "str", "true", "false", "call", "none", "static", "enum", "table", "with",
    };

    private readonly Dictionary<string, Intrinsic> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Intrinsic> _byOpcode = new();

    public string AbiId { get; }

    /// <summary>The contracts the catalog was built from (operand access for the decompiler's folding).</summary>
    public OpcodeContracts Contracts { get; }

    public IntrinsicCatalog(OpcodeTable table, OpcodeContracts contracts)
    {
        AbiId = table.AbiId;
        Contracts = contracts;
        var nameCounts = table.Entries
            .Select(entry => SnakeName(contracts.SemanticNames.GetValueOrDefault(entry.Opcode)))
            .Where(name => name != null)
            .GroupBy(name => name!)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (OpcodeDefinition entry in table.Entries.OrderBy(entry => entry.Opcode))
        {
            var outs = new HashSet<int>();
            bool hasContract = false;
            for (int operand = 1; operand <= entry.Argc; operand++)
            {
                IReadOnlyList<string>? kinds = contracts.Access(entry.Opcode, operand, table.AbiId);
                if (kinds == null) continue;
                hasContract = true;
                if (kinds.Any(OutKinds.Contains)) outs.Add(operand);
            }
            string? semantic = SnakeName(contracts.SemanticNames.GetValueOrDefault(entry.Opcode));
            string name = semantic != null && nameCounts[semantic] == 1 && !Keywords.Contains(semantic)
                          && !semantic.StartsWith("op_0x", StringComparison.Ordinal)
                ? semantic : RawName(entry.Opcode);
            var intrinsic = new Intrinsic(entry.Opcode, name, entry.Argc, outs, hasContract);
            _byOpcode[entry.Opcode] = intrinsic;
            _byName[name] = intrinsic;
            _byName[RawName(entry.Opcode)] = intrinsic;
        }
    }

    public static string RawName(int opcode) => $"op_0x{opcode:x}";

    public bool TryGet(string name, out Intrinsic intrinsic) => _byName.TryGetValue(name, out intrinsic!);
    public bool TryGet(int opcode, out Intrinsic intrinsic) => _byOpcode.TryGetValue(opcode, out intrinsic!);

    private static string? SnakeName(string? semantic)
    {
        if (string.IsNullOrWhiteSpace(semantic)) return null;
        string snake = semantic.Trim().Replace('-', '_');
        return Identifier.IsMatch(snake) ? snake : null;
    }
}
