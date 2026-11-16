namespace Ome.Launcher.ViewModels;

/// <summary>A question the window asks over a scrim, as the mock's dialogs (launcher plan §6.4, §6.10): a title, what
/// the change does, the items it touches, and the two answers. <see cref="Answer"/> completes with the player's
/// choice.</summary>
public sealed class ConfirmViewModel : ViewModel
{
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConfirmViewModel(string title, string text, string confirm, string cancel, IReadOnlyList<string>? items = null,
                            string? itemsHeading = null, bool danger = false)
    {
        Title = title;
        Text = text;
        ConfirmText = confirm;
        CancelText = cancel;
        Items = items ?? [];
        ItemsHeading = itemsHeading ?? "";
        Danger = danger;
        Yes = new Command(() => _answer.TrySetResult(true));
        No = new Command(() => _answer.TrySetResult(false));
    }

    public string Title { get; }
    public string Text { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }
    public IReadOnlyList<string> Items { get; }
    public string ItemsHeading { get; }
    public bool HasItems => Items.Count > 0;

    /// <summary>The change removes or turns something off: the confirm button reads as a warning.</summary>
    public bool Danger { get; }

    public Command Yes { get; }
    public Command No { get; }
    public Task<bool> Answer => _answer.Task;
}
