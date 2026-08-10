using System.Net;
using System.Net.Http.Headers;
using Unpwn.Core;

namespace Unpwn.Automation.Recovery;

public sealed class ProviderSmokeCheckService(
    HttpMessageInvoker http,
    int maxRedirects = 5,
    TimeSpan? requestTimeout = null,
    int staleAfterDays = 90,
    bool disposeInvoker = false)
    : IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    private readonly HttpMessageInvoker _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly int _maxRedirects = ValidateMaxRedirects(maxRedirects);
    private readonly TimeSpan _requestTimeout = ValidateTimeout(requestTimeout ?? DefaultRequestTimeout);
    private readonly int _staleAfterDays = ValidateStaleAfterDays(staleAfterDays);
    private readonly bool _disposeInvoker = disposeInvoker;
    private bool _disposed;

    public static ProviderSmokeCheckService CreateDefault()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        return new ProviderSmokeCheckService(
            new HttpMessageInvoker(handler, disposeHandler: true),
            disposeInvoker: true);
    }

    public async Task<ProviderSmokeCheckReport> CheckAsync(
        IReadOnlyList<RecoveryWorkflowDefinition> workflows,
        DateOnly checkedOn,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(workflows);
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<ProviderLocationSmokeCheckResult>();
        foreach (var workflow in workflows)
        {
            ArgumentNullException.ThrowIfNull(workflow);
            var stale = checkedOn.DayNumber - workflow.VerifiedAt.DayNumber > _staleAfterDays;
            foreach (var location in workflow.RecoveryLocations)
            {
                results.Add(await CheckLocationAsync(workflow, location, stale, cancellationToken));
            }
        }

        return new ProviderSmokeCheckReport(checkedOn, _staleAfterDays, results);
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

    private async Task<ProviderLocationSmokeCheckResult> CheckLocationAsync(
        RecoveryWorkflowDefinition workflow,
        RecoveryLocationDefinition location,
        bool stale,
        CancellationToken cancellationToken)
    {
        if (!TryValidateLocation(location, out var current, out var expectedOrigins))
        {
            return Result(
                workflow,
                location,
                stale,
                ProviderLocationSmokeCheckStatus.Insecure,
                null,
                [],
                "invalid-or-insecure-location");
        }

        var redirectOrigins = new List<string>
        {
            RecoveryLocationUriNormalizer.GetOrigin(current),
        };
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
                return Result(
                    workflow,
                    location,
                    stale,
                    ProviderLocationSmokeCheckStatus.Unavailable,
                    null,
                    redirectOrigins,
                    "request-timeout");
            }
            catch (HttpRequestException)
            {
                return Result(
                    workflow,
                    location,
                    stale,
                    ProviderLocationSmokeCheckStatus.Unavailable,
                    null,
                    redirectOrigins,
                    "transport-failure");
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= _maxRedirects)
                    {
                        return Result(
                            workflow,
                            location,
                            stale,
                            ProviderLocationSmokeCheckStatus.UnexpectedRedirect,
                            (int)response.StatusCode,
                            redirectOrigins,
                            "redirect-limit-exceeded");
                    }

                    if (!TryGetRedirectTarget(response.Headers, current, out var target) ||
                        !RecoveryLocationUriNormalizer.TryNormalizeHttps(target, out var normalizedTarget))
                    {
                        return Result(
                            workflow,
                            location,
                            stale,
                            ProviderLocationSmokeCheckStatus.Insecure,
                            (int)response.StatusCode,
                            redirectOrigins,
                            "invalid-or-insecure-redirect");
                    }

                    var targetOrigin = RecoveryLocationUriNormalizer.GetOrigin(normalizedTarget);
                    redirectOrigins.Add(targetOrigin);
                    if (!expectedOrigins.Contains(targetOrigin))
                    {
                        return Result(
                            workflow,
                            location,
                            stale,
                            ProviderLocationSmokeCheckStatus.UnexpectedRedirect,
                            (int)response.StatusCode,
                            redirectOrigins,
                            "unexpected-cross-origin-redirect");
                    }

                    current = normalizedTarget;
                    continue;
                }

                if (IsProviderBlocked(response.StatusCode))
                {
                    return Result(
                        workflow,
                        location,
                        stale,
                        ProviderLocationSmokeCheckStatus.ProviderBlocked,
                        (int)response.StatusCode,
                        redirectOrigins,
                        "provider-blocked-or-rate-limited");
                }

                if (response.IsSuccessStatusCode)
                {
                    return Result(
                        workflow,
                        location,
                        stale,
                        redirectCount == 0
                            ? ProviderLocationSmokeCheckStatus.Reachable
                            : ProviderLocationSmokeCheckStatus.Redirected,
                        (int)response.StatusCode,
                        redirectOrigins,
                        redirectCount == 0
                            ? "reachable"
                            : "redirected-within-expected-origins");
                }

                return Result(
                    workflow,
                    location,
                    stale,
                    ProviderLocationSmokeCheckStatus.Unavailable,
                    (int)response.StatusCode,
                    redirectOrigins,
                    "unsupported-or-unavailable-response");
            }
        }
    }

    private static HttpRequestMessage CreateRequest(Uri destination)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, destination);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.UserAgent.ParseAdd("unpwn-provider-smoke-check/1.0");
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
        };
        return request;
    }

    private static bool TryValidateLocation(
        RecoveryLocationDefinition location,
        out Uri normalizedLocation,
        out HashSet<string> expectedOrigins)
    {
        expectedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!RecoveryLocationUriNormalizer.TryNormalizeHttps(location.Url, out normalizedLocation))
        {
            return false;
        }

        foreach (var expectedOrigin in location.ExpectedOrigins)
        {
            if (!RecoveryLocationUriNormalizer.TryNormalizeOrigin(expectedOrigin, out var normalizedOrigin))
            {
                return false;
            }

            expectedOrigins.Add(normalizedOrigin);
        }

        return expectedOrigins.Count > 0 &&
            expectedOrigins.Contains(RecoveryLocationUriNormalizer.GetOrigin(normalizedLocation));
    }

    private static bool TryGetRedirectTarget(
        HttpResponseHeaders headers,
        Uri current,
        out Uri redirectTarget)
    {
        redirectTarget = null!;
        if (!headers.TryGetValues("Location", out var values))
        {
            return false;
        }

        var materialized = values.ToArray();
        if (materialized.Length != 1 ||
            string.IsNullOrWhiteSpace(materialized[0]) ||
            !Uri.TryCreate(materialized[0], UriKind.RelativeOrAbsolute, out var location))
        {
            return false;
        }

        try
        {
            redirectTarget = location.IsAbsoluteUri ? location : new Uri(current, location);
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static ProviderLocationSmokeCheckResult Result(
        RecoveryWorkflowDefinition workflow,
        RecoveryLocationDefinition location,
        bool stale,
        ProviderLocationSmokeCheckStatus status,
        int? httpStatusCode,
        IReadOnlyList<string> redirectOrigins,
        string diagnosticCode) =>
        new(
            workflow.WorkflowId,
            workflow.WorkflowVersion,
            workflow.VerifiedAt,
            stale,
            location.Id,
            SanitizeCatalogLocation(location.Url),
            status,
            httpStatusCode,
            [.. redirectOrigins],
            diagnosticCode);

    private static string SanitizeCatalogLocation(Uri location)
    {
        if (!location.IsAbsoluteUri)
        {
            return "[invalid-location]";
        }

        var builder = new UriBuilder(location)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static bool IsProviderBlocked(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.Unauthorized or
        HttpStatusCode.Forbidden or
        HttpStatusCode.ProxyAuthenticationRequired or
        HttpStatusCode.TooManyRequests;

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

    private static int ValidateStaleAfterDays(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return value;
    }
}
