using System;
using Packman.Services;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Packman.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) =>
        !TryCoerce(parameter, out var value) || (_canExecute?.Invoke(value) ?? true);

    public void Execute(object? parameter)
    {
        if (TryCoerce(parameter, out var value)) _execute(value);
    }

    /// <summary>Binding can pass null or a mismatched type. That is a no-op, not a throw.</summary>
    private static bool TryCoerce(object? p, out T value)
    {
        if (p is T typed) { value = typed; return true; }
        if (p is null && default(T) is null) { value = default!; return true; }

        try
        {
            value = (T)Convert.ChangeType(p!, typeof(T));
            return true;
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException or ArgumentNullException)
        {
            value = default!;
            return false;
        }
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Command for awaited work. The framework discards the returned Task, so this awaits
/// internally, reports through <see cref="ErrorReporter"/> and blocks re-entry while
/// the work is in flight.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        IsRunning = true;
        try
        {
            await _execute();
        }
        catch (OperationCanceledException)
        {
            // Cancelling is a normal outcome.
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
