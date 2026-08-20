using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Core.Tests.Recovery.Workflows;

public sealed class RepositoryRecoveryBrowserEntryCatalogTests
{
    [Fact]
    public void BitwardenEntryUsesOnlyRepositoryReviewedHttpsOrigins()
    {
        var entry = RepositoryRecoveryBrowserEntryCatalog.Resolve("bitwarden.com");

        Assert.NotNull(entry);
        Assert.Equal("bitwarden", entry.ProviderId);
        Assert.Equal("https://vault.bitwarden.com/", entry.Location.Url.AbsoluteUri);
        Assert.Equal(
            ["https://vault.bitwarden.com", "https://vault.bitwarden.eu"],
            entry.Location.ExpectedOrigins);
        Assert.All(entry.Location.ExpectedOrigins, origin =>
        {
            Assert.True(Uri.TryCreate(origin, UriKind.Absolute, out var uri));
            Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
            Assert.Equal(origin, uri.GetLeftPart(UriPartial.Authority));
        });
    }

    [Fact]
    public void UnknownProviderCannotCreateAReviewedEntryFromImportedMetadata()
    {
        Assert.Null(RepositoryRecoveryBrowserEntryCatalog.Resolve("vault.attacker.example"));
        Assert.Null(RepositoryRecoveryBrowserEntryCatalog.Resolve(""));
    }
}
