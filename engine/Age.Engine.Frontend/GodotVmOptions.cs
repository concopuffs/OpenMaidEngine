using Age.Engine.Vm;

/// <summary>Separates bounded diagnostic runs from the persistent interactive AGE session.</summary>
public static class GodotVmOptions
{
    public const long DiagnosticMaxSteps = 20_000_000;

    public static VmOptions Create(bool selftest, bool ignoreExitRequests)
        => new(
            MaxSteps: selftest ? DiagnosticMaxSteps : long.MaxValue,
            IgnoreExitRequests: ignoreExitRequests,
            NoSaveDat: selftest);
}
