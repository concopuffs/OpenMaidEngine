namespace Age.Engine.Vm;
public sealed record VmOptions(int EmitCap = 2, long MaxSteps = 2_000_000);
