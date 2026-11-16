using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;

namespace Ome.Launcher.Views;

/// <summary>The main window. It opens where it was (<c>launcher.toml</c>'s <c>window</c>, when that is still on a
/// screen), starts the view model's first work once shown, and keeps its place when it closes. It is the view models'
/// <see cref="IWindowHost"/>: while an engine the launcher started runs, closing it only hides it, and the launcher
/// ends once the last engine exits (spec §8.5).</summary>
public sealed partial class MainWindow : Window, IWindowHost
{
    private PixelRect? _normal;

    public MainWindow()
    {
        InitializeComponent();
        PositionChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
        Activated += (_, _) => { if (Model is { } model) _ = model.RefreshRunningAsync(); };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Model is { } model) model.Host = this;
    }

    private MainViewModel? Model => DataContext as MainViewModel;

    /// <summary>Puts the window where <paramref name="placement"/> says, when a screen still shows that place.</summary>
    public void Place(WindowPlacement? placement)
    {
        if (placement == null || placement.Width < MinWidth || placement.Height < MinHeight) return;
        var area = new PixelRect(placement.X, placement.Y, placement.Width, placement.Height);
        if (Screens.All.Count > 0 && !Screens.All.Any(screen => screen.WorkingArea.Intersects(area))) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(placement.X, placement.Y);
        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized) WindowState = WindowState.Maximized;
    }

    private void Remember()
    {
        if (WindowState == WindowState.Normal && IsVisible)
            _normal = new PixelRect(Position.X, Position.Y, (int)Width, (int)Height);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Remember();
        if (Model is { } model) _ = model.StartAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Model is { } model && _normal is { } normal)
            model.SavePlacement(new WindowPlacement(normal.X, normal.Y, normal.Width, normal.Height,
                                                    WindowState == WindowState.Maximized));
        if (Model is { } running && !running.CloseRequested()) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Model?.Dispose();
        Quit();
    }

    // ---- IWindowHost ----

    public void Minimize() => WindowState = WindowState.Minimized;

    void IWindowHost.Hide() => Hide();

    public void Restore()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void Quit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public (int Width, int Height)? UsableArea()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen) return null;
        // The launcher's own frame stands for the game window's: both are ordinary decorated windows.
        double scaling = screen.Scaling;
        int frameWidth = FrameSize is { } frame ? (int)Math.Ceiling((frame.Width - ClientSize.Width) * scaling) : 0;
        int frameHeight = FrameSize is { } framed ? (int)Math.Ceiling((framed.Height - ClientSize.Height) * scaling) : 0;
        return (screen.WorkingArea.Width - frameWidth, screen.WorkingArea.Height - frameHeight);
    }

    public async Task CopyTextAsync(string text)
    {
        if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    public async Task<string?> PickFileAsync(string title, string extension)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(extension) { Patterns = ["*" + extension] }],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException
                                          or FileNotFoundException)
        {
            Trace.WriteLine($"OME-Launcher: {path} cannot be opened: {error.Message}");
        }
    }
}
