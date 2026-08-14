using System.Net;
using System.Net.Http.Headers;
using Unpwn.Application.Recovery;
using Unpwn.Core;

namespace Unpwn.Automation.Recovery;

public sealed class HttpRecoveryLocationDiscoveryService(
    HttpMessageInvoker http,
    int maxRedirects = 5,
    TimeSpan? requestTimeout = null,
    bool disposeInvoker = false,
    IRecoveryNetworkTargetPolicy? networkTargetPolicy = null)
    : IRecoveryLocationDiscoveryService, IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpMessageInvoker _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly int _maxRedirects = ValidateMaxRedirects(maxRedirects);
    private readonly TimeSpan _requestTimeout = ValidateTimeout(requestTimeout ?? DefaultRequestTimeout);
    private readonly bool _disposeInvoker = disposeInvoker;
    private readonly IRecoveryNetworkTargetPolicy _networkTargetPolicy =
        networkTargetPolicy ?? PublicRecoveryNetworkTargetPolicy.CreateDefault();
    private bool _disposed;

    public static HttpRecoveryLocationDiscoveryService CreateDefault()
    {
        var resolver = new SystemRecoveryDnsResolver();
        var networkTargetPolicy = new PublicRecoveryNetworkTargetPolicy(resolver);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = (context, cancellationToken) =>
                RecoveryDiscoveryPublicConnector.ConnectAsync(
                    context,
                    resolver,
                    cancellationToken),
        };
        return new HttpRecoveryLocationDiscoveryService(
            new HttpMessageInvoker(handler, disposeHandler: true),
            disposeInvoker: true,
            networkTargetPolicy: networkTargetPolicy);
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

        if (request.SelectionPolicy == RecoveryLocationSelectionPolicy.AccountOriginOnly)
        {
            return await CreateAccountOriginHandoffAsync(
                normalizedAccountUri,
                cancellationToken);
        }

        var allowedOrigins = CreateAllowedOrigins(
            normalizedAccountUri,
            providerHandoffAvailable ? providerHandoff.ExpectedOrigins : []);
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
            wellKnownResult.RedirectOrigins);
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

    private async Task<RecoveryLocationDiscoveryResult> CreateAccountOriginHandoffAsync(
        Uri accountUri,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);

        bool networkTargetAllowed;
        try
        {
            networkTargetAllowed = await _networkTargetPolicy.IsAllowedAsync(
                accountUri,
                timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return RecoveryLocationDiscoveryResult.Failure(
                RecoveryLocationDiscoveryFailureCode.NetworkFailure);
        }

        if (!networkTargetAllowed)
        {
            return RecoveryLocationDiscoveryResult.Failure(
                RecoveryLocationDiscoveryFailureCode.UnsafeNetworkTarget);
        }

        var origin = RecoveryLocationUriNormalizer.GetOrigin(accountUri);
        return RecoveryLocationDiscoveryResult.Success(
            new RecoveryNavigationHandoff(
                accountUri,
                origin,
                [origin],
                RecoveryLocationResolutionSource.AccountOrigin,
                RequiresVisibleConfirmation: true));
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
            bool networkTargetAllowed;
            try
            {
                networkTargetAllowed = await _networkTargetPolicy.IsAllowedAsync(
                    current,
                    timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return Failure(
                    RecoveryLocationDiscoveryFailureCode.NetworkFailure,
                    redirectChain);
            }

            if (!networkTargetAllowed)
            {
                return Failure(
                    RecoveryLocationDiscoveryFailureCode.NetworkFailure,
                    redirectChain);
            }

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
                return Failure(
                    RecoveryLocationDiscoveryFailureCode.NetworkFailure,
                    redirectChain);
            }
            catch (HttpRequestException)
            {
                return Failure(
                    RecoveryLocationDiscoveryFailureCode.NetworkFailure,
                    redirectChain);
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= _maxRedirects)
                    {
                        return Failure(
                            RecoveryLocationDiscoveryFailureCode.RedirectLimitExceeded,
                            redirectChain);
                    }

                    if (!TryGetRedirectTarget(
                            response.Headers,
                            current,
                            out var redirectTarget,
                            out var redirectFailure))
                    {
                        return Failure(redirectFailure, redirectChain);
                    }

                    if (!RecoveryLocationUriNormalizer.TryNormalizeHttps(
                            redirectTarget,
                            out var normalizedRedirect))
                    {
                        return Failure(
                            RecoveryLocationDiscoveryFailureCode.InsecureRedirect,
                            redirectChain);
                    }

                    if (!allowedOrigins.Contains(
                            RecoveryLocationUriNormalizer.GetOrigin(normalizedRedirect)))
                    {
                        return Failure(
                            RecoveryLocationDiscoveryFailureCode.UnexpectedRedirectOrigin,
                            redirectChain);
                    }

                    current = normalizedRedirect;
                    redirectChain.Add(current);
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return Failure(
                        RecoveryLocationDiscoveryFailureCode.UnsupportedResponse,
                        redirectChain);
                }

                string[] expectedOrigins =
                [
                    .. allowedOrigins.Order(StringComparer.OrdinalIgnoreCase),
                ];
                var handoff = new RecoveryNavigationHandoff(
                    current,
                    RecoveryLocationUriNormalizer.GetOrigin(current),
                    expectedOrigins,
                    RecoveryLocationResolutionSource.WellKnownChangePassword,
                    RequiresVisibleConfirmation: true);
                return RecoveryLocationDiscoveryResult.Success(
                    handoff,
                    RecoveryLocationUriNormalizer.SanitizeOrigins(redirectChain));
            }
        }
    }

    private static bool TryGetRedirectTarget(
        HttpResponseHeaders headers,
        Uri current,
        out Uri redirectTarget,
        out RecoveryLocationDiscoveryFailureCode failureCode)
    {
        redirectTarget = null!;
        failureCode = RecoveryLocationDiscoveryFailureCode.None;
        if (!headers.TryGetValues("Location", out var values))
        {
            failureCode = RecoveryLocationDiscoveryFailureCode.MissingRedirectLocation;
            return false;
        }

        var materialized = values.ToArray();
        if (materialized.Length != 1 || string.IsNullOrWhiteSpace(materialized[0]) ||
            !Uri.TryCreate(materialized[0], UriKind.RelativeOrAbsolute, out var location))
        {
            failureCode = RecoveryLocationDiscoveryFailureCode.InvalidRedirectLocation;
            return false;
        }

        try
        {
            redirectTarget = location.IsAbsoluteUri ? location : new Uri(current, location);
            return true;
        }
        catch (UriFormatException)
        {
            failureCode = RecoveryLocationDiscoveryFailureCode.InvalidRedirectLocation;
            return false;
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
        IReadOnlyList<string> redirectOrigins)
    {
        if (!providerHandoffAvailable)
        {
            return RecoveryLocationDiscoveryResult.Failure(failureCode, redirectOrigins);
        }

        return RecoveryLocationDiscoveryResult.Success(
            providerHandoff with { Source = RecoveryLocationResolutionSource.ProviderFallback },
            redirectOrigins,
            fallbackReason);
    }

    private static RecoveryLocationDiscoveryResult Failure(
        RecoveryLocationDiscoveryFailureCode failureCode,
        IReadOnlyList<Uri> redirectChain) =>
        RecoveryLocationDiscoveryResult.Failure(
            failureCode,
            RecoveryLocationUriNormalizer.SanitizeOrigins(redirectChain));

    private static HashSet<string> CreateAllowedOrigins(
        Uri normalizedAccountUri,
        IReadOnlyList<string> providerExpectedOrigins)
    {
        HashSet<string> origins = new(StringComparer.OrdinalIgnoreCase)
        {
            RecoveryLocationUriNormalizer.GetOrigin(normalizedAccountUri),
        };
        foreach (var expectedOrigin in providerExpectedOrigins)
        {
            origins.Add(expectedOrigin);
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

        string[] normalizedExpectedOrigins =
        [
            .. expectedOrigins.Order(StringComparer.OrdinalIgnoreCase),
        ];
        handoff = new RecoveryNavigationHandoff(
            normalizedDestination,
            destinationOrigin,
            normalizedExpectedOrigins,
            source,
            RequiresVisibleConfirmation: true);
        return true;
    }

    private static RecoveryLocationFallbackReason MapFallbackReason(
        RecoveryLocationDiscoveryFailureCode failureCode)
    {
        return failureCode switch
        {
            RecoveryLocationDiscoveryFailureCode.InsecureAccountOrigin =>
                RecoveryLocationFallbackReason.InsecureAccountOrigin,
            RecoveryLocationDiscoveryFailureCode.NetworkFailure =>
                RecoveryLocationFallbackReason.NetworkFailure,
            RecoveryLocationDiscoveryFailureCode.UnsupportedResponse =>
                RecoveryLocationFallbackReason.UnsupportedResponse,
            RecoveryLocationDiscoveryFailureCode.MissingRedirectLocation =>
                RecoveryLocationFallbackReason.MissingRedirectLocation,
            RecoveryLocationDiscoveryFailureCode.InvalidRedirectLocation =>
                RecoveryLocationFallbackReason.InvalidRedirectLocation,
            RecoveryLocationDiscoveryFailureCode.InsecureRedirect =>
                RecoveryLocationFallbackReason.InsecureRedirect,
            RecoveryLocationDiscoveryFailureCode.UnexpectedRedirectOrigin =>
                RecoveryLocationFallbackReason.UnexpectedRedirectOrigin,
            RecoveryLocationDiscoveryFailureCode.RedirectLimitExceeded =>
                RecoveryLocationFallbackReason.RedirectLimitExceeded,
            _ => RecoveryLocationFallbackReason.None,
        };
    }

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
