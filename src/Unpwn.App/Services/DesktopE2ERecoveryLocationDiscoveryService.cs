using Unpwn.Application.Recovery;

namespace Unpwn.App.Services;

internal sealed class DesktopE2ERecoveryLocationDiscoveryService(Uri destination)
    : IRecoveryLocationDiscoveryService
{
    private readonly Uri _destination = Validate(destination);

    public Task<RecoveryLocationDiscoveryResult> DiscoverAsync(
        RecoveryLocationDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                request.Workflow.ProviderId,
                "synthetic",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(RecoveryLocationDiscoveryResult.Failure(
                RecoveryLocationDiscoveryFailureCode.InvalidRequest));
        }

        var origin = _destination.GetLeftPart(UriPartial.Authority);
        return Task.FromResult(RecoveryLocationDiscoveryResult.Success(
            new RecoveryNavigationHandoff(
                _destination,
                origin,
                [origin],
                RecoveryLocationResolutionSource.ProviderDefined,
                RequiresVisibleConfirmation: true)));
    }

    private static Uri Validate(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri || destination.Scheme != Uri.UriSchemeHttp ||
            !destination.IsLoopback || !string.IsNullOrEmpty(destination.UserInfo))
        {
            throw new ArgumentException(
                "The desktop E2E recovery destination must be HTTP loopback.",
                nameof(destination));
        }

        return destination;
    }
}
