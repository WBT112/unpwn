using System.Net;
using System.Net.Http.Headers;
using Unpwn.Application.Recovery;
using Unpwn.Core;

namespace Unpwn.Infrastructure.Recovery;

public sealed class HttpRecoveryLocationDiscoveryService(
    HttpMessageInvoker http,
    int maxRedirects = 5,
    TimeSpan? requestTimeout = null,
    bool disposeInvoker = false)
    : IRecoveryLocationDiscoveryService, IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpMessageInvoker _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly int _maxRedirects = ValidateMaxRedirects(maxRedirects);
    private readonly TimeSpan _requestTimeout = ValidateTimeout(requestTimeout ?? DefaultRequestTimeout);
    private readonly bool _disposeInvoker = disposeInvoker;
    private bool _disposed;

    public static HttpRecoveryLocationDiscoveryService CreateDefault()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        return new HttpRecoveryLocationDiscoveryService(
            new HttpMessageInvoker(handler, disposeHandler: true),
            disposeInvoker: true);
    }

    public async Task<RecoveryLocationDiscoveryResult> DiscoverAsync(
        RecoveryLocationDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryValidateRequest(request))
        {
            return RecoveryLocationDiscoveryResult.Failure(
                RecoveryLocationDiscoveryFailureCode.InvalidRequest);
        }

        var providerLocation = ResolveProviderLocation(request.Workflow, request.ProviderLocationId);
        var providerHandoffAvailable = TryCreateProviderHandoff(
            providerLocation,
            RecoveryLocationResolutionSource.ProviderDefined,
            out var providerHandoff);

        if (request.SelectionPolicy == RecoveryLocationSelectionPolicy.ProviderDefinedOnly)
        {
            return providerHandoffAvailable
                ? RecoveryLocationDiscoveryResult.Success(providerHandoff)
                : RecoveryLocationDiscoveryResult.Failure(
                    providerLocation is null
                        ? RecoveryLocationDiscoveryFailureCode.ProviderLocationNotFound
                        : RecoveryLocationDiscoveryFailureCode.ProviderLocationInvalid);
        }

        if (request.SelectionPolicy == RecoveryLocationSelectionPolicy.ProviderDefinedFirst &&
            providerHandoffAvailable)
        {
            return RecoveryLocationDiscoveryResult.Success(providerHandoff);
        }

        if (request.AccountUri is null)
        {
            return providerHandoffAvailable
                ? RecoveryLocationDiscoveryResult.Success(providerHandoff)
                : RecoveryLocationDiscoveryResult.Failure(
                    providerLocation is null
                        ? RecoveryLocationDiscoveryFailureCode.InvalidRequest
                        : RecoveryLocationDiscoveryFailureCode.ProviderLocationInvalid);
        }

        if (!RecoveryLocationUriNormalizer.TryNormalizeHttps(request.AccountUri, out var normalizedAccountUri))
        {
            return CreateProviderFallbackOrFailure(
                providerHandoffAvailable,
                providerHandoff,
                RecoveryLocationDiscoveryFailureCode.InsecureAccountOrigin,
                RecoveryLocationFallbackReason.InsecureAccountOrigin,
                []);
        }

        var allowedOrigins = CreateAllowedOrigins(normalizedAccountUri, providerLocation);
        var wellKnownResult = await DiscoverWellKnownAsync(
            normalizedAccountUri,
            allowedOrigins,
            cancellationToken);
        if (wellKnownResult.Succeeded)
        {
            return wellKnownResult;
        }

        return CreateProviderFallbackOrFailure(
            providerHandoffAvailable,
            providerHandoff,
            wellKnownResult.FailureCode,
            MapFallbackReason(wellKnownResult.FailureCode),
            wellKnownResult.RedirectChain);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_disposeInvoker)
        {
            _http.Dispose();
        }

        _disposed = true;
    }

    private async Task<RecoveryLocationDiscoveryResult> DiscoverWellKnownAsync(
        Uri accountUri,
        HashSet<string> allowedOrigins,
        CancellationToken cancellationToken)
    {
        var current = RecoveryLocationUriNormalizer.GetWellKnownChangePasswordUri(accountUri);
        List<Uri> redirectChain = [current];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);

        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = CreateRequest(current);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return RecoveryLocationDiscoveryResult.Failure(
                    RecoveryLocationDiscoveryFailureCode.NetworkFailure,
                    redirectChain);
            }
            catch (HttpRequestException)
            {
                return RecoveryLocationDiscoveryResult.Failure(
                    RecoveryLocationDiscoveryFailureCode.NetworkFailure,
                    redirectChain);
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= _maxRedirects)
                    {
                        return RecoveryLocationDiscoveryResult.Failure(
                            RecoveryLocationDiscoveryFailureCode.RedirectLimitExceeded,
                            redirectChain);
                    }

                    if (response.Headers.Location is null)
                    {
                        return RecoveryLocationDiscoveryResult.Failure(
                            RecoveryLocationDiscoveryFailureCode.MissingRedirectLocation,
                            redirectChain);
                    }

                    var redirectTarget = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(current, response.Headers.Location);
                    if (!RecoveryLocationUriNormalizer.TryNormalizeHttps(
                            redirectTarget,
                            out var normalizedRedirect))
                    {
                        return RecoveryLocationDiscoveryResult.Failure(
                            RecoveryLocationDiscoveryFailureCode.InsecureRedirect,
                            redirectChain);
                    }

                    if (!allowedOrigins.Contains(
                            RecoveryLocationUriNormalizer.GetOrigin(normalizedRedirect)))
                    {
                        return RecoveryLocationDiscoveryResult.Failure(
                            RecoveryLocationDiscoveryFailureCode.UnexpectedRedirectOrigin,
                            redirectChain);
                    }

                    current = normalizedRedirect;
                    redirectChain.Add(current);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return RecoveryLocationDiscoveryResult.Failure(
                        RecoveryLocationDiscoveryFailureCode.UnsupportedResponse,
                        redirectChain);
                }

                var expectedOrigins = allowedOrigins
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var handoff = new RecoveryNavigationHandoff(
                    current,
                    RecoveryLocationUriNormalizer.GetOrigin(current),
                    expectedOrigins,
                    RecoveryLocationResolutionSource.WellKnownChangePassword,
                    RequiresVisibleConfirmation: true);
                return RecoveryLocationDiscoveryResult.Success(handoff, redirectChain);
            }
        }
    }

    private static HttpRequestMessage CreateRequest(Uri destination)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, destination);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.UserAgent.ParseAdd("unpwn-recovery-location-discovery/1.0");
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
        };
        return request;
    }

    private static RecoveryLocationDiscoveryResult CreateProviderFallbackOrFailure(
        bool providerHandoffAvailable,
        RecoveryNavigationHandoff providerHandoff,
        RecoveryLocationDiscoveryFailureCode failureCode,
        RecoveryLocationFallbackReason fallbackReason,
        IReadOnlyList<Uri> redirectChain)
    {
        if (!providerHandoffAvailable)
        {
            return RecoveryLocationDiscoveryResult.Failure(failureCode, redirectChain);
        }

        return RecoveryLocationDiscoveryResult.Success(
            providerHandoff with { Source = RecoveryLocationResolutionSource.ProviderFallback },
            redirectChain,
            fallbackReason);
    }

    private static HashSet<string> CreateAllowedOrigins(
        Uri normalizedAccountUri,
        RecoveryLocationDefinition? providerLocation)
    {
        HashSet<string> origins = new(StringComparer.OrdinalIgnoreCase)
        {
            RecoveryLocationUriNormalizer.GetOrigin(normalizedAccountUri),
        };
        if (providerLocation is null)
        {
            return origins;
        }

        foreach (var expectedOrigin in providerLocation.ExpectedOrigins)
        {
            if (RecoveryLocationUriNormalizer.TryNormalizeOrigin(
                    expectedOrigin,
                    out var normalizedOrigin))
            {
                origins.Add(normalizedOrigin);
            }
        }

        return origins;
    }

    private static RecoveryLocationDefinition? ResolveProviderLocation(
        RecoveryWorkflowDefinition workflow,
        string? providerLocationId)
    {
        if (string.IsNullOrWhiteSpace(providerLocationId))
        {
            return null;
        }

        return workflow.RecoveryLocations.FirstOrDefault(location =>
            string.Equals(location.Id, providerLocationId, StringComparison.Ordinal));
    }

    private static bool TryCreateProviderHandoff(
        RecoveryLocationDefinition? providerLocation,
        RecoveryLocationResolutionSource source,
        out RecoveryNavigationHandoff handoff)
    {
        handoff = null!;
        if (providerLocation is null ||
            !RecoveryLocationUriNormalizer.TryNormalizeHttps(
                providerLocation.Url,
                out var normalizedDestination))
        {
            return false;
        }

        var expectedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var expectedOrigin in providerLocation.ExpectedOrigins)
        {
            if (!RecoveryLocationUriNormalizer.TryNormalizeOrigin(
                    expectedOrigin,
                    out var normalizedOrigin))
            {
                return false;
            }

            expectedOrigins.Add(normalizedOrigin);
        }

        var destinationOrigin = RecoveryLocationUriNormalizer.GetOrigin(normalizedDestination);
        if (expectedOrigins.Count == 0 || !expectedOrigins.Contains(destinationOrigin))
        {
            return false;
        }

        handoff = new RecoveryNavigationHandoff(
            normalizedDestination,
            destinationOrigin,
            expectedOrigins.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            source,
            RequiresVisibleConfirmation: true);
        return true;
    }

    private static RecoveryLocationFallbackReason MapFallbackReason(
        RecoveryLocationDiscoveryFailureCode failureCode) => failureCode switch
    {
        RecoveryLocationDiscoveryFailureCode.InsecureAccountOrigin =>
            RecoveryLocationFallbackReason.InsecureAccountOrigin,
        RecoveryLocationDiscoveryFailureCode.NetworkFailure =>
            RecoveryLocationFallbackReason.NetworkFailure,
        RecoveryLocationDiscoveryFailureCode.UnsupportedResponse =>
            RecoveryLocationFallbackReason.UnsupportedResponse,
        RecoveryLocationDiscoveryFailureCode.MissingRedirectLocation =>
            RecoveryLocationFallbackReason.MissingRedirectLocation,
        RecoveryLocationDiscoveryFailureCode.InsecureRedirect =>
            RecoveryLocationFallbackReason.InsecureRedirect,
        RecoveryLocationDiscoveryFailureCode.UnexpectedRedirectOrigin =>
            RecoveryLocationFallbackReason.UnexpectedRedirectOrigin,
        RecoveryLocationDiscoveryFailureCode.RedirectLimitExceeded =>
            RecoveryLocationFallbackReason.RedirectLimitExceeded,
        _ => RecoveryLocationFallbackReason.None,
    };

    private static bool TryValidateRequest(RecoveryLocationDiscoveryRequest? request) =>
        request is not null &&
        request.Workflow is not null &&
        Enum.IsDefined(request.SelectionPolicy) &&
        (request.ProviderLocationId is null ||
            !string.IsNullOrWhiteSpace(request.ProviderLocationId)) &&
        (request.AccountUri is not null ||
            !string.IsNullOrWhiteSpace(request.ProviderLocationId));

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static int ValidateMaxRedirects(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 10);
        return value;
    }

    private static TimeSpan ValidateTimeout(TimeSpan value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        return value;
    }
}
