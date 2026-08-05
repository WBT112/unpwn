using Avalonia.Controls;
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

    private void CancelButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Close(false);

    private void ConfirmButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Close(true);
}
