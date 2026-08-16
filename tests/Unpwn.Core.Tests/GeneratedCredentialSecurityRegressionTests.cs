using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class GeneratedCredentialSecurityRegressionTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void CredentialCannotBeConfirmedBeforeUseIsRecorded()
    {
        var metadata = GeneratedCredentialMetadata.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            StartedAt);

        Assert.Throws<InvalidOperationException>(() => metadata.Confirm(
            Guid.NewGuid(),
            StartedAt.AddMinutes(1)));
    }

    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void DeletedCredentialCannotReturnToAnActiveLifecycleStage()
    {
        var metadata = GeneratedCredentialMetadata.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                StartedAt)
            .Delete(Guid.NewGuid(), StartedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => metadata.MarkUsed(
            Guid.NewGuid(),
            StartedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => metadata.MarkExported(
            Guid.NewGuid(),
            StartedAt.AddMinutes(2)));
    }
}
