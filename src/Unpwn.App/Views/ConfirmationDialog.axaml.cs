using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Unpwn.App.Services;

namespace Unpwn.App.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(SensitiveConfirmationRequest request) : this()
    {
        ArgumentNullException.ThrowIfNull(request);
        DataContext = request;
    }

    private void Window_OnOpened(object? sender, EventArgs eventArgs) =>
        CancelButton.Focus(NavigationMethod.Tab);

    private void Window_OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape)
        {
            return;
        }

        eventArgs.Handled = true;
        Close(false);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Close(false);

    private void ConfirmButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Close(true);
}
