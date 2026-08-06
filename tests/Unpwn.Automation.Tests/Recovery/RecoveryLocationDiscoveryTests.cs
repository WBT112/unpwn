using System.Net;
using Unpwn.Application.Recovery;
using Unpwn.Automation.Recovery;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Automation.Tests.Recovery;

public sealed class RecoveryLocationDiscoveryTests
{
    [Fact]
    public async Task ProviderDefinedFirstReturnsReviewedLocationWithoutNetworkRequest()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Network access was not expected."));
        using var service = CreateService(handler);
        var request = CreateRequest(
            RecoveryLocationSelectionPolicy.ProviderDefinedFirst,
            accountUri: new Uri("https://github.com/settings/profile"));

        var result = await service.DiscoverAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryLocationResolutionSource.ProviderDefined, result.Handoff?.Source);
        Assert.Equal(new Uri("https://github.com/settings/security"), result.Handoff?.Destination);
        Assert.Equal("https://github.com", result.Handoff?.ExpectedOrigin);
        Assert.True(result.Handoff?.RequiresVisibleConfirmation);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task WellKnownRedirectWithinExpectedOriginIsAcceptedWithSanitizedTrace()
    {
        var handler = new RecordingHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/.well-known/change-password" => Redirect("/settings/password?temporary=value"),
            "/settings/password" => Success(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.WellKnownFirst,
                accountUri: new Uri("https://github.com/account?ignored=value")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryLocationResolutionSource.WellKnownChangePassword, result.Handoff?.Source);
        Assert.Equal(new Uri("https://github.com/settings/password?temporary=value"), result.Handoff?.Destination);
        Assert.Equal(1, result.RedirectCount);
        Assert.Equal(["https://github.com", "https://github.com"], result.RedirectOrigins);
        Assert.DoesNotContain("temporary", string.Join('|', result.RedirectOrigins), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitlyExpectedCrossOriginRedirectIsAccepted()
    {
        var location = new RecoveryLocationDefinition(
            "settings",
            new Uri("https://accounts.example.test/security"),
            ["https://example.test", "https://accounts.example.test"]);
        var handler = new RecordingHandler(request => request.RequestUri?.Host switch
        {
            "example.test" => Redirect("https://accounts.example.test/change-password"),
            "accounts.example.test" => Success(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.WellKnownFirst,
                accountUri: new Uri("https://example.test/profile"),
                location: location),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new Uri("https://accounts.example.test/change-password"), result.Handoff?.Destination);
        Assert.Equal("https://accounts.example.test", result.Handoff?.ExpectedOrigin);
        Assert.Equal(["https://example.test", "https://accounts.example.test"], result.RedirectOrigins);
    }

    [Fact]
    public async Task UnexpectedCrossOriginRedirectUsesReviewedProviderFallback()
    {
        var handler = new RecordingHandler(_ => Redirect("https://attacker.example/change-password?token=discarded"));
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.WellKnownFirst,
                accountUri: new Uri("https://github.com/account")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryLocationResolutionSource.ProviderFallback, result.Handoff?.Source);
        Assert.Equal(new Uri("https://github.com/settings/security"), result.Handoff?.Destination);
        Assert.Equal(RecoveryLocationFallbackReason.UnexpectedRedirectOrigin, result.FallbackReason);
        Assert.Equal(["https://github.com"], result.RedirectOrigins);
    }

    [Fact]
    public async Task InsecureRedirectUsesReviewedProviderFallback()
    {
        var handler = new RecordingHandler(_ => Redirect("http://github.com/settings/password"));
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.WellKnownFirst,
                accountUri: new Uri("https://github.com/account")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryLocationResolutionSource.ProviderFallback, result.Handoff?.Source);
        Assert.Equal(RecoveryLocationFallbackReason.InsecureRedirect, result.FallbackReason);
    }

    [Fact]
    public async Task MissingProviderFallbackReturnsStructuredFailure()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var service = CreateService(handler);
        var request = new RecoveryLocationDiscoveryRequest(
            CreateWorkflow([]),
            ProviderLocationId: null,
            new Uri("https://example.test/account"));

        var result = await service.DiscoverAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Handoff);
        Assert.Equal(RecoveryLocationDiscoveryFailureCode.UnsupportedResponse, result.FailureCode);
    }

    [Fact]
    public async Task MissingRedirectLocationReturnsProviderFallbackReason()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Found));
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.WellKnownFirst,
                accountUri: new Uri("https://github.com/account")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryLocationFallbackReason.MissingRedirectLocation, result.FallbackReason);
    }

    [Fact]
    public async Task InvalidRedirectLocationReturnsProviderFallbackReason()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            _ = response.Headers.TryAddWithoutValidation("Location", "https://[");
            return response;
        });
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.WellKnownFirst,
                accountUri: new Uri("https://github.com/account")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryLocationFallbackReason.InvalidRedirectLocation, result.FallbackReason);
    }

    [Fact]
    public async Task RedirectLimitIsEnforcedBeforeFurtherNavigation()
    {
        var handler = new RecordingHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/.well-known/change-password" => Redirect("/first"),
            "/first" => Redirect("/second"),
            _ => Success(),
        });
        using var service = CreateService(handler, maxRedirects: 1);

        var result = await service.DiscoverAsync(
            new RecoveryLocationDiscoveryRequest(
                CreateWorkflow([]),
                ProviderLocationId: null,
                new Uri("https://example.test/account")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RecoveryLocationDiscoveryFailureCode.RedirectLimitExceeded, result.FailureCode);
        Assert.Equal(1, result.RedirectCount);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DiscoveryRequestTransmitsNoCredentialsOrAccountPathData()
    {
        const string discardedQueryValue = "discarded-token-value";
        var handler = new RecordingHandler(_ => Success());
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            new RecoveryLocationDiscoveryRequest(
                CreateWorkflow([]),
                ProviderLocationId: null,
                new Uri($"https://example.test/private/account?reset_token={discardedQueryValue}")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var observed = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, observed.Method);
        Assert.Equal(new Uri("https://example.test/.well-known/change-password"), observed.RequestUri);
        Assert.False(observed.HasAuthorization);
        Assert.False(observed.HasCookie);
        Assert.False(observed.HasReferrer);
        Assert.False(observed.HasContent);
        Assert.DoesNotContain(discardedQueryValue, observed.RequestUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoContentIsNotAcceptedAsRecoveryPage()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            new RecoveryLocationDiscoveryRequest(
                CreateWorkflow([]),
                ProviderLocationId: null,
                new Uri("https://example.test/account")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RecoveryLocationDiscoveryFailureCode.UnsupportedResponse, result.FailureCode);
    }

    [Fact]
    public async Task InsecureProviderLocationFailsClosed()
    {
        var location = new RecoveryLocationDefinition(
            "settings",
            new Uri("http://example.test/security"),
            ["http://example.test"]);
        var handler = new RecordingHandler(_ => Success());
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.ProviderDefinedOnly,
                accountUri: null,
                location: location),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RecoveryLocationDiscoveryFailureCode.ProviderLocationInvalid, result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task InvalidProviderDefinitionCannotExpandRedirectAllowlist()
    {
        var location = new RecoveryLocationDefinition(
            "settings",
            new Uri("http://example.test/security"),
            ["https://attacker.example"]);
        var handler = new RecordingHandler(_ => Redirect("https://attacker.example/change-password"));
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.WellKnownFirst,
                accountUri: new Uri("https://example.test/account"),
                location: location),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Handoff);
        Assert.Equal(RecoveryLocationDiscoveryFailureCode.UnexpectedRedirectOrigin, result.FailureCode);
    }

    [Fact]
    public async Task ProviderOriginWithFragmentFailsClosed()
    {
        var location = new RecoveryLocationDefinition(
            "settings",
            new Uri("https://example.test/security"),
            ["https://example.test#decorated"]);
        var handler = new RecordingHandler(_ => Success());
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.ProviderDefinedOnly,
                accountUri: null,
                location: location),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RecoveryLocationDiscoveryFailureCode.ProviderLocationInvalid, result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NetworkFailureFallsBackWithoutExposingExceptionDetails()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("sensitive transport detail"));
        using var service = CreateService(handler);

        var result = await service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.WellKnownFirst,
                accountUri: new Uri("https://github.com/account")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryLocationResolutionSource.ProviderFallback, result.Handoff?.Source);
        Assert.Equal(RecoveryLocationFallbackReason.NetworkFailure, result.FallbackReason);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedIntoFallback()
    {
        var handler = new RecordingHandler(_ => Success());
        using var service = CreateService(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.DiscoverAsync(
            CreateRequest(
                RecoveryLocationSelectionPolicy.WellKnownFirst,
                accountUri: new Uri("https://github.com/account")),
            cancellation.Token));
    }

    private static HttpRecoveryLocationDiscoveryService CreateService(
        HttpMessageHandler handler,
        int maxRedirects = 5) =>
        new(new HttpMessageInvoker(handler, disposeHandler: true), maxRedirects, disposeInvoker: true);

    private static RecoveryLocationDiscoveryRequest CreateRequest(
        RecoveryLocationSelectionPolicy selectionPolicy,
        Uri? accountUri,
        RecoveryLocationDefinition? location = null)
    {
        location ??= new RecoveryLocationDefinition(
            "settings",
            new Uri("https://github.com/settings/security"),
            ["https://github.com"]);
        return new RecoveryLocationDiscoveryRequest(
            CreateWorkflow([location]),
            location.Id,
            accountUri,
            selectionPolicy);
    }

    private static RecoveryWorkflowDefinition CreateWorkflow(
        IReadOnlyList<RecoveryLocationDefinition> locations) =>
        new(
            "synthetic.test/recovery",
            "synthetic.test",
            "Synthetic Provider",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 6),
            locations,
            []);

    private static HttpResponseMessage Redirect(
        string destination,
        HttpStatusCode statusCode = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.Location = new Uri(destination, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Success() => new(HttpStatusCode.OK);

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<ObservedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new ObservedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization is not null,
                request.Headers.Contains("Cookie"),
                request.Headers.Referrer is not null,
                request.Content is not null));
            return Task.FromResult(responder(request));
        }
    }

    private sealed record ObservedRequest(
        HttpMethod Method,
        Uri RequestUri,
        bool HasAuthorization,
        bool HasCookie,
        bool HasReferrer,
        bool HasContent);
}
