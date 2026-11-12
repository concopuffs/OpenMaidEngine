using Ome.Engine.Model;

namespace Ome.Engine.Diagnostics;

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
    string OperandContractLayerId,
    IReadOnlyList<Operand> Operands)
{
    /// <summary>The diagnostic for <paramref name="instruction"/> of <paramref name="script"/>, run over
    /// <paramref name="table"/>; <paramref name="profileId"/> labels it (<c>VmOptions.ProfileId</c>).</summary>
    public static UnsupportedOpcodeDiagnostic For(
        string profileId, OpcodeTable table, Script script, Instruction instruction, OpcodeDefinition definition)
        => new(
            profileId,
            table.AbiId,
            script.EngineRevision,
            script.Name,
            script.PackedId,
            instruction.Offset,
            instruction.Opcode,
            definition.CanonicalLabel,
            definition.ContractProvenance.LayerId,
            instruction.Args.ToArray());

    /// <summary>The halt reason for an opcode that <paramref name="table"/>'s ABI does not define.</summary>
    public static string NotInAbi(OpcodeTable table, int opcode) => $"opcode-not-in-abi:{table.AbiId}:0x{opcode:x}";

    public override string ToString()
        => $"unsupported opcode: profile={ProfileId}; abi={EngineAbiId}; revision={ScriptRevision}; "
           + $"script={ScriptName}; script_id=0x{PackedScriptId:x}; offset=0x{Offset:x}; "
           + $"opcode=0x{Opcode:x}; label={CanonicalLabel}; "
           + $"contract_layer={OperandContractLayerId}; operands=["
           + string.Join(", ", Operands.Select(value => $"{value.Type}:{value.Value}")) + "]";
}
