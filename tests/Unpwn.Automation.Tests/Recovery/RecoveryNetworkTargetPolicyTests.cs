using System.Net;
using Unpwn.Application.Recovery;
using Unpwn.Automation.Recovery;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Automation.Tests.Recovery;

public sealed class RecoveryNetworkTargetPolicyTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    public void NonPublicAddressRangesAreRejected(string address)
    {
        Assert.False(PublicRecoveryNetworkTargetPolicy.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void PublicAddressRangesAreAccepted(string address)
    {
        Assert.True(PublicRecoveryNetworkTargetPolicy.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task LocalhostNameIsRejectedWithoutDnsResolution()
    {
        var resolver = new StaticDnsResolver([]);
        var policy = new PublicRecoveryNetworkTargetPolicy(resolver);

        var allowed = await policy.IsAllowedAsync(
            new Uri("https://localhost/account"),
            CancellationToken.None);

        Assert.False(allowed);
        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task DnsTargetResolvingToPrivateAddressIsRejected()
    {
        var resolver = ResolverFor(("service.example", "10.20.30.40"));
        var policy = new PublicRecoveryNetworkTargetPolicy(resolver);

        var allowed = await policy.IsAllowedAsync(
            new Uri("https://service.example/account"),
            CancellationToken.None);

        Assert.False(allowed);
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public async Task MixedPublicAndPrivateDnsAnswersAreRejected()
    {
        var resolver = new StaticDnsResolver(new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["service.example"] = [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("192.168.10.5")],
        });
        var policy = new PublicRecoveryNetworkTargetPolicy(resolver);

        var allowed = await policy.IsAllowedAsync(
            new Uri("https://service.example/account"),
            CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task PublicDnsTargetIsAllowedWithoutContactingTheNetwork()
    {
        var resolver = ResolverFor(("service.example", "93.184.216.34"));
        var policy = new PublicRecoveryNetworkTargetPolicy(resolver);

        var allowed = await policy.IsAllowedAsync(
            new Uri("https://service.example/account"),
            CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task LiteralPrivateTargetStopsBeforeHttpHandler()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var policy = new PublicRecoveryNetworkTargetPolicy(new StaticDnsResolver([]));
        using var service = CreateService(handler, policy);

        var result = await service.DiscoverAsync(
            new RecoveryLocationDiscoveryRequest(
                CreateWorkflow([]),
                ProviderLocationId: null,
                new Uri("https://127.0.0.1/account")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RecoveryLocationDiscoveryFailureCode.NetworkFailure, result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RedirectTargetIsRevalidatedBeforeSecondRequest()
    {
        var resolver = new StaticDnsResolver(new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["service.example"] = [IPAddress.Parse("93.184.216.34")],
            ["accounts.service.example"] = [IPAddress.Parse("10.0.0.7")],
        });
        var policy = new PublicRecoveryNetworkTargetPolicy(resolver);
        var handler = new RecordingHandler(request => request.RequestUri?.Host switch
        {
            "service.example" => Redirect("https://accounts.service.example/change-password"),
            _ => new HttpResponseMessage(HttpStatusCode.OK),
        });
        var location = new RecoveryLocationDefinition(
            "settings",
            new Uri("https://service.example/security"),
            ["https://service.example", "https://accounts.service.example"]);
        var workflow = CreateWorkflow([location]);
        using var service = CreateService(handler, policy);

        var result = await service.DiscoverAsync(
            new RecoveryLocationDiscoveryRequest(
                workflow,
                location.Id,
                new Uri("https://service.example/account"),
                RecoveryLocationSelectionPolicy.WellKnownFirst),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryLocationResolutionSource.ProviderFallback, result.Handoff?.Source);
        Assert.Equal(RecoveryLocationFallbackReason.NetworkFailure, result.FallbackReason);
        Assert.Single(handler.Requests);
        Assert.Equal("service.example", handler.Requests[0].Host);
    }

    private static HttpRecoveryLocationDiscoveryService CreateService(
        HttpMessageHandler handler,
        IRecoveryNetworkTargetPolicy policy) =>
        new(
            new HttpMessageInvoker(handler, disposeHandler: true),
            disposeInvoker: true,
            networkTargetPolicy: policy);

    private static RecoveryWorkflowDefinition CreateWorkflow(
        IReadOnlyList<RecoveryLocationDefinition> locations) =>
        new(
            "synthetic.test/recovery",
            "synthetic.test",
            "Synthetic Provider",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 13),
            locations,
            []);

    private static StaticDnsResolver ResolverFor(params (string Host, string Address)[] entries) =>
        new(entries.ToDictionary(
            entry => entry.Host,
            entry => new[] { IPAddress.Parse(entry.Address) },
            StringComparer.OrdinalIgnoreCase));

    private static HttpResponseMessage Redirect(string destination)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(destination, UriKind.Absolute);
        return response;
    }

    private sealed class StaticDnsResolver(Dictionary<string, IPAddress[]> answers) : IRecoveryDnsResolver
    {
        private readonly Dictionary<string, IPAddress[]> _answers = answers;

        public int CallCount { get; private set; }

        public ValueTask<IPAddress[]> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return new ValueTask<IPAddress[]>(
                _answers.TryGetValue(host, out var addresses) ? addresses : []);
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(responder(request));
        }
    }
}
