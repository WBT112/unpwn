using Unpwn.App.Services;
using Unpwn.Application.Credentials;
using Unpwn.Application.Recovery;
using Unpwn.Core;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoveryBrowserCredentialInsertionCoordinatorTests
{
    [Fact]
    public async Task DeniedAuthorizationDoesNotInspectOrReadCredential()
    {
        var repository = new TestCredentialRepository();
        var coordinator = new RecoveryBrowserCredentialInsertionCoordinator(repository);
        var inspectCalls = 0;
        var insertCalls = 0;

        var outcome = await coordinator.ExecuteAsync(
            repository.Metadata.Reference,
            Contract(),
            _ => Task.FromResult(false),
            (_, _) =>
            {
                inspectCalls++;
                return Task.FromResult(RecoveryBrowserCredentialAssistanceResult.Ready);
            },
            (_, _, _) =>
            {
                insertCalls++;
                return Task.FromResult(RecoveryBrowserCredentialAssistanceResult.Inserted);
            },
            CancellationToken.None);

        Assert.Equal(
            RecoveryBrowserCredentialInsertionOutcomeCode.AuthorizationDenied,
            outcome.Code);
        Assert.Equal(0, inspectCalls);
        Assert.Equal(0, repository.ReadSecretCalls);
        Assert.Equal(0, insertCalls);
        Assert.Equal(0, repository.MarkUsedCalls);
    }

    [Theory]
    [InlineData(RecoveryBrowserCredentialAssistanceState.PausedForMfa)]
    [InlineData(RecoveryBrowserCredentialAssistanceState.PausedForCaptcha)]
    [InlineData(RecoveryBrowserCredentialAssistanceState.PausedForEmailLink)]
    [InlineData(RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired)]
    public async Task InspectionStopOccursBeforeCredentialRead(
        RecoveryBrowserCredentialAssistanceState state)
    {
        var repository = new TestCredentialRepository();
        var coordinator = new RecoveryBrowserCredentialInsertionCoordinator(repository);
        var browserResult = state == RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired
            ? RecoveryBrowserCredentialAssistanceResult.Failure(
                state,
                RecoveryBrowserCredentialAssistanceFailureCode.UnexpectedContent)
            : RecoveryBrowserCredentialAssistanceResult.Pause(state);

        var outcome = await coordinator.ExecuteAsync(
            repository.Metadata.Reference,
            Contract(),
            _ => Task.FromResult(true),
            (_, _) => Task.FromResult(browserResult),
            (_, _, _) => throw new InvalidOperationException("Insertion must not run."),
            CancellationToken.None);

        Assert.Equal(
            RecoveryBrowserCredentialInsertionOutcomeCode.InspectionStopped,
            outcome.Code);
        Assert.Same(browserResult, outcome.BrowserResult);
        Assert.Equal(0, repository.ReadSecretCalls);
        Assert.Equal(0, repository.MarkUsedCalls);
    }

    [Fact]
    public async Task SuccessfulInsertionReadsSecretLateAndRecordsUsedAfterInsertion()
    {
        var repository = new TestCredentialRepository();
        var coordinator = new RecoveryBrowserCredentialInsertionCoordinator(repository);
        var order = new List<string>();

        var outcome = await coordinator.ExecuteAsync(
            repository.Metadata.Reference,
            Contract(),
            _ =>
            {
                order.Add("authorize");
                return Task.FromResult(true);
            },
            (_, _) =>
            {
                order.Add("inspect");
                Assert.Equal(0, repository.ReadSecretCalls);
                return Task.FromResult(RecoveryBrowserCredentialAssistanceResult.Ready);
            },
            (_, secret, _) =>
            {
                order.Add("insert");
                Assert.Equal(1, repository.ReadSecretCalls);
                Assert.Equal(new byte[] { 65, 66, 67 }, secret.ToArray());
                Assert.Equal(0, repository.MarkUsedCalls);
                return Task.FromResult(RecoveryBrowserCredentialAssistanceResult.Inserted);
            },
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(
            RecoveryBrowserCredentialInsertionOutcomeCode.InsertedAndRecordedUsed,
            outcome.Code);
        Assert.Equal(["authorize", "inspect", "insert"], order);
        Assert.Equal(1, repository.ReadSecretCalls);
        Assert.Equal(1, repository.MarkUsedCalls);
        Assert.NotNull(outcome.Metadata?.UsedAt);
    }

    [Fact]
    public async Task FailedInsertionDoesNotRecordCredentialAsUsed()
    {
        var repository = new TestCredentialRepository();
        var coordinator = new RecoveryBrowserCredentialInsertionCoordinator(repository);
        var stopped = RecoveryBrowserCredentialAssistanceResult.Failure(
            RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired,
            RecoveryBrowserCredentialAssistanceFailureCode.WrongOrigin);

        var outcome = await coordinator.ExecuteAsync(
            repository.Metadata.Reference,
            Contract(),
            _ => Task.FromResult(true),
            (_, _) => Task.FromResult(RecoveryBrowserCredentialAssistanceResult.Ready),
            (_, _, _) => Task.FromResult(stopped),
            CancellationToken.None);

        Assert.Equal(
            RecoveryBrowserCredentialInsertionOutcomeCode.InsertionStopped,
            outcome.Code);
        Assert.Same(stopped, outcome.BrowserResult);
        Assert.Equal(1, repository.ReadSecretCalls);
        Assert.Equal(0, repository.MarkUsedCalls);
    }

    [Fact]
    public async Task InsertedButLifecyclePersistenceFailureIsReportedSeparately()
    {
        var repository = new TestCredentialRepository { FailMarkUsed = true };
        var coordinator = new RecoveryBrowserCredentialInsertionCoordinator(repository);

        var outcome = await coordinator.ExecuteAsync(
            repository.Metadata.Reference,
            Contract(),
            _ => Task.FromResult(true),
            (_, _) => Task.FromResult(RecoveryBrowserCredentialAssistanceResult.Ready),
            (_, _, _) => Task.FromResult(RecoveryBrowserCredentialAssistanceResult.Inserted),
            CancellationToken.None);

        Assert.Equal(
            RecoveryBrowserCredentialInsertionOutcomeCode.InsertedStateSaveFailed,
            outcome.Code);
        Assert.Equal(1, repository.ReadSecretCalls);
        Assert.Equal(1, repository.MarkUsedCalls);
        Assert.False(outcome.Succeeded);
    }

    private static RecoveryBrowserCredentialInsertionContract Contract() => new(
        "synthetic",
        "change-password",
        RecoveryBrowserContentMode.SyntheticTest,
        ["http://127.0.0.1:49990"],
        "body[data-unpwn-provider='synthetic']",
        "[data-testid='new-password']",
        "[data-testid='confirm-password']",
        "[data-unpwn-stop-reason='mfa']",
        "[data-unpwn-stop-reason='captcha']",
        "[data-unpwn-stop-reason='email-link']");

    private sealed class TestCredentialRepository : IGeneratedCredentialRepository
    {
        private DateTimeOffset _clock = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

        public TestCredentialRepository()
        {
            Metadata = GeneratedCredentialMetadata.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                _clock);
        }

        public bool IsUnlocked => true;

        public GeneratedCredentialMetadata Metadata { get; private set; }

        public int ReadSecretCalls { get; private set; }

        public int MarkUsedCalls { get; private set; }

        public bool FailMarkUsed { get; init; }

        public Task<GeneratedCredentialCreationResult> GenerateAsync(
            Guid accountId,
            CredentialGenerationPolicy policy,
            Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(GeneratedCredentialCreationResult.Failure(
                GeneratedCredentialFailureCode.InvalidInput));

        public Task<IReadOnlyList<GeneratedCredentialMetadata>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GeneratedCredentialMetadata>>([Metadata]);

        public Task<GeneratedCredentialMetadata?> GetMetadataAsync(
            GeneratedCredentialReference reference,
            CancellationToken cancellationToken) =>
            Task.FromResult<GeneratedCredentialMetadata?>(Metadata);

        public Task<CredentialSecretLease?> ReadSecretAsync(
            GeneratedCredentialReference reference,
            CancellationToken cancellationToken)
        {
            ReadSecretCalls++;
            return Task.FromResult<CredentialSecretLease?>(new([65, 66, 67]));
        }

        public Task<GeneratedCredentialOperationResult> MarkUsedAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            MarkUsedCalls++;
            if (FailMarkUsed)
            {
                return Task.FromResult(GeneratedCredentialOperationResult.Failure(
                    GeneratedCredentialFailureCode.PersistenceFailure));
            }

            Metadata = Metadata.MarkUsed(operationId, NextTime());
            return Task.FromResult(GeneratedCredentialOperationResult.Success(Metadata));
        }

        public Task<GeneratedCredentialOperationResult> ConfirmAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialBatchResult> MarkExportedAsync(
            IReadOnlyCollection<GeneratedCredentialReference> references,
            Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(GeneratedCredentialBatchResult.Failure(
                GeneratedCredentialFailureCode.InvalidInput));

        public Task<GeneratedCredentialOperationResult> ConfirmPasswordManagerImportAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialOperationResult> RevokePasswordManagerImportConfirmationAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialOperationResult> PostponePasswordManagerImportConfirmationAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialOperationResult> ConfirmPlaintextExportCleanupAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialOperationResult> DeleteAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        private Task<GeneratedCredentialOperationResult> Unsupported() =>
            Task.FromResult(GeneratedCredentialOperationResult.Failure(
                GeneratedCredentialFailureCode.InvalidInput));

        private DateTimeOffset NextTime() => _clock = _clock.AddMinutes(1);
    }
}
