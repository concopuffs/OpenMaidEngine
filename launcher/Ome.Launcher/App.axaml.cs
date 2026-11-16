using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Ome.Engine.Frontend.Shell;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Launcher.Views;

namespace Ome.Launcher;

/// <summary>
/// The launcher's application (launcher spec §9, plan §6.1). It opens the main window over a <see cref="LauncherSession"/>
/// when it has one; the headless tests start it with none and open their own windows.
/// </summary>
public sealed partial class App : Application
{
    private readonly LauncherSession? _session;

    public App() { }

    /// <summary>The launcher's version as the engine shows its own: the <c>OmeVersion</c> its build stamped (git
    /// describe, Directory.Build.targets), read by <see cref="EngineIdentity.DisplayVersion"/> (plan §9.1).</summary>
    public static string Version { get; } = EngineIdentity.DisplayVersion(
        typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == EngineIdentity.VersionMetadataKey)?.Value);

    public App(LauncherSession session) => _session = session;

    /// <summary>The program's app: the platform's windowing, the launcher's font for every text that names none.</summary>
    public static AppBuilder Build(Func<App> factory) => AppBuilder.Configure(factory).UsePlatformDetect().LogToTrace();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (_session != null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RequestedThemeVariant = Variant(_session.Settings.Theme);
            var window = new MainWindow { DataContext = new MainViewModel(_session) };
            window.Place(_session.Settings.Window);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary><c>launcher.toml</c>'s <c>theme</c> as Avalonia's variant: <c>system</c> follows the OS.</summary>
    public static ThemeVariant Variant(LauncherTheme theme) => theme switch
    {
        LauncherTheme.Light => ThemeVariant.Light,
        LauncherTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
