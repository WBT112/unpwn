using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class GeneratedCredentialLifecycleTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 6, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LifecycleIsLanguageNeutralAndIdempotentPerOperation()
    {
        var generatedOperation = Guid.NewGuid();
        var usedOperation = Guid.NewGuid();
        var confirmedOperation = Guid.NewGuid();
        var exportedOperation = Guid.NewGuid();
        var deletedOperation = Guid.NewGuid();
        var metadata = GeneratedCredentialMetadata.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            generatedOperation,
            StartedAt);

        metadata = metadata.MarkUsed(usedOperation, StartedAt.AddMinutes(1));
        var usedRevision = metadata.Revision;
        metadata = metadata.MarkUsed(usedOperation, StartedAt.AddMinutes(2));
        Assert.Equal(usedRevision, metadata.Revision);

        metadata = metadata.Confirm(confirmedOperation, StartedAt.AddMinutes(2));
        metadata = metadata.MarkExported(exportedOperation, StartedAt.AddMinutes(3));
        metadata = metadata.MarkExported(exportedOperation, StartedAt.AddMinutes(4));
        metadata = metadata.Delete(deletedOperation, StartedAt.AddMinutes(5));

        Assert.Equal(GeneratedCredentialStage.Deleted, metadata.Stage);
        Assert.Equal(1, metadata.ExportCount);
        Assert.Equal(5, metadata.AuditEvents.Length);
        Assert.DoesNotContain(metadata.AuditEvents, auditEvent =>
            auditEvent.ToString().Contains("password", StringComparison.OrdinalIgnoreCase));
        metadata.Validate();
    }

    [Fact]
    public void ConfirmationRequiresRecordedUse()
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
    public void DeletedCredentialCannotBeMutated()
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

    [Theory]
    [InlineData(11)]
    [InlineData(129)]
    public void GenerationPolicyRejectsUnsafeLengths(int length)
    {
        var policy = CredentialGenerationPolicy.Default with { Length = length };

        Assert.Throws<ArgumentOutOfRangeException>(policy.Validate);
    }
}
