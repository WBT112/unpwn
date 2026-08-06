using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Unpwn.App.Services;

namespace Unpwn.App;

public partial class MainWindow : Window
{
    private IVaultLifecycleService? _vaultLifecycle;
    private DispatcherTimer? _inactivityTimer;

    public MainWindow()
    {
        InitializeComponent();
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

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        _vaultLifecycle?.RecordUserActivity(DateTimeOffset.UtcNow);
        base.OnKeyDown(eventArgs);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        _vaultLifecycle?.RecordUserActivity(DateTimeOffset.UtcNow);
        base.OnPointerPressed(eventArgs);
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        if (_inactivityTimer is not null)
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Tick -= InactivityTimer_OnTick;
            _inactivityTimer = null;
        }

        base.OnClosed(eventArgs);
    }

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
