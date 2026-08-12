using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Unpwn.App.Presentation;

namespace Unpwn.App.Views;

public partial class WorkflowExecutionView : AccessibleScreen
{
    private WorkflowExecutionScreenViewModel? _subscribedViewModel;

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
        _subscribedViewModel?.PropertyChanged -= ViewModel_OnPropertyChanged;
        _subscribedViewModel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void WorkflowExecutionView_OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        SubscribeToViewModel();
    }

    private void SubscribeToViewModel()
    {
        _subscribedViewModel?.PropertyChanged -= ViewModel_OnPropertyChanged;
        _subscribedViewModel = DataContext as WorkflowExecutionScreenViewModel;
        _subscribedViewModel?.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(WorkflowExecutionScreenViewModel.CurrentActionFocusRequest))
        {
            FocusCurrentAction();
        }
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
