using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Ome.Launcher.ViewModels;

namespace Ome.Launcher.Views;

/// <summary>The new-profile wizard. Its scrim covers the window, so a folder dropped anywhere on it is the install the
/// first step looks at (spec §8.3): a file manager's folder, or a path dropped as text.</summary>
public sealed partial class WizardView : UserControl
{
    public WizardView()
    {
        InitializeComponent();
        Scrim.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Scrim.AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropLine.Classes.Set("over", false));
        Scrim.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private WizardViewModel? Model => DataContext as WizardViewModel;

    /// <summary>The folder a drag carries: the first dropped item that is a folder on this machine, or text naming
    /// one.</summary>
    public static string? Folder(IDataTransfer data)
    {
        foreach (IStorageItem item in data.TryGetFiles() ?? [])
            if (item.TryGetLocalPath() is { } path && Directory.Exists(path))
                return path;
        return data.TryGetText()?.Trim().Trim('"') is { Length: > 0 } text && Path.IsPathRooted(text) && Directory.Exists(text)
            ? text
            : null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        bool takes = Model is { OnGame: true, Busy: false } && Folder(e.DataTransfer) != null;
        e.DragEffects = takes ? DragDropEffects.Link : DragDropEffects.None;
        DropLine.Classes.Set("over", takes);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        DropLine.Classes.Set("over", false);
        if (Model is { OnGame: true } model && Folder(e.DataTransfer) is { } folder) _ = model.ChooseAsync(folder);
    }
}
