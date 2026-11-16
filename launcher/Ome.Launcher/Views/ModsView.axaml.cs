using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Ome.Launcher.ViewModels;

namespace Ome.Launcher.Views;

/// <summary>The Mods tab. Dragging a row's grip sets the preferred order (spec §8.4): the drag carries the package's
/// id as text with <see cref="DragPrefix"/>, the list marks where it would land, and a drop asks the view model to
/// move it there. Only a package the list shows is moved.</summary>
public sealed partial class ModsView : UserControl
{
    public const string DragPrefix = "ome-mod:";

    public ModsView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, GripPressed, RoutingStrategies.Tunnel);
        List.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        List.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        List.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private ModsViewModel? Model => DataContext as ModsViewModel;

    private async void GripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source || Model is not { } model) return;
        Border? grip = source as Border is { } border && border.Classes.Contains("grip")
            ? border
            : source.FindAncestorOfType<Border>() is { } parent && parent.Classes.Contains("grip") ? parent : null;
        if (grip?.DataContext is not ModRowViewModel row || !model.CanDrag(row.Id)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        // The row's button would take the press as a click; the grip only drags.
        e.Handled = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(DragPrefix + row.Id));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        model.MarkDrop(null, false);
    }

    /// <summary>The package a drag carries, when it is one of this list's.</summary>
    private string? Dragged(DragEventArgs e)
        => e.DataTransfer.TryGetText() is { } text && text.StartsWith(DragPrefix, StringComparison.Ordinal)
           && Model?.CanDrag(text[DragPrefix.Length..]) == true
            ? text[DragPrefix.Length..]
            : null;

    /// <summary>The id the drag would land before (null: the end), from the row under the pointer.</summary>
    private string? Before(DragEventArgs e, ModsViewModel model)
    {
        for (int index = 0; index < model.Rows.Count; index++)
        {
            if (RowList.ContainerFromIndex(index) is not Control container) continue;
            double y = e.GetPosition(container).Y;
            if (y < container.Bounds.Height) return model.BeforeAt(index, y >= container.Bounds.Height / 2);
        }
        return null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Model is not { } model || Dragged(e) == null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Move;
        model.MarkDrop(Before(e, model), true);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => Model?.MarkDrop(null, false);

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Model is not { } model) return;
        string? id = Dragged(e);
        string? before = id == null ? null : Before(e, model);
        model.MarkDrop(null, false);
        if (id == null) return;
        e.DragEffects = DragDropEffects.Move;
        await model.MoveAsync(id, before);
    }
}
