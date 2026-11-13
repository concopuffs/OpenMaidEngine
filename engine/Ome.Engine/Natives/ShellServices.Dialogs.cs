using Ome.Engine.Model;

namespace Ome.Engine.Natives;

/// <summary>
/// The front end's side of the engine's dialogs (engine layering spec §6.2): it shows a dialog the shell-services
/// native opens and completes it with the user's answer. Each call returns at once; the front end shows the dialog on
/// its own thread. A dialog's methods are safe from any thread, and an answer to a dialog that was cancelled (a stop, a
/// return to the title) changes nothing.
/// </summary>
/// <remarks>It carries AGE vocabulary (the boxes' messages, op 0x144's text entry), so it is the native's seam in
/// Ome.Engine and not a device (spec §6.4).</remarks>
public interface IShellDialogs
{
    /// <summary>Op 0x1b4's box (age_show_message_box, OK only).</summary>
    void ShowDiagnostic(DiagnosticBox box);

    /// <summary>AGE's interpreter error box (中止/再試行/無視).</summary>
    void ShowScriptError(ScriptErrorBox box);

    /// <summary>Op 0x144's full-width text entry.</summary>
    void ShowTextEntry(TextEntry entry);
}

/// <summary>
/// A dialog the shell-services native opened: a request the front end completes (spec §6.2). It is answered once,
/// either by the front end or by a cancel; the first wins.
/// </summary>
public abstract class ShellDialog
{
    private const int Open = 0, Answered = 1, Cancelled = 2;

    private readonly ManualResetEventSlim _closed = new(false);
    private int _state;

    /// <summary>Guards the state and the answer.</summary>
    private protected readonly object Sync = new();

    /// <summary>Set once the dialog is answered or cancelled.</summary>
    public WaitHandle Closed => _closed.WaitHandle;

    /// <summary>True until the dialog is answered or cancelled.</summary>
    public bool IsOpen => Volatile.Read(ref _state) == Open;

    /// <summary>True when a stop, a return to the title or a scene reset closed the dialog before an answer.</summary>
    public bool IsCancelled => Volatile.Read(ref _state) == Cancelled;

    /// <summary>Close the dialog with an answer, which <paramref name="record"/> stores under <see cref="Sync"/>.
    /// False when it was already closed: the answer is dropped.</summary>
    private protected bool TryAnswer(Action record)
    {
        lock (Sync)
        {
            if (_state != Open) return false;
            record();
            Volatile.Write(ref _state, Answered);
        }
        _closed.Set();
        return true;
    }

    internal void Cancel()
    {
        lock (Sync)
        {
            if (_state != Open) return;
            Volatile.Write(ref _state, Cancelled);
        }
        _closed.Set();
    }
}

/// <summary>Op 0x1b4's box: the diagnostic text, closed by its OK.</summary>
public sealed class DiagnosticBox(DiagnosticMessage message) : ShellDialog
{
    public DiagnosticMessage Message { get; } = message;

    /// <summary>The box's OK (or its failure to show: the run goes on).</summary>
    public void Close() => TryAnswer(static () => { });
}

/// <summary>AGE's interpreter error box (age_show_message_box mode 7: MessageBoxA style 0x112).</summary>
public sealed class ScriptErrorBox(DiagnosticMessage message) : ShellDialog
{
    private ScriptErrorChoice _choice = ScriptErrorChoice.Abort;

    public DiagnosticMessage Message { get; } = message;

    /// <summary>The answer; 中止 until one comes.</summary>
    public ScriptErrorChoice Choice
    {
        get
        {
            lock (Sync) return _choice;
        }
    }

    public void Answer(ScriptErrorChoice choice) => TryAnswer(() => _choice = choice);
}

/// <summary>Op 0x144's full-width text entry (AGERC's name dialog).</summary>
public sealed class TextEntry : ShellDialog
{
    private FullwidthTextEditResult _result;

    public TextEntry(FullwidthTextEditRequest request)
    {
        Request = request;
        _result = new(false, request.CurrentText);
    }

    public FullwidthTextEditRequest Request { get; }

    /// <summary>The answer; until one comes, not accepted with the current text.</summary>
    public FullwidthTextEditResult Result
    {
        get
        {
            lock (Sync) return _result;
        }
    }

    public void Answer(bool accepted, string text) => TryAnswer(() => _result = new(accepted, text));
}

public sealed partial class ShellServices
{
    private readonly IShellDialogs? _dialogs;
    private readonly List<ShellDialog> _openDialogs = new();

    /// <summary>Open op 0x1b4's box. The caller waits on <see cref="ShellDialog.Closed"/>.</summary>
    public DiagnosticBox OpenDiagnostic(DiagnosticMessage message)
    {
        var box = new DiagnosticBox(message);
        Track(box);
        _dialogs?.ShowDiagnostic(box);
        return box;
    }

    /// <summary>Open the interpreter error box. The caller waits on <see cref="ShellDialog.Closed"/>.</summary>
    public ScriptErrorBox OpenScriptError(DiagnosticMessage message)
    {
        var box = new ScriptErrorBox(message);
        Track(box);
        _dialogs?.ShowScriptError(box);
        return box;
    }

    /// <summary>Open op 0x144's text entry. The caller waits on <see cref="ShellDialog.Closed"/>.</summary>
    public TextEntry OpenTextEntry(FullwidthTextEditRequest request)
    {
        var entry = new TextEntry(request);
        Track(entry);
        _dialogs?.ShowTextEntry(entry);
        return entry;
    }

    /// <summary>Cancel every open dialog (a stop, a return to the title, a scene reset): its wait ends, and a later
    /// answer is dropped, so it cannot answer the next dialog.</summary>
    public void CancelDialogs()
    {
        ShellDialog[] open;
        lock (_openDialogs)
        {
            open = _openDialogs.ToArray();
            _openDialogs.Clear();
        }
        foreach (ShellDialog dialog in open) dialog.Cancel();
    }

    private void Track(ShellDialog dialog)
    {
        lock (_openDialogs)
        {
            _openDialogs.RemoveAll(static d => !d.IsOpen);
            _openDialogs.Add(dialog);
        }
    }
}
