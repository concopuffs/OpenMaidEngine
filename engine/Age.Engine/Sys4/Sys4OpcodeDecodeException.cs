namespace Age.Engine.Sys4;

public enum Sys4OpcodeDecodeFailure
{
    OpcodeAbsentFromAbi,
    TruncatedOperands,
}

/// <summary>A structured failure at the boundary between a selected ABI table and script bytes.</summary>
public sealed class Sys4OpcodeDecodeException : IOException
{
    public Sys4OpcodeDecodeException(
        Sys4OpcodeDecodeFailure failure,
        string engineAbiId,
        string scriptRevision,
        string scriptName,
        uint packedScriptId,
        int offset,
        int opcode,
        int? expectedOperandCount = null)
        : base(FormatMessage(failure, engineAbiId, scriptRevision, scriptName, packedScriptId,
                             offset, opcode, expectedOperandCount))
    {
        Failure = failure;
        EngineAbiId = engineAbiId;
        ScriptRevision = scriptRevision;
        ScriptName = scriptName;
        PackedScriptId = packedScriptId;
        Offset = offset;
        Opcode = opcode;
        ExpectedOperandCount = expectedOperandCount;
    }

    public Sys4OpcodeDecodeFailure Failure { get; }
    public string EngineAbiId { get; }
    public string ScriptRevision { get; }
    public string ScriptName { get; }
    public uint PackedScriptId { get; }
    public int Offset { get; }
    public int Opcode { get; }
    public int? ExpectedOperandCount { get; }

    private static string FormatMessage(
        Sys4OpcodeDecodeFailure failure,
        string engineAbiId,
        string scriptRevision,
        string scriptName,
        uint packedScriptId,
        int offset,
        int opcode,
        int? expectedOperandCount)
    {
        string reason = failure == Sys4OpcodeDecodeFailure.OpcodeAbsentFromAbi
            ? "opcode is absent from selected ABI"
            : $"instruction is truncated; expected {expectedOperandCount ?? 0} operands";
        return $"SYS4 decode error: {reason}; abi={engineAbiId}; revision={scriptRevision}; "
               + $"script={scriptName}; script_id=0x{packedScriptId:x}; offset=0x{offset:x}; "
               + $"opcode=0x{opcode:x}";
    }
}
