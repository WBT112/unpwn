using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Unpwn.App.Views;

namespace Unpwn.App.Services;

public sealed class AvaloniaConfirmationDialogService(Func<Window?> ownerProvider) : IConfirmationDialogService
{
    public async Task<bool> ConfirmAsync(
        SensitiveConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var owner = ownerProvider() ?? throw new InvalidOperationException(
            "The confirmation dialog owner is not available.");
        var previouslyFocusedElement = owner.FocusManager?.GetFocusedElement();
        var dialog = new ConfirmationDialog(request);
        using var registration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() => dialog.Close(false)));
        try
        {
            return await dialog.ShowDialog<bool>(owner);
        }
        finally
        {
            if (previouslyFocusedElement is not null)
            {
                Dispatcher.UIThread.Post(() =>
                    owner.FocusManager?.Focus(previouslyFocusedElement, NavigationMethod.Tab));
            }
        }
    }
}
