namespace Age.Engine.Model;
public sealed record Instruction(int Offset, int Opcode, IReadOnlyList<Operand> Args);
