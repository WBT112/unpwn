using System.Text;
using Unpwn.Application.Credentials;
using Unpwn.Core;
using Unpwn.Export.Credentials;
using Xunit;

namespace Unpwn.Export.Tests.Credentials;

public sealed class GeneratedCredentialExportPermissionTests : IDisposable
{
    private const UnixFileMode OwnerReadWrite =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "unpwn-export-permission-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PlaintextExportUsesOwnerOnlyPermissionsOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        File.SetUnixFileMode(
            _directory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupWrite |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherWrite |
            UnixFileMode.OtherExecute);
        var repository = new TestCredentialRepository();
        var selection = repository.Add("permission-secret");
        var destination = Path.Combine(_directory, "credentials.csv");
        var service = new GeneratedCredentialExportService(repository);

        var result = await service.ExportAsync(
            Request(selection, destination),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(OwnerReadWrite, File.GetUnixFileMode(destination));
        Assert.Equal(1, repository.ExportCount);
    }

    [Fact]
    public async Task MoveFailureRemovesTemporaryPlaintextAndDoesNotMarkExported()
    {
        Directory.CreateDirectory(_directory);
        var destination = Path.Combine(_directory, "existing-directory");
        Directory.CreateDirectory(destination);
        var repository = new TestCredentialRepository();
        var selection = repository.Add("move-failure-secret");
        var service = new GeneratedCredentialExportService(repository);

        var result = await service.ExportAsync(
            Request(selection, destination),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialExportFailureCode.WriteFailure, result.FailureCode);
        Assert.Equal(0, repository.ExportCount);
        Assert.Empty(Directory.GetFiles(_directory, ".unpwn-export-*.tmp"));
    }

    [Fact]
    public async Task CancellationAfterTemporaryCreationRemovesPlaintextAndDoesNotMarkExported()
    {
        Directory.CreateDirectory(_directory);
        using var cancellation = new CancellationTokenSource();
        var repository = new TestCredentialRepository
        {
            AfterSecretRead = cancellation.Cancel,
        };
        var selection = repository.Add("cancelled-write-secret");
        var destination = Path.Combine(_directory, "cancelled.csv");
        var service = new GeneratedCredentialExportService(repository);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportAsync(
                Request(selection, destination),
                cancellation.Token));

        Assert.False(File.Exists(destination));
        Assert.Equal(0, repository.ExportCount);
        Assert.Empty(Directory.GetFiles(_directory, ".unpwn-export-*.tmp"));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                _directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Directory.Delete(_directory, recursive: true);
    }

    private static CredentialExportRequest Request(
        CredentialExportSelection selection,
        string destination) =>
        new(
            Guid.NewGuid(),
            CredentialExportFormatId.GenericCsv,
            destination,
            [selection],
            PlaintextRiskAcknowledged: true);

    private sealed class TestCredentialRepository : IGeneratedCredentialRepository
    {
        private byte[]? _secret;
        private GeneratedCredentialMetadata? _metadata;

        public Action? AfterSecretRead { get; init; }

        public int ExportCount { get; private set; }

        public bool IsUnlocked => true;

        public CredentialExportSelection Add(string secret)
        {
            var credentialId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            _metadata = GeneratedCredentialMetadata.Create(
                credentialId,
                accountId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow);
            _secret = Encoding.UTF8.GetBytes(secret);
            return new CredentialExportSelection(
                new GeneratedCredentialReference(credentialId, accountId),
                "Test account",
                "user@example.test",
                "https://example.test/account");
        }

        public Task<GeneratedCredentialMetadata?> GetMetadataAsync(
            GeneratedCredentialReference reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _metadata?.CredentialId == reference.CredentialId &&
                _metadata.AccountId == reference.AccountId
                    ? _metadata
                    : null);
        }

        public Task<CredentialSecretLease?> ReadSecretAsync(
            GeneratedCredentialReference reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease =
                _metadata?.CredentialId == reference.CredentialId &&
                _metadata.AccountId == reference.AccountId &&
                _secret is not null
                    ? new CredentialSecretLease(_secret.ToArray())
                    : null;
            AfterSecretRead?.Invoke();
            return Task.FromResult(lease);
        }

        public Task<GeneratedCredentialBatchResult> MarkExportedAsync(
            IReadOnlyCollection<GeneratedCredentialReference> references,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExportCount++;
            var metadata = _metadata ?? throw new InvalidOperationException();
            metadata = metadata.MarkExported(operationId, DateTimeOffset.UtcNow);
            _metadata = metadata;
            return Task.FromResult(GeneratedCredentialBatchResult.Success([metadata]));
        }

        public Task<GeneratedCredentialCreationResult> GenerateAsync(
            Guid accountId,
            CredentialGenerationPolicy policy,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedCredentialMetadata>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GeneratedCredentialMetadata>>(
                _metadata is null ? [] : [_metadata]);

        public Task<GeneratedCredentialOperationResult> MarkUsedAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GeneratedCredentialOperationResult> ConfirmAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GeneratedCredentialOperationResult> DeleteAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GeneratedCredentialOperationResult> ConfirmPasswordManagerImportAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GeneratedCredentialOperationResult> RevokePasswordManagerImportConfirmationAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GeneratedCredentialOperationResult> PostponePasswordManagerImportConfirmationAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GeneratedCredentialOperationResult> ConfirmPlaintextExportCleanupAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
