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
    Justification = "The embedded browser and credential handoff are released through the visual/session lifecycle.")]
public partial class WorkflowExecutionView : AccessibleScreen
{
    private WorkflowExecutionScreenViewModel? _subscribedViewModel;
    private RecoveryBrowserView? _browserView;
    private RecoveryCredentialHandoffViewModel? _credentialHandoffViewModel;
    private RecoveryCredentialHandoffView? _credentialHandoffView;

    public WorkflowExecutionView()
    {
        InitializeComponent();
        DataContextChanged += WorkflowExecutionView_OnDataContextChanged;
    }

    internal RecoveryBrowserView? RecoveryBrowser => _browserView;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromViewModel();
        RemoveCredentialHandoff();
        if (_browserView is not null)
        {
            _ = _browserView.CloseSessionAsync();
        }
        _subscribedViewModel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void WorkflowExecutionView_OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        UnsubscribeFromViewModel();
        _subscribedViewModel = null;
        if (TopLevel.GetTopLevel(this) is not null)
        {
            SubscribeToViewModel();
        }
    }

    private void SubscribeToViewModel()
    {
        UnsubscribeFromViewModel();
        _subscribedViewModel = DataContext as WorkflowExecutionScreenViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            _subscribedViewModel.RecoveryBrowserRequested += ViewModel_OnRecoveryBrowserRequested;
            if (_subscribedViewModel.TryTakePendingRecoveryBrowserRequest(out var request))
            {
                ViewModel_OnRecoveryBrowserRequested(_subscribedViewModel, request);
            }
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

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Native browser initialization can fail through platform-specific exceptions. The recovery boundary must fail closed without surfacing exception text or crashing the UI thread.")]
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
            var owner = TopLevel.GetTopLevel(this);
            if (owner is null)
            {
                viewModel.ReportRecoveryBrowserOpenResult(false);
                return;
            }

            _browserView = new RecoveryBrowserView(
                viewModel.BrowserSessions,
                RecoveryBrowserPlatformAdapter.Create,
                allowLinuxDialogFallback: true,
                dialogOwner: owner,
                applicationDataRoot: global::Unpwn.App.Program.DesktopE2E?.DataRoot);
            _browserView.SessionClosed += BrowserView_OnSessionClosed;
            BrowserWorkspaceHost.Content = _browserView;
        }

        bool opened;
        try
        {
            opened = await _browserView.StartAsync(
                new RecoveryBrowserSessionStartRequest(
                    request.AccountId,
                    request.Handoff,
                    request.ContentMode));
        }
        catch (Exception)
        {
            // Do not surface native exception text: it can contain provider/runtime details and
            // an async-void exception here would otherwise terminate the UI path. The prepared
            // navigation remains available for the explicit external-browser fallback.
            opened = false;
        }

        viewModel.ReportRecoveryBrowserOpenResult(
            opened,
            _browserView.SessionSnapshot.State != RecoveryBrowserSessionLifecycleState.Idle);
        if (!opened)
        {
            return;
        }

        await AttachCredentialHandoffAsync(viewModel, request);
        Dispatcher.UIThread.Post(
            () => CurrentActionCard.Focus(NavigationMethod.Tab),
            DispatcherPriority.Loaded);
    }

    private async Task AttachCredentialHandoffAsync(
        WorkflowExecutionScreenViewModel workflow,
        RecoveryBrowserWorkspaceRequest request)
    {
        RemoveCredentialHandoff();
        var services = (Avalonia.Application.Current as global::Unpwn.App.App)
            ?.RecoveryCredentialHandoffServices;
        var actionId = workflow.SelectedAction?.DefinitionId;
        if (services is null || string.IsNullOrWhiteSpace(actionId) || _browserView is null)
        {
            return;
        }

        var handoff = new RecoveryCredentialHandoffViewModel(
            workflow,
            services,
            request,
            actionId,
            (contract, token) => _browserView?.InspectCredentialInsertionAsync(contract, token) ??
                Task.FromResult(RecoveryBrowserCredentialAssistanceResult.Failure(
                    RecoveryBrowserCredentialAssistanceState.Unavailable,
                    RecoveryBrowserCredentialAssistanceFailureCode.BrowserUnavailable)),
            (contract, secret, token) => _browserView?.InsertCredentialAsync(contract, secret, token) ??
                Task.FromResult(RecoveryBrowserCredentialAssistanceResult.Failure(
                    RecoveryBrowserCredentialAssistanceState.Unavailable,
                    RecoveryBrowserCredentialAssistanceFailureCode.BrowserUnavailable)));
        var view = new RecoveryCredentialHandoffView { DataContext = handoff };
        _credentialHandoffViewModel = handoff;
        _credentialHandoffView = view;
        if (CurrentActionCard.Child is StackPanel assistantPanel)
        {
            assistantPanel.Children.Add(view);
        }

        await handoff.InitializeAsync();
    }

    private async void BrowserView_OnSessionClosed(object? sender, EventArgs eventArgs)
    {
        if (_credentialHandoffViewModel is not null)
        {
            await _credentialHandoffViewModel.OnBrowserClosedAsync();
        }

        _browserView?.SessionClosed -= BrowserView_OnSessionClosed;
        _browserView = null;
        BrowserWorkspaceHost.Content = null;
        _subscribedViewModel?.ReportRecoveryBrowserClosed();
        FocusCurrentAction();
    }

    private void RemoveCredentialHandoff()
    {
        if (_credentialHandoffView is not null &&
            CurrentActionCard.Child is StackPanel assistantPanel)
        {
            assistantPanel.Children.Remove(_credentialHandoffView);
        }

        _credentialHandoffViewModel?.Dispose();
        _credentialHandoffViewModel = null;
        _credentialHandoffView = null;
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
