using System.Net;
using Unpwn.Automation.Recovery;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Automation.Tests.Recovery;

public sealed class ProviderSmokeCheckServiceTests
{
    private static readonly DateOnly CheckedOn = new(2026, 8, 10);

    [Fact]
    public async Task ReachableLocationUsesBodylessSecretFreeGetAndReportsWorkflowMetadata()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var service = CreateService(handler);

        var report = await service.CheckAsync(
            [Workflow(new DateOnly(2026, 8, 1))],
            CheckedOn,
            CancellationToken.None);

        var result = Assert.Single(report.Locations);
        Assert.Equal("example.test/account-recovery", result.WorkflowId);
        Assert.Equal("1.2.3", result.WorkflowVersion);
        Assert.Equal(new DateOnly(2026, 8, 1), result.VerifiedAt);
        Assert.False(result.VerificationIsStale);
        Assert.Equal(ProviderLocationSmokeCheckStatus.Reachable, result.Status);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("reachable", result.DiagnosticCode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://example.test/recovery", request.Destination.AbsoluteUri);
        Assert.False(request.HasContent);
        Assert.False(request.HasAuthorization);
        Assert.False(request.HasCookie);
        Assert.False(request.HasReferrer);
    }

    [Fact]
    public async Task FollowsHttpsRedirectsOnlyWithinExpectedOrigins()
    {
        var handler = new RecordingHandler((_, call) => call switch
        {
            0 => Redirect("/security"),
            _ => new HttpResponseMessage(HttpStatusCode.OK),
        });
        using var service = CreateService(handler);

        var report = await service.CheckAsync(
            [Workflow(CheckedOn)],
            CheckedOn,
            CancellationToken.None);

        var result = Assert.Single(report.Locations);
        Assert.Equal(ProviderLocationSmokeCheckStatus.Redirected, result.Status);
        Assert.Equal("redirected-within-expected-origins", result.DiagnosticCode);
        Assert.Equal(["https://example.test", "https://example.test"], result.RedirectOrigins);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task UnexpectedCrossOriginRedirectStopsBeforeFollowingAndRetainsOnlyOrigins()
    {
        const string syntheticMarker = "UNPWN_TEST_SECRET_REDIRECT_QUERY";
        var handler = new RecordingHandler((_, _) =>
            Redirect($"https://login.other.test/sign-in?state={syntheticMarker}"));
        using var service = CreateService(handler);

        var report = await service.CheckAsync(
            [Workflow(CheckedOn)],
            CheckedOn,
            CancellationToken.None);
        var markdown = ProviderSmokeCheckMarkdownReporter.Render(report);

        var result = Assert.Single(report.Locations);
        Assert.Equal(ProviderLocationSmokeCheckStatus.UnexpectedRedirect, result.Status);
        Assert.Equal("unexpected-cross-origin-redirect", result.DiagnosticCode);
        Assert.Equal(["https://example.test", "https://login.other.test"], result.RedirectOrigins);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(syntheticMarker, markdown, StringComparison.Ordinal);
        Assert.Contains("Live observations are warnings", markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task DistinguishesProviderBlockingFromUnavailableLocations(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(statusCode));
        using var service = CreateService(handler);

        var report = await service.CheckAsync(
            [Workflow(CheckedOn)],
            CheckedOn,
            CancellationToken.None);

        var result = Assert.Single(report.Locations);
        Assert.Equal(ProviderLocationSmokeCheckStatus.ProviderBlocked, result.Status);
        Assert.Equal("provider-blocked-or-rate-limited", result.DiagnosticCode);
    }

    [Fact]
    public async Task TransportFailureUsesStaticIssueReadyDiagnostic()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("sensitive provider response"));
        using var service = CreateService(handler);

        var report = await service.CheckAsync(
            [Workflow(CheckedOn)],
            CheckedOn,
            CancellationToken.None);
        var markdown = ProviderSmokeCheckMarkdownReporter.Render(report);

        var result = Assert.Single(report.Locations);
        Assert.Equal(ProviderLocationSmokeCheckStatus.Unavailable, result.Status);
        Assert.Equal("transport-failure", result.DiagnosticCode);
        Assert.DoesNotContain("sensitive provider response", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsStaleVerificationIndependentlyOfReachability()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var service = CreateService(handler, staleAfterDays: 30);

        var report = await service.CheckAsync(
            [Workflow(new DateOnly(2026, 7, 1))],
            CheckedOn,
            CancellationToken.None);

        var result = Assert.Single(report.Locations);
        Assert.True(result.VerificationIsStale);
        Assert.True(result.RequiresReview);
        Assert.Equal(ProviderLocationSmokeCheckStatus.Reachable, result.Status);
        Assert.Contains("2026-07-01", ProviderSmokeCheckMarkdownReporter.Render(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsInsecureCatalogLocationWithoutSendingARequest()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var service = CreateService(handler);
        var workflow = Workflow(
            CheckedOn,
            new RecoveryLocationDefinition(
                "recovery",
                new Uri("http://example.test/recovery"),
                ["http://example.test"]));

        var report = await service.CheckAsync(
            [workflow],
            CheckedOn,
            CancellationToken.None);

        var result = Assert.Single(report.Locations);
        Assert.Equal(ProviderLocationSmokeCheckStatus.Insecure, result.Status);
        Assert.Equal("invalid-or-insecure-location", result.DiagnosticCode);
        Assert.Empty(handler.Requests);
    }

    private static ProviderSmokeCheckService CreateService(
        HttpMessageHandler handler,
        int staleAfterDays = 90) =>
        new(
            new HttpMessageInvoker(handler, disposeHandler: true),
            requestTimeout: TimeSpan.FromSeconds(2),
            staleAfterDays: staleAfterDays,
            disposeInvoker: true);

    private static RecoveryWorkflowDefinition Workflow(
        DateOnly verifiedAt,
        RecoveryLocationDefinition? location = null) =>
        new(
            "example.test/account-recovery",
            "example.test",
            "Example",
            "consumer",
            "1.2.3",
            verifiedAt,
            [location ?? new RecoveryLocationDefinition(
                "recovery",
                new Uri("https://example.test/recovery"),
                ["https://example.test"])],
            []);

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        private int _calls;

        public List<RequestSnapshot> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri!,
                request.Content is not null,
                request.Headers.Authorization is not null,
                request.Headers.Contains("Cookie"),
                request.Headers.Referrer is not null));
            return Task.FromResult(responseFactory(request, _calls++));
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri Destination,
        bool HasContent,
        bool HasAuthorization,
        bool HasCookie,
        bool HasReferrer);
}
