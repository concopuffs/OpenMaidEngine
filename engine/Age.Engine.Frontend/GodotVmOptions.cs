using Age.Engine.Sys4;
using Age.Engine.Vm;

/// <summary>Separates bounded diagnostic runs from the persistent interactive AGE session.</summary>
public static class GodotVmOptions
{
    public const long DiagnosticMaxSteps = 20_000_000;

    /// <summary>The interactive session's options; <paramref name="startupSettings"/> (the selected profile's
    /// SYS4INI settings trailer) supplies the native settings the VM applies.</summary>
    public static VmOptions Create(bool selftest, bool ignoreExitRequests, Sys4StartupSettings? startupSettings = null)
    {
        Sys4StartupSettings settings = startupSettings ?? Sys4StartupSettings.Empty;
        return new(
            MaxSteps: selftest ? DiagnosticMaxSteps : long.MaxValue,
            IgnoreExitRequests: ignoreExitRequests,
            NoSaveDat: selftest,
            CancelMessageSkipOnClick: settings.CancelMessageSkipOnClick,
            FastForwardOpMicroseconds: VmOptions.NativeFastForwardOpMicroseconds,
            WheelKeyUpAction: settings.WheelKeyUpAction,
            WheelKeyDownAction: settings.WheelKeyDownAction,
            CoexistMessageSkip: settings.CoexistMessageSkip);
    }
}
