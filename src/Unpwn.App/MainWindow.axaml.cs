using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Unpwn.App.Presentation;
using Unpwn.App.Services;

namespace Unpwn.App;

public partial class MainWindow : Window
{
    private IVaultLifecycleService? _vaultLifecycle;
    private DispatcherTimer? _inactivityTimer;
    private IApplicationPreferences? _applicationPreferences;
    private SettingsScreenViewModel? _settingsViewModel;
    private SettingsWindow? _settingsWindow;
    private double _lastNormalWidth = MainWindowPresentationPreferences.DefaultWidth;
    private double _lastNormalHeight = MainWindowPresentationPreferences.DefaultHeight;

    public MainWindow()
    {
        InitializeComponent();
        Resized += MainWindow_OnResized;
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

    public void AttachApplicationPreferences(IApplicationPreferences applicationPreferences)
    {
        ArgumentNullException.ThrowIfNull(applicationPreferences);
        _applicationPreferences = applicationPreferences;
        var saved = applicationPreferences.Load().MainWindow;
        var (availableWidth, availableHeight) = GetPrimaryWorkingAreaInDips();
        var restored = MainWindowPresentationPolicy.Normalize(
            saved,
            availableWidth,
            availableHeight);
        _lastNormalWidth = restored.NormalWidth;
        _lastNormalHeight = restored.NormalHeight;
        Width = restored.NormalWidth;
        Height = restored.NormalHeight;
        WindowState = restored.IsMaximized ? WindowState.Maximized : WindowState.Normal;
    }

    public void AttachSettings(SettingsScreenViewModel settingsViewModel)
    {
        _settingsViewModel = settingsViewModel ??
            throw new ArgumentNullException(nameof(settingsViewModel));
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
        _settingsWindow?.Close();
        _settingsWindow = null;
        SaveWindowPreferences();

        Resized -= MainWindow_OnResized;
        if (_inactivityTimer is not null)
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Tick -= InactivityTimer_OnTick;
            _inactivityTimer = null;
        }

        base.OnClosed(e);
    }

    private void MainWindow_OnResized(object? sender, WindowResizedEventArgs eventArgs)
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        if (double.IsFinite(eventArgs.ClientSize.Width) &&
            eventArgs.ClientSize.Width >= MainWindowPresentationPolicy.MinimumWidth)
        {
            _lastNormalWidth = eventArgs.ClientSize.Width;
        }

        if (double.IsFinite(eventArgs.ClientSize.Height) &&
            eventArgs.ClientSize.Height >= MainWindowPresentationPolicy.MinimumHeight)
        {
            _lastNormalHeight = eventArgs.ClientSize.Height;
        }
    }

    private void SettingsButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_settingsViewModel is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow
        {
            DataContext = _settingsViewModel,
        };
        window.Closed += SettingsWindow_OnClosed;
        _settingsWindow = window;
        window.Show(this);
    }

    private void SettingsWindow_OnClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is SettingsWindow window)
        {
            window.Closed -= SettingsWindow_OnClosed;
        }

        _settingsWindow = null;
    }

    private void SaveWindowPreferences()
    {
        if (_applicationPreferences is null)
        {
            return;
        }

        var snapshot = new ApplicationPreferencesSnapshot(
            new MainWindowPresentationPreferences(
                _lastNormalWidth,
                _lastNormalHeight,
                WindowState == WindowState.Maximized));
        _applicationPreferences.TrySave(snapshot);
    }

    private (double Width, double Height) GetPrimaryWorkingAreaInDips()
    {
        try
        {
            var screen = Screens.All.FirstOrDefault(candidate => candidate.IsPrimary) ??
                Screens.ScreenFromTopLevel(this);
            return screen is null || screen.Scaling <= 0
                ? (double.PositiveInfinity, double.PositiveInfinity)
                : (screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling);
        }
        catch (InvalidOperationException)
        {
            return (double.PositiveInfinity, double.PositiveInfinity);
        }
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
