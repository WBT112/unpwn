namespace Unpwn.App.Services;

public interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(
        SensitiveConfirmationRequest request,
        CancellationToken cancellationToken);
}
