using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>Separates bounded diagnostic runs from the persistent interactive AGE session.</summary>
public static class GodotVmOptions
{
    public const long DiagnosticMaxSteps = 20_000_000;

    /// <summary>Native set:NoSaveDat for the session's save native: a self-test writes no shared profile at shutdown.
    /// </summary>
    public static bool NoSaveDat(bool selftest) => selftest;

    /// <summary>The interactive session's options; <paramref name="startupSettings"/> (the selected profile's
    /// SYS4INI settings trailer) supplies the native settings the VM applies.</summary>
    public static VmOptions Create(bool selftest, bool ignoreExitRequests, Sys4StartupSettings? startupSettings = null)
    {
        Sys4StartupSettings settings = startupSettings ?? Sys4StartupSettings.Empty;
        return new(
            // The emit cap is a headless runaway-line guard; native has none, and a script may show one authored line
            // any number of times in a frame (a menu loop's prompt), so the interactive session never halts on it.
            EmitCap: selftest ? new VmOptions().EmitCap : int.MaxValue,
            MaxSteps: selftest ? DiagnosticMaxSteps : long.MaxValue,
            IgnoreExitRequests: ignoreExitRequests,
            CreateObject: settings.CreateObject,
            AutoFreeTex: settings.AutoFreeTex,
            WheelKeyUpAction: settings.WheelKeyUpAction,
            WheelKeyDownAction: settings.WheelKeyDownAction);
    }

    /// <summary>The interactive session's skip and auto settings: the profile's CancelMesSkipOnClick and CoexistMesSkip,
    /// and native's fast-forward pace.</summary>
    public static SkipAndAutoSettings CreateSkipAndAuto(Sys4StartupSettings? startupSettings = null)
    {
        Sys4StartupSettings settings = startupSettings ?? Sys4StartupSettings.Empty;
        return new(
            CancelMessageSkipOnClick: settings.CancelMessageSkipOnClick,
            CoexistMessageSkip: settings.CoexistMessageSkip,
            FastForwardOpMicroseconds: SkipAndAutoSettings.NativeFastForwardOpMicroseconds,
            FastForwardPageMicroseconds: SkipAndAutoSettings.NativeFastForwardPageMicroseconds);
    }
}
