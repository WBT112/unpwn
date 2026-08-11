using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Unpwn.App.Presentation;
using Unpwn.App.Services;

namespace Unpwn.App;

public partial class MainWindow : Window
{
    private IVaultLifecycleService? _vaultLifecycle;
    private DispatcherTimer? _inactivityTimer;
    private ShellViewModel? _subscribedShell;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_OnDataContextChanged;
        Opened += MainWindow_OnOpened;
    }

    public void AttachInactivityMonitor(IVaultLifecycleService vaultLifecycle)
    {
        ArgumentNullException.ThrowIfNull(vaultLifecycle);
        _vaultLifecycle = vaultLifecycle;
        _vaultLifecycle.RecordUserActivity(DateTimeOffset.UtcNow);
        _inactivityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        _inactivityTimer.Tick += InactivityTimer_OnTick;
        _inactivityTimer.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _vaultLifecycle?.RecordUserActivity(DateTimeOffset.UtcNow);
        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _vaultLifecycle?.RecordUserActivity(DateTimeOffset.UtcNow);
        base.OnPointerPressed(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _subscribedShell?.PropertyChanged -= Shell_OnPropertyChanged;
        _subscribedShell = null;

        DataContextChanged -= MainWindow_OnDataContextChanged;
        Opened -= MainWindow_OnOpened;
        if (_inactivityTimer is not null)
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Tick -= InactivityTimer_OnTick;
            _inactivityTimer = null;
        }

        base.OnClosed(e);
    }

    private void MainWindow_OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        _subscribedShell?.PropertyChanged -= Shell_OnPropertyChanged;

        _subscribedShell = window.DataContext as ShellViewModel;
        _subscribedShell?.PropertyChanged += Shell_OnPropertyChanged;
    }

    private void MainWindow_OnOpened(object? sender, EventArgs eventArgs) =>
        FocusAssistantTask();

    private void Shell_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ShellViewModel.AssistantFocusRequest))
        {
            FocusAssistantTask();
        }
    }

    private void FocusAssistantTask() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (AssistantPrimaryAction is { IsVisible: true, IsEnabled: true })
                {
                    AssistantPrimaryAction.Focus(NavigationMethod.Tab);
                }
            },
            DispatcherPriority.Loaded);

    private async void InactivityTimer_OnTick(object? sender, EventArgs eventArgs)
    {
        if (_vaultLifecycle is null)
        {
            return;
        }

        await _vaultLifecycle.CheckInactivityAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);
    }
}
