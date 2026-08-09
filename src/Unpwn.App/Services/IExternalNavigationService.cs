using Avalonia.Controls;

namespace Unpwn.App.Services;

public enum ExternalNavigationFailureCode
{
    None,
    Unavailable,
    Rejected,
}

public sealed record ExternalNavigationResult(
    bool Succeeded,
    ExternalNavigationFailureCode FailureCode)
{
    public static ExternalNavigationResult Success { get; } =
        new(true, ExternalNavigationFailureCode.None);

    public static ExternalNavigationResult Failure(ExternalNavigationFailureCode failureCode) =>
        new(false, failureCode);
}

public interface IExternalNavigationService
{
    Task<ExternalNavigationResult> OpenAsync(Uri destination, CancellationToken cancellationToken);
}

public sealed class AvaloniaExternalNavigationService(Func<TopLevel?> topLevelProvider)
    : IExternalNavigationService
{
    private readonly Func<TopLevel?> _topLevelProvider =
        topLevelProvider ?? throw new ArgumentNullException(nameof(topLevelProvider));

    public async Task<ExternalNavigationResult> OpenAsync(
        Uri destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        var launcher = _topLevelProvider()?.Launcher;
        if (launcher is null)
        {
            return ExternalNavigationResult.Failure(ExternalNavigationFailureCode.Unavailable);
        }

        try
        {
            var opened = await launcher.LaunchUriAsync(destination);
            cancellationToken.ThrowIfCancellationRequested();
            return opened
                ? ExternalNavigationResult.Success
                : ExternalNavigationResult.Failure(ExternalNavigationFailureCode.Rejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return ExternalNavigationResult.Failure(ExternalNavigationFailureCode.Unavailable);
        }
    }
}
