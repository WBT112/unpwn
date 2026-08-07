using System.Text;
using Unpwn.Application.Credentials;
using Unpwn.Core;
using Unpwn.Export.Credentials;
using Xunit;

namespace Unpwn.Export.Tests.Credentials;

public sealed class GeneratedCredentialExportServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "unpwn-export-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PlaintextExportRequiresExplicitRiskAcknowledgement()
    {
        var repository = new TestCredentialRepository();
        var selection = repository.Add("selected-secret");
        var service = new GeneratedCredentialExportService(repository);
        var destination = Destination("credentials.csv");

        var result = await service.ExportAsync(
            Request(selection, destination, acknowledged: false),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialExportFailureCode.RiskAcknowledgementRequired, result.FailureCode);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task GenericCsvContainsOnlyExplicitlySelectedCredential()
    {
        var repository = new TestCredentialRepository();
        var selected = repository.Add("selected,secret");
        _ = repository.Add("unselected-secret");
        var service = new GeneratedCredentialExportService(repository);
        var destination = Destination("credentials.csv");

        var result = await service.ExportAsync(
            Request(selected, destination),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var file = await File.ReadAllTextAsync(destination);
        Assert.Contains("credential_id,name,login,uri,password", file, StringComparison.Ordinal);
        Assert.Contains("\"selected,secret\"", file, StringComparison.Ordinal);
        Assert.DoesNotContain("unselected-secret", file, StringComparison.Ordinal);
        Assert.Equal(1, repository.Metadata[selected.Reference.CredentialId].ExportCount);
    }

    [Fact]
    public async Task BitwardenCsvUsesSupportedLoginSchema()
    {
        var repository = new TestCredentialRepository();
        var selection = repository.Add("bitwarden-secret");
        var service = new GeneratedCredentialExportService(repository);
        var destination = Destination("bitwarden.csv");
        var request = Request(selection, destination) with
        {
            Format = CredentialExportFormatId.BitwardenCsv,
        };

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        var file = await File.ReadAllTextAsync(destination);
        Assert.StartsWith(
            "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp",
            file,
            StringComparison.Ordinal);
        Assert.Contains("bitwarden-secret", file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSelectedCredentialPreventsPartialFile()
    {
        var repository = new TestCredentialRepository();
        var selection = repository.Add("available-secret");
        var missing = new CredentialExportSelection(
            new GeneratedCredentialReference(Guid.NewGuid(), Guid.NewGuid()),
            "Missing",
            null,
            null);
        var service = new GeneratedCredentialExportService(repository);
        var destination = Destination("partial.csv");
        var request = new CredentialExportRequest(
            Guid.NewGuid(),
            CredentialExportFormatId.GenericCsv,
            destination,
            [selection, missing],
            PlaintextRiskAcknowledged: true);

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialExportFailureCode.CredentialUnavailable, result.FailureCode);
        Assert.False(File.Exists(destination));
        Assert.Equal(0, repository.Metadata[selection.Reference.CredentialId].ExportCount);
    }

    [Fact]
    public async Task ExistingDestinationIsNeverOverwritten()
    {
        var repository = new TestCredentialRepository();
        var selection = repository.Add("new-secret");
        var service = new GeneratedCredentialExportService(repository);
        var destination = Destination("existing.csv");
        await File.WriteAllTextAsync(destination, "existing-content");

        var result = await service.ExportAsync(
            Request(selection, destination),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialExportFailureCode.DestinationExists, result.FailureCode);
        Assert.Equal("existing-content", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task StateFailureAfterFileCreationIsReportedPrecisely()
    {
        var repository = new TestCredentialRepository
        {
            FailExportStateUpdate = true,
        };
        var selection = repository.Add("file-created-secret");
        var service = new GeneratedCredentialExportService(repository);
        var destination = Destination("state-failure.csv");

        var result = await service.ExportAsync(
            Request(selection, destination),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.FileCreated);
        Assert.Equal(
            CredentialExportFailureCode.StateUpdateFailedAfterFileCreation,
            result.FailureCode);
        Assert.True(File.Exists(destination));
        Assert.Contains(
            "file-created-secret",
            await File.ReadAllTextAsync(destination),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedCompletedOperationDoesNotCreateAnotherFile()
    {
        var repository = new TestCredentialRepository();
        var selection = repository.Add("repeated-secret");
        var service = new GeneratedCredentialExportService(repository);
        var operationId = Guid.NewGuid();
        var firstDestination = Destination("first.csv");
        var firstRequest = Request(selection, firstDestination) with { OperationId = operationId };
        var first = await service.ExportAsync(firstRequest, CancellationToken.None);
        var secondDestination = Destination("second.csv");
        var secondRequest = firstRequest with { DestinationPath = secondDestination };

        var repeated = await service.ExportAsync(secondRequest, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.False(repeated.Succeeded);
        Assert.Equal(CredentialExportFailureCode.AlreadyCompleted, repeated.FailureCode);
        Assert.False(File.Exists(secondDestination));
        Assert.Equal(1, repository.Metadata[selection.Reference.CredentialId].ExportCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CredentialExportRequest Request(
        CredentialExportSelection selection,
        string destination,
        bool acknowledged = true) =>
        new(
            Guid.NewGuid(),
            CredentialExportFormatId.GenericCsv,
            destination,
            [selection],
            acknowledged);

    private string Destination(string name)
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, name);
    }

    private sealed class TestCredentialRepository : IGeneratedCredentialRepository
    {
        private readonly Dictionary<Guid, byte[]> _secrets = [];

        public Dictionary<Guid, GeneratedCredentialMetadata> Metadata { get; } = [];

        public bool FailExportStateUpdate { get; init; }

        public bool IsUnlocked => true;

        public CredentialExportSelection Add(string secret)
        {
            var credentialId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            Metadata[credentialId] = GeneratedCredentialMetadata.Create(
                credentialId,
                accountId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow);
            _secrets[credentialId] = Encoding.UTF8.GetBytes(secret);
            return new CredentialExportSelection(
                new GeneratedCredentialReference(credentialId, accountId),
                $"Account {Metadata.Count}",
                $"user{Metadata.Count}@example.test",
                "https://example.test/account");
        }

        public Task<GeneratedCredentialCreationResult> GenerateAsync(
            Guid accountId,
            CredentialGenerationPolicy policy,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedCredentialMetadata>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GeneratedCredentialMetadata>>([.. Metadata.Values]);

        public Task<GeneratedCredentialMetadata?> GetMetadataAsync(
            GeneratedCredentialReference reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Metadata.TryGetValue(reference.CredentialId, out var metadata);
            return Task.FromResult(metadata?.AccountId == reference.AccountId ? metadata : null);
        }

        public Task<CredentialSecretLease?> ReadSecretAsync(
            GeneratedCredentialReference reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Metadata.TryGetValue(reference.CredentialId, out var metadata) &&
                metadata.AccountId == reference.AccountId &&
                !metadata.IsDeleted &&
                _secrets.TryGetValue(reference.CredentialId, out var secret)
                    ? new CredentialSecretLease(secret.ToArray())
                    : null);
        }

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

        public Task<GeneratedCredentialBatchResult> MarkExportedAsync(
            IReadOnlyCollection<GeneratedCredentialReference> references,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailExportStateUpdate)
            {
                return Task.FromResult(GeneratedCredentialBatchResult.Failure(
                    GeneratedCredentialFailureCode.PersistenceFailure));
            }

            var occurredAt = DateTimeOffset.UtcNow;
            var updated = references.Select(reference =>
            {
                var metadata = Metadata[reference.CredentialId]
                    .MarkExported(operationId, occurredAt);
                Metadata[reference.CredentialId] = metadata;
                return metadata;
            }).ToArray();
            return Task.FromResult(GeneratedCredentialBatchResult.Success(updated));
        }

        public Task<GeneratedCredentialOperationResult> DeleteAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
