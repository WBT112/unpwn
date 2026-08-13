using System.Text;
using Unpwn.App.Services;
using Unpwn.Application.Credentials;
using Unpwn.Application.Diagnostics;
using Unpwn.Application.Recovery;
using Unpwn.Core;
using Unpwn.Export.Credentials;
using Unpwn.Import.Csv;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.App.Tests.EndToEnd;

public sealed class RecoveryJourneySmokeTests
{
    private const string VaultPassword = "UNPWN_TEST_SECRET_e2e-vault-password";
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "EndToEndSmoke")]
    public async Task CompleteRecoveryJourneySurvivesLockAndApplicationRestart()
    {
        using var directory = new TemporaryDirectory();
        var vaultPath = Path.Combine(directory.Path, "journey.sqlite");
        Guid sessionId;
        Guid accountId;

        using (var journey = await RecoveryJourney.StartNewAsync(vaultPath, directory.Path))
        {
            await journey.CreateSessionAsync();
            var preview = Preview(
                "service,account,username,url\n" +
                "google.com,Synthetic Google account,synthetic-user@accounts.example.test," +
                "https://google-auth.example.test/account\n",
                new CsvColumnMapping("service", "account", "username", "url", []));

            var imported = await journey.Inventory.ImportAsync(
                preview.Candidates,
                ImportDuplicateResolution.SkipDuplicates,
                CancellationToken.None);
            Assert.True(imported.Succeeded);
            var account = Assert.Single(journey.Inventory.CurrentInventory!.Accounts);
            accountId = account.Id;
            sessionId = journey.Session.CurrentSession!.Id;

            var inventoryMove = await journey.Guided.AdvanceAsync(CancellationToken.None);
            Assert.True(inventoryMove.Succeeded);
            Assert.Equal(RecoveryWizardStepId.AccountTriage, journey.Guided.Current.CurrentStep);

            journey.Tick();
            Assert.True((await journey.Inventory.CategorizeAsync(
                account.Id,
                account.SuggestedCategory,
                CancellationToken.None)).Succeeded);
            account = Assert.Single(journey.Inventory.CurrentInventory!.Accounts);

            Assert.True((await journey.Guided.AdvanceAsync(CancellationToken.None)).Succeeded);
            Assert.Equal(RecoveryWizardStepId.RecoveryPlan, journey.Guided.Current.CurrentStep);

            var workflow = RepositoryWorkflowCatalog.Workflows.Single(candidate =>
                candidate.ProviderId == account.ProviderId);
            var projection = RecoveryJourney.CreateProjection(account);
            journey.Tick();
            var createdExecution = await journey.Execution.CreateAsync(
                new AccountRecoveryExecutionCreateRequest(
                    Guid.NewGuid(),
                    account.Id,
                    workflow,
                    RecoveryPath.AuthenticatedChange,
                    projection),
                CancellationToken.None);
            Assert.True(
                createdExecution.Succeeded,
                $"{createdExecution.FailureCode}; diagnostics: " +
                string.Join(", ", journey.Diagnostics.Snapshot().Select(item => item.ExceptionType)));

            Assert.True((await journey.Guided.AdvanceAsync(CancellationToken.None)).Succeeded);
            Assert.Equal(RecoveryWizardStepId.AccountRecovery, journey.Guided.Current.CurrentStep);

            var execution = createdExecution.State!;
            GeneratedCredentialReference? credentialReference = null;
            foreach (var action in execution.Actions)
            {
                journey.Tick();
                execution = AssertSuccess(await journey.ApplyAsync(
                    execution,
                    workflow,
                    projection,
                    AccountRecoveryExecutionTransitionKind.StartAction,
                    action.DefinitionId));

                if (action.DefinitionId == "change-password")
                {
                    journey.Tick();
                    using var generated = await journey.Vault.GenerateAsync(
                        account.Id,
                        CredentialGenerationPolicy.Default,
                        Guid.NewGuid(),
                        CancellationToken.None);
                    Assert.True(generated.Succeeded);
                    credentialReference = generated.Metadata!.Reference;
                    execution = AssertSuccess(await journey.ApplyAsync(
                        execution,
                        workflow,
                        projection,
                        AccountRecoveryExecutionTransitionKind.AttachCredentialReference,
                        action.DefinitionId,
                        credentialReference: credentialReference));
                    Assert.True((await journey.Vault.MarkUsedAsync(
                        credentialReference,
                        Guid.NewGuid(),
                        CancellationToken.None)).Succeeded);
                }

                journey.Tick();
                execution = AssertSuccess(await journey.ApplyAsync(
                    execution,
                    workflow,
                    projection,
                    AccountRecoveryExecutionTransitionKind.CompleteAction,
                    action.DefinitionId,
                    completionCriteriaAcknowledged: true));

                if (action.DefinitionId == "change-password")
                {
                    Assert.True((await journey.Vault.ConfirmAsync(
                        credentialReference!,
                        Guid.NewGuid(),
                        CancellationToken.None)).Succeeded);
                }
            }

            Assert.Equal(AccountRecoveryStatus.FullyReviewed, execution.RecoveryStatus);
            Assert.Equal(1, journey.Session.Dashboard!.CredentialsAwaitingExport);
            Assert.True((await journey.Guided.AdvanceAsync(CancellationToken.None)).Succeeded);
            Assert.Equal(RecoveryWizardStepId.RecoveryPlan, journey.Guided.Current.CurrentStep);
            Assert.True((await journey.Guided.AdvanceAsync(CancellationToken.None)).Succeeded);
            Assert.Equal(RecoveryWizardStepId.CredentialExport, journey.Guided.Current.CurrentStep);

            var exportPath = Path.Combine(directory.Path, "generated-credentials.csv");
            var export = await journey.Export.ExportAsync(
                new CredentialExportRequest(
                    Guid.NewGuid(),
                    CredentialExportFormatId.GenericCsv,
                    exportPath,
                    [new CredentialExportSelection(
                        credentialReference!,
                        account.AccountName!,
                        account.LoginIdentifier,
                        account.AccountUrl)],
                    PlaintextRiskAcknowledged: true),
                CancellationToken.None);
            Assert.True(export.Succeeded);
            Assert.True(File.Exists(exportPath));
            Assert.True((await journey.Vault.ConfirmPasswordManagerImportAsync(
                credentialReference!, Guid.NewGuid(), CancellationToken.None)).Succeeded);
            File.Delete(exportPath);
            Assert.True((await journey.Vault.ConfirmPlaintextExportCleanupAsync(
                credentialReference!, Guid.NewGuid(), CancellationToken.None)).Succeeded);
            Assert.True((await journey.Vault.DeleteAsync(
                credentialReference!, Guid.NewGuid(), CancellationToken.None)).Succeeded);

            Assert.True((await journey.Guided.AdvanceAsync(CancellationToken.None)).Succeeded);
            Assert.Equal(RecoveryWizardStepId.CompletionPreflight, journey.Guided.Current.CurrentStep);
            var review = await journey.Completion.ReviewAsync(CancellationToken.None);
            Assert.True(review.Succeeded);
            Assert.True(review.Preflight!.IsClean);
            Assert.Equal(1, review.Report!.DeletedCredentials);

            Assert.True((await journey.Guided.MarkCompletionReviewReadyAsync(
                CancellationToken.None)).Succeeded);
            Assert.Equal(RecoveryWizardStepId.FinalReport, journey.Guided.Current.CurrentStep);
            journey.Tick();
            var refreshedReview = await journey.Completion.ReviewAsync(CancellationToken.None);
            Assert.Equal(review.Preflight.SessionRevision, refreshedReview.Preflight!.SessionRevision);
            Assert.Equal(review.Preflight.InventoryRevision, refreshedReview.Preflight.InventoryRevision);
            Assert.Equal(
                review.Preflight.CredentialMetadataRevisionSum,
                refreshedReview.Preflight.CredentialMetadataRevisionSum);
            Assert.Equal(review.Preflight.Issues, refreshedReview.Preflight.Issues);
            var completed = await journey.Completion.CompleteAsync(
                review.Preflight,
                unresolvedRiskExplicitlyAccepted: false,
                archive: false,
                CancellationToken.None);
            Assert.True(completed.Succeeded, completed.FailureCode.ToString());
            Assert.Equal(RecoveryCompletionOutcome.Completed, completed.Completion!.Outcome);
            Assert.True(journey.Session.CurrentSession!.IsReadOnly);
            Assert.Equal(RecoveryWizardLifecycleStatus.Completed, journey.Guided.Current.Status);

            await journey.Vault.LockAsync(CancellationToken.None);
        }

        using var reopened = await RecoveryJourney.OpenExistingAsync(vaultPath, directory.Path);
        Assert.Equal(sessionId, reopened.Session.CurrentSession!.Id);
        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Completed, reopened.Session.CurrentSession.Status);
        Assert.Equal(RecoveryWizardLifecycleStatus.Completed, reopened.Guided.Current.Status);
        Assert.Equal(accountId, Assert.Single(reopened.Inventory.CurrentInventory!.Accounts).Id);
        var persistedCredentials = await reopened.Vault.ListAsync(CancellationToken.None);
        Assert.True(Assert.Single(persistedCredentials).IsDeleted);
        Assert.Null(await reopened.Vault.ReadSecretAsync(
            persistedCredentials[0].Reference,
            CancellationToken.None));
    }

    [Theory]
    [InlineData("generic-recovery-sample.csv", 16, false)]
    [InlineData("bitwarden-recovery-sample.csv", 5, true)]
    [Trait("Category", "EndToEndSmoke")]
    public async Task CanonicalCsvFixturesPersistEncryptedAcrossRestart(
        string fixtureName,
        int expectedAccounts,
        bool excludesPasswordColumn)
    {
        using var directory = new TemporaryDirectory();
        var vaultPath = Path.Combine(directory.Path, $"{fixtureName}.sqlite");

        using (var journey = await RecoveryJourney.StartNewAsync(vaultPath, directory.Path))
        {
            await journey.CreateSessionAsync();
            var mapping = excludesPasswordColumn
                ? new CsvColumnMapping(
                    "folder", "name", "login_username", "login_uri", ["login_password"])
                : new CsvColumnMapping("service", "account", "username", "url", []);
            using var source = File.OpenText(FixturePath(fixtureName));
            var preview = CsvAccountImportService.CreatePreview(source, mapping);

            Assert.True(preview.CanImport);
            var imported = await journey.Inventory.ImportAsync(
                preview.Candidates,
                ImportDuplicateResolution.SkipDuplicates,
                CancellationToken.None);
            Assert.True(imported.Succeeded);
            Assert.Equal(expectedAccounts, journey.Inventory.CurrentInventory!.Accounts.Length);
            await journey.Vault.LockAsync(CancellationToken.None);
        }

        var vaultBytes = await File.ReadAllBytesAsync(vaultPath);
        var markerHex = Convert.ToHexString(Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_"));
        Assert.DoesNotContain(markerHex, Convert.ToHexString(vaultBytes), StringComparison.Ordinal);

        using var reopened = await RecoveryJourney.OpenExistingAsync(vaultPath, directory.Path);
        Assert.Equal(expectedAccounts, reopened.Inventory.CurrentInventory!.Accounts.Length);
        Assert.All(reopened.Inventory.CurrentInventory.Accounts, account => account.Validate());
    }

    [Fact]
    [Trait("Category", "EndToEndSmoke")]
    public async Task UnsupportedProviderRecoveryWithCredentialAndNotApplicableControlSurvivesRestart()
    {
        using var directory = new TemporaryDirectory();
        var vaultPath = Path.Combine(directory.Path, "generic-unsupported.sqlite");
        Guid accountId;
        GeneratedCredentialReference credentialReference;

        using (var journey = await RecoveryJourney.StartNewAsync(vaultPath, directory.Path))
        {
            await journey.CreateSessionAsync();
            using var source = File.OpenText(FixturePath("generic-recovery-sample.csv"));
            var preview = CsvAccountImportService.CreatePreview(
                source,
                new CsvColumnMapping("service", "account", "username", "url", []));
            Assert.True((await journey.Inventory.ImportAsync(
                preview.Candidates,
                ImportDuplicateResolution.SkipDuplicates,
                CancellationToken.None)).Succeeded);
            var account = Assert.Single(
                journey.Inventory.CurrentInventory!.Accounts,
                candidate => candidate.ProviderId == "unsupported.example");
            accountId = account.Id;
            var workflow = RepositoryWorkflowCatalog.CreateGenericManualWorkflow(account.ProviderId);
            var projection = RecoveryJourney.CreateProjection(account);
            journey.Tick();
            var execution = AssertSuccess(await journey.Execution.CreateAsync(
                new AccountRecoveryExecutionCreateRequest(
                    Guid.NewGuid(),
                    account.Id,
                    workflow,
                    RecoveryPath.AuthenticatedChange,
                    projection),
                CancellationToken.None));
            GeneratedCredentialReference? generatedReference = null;

            foreach (var action in execution.Actions)
            {
                journey.Tick();
                execution = AssertSuccess(await journey.ApplyAsync(
                    execution,
                    workflow,
                    projection,
                    AccountRecoveryExecutionTransitionKind.StartAction,
                    action.DefinitionId));
                journey.Tick();
                if (action.DefinitionId == "change-password")
                {
                    using var generated = await journey.Vault.GenerateAsync(
                        account.Id,
                        CredentialGenerationPolicy.Default,
                        Guid.NewGuid(),
                        CancellationToken.None);
                    Assert.True(generated.Succeeded);
                    generatedReference = generated.Metadata!.Reference;
                    execution = AssertSuccess(await journey.ApplyAsync(
                        execution,
                        workflow,
                        projection,
                        AccountRecoveryExecutionTransitionKind.AttachCredentialReference,
                        action.DefinitionId,
                        credentialReference: generatedReference));
                    journey.Tick();
                    Assert.True((await journey.Vault.MarkUsedAsync(
                        generatedReference,
                        Guid.NewGuid(),
                        CancellationToken.None)).Succeeded);
                    execution = AssertSuccess(await journey.ApplyAsync(
                        execution,
                        workflow,
                        projection,
                        AccountRecoveryExecutionTransitionKind.CompleteAction,
                        action.DefinitionId,
                        completionCriteriaAcknowledged: true));
                    Assert.True((await journey.Vault.ConfirmAsync(
                        generatedReference,
                        Guid.NewGuid(),
                        CancellationToken.None)).Succeeded);
                }
                else if (action.DefinitionId == "review-connected-access-auth")
                {
                    execution = AssertSuccess(await journey.ApplyAsync(
                        execution,
                        workflow,
                        projection,
                        AccountRecoveryExecutionTransitionKind.MarkTrulyNotApplicable,
                        action.DefinitionId,
                        userReason: "The synthetic service has no connected-access control."));
                }
                else
                {
                    execution = AssertSuccess(await journey.ApplyAsync(
                        execution,
                        workflow,
                        projection,
                        AccountRecoveryExecutionTransitionKind.CompleteAction,
                        action.DefinitionId,
                        completionCriteriaAcknowledged: true));
                }
            }

            credentialReference = Assert.IsType<GeneratedCredentialReference>(generatedReference);
            Assert.Equal(AccountRecoveryStatus.FullyReviewed, execution.RecoveryStatus);
            Assert.Equal(
                NotApplicableDisposition.TrulyNotApplicable,
                execution.GetAction("review-connected-access-auth").NotApplicableDisposition);
            await journey.Vault.LockAsync(CancellationToken.None);
        }

        using var reopened = await RecoveryJourney.OpenExistingAsync(vaultPath, directory.Path);
        var reopenedAccount = Assert.Single(
            reopened.Inventory.CurrentInventory!.Accounts,
            candidate => candidate.Id == accountId);
        Assert.Equal(accountId, reopenedAccount.Id);
        var reopenedWorkflow = RepositoryWorkflowCatalog.CreateGenericManualWorkflow(
            reopenedAccount.ProviderId);
        var loaded = await reopened.Execution.LoadAsync(
            accountId,
            reopenedWorkflow,
            CancellationToken.None);

        Assert.True(loaded.Succeeded);
        Assert.Equal(AccountRecoveryStatus.FullyReviewed, loaded.State!.RecoveryStatus);
        Assert.Equal(
            credentialReference,
            loaded.State.GetAction("change-password").CredentialReference);
        Assert.Equal(
            NotApplicableDisposition.TrulyNotApplicable,
            loaded.State.GetAction("review-connected-access-auth").NotApplicableDisposition);
    }

    [Theory]
    [InlineData(TrustedDeviceDecision.NotTrusted)]
    [InlineData(TrustedDeviceDecision.Unsure)]
    [Trait("Category", "EndToEndSmoke")]
    public async Task UntrustedDeviceDecisionStopsBeforeVaultPersistence(
        TrustedDeviceDecision decision)
    {
        using var directory = new TemporaryDirectory();
        var vaultPath = Path.Combine(directory.Path, "must-not-exist.sqlite");
        var wizard = new RecoveryWizardSessionService(StartedAt);
        wizard.BeginTrustedDeviceCheck(StartedAt);
        wizard.RecordTrustedDeviceDecision(decision, StartedAt);
        using var vault = new RecoveryVaultLifecycleService(
            new JsonRecentVaultStore(Path.Combine(directory.Path, "recent.json")),
            wizard,
            clock: () => StartedAt);

        var result = await vault.CreateAsync(vaultPath, VaultPassword, CancellationToken.None);
        wizard.StopAfterTrustedDeviceGuidance(StartedAt);

        Assert.False(result.Succeeded);
        Assert.Equal(VaultOperationFailureCode.InvalidInput, result.FailureCode);
        Assert.False(File.Exists(vaultPath));
        Assert.True(wizard.Current.IsTerminal);
        Assert.False(wizard.Current.HasVaultContext);
    }

    private static AccountRecoveryExecutionState AssertSuccess(AccountRecoveryExecutionResult result)
    {
        Assert.True(result.Succeeded);
        return Assert.IsType<AccountRecoveryExecutionState>(result.State);
    }

    private static CsvImportPreview Preview(string csv, CsvColumnMapping mapping) =>
        CsvAccountImportService.CreatePreview(new StringReader(csv), mapping);

    private static string FixturePath(string fileName) =>
        Path.Combine(RepositoryRoot, "samples", "import", fileName);

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "unpwn.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class RecoveryJourney : IDisposable
    {
        private DateTimeOffset _time = StartedAt;

        private RecoveryJourney(string vaultPath, string workingDirectory)
        {
            VaultPath = vaultPath;
            Wizard = new RecoveryWizardSessionService(_time);
            Diagnostics = new BoundedSecretSafeDiagnosticStore();
            var diagnostics = new SecretSafeDiagnostics(Diagnostics);
            Mutations = new WorkspaceMutationCoordinator();
            Vault = new RecoveryVaultLifecycleService(
                new JsonRecentVaultStore(Path.Combine(workingDirectory, "recent.json")),
                Wizard,
                clock: () => _time);
            Store = new ResilientWorkspaceRecordStore(Vault, diagnostics);
            Session = new RecoverySessionService(Store, Vault, () => _time, Mutations);
            Inventory = new AccountInventoryService(Store, Session, () => _time, Mutations);
            Execution = new AccountRecoveryExecutionService(Store, Session, Mutations, () => _time);
            Guided = new GuidedRecoveryWizardService(
                Store, Wizard, Session, Inventory, Mutations, () => _time);
            Completion = new RecoveryCompletionService(Session, Inventory, Vault, () => _time);
            Export = new GeneratedCredentialExportService(Vault);
        }

        public string VaultPath { get; }

        public RecoveryWizardSessionService Wizard { get; }

        public BoundedSecretSafeDiagnosticStore Diagnostics { get; }

        public RecoveryVaultLifecycleService Vault { get; }

        public ResilientWorkspaceRecordStore Store { get; }

        public WorkspaceMutationCoordinator Mutations { get; }

        public RecoverySessionService Session { get; }

        public AccountInventoryService Inventory { get; }

        public AccountRecoveryExecutionService Execution { get; }

        public GuidedRecoveryWizardService Guided { get; }

        public RecoveryCompletionService Completion { get; }

        public GeneratedCredentialExportService Export { get; }

        public static async Task<RecoveryJourney> StartNewAsync(
            string vaultPath,
            string workingDirectory)
        {
            var journey = new RecoveryJourney(vaultPath, workingDirectory);
            journey.Wizard.BeginTrustedDeviceCheck(journey._time);
            journey.Wizard.RecordTrustedDeviceDecision(TrustedDeviceDecision.Trusted, journey._time);
            Assert.True((await journey.Vault.CreateAsync(
                vaultPath, VaultPassword, CancellationToken.None)).Succeeded);
            await journey.Session.InitializeAsync(CancellationToken.None);
            return journey;
        }

        public static async Task<RecoveryJourney> OpenExistingAsync(
            string vaultPath,
            string workingDirectory)
        {
            var journey = new RecoveryJourney(vaultPath, workingDirectory);
            journey.Wizard.BeginTrustedDeviceCheck(journey._time);
            journey.Wizard.RecordTrustedDeviceDecision(TrustedDeviceDecision.Trusted, journey._time);
            Assert.True((await journey.Vault.OpenAsync(
                vaultPath, VaultPassword, CancellationToken.None)).Succeeded);
            journey._time = journey.Wizard.Current.UpdatedAt;
            await journey.Session.InitializeAsync(CancellationToken.None);
            await journey.Inventory.InitializeAsync(CancellationToken.None);
            return journey;
        }

        public async Task CreateSessionAsync()
        {
            Tick();
            var result = await Session.CreateAsync(
                new RecoverySessionCreateRequest(
                    "Synthetic end-to-end recovery",
                    IncidentIndicator.None,
                    SecurityWarningAcknowledged: true),
                CancellationToken.None);
            Assert.True(result.Succeeded);
            await Inventory.InitializeAsync(CancellationToken.None);
        }

        public static AccountRecoveryProjectionContext CreateProjection(AccountInventoryEntry account)
        {
            return new AccountRecoveryProjectionContext(account.DashboardCriticality);
        }

        public Task<AccountRecoveryExecutionResult> ApplyAsync(
            AccountRecoveryExecutionState state,
            RecoveryWorkflowDefinition workflow,
            AccountRecoveryProjectionContext projection,
            AccountRecoveryExecutionTransitionKind transition,
            string actionId,
            bool completionCriteriaAcknowledged = false,
            GeneratedCredentialReference? credentialReference = null,
            string? userReason = null) =>
            Execution.ApplyAsync(
                new AccountRecoveryExecutionTransitionRequest(
                    Guid.NewGuid(),
                    state.AccountId,
                    state.Revision,
                    workflow,
                    transition,
                    actionId,
                    UserReason: userReason,
                    UserNotes: null,
                    completionCriteriaAcknowledged,
                    credentialReference,
                    projection),
                CancellationToken.None);

        public void Tick() => _time = _time.AddMinutes(1);

        public void Dispose()
        {
            Guided.Dispose();
            Inventory.Dispose();
            Session.Dispose();
            Mutations.Dispose();
            Vault.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"unpwn-e2e-smoke-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
