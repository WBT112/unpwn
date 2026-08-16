using System.Net;
using Unpwn.Automation.Recovery;
using Xunit;

namespace Unpwn.Automation.Tests.Recovery;

public sealed class RecoveryNetworkSecurityRegressionTests
{
    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void PrivateLoopbackLinkLocalAndDocumentationRangesRemainBlocked()
    {
        string[] blocked =
        [
            "0.0.0.0",
            "10.0.0.1",
            "100.64.0.1",
            "127.0.0.1",
            "169.254.1.1",
            "172.16.0.1",
            "192.168.1.1",
            "224.0.0.1",
            "::",
            "::1",
            "fc00::1",
            "fe80::1",
            "ff02::1",
            "2001:db8::1",
        ];

        foreach (var address in blocked)
        {
            Assert.False(
                PublicRecoveryNetworkTargetPolicy.IsPubliclyRoutable(IPAddress.Parse(address)),
                address);
        }
    }

    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void RepresentativePublicAddressesRemainAllowed()
    {
        string[] allowed =
        [
            "1.1.1.1",
            "8.8.8.8",
            "2606:4700:4700::1111",
        ];

        foreach (var address in allowed)
        {
            Assert.True(
                PublicRecoveryNetworkTargetPolicy.IsPubliclyRoutable(IPAddress.Parse(address)),
                address);
        }
    }
}
