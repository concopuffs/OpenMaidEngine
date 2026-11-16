using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Ome.Launcher.ViewModels;

/// <summary>The view models' base (launcher plan §6.1): property change notices, with no MVVM package.</summary>
public abstract class ViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets <paramref name="field"/> and tells the views, when the value changed.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(name);
        return true;
    }

    protected void Changed([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Tells the views every property may have changed (a new language, a reloaded profile).</summary>
    protected void ChangedAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>A button's action and whether it may run now.</summary>
public sealed class Command(Action<object?> run, Func<object?, bool>? can = null) : ICommand
{
    public Command(Action run, Func<bool>? can = null) : this(_ => run(), can == null ? null : _ => can()) { }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => can?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) run(parameter);
    }

    /// <summary>Tells the views to ask <see cref="CanExecute"/> again.</summary>
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
