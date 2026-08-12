using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unpwn.App.Presentation;
using Unpwn.App.Services;

namespace Unpwn.App.Views;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The embedded browser is released through its asynchronous account-session cleanup boundary.")]
public partial class WorkflowExecutionView : AccessibleScreen
{
    private WorkflowExecutionScreenViewModel? _subscribedViewModel;
    private RecoveryBrowserView? _browserView;

    public WorkflowExecutionView()
    {
        InitializeComponent();
        DataContextChanged += WorkflowExecutionView_OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromViewModel();
        if (_browserView is not null)
        {
            _ = _browserView.CloseSessionAsync();
        }
        _subscribedViewModel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void WorkflowExecutionView_OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        SubscribeToViewModel();
    }

    private void SubscribeToViewModel()
    {
        UnsubscribeFromViewModel();
        _subscribedViewModel = DataContext as WorkflowExecutionScreenViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            _subscribedViewModel.RecoveryBrowserRequested += ViewModel_OnRecoveryBrowserRequested;
        }
    }

    private void UnsubscribeFromViewModel()
    {
        if (_subscribedViewModel is null)
        {
            return;
        }

        _subscribedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _subscribedViewModel.RecoveryBrowserRequested -= ViewModel_OnRecoveryBrowserRequested;
    }

    private async void ViewModel_OnRecoveryBrowserRequested(
        object? sender,
        RecoveryBrowserWorkspaceRequest request)
    {
        var viewModel = _subscribedViewModel;
        if (viewModel?.BrowserSessions is null)
        {
            viewModel?.ReportRecoveryBrowserOpenResult(false);
            return;
        }

        if (_browserView is null)
        {
            _browserView = new RecoveryBrowserView(
                viewModel.BrowserSessions,
                RecoveryBrowserPlatformAdapter.Create);
            _browserView.SessionClosed += BrowserView_OnSessionClosed;
            BrowserWorkspaceHost.Content = _browserView;
        }

        var opened = await _browserView.StartAsync(
            new RecoveryBrowserSessionStartRequest(
                request.AccountId,
                request.Handoff,
                request.ContentMode));
        viewModel.ReportRecoveryBrowserOpenResult(
            opened,
            _browserView.SessionSnapshot.State != RecoveryBrowserSessionLifecycleState.Idle);
        if (opened)
        {
            Dispatcher.UIThread.Post(
                () => BrowserWorkspacePanel.Focus(NavigationMethod.Tab),
                DispatcherPriority.Loaded);
        }
    }

    private void BrowserView_OnSessionClosed(object? sender, EventArgs eventArgs)
    {
        _browserView?.SessionClosed -= BrowserView_OnSessionClosed;
        _browserView = null;
        BrowserWorkspaceHost.Content = null;
        _subscribedViewModel?.ReportRecoveryBrowserClosed();
        FocusCurrentAction();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(WorkflowExecutionScreenViewModel.CurrentActionFocusRequest))
        {
            FocusCurrentActionUnlessBrowserHasFocus();
        }
    }

    internal void FocusCurrentActionUnlessBrowserHasFocus()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (BrowserWorkspacePanel.IsVisible &&
            focused is Visual focusedVisual &&
            (ReferenceEquals(focusedVisual, BrowserWorkspacePanel) ||
             BrowserWorkspacePanel.GetVisualDescendants().Contains(focusedVisual)))
        {
            return;
        }

        FocusCurrentAction();
    }

    private void FocusCurrentAction() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (CurrentActionCard is { IsVisible: true, IsEnabled: true })
                {
                    CurrentActionCard.Focus(NavigationMethod.Tab);
                }
            },
            DispatcherPriority.Loaded);
}
