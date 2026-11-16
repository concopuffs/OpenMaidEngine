using Avalonia;
using Avalonia.Controls;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;

namespace Ome.Launcher.Views;

/// <summary>The main window. It opens where it was (<c>launcher.toml</c>'s <c>window</c>, when that is still on a
/// screen), starts the view model's first work once shown, and keeps its place when it closes.</summary>
public sealed partial class MainWindow : Window
{
    private PixelRect? _normal;

    public MainWindow()
    {
        InitializeComponent();
        PositionChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
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
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Model?.Dispose();
    }
}
