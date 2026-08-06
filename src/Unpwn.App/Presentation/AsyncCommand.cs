using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace Unpwn.App.Presentation;

public enum AsyncCommandOutcome
{
    Completed,
    Canceled,
    Failed,
    Skipped,
}

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The per-execution cancellation source is always disposed in ExecuteAsync finally.")]
public sealed class AsyncCommand : ObservableObject, ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Func<string> _safeFailureMessageProvider;
    private readonly RelayCommand _cancelCommand;
    private CancellationTokenSource? _cancellation;
    private int _executionGate;
    private bool _isRunning;
    private string? _lastErrorMessage;
    private AsyncCommandOutcome? _lastOutcome;

    public AsyncCommand(
        Func<CancellationToken, Task> execute,
        string safeFailureMessage,
        Func<bool>? canExecute = null)
        : this(execute, () => safeFailureMessage, canExecute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeFailureMessage);
    }

    public AsyncCommand(
        Func<CancellationToken, Task> execute,
        Func<string> safeFailureMessageProvider,
        Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(safeFailureMessageProvider);

        _execute = execute;
        _safeFailureMessageProvider = safeFailureMessageProvider;
        _canExecute = canExecute;
        _cancelCommand = new RelayCommand(Cancel, () => CanBeCanceled);
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanBeCanceled));
                _cancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanBeCanceled => IsRunning && _cancellation?.IsCancellationRequested == false;

    public string? LastErrorMessage
    {
        get => _lastErrorMessage;
        private set
        {
            if (SetProperty(ref _lastErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => LastErrorMessage is not null;

    public AsyncCommandOutcome? LastOutcome
    {
        get => _lastOutcome;
        private set => SetProperty(ref _lastOutcome, value);
    }

    public ICommand CancelCommand => _cancelCommand;

    public bool CanExecute(object? parameter) =>
        Volatile.Read(ref _executionGate) == 0 && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This is the UI command boundary; source exceptions must not escape or expose sensitive messages.")]
    public async Task<AsyncCommandOutcome> ExecuteAsync()
    {
        if (!(_canExecute?.Invoke() ?? true))
        {
            return AsyncCommandOutcome.Skipped;
        }

        if (Interlocked.CompareExchange(ref _executionGate, 1, 0) != 0)
        {
            return AsyncCommandOutcome.Skipped;
        }

        _cancellation = new CancellationTokenSource();
        LastErrorMessage = null;
        LastOutcome = null;
        IsRunning = true;
        RaiseCanExecuteChanged();

        AsyncCommandOutcome outcome;
        try
        {
            await _execute(_cancellation.Token);
            outcome = AsyncCommandOutcome.Completed;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            outcome = AsyncCommandOutcome.Canceled;
        }
        catch (Exception)
        {
            var safeFailureMessage = _safeFailureMessageProvider();
            LastErrorMessage = string.IsNullOrWhiteSpace(safeFailureMessage)
                ? throw new InvalidOperationException("The safe failure message provider returned no message.")
                : safeFailureMessage;
            outcome = AsyncCommandOutcome.Failed;
        }
        finally
        {
            IsRunning = false;
            _cancellation.Dispose();
            _cancellation = null;
            _ = Interlocked.Exchange(ref _executionGate, 0);
            RaiseCanExecuteChanged();
        }

        LastOutcome = outcome;
        return outcome;
    }

    public void Cancel()
    {
        _cancellation?.Cancel();
        OnPropertyChanged(nameof(CanBeCanceled));
        _cancelCommand.RaiseCanExecuteChanged();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
