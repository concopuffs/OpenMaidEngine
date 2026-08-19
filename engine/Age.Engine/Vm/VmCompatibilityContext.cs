using Age.Engine.Model;

namespace Age.Engine.Vm;

/// <summary>The selected game/profile facts needed to make compatibility behavior explicit.</summary>
public sealed record VmCompatibilityContext(string ProfileId, string EngineAbiId, bool ProbeMode = false)
{
    public static VmCompatibilityContext ForTable(OpcodeTable table)
        => new("unselected", table.AbiId);
}

/// <summary>A recognized ABI instruction for which the runtime has no implementation.</summary>
public sealed record UnsupportedOpcodeDiagnostic(
    string ProfileId,
    string EngineAbiId,
    string ScriptRevision,
    string ScriptName,
    uint PackedScriptId,
    int Offset,
    int Opcode,
    string CanonicalLabel,
    IReadOnlyList<Operand> Operands)
{
    public override string ToString()
        => $"unsupported opcode: profile={ProfileId}; abi={EngineAbiId}; revision={ScriptRevision}; "
           + $"script={ScriptName}; script_id=0x{PackedScriptId:x}; offset=0x{Offset:x}; "
           + $"opcode=0x{Opcode:x}; label={CanonicalLabel}; operands=["
           + string.Join(", ", Operands.Select(value => $"{value.Type}:{value.Value}")) + "]";
}
