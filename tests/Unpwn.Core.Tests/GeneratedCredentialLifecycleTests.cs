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

    [Fact]
    public void PasswordManagerHandoffCanBePostponedConfirmedCorrectedAndCleanedUp()
    {
        var metadata = GeneratedCredentialMetadata.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                StartedAt)
            .MarkExported(Guid.NewGuid(), StartedAt.AddMinutes(1));

        metadata = metadata.PostponePasswordManagerImportConfirmation(
            Guid.NewGuid(),
            StartedAt.AddMinutes(2));
        Assert.True(metadata.IsPasswordManagerImportConfirmationPostponed);
        Assert.True(metadata.IsPlaintextExportCleanupPending);

        metadata = metadata.ConfirmPasswordManagerImport(
            Guid.NewGuid(),
            StartedAt.AddMinutes(3));
        Assert.Equal(GeneratedCredentialStage.PasswordManagerImportConfirmed, metadata.Stage);
        Assert.False(metadata.IsPasswordManagerImportConfirmationPostponed);

        metadata = metadata.RevokePasswordManagerImportConfirmation(
            Guid.NewGuid(),
            StartedAt.AddMinutes(4));
        Assert.Equal(GeneratedCredentialStage.Exported, metadata.Stage);
        Assert.Null(metadata.PasswordManagerImportConfirmedAt);

        metadata = metadata.ConfirmPlaintextExportCleanup(
            Guid.NewGuid(),
            StartedAt.AddMinutes(5));
        Assert.False(metadata.IsPlaintextExportCleanupPending);
        Assert.Equal(6, metadata.AuditEvents.Length);
        metadata.Validate();
    }

    [Fact]
    public void HandoffStateRequiresACompletedExport()
    {
        var metadata = GeneratedCredentialMetadata.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            StartedAt);

        Assert.Throws<InvalidOperationException>(() =>
            metadata.ConfirmPasswordManagerImport(Guid.NewGuid(), StartedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() =>
            metadata.PostponePasswordManagerImportConfirmation(Guid.NewGuid(), StartedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() =>
            metadata.ConfirmPlaintextExportCleanup(Guid.NewGuid(), StartedAt.AddMinutes(1)));
    }

    [Fact]
    public void ExistingPersistedAuditEnumValuesRemainStable()
    {
        Assert.Equal(0, (int)GeneratedCredentialAuditEventType.Generated);
        Assert.Equal(1, (int)GeneratedCredentialAuditEventType.Used);
        Assert.Equal(2, (int)GeneratedCredentialAuditEventType.Confirmed);
        Assert.Equal(3, (int)GeneratedCredentialAuditEventType.Exported);
        Assert.Equal(4, (int)GeneratedCredentialAuditEventType.Deleted);
    }

    [Fact]
    public void RepeatedExportReopensHandoffAndCleanupState()
    {
        var metadata = GeneratedCredentialMetadata.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                StartedAt)
            .MarkExported(Guid.NewGuid(), StartedAt.AddMinutes(1))
            .ConfirmPasswordManagerImport(Guid.NewGuid(), StartedAt.AddMinutes(2))
            .ConfirmPlaintextExportCleanup(Guid.NewGuid(), StartedAt.AddMinutes(3))
            .MarkExported(Guid.NewGuid(), StartedAt.AddMinutes(4));

        Assert.Equal(GeneratedCredentialStage.Exported, metadata.Stage);
        Assert.Null(metadata.PasswordManagerImportConfirmedAt);
        Assert.True(metadata.IsPlaintextExportCleanupPending);
        Assert.Equal(2, metadata.ExportCount);
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
