using System.Net;
using Unpwn.Automation.Recovery;
using Xunit;

namespace Unpwn.Automation.Tests.Recovery;

public sealed class RecoveryNetworkTargetPolicyEdgeTests
{
    [Fact]
    public async Task EmptyDnsAnswerFailsClosed()
    {
        var resolver = new CountingDnsResolver([]);
        var policy = new PublicRecoveryNetworkTargetPolicy(resolver);

        var allowed = await policy.IsAllowedAsync(
            new Uri("https://service.example/account"),
            CancellationToken.None);

        Assert.False(allowed);
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public void Ipv4MappedIpv6UsesIpv4Classification()
    {
        var privateMapped = IPAddress.Parse("::ffff:192.168.1.1");
        var publicMapped = IPAddress.Parse("::ffff:8.8.8.8");

        Assert.False(PublicRecoveryNetworkTargetPolicy.IsPubliclyRoutable(privateMapped));
        Assert.True(PublicRecoveryNetworkTargetPolicy.IsPubliclyRoutable(publicMapped));
    }

    [Fact]
    public async Task NonHttpsTargetFailsBeforeDnsResolution()
    {
        var resolver = new CountingDnsResolver([IPAddress.Parse("93.184.216.34")]);
        var policy = new PublicRecoveryNetworkTargetPolicy(resolver);

        var allowed = await policy.IsAllowedAsync(
            new Uri("http://service.example/account"),
            CancellationToken.None);

        Assert.False(allowed);
        Assert.Equal(0, resolver.CallCount);
    }

    private sealed class CountingDnsResolver(IPAddress[] addresses) : IRecoveryDnsResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<IPAddress[]> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(host);
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return new ValueTask<IPAddress[]>(addresses);
        }
    }
}
