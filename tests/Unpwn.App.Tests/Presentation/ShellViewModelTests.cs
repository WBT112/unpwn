using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Application;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class ShellViewModelTests
{
    [Fact]
    public void ShellStartsLockedWithoutRecoveryContext()
    {
        var shellContext = new TestVaultLifecycleService();
        var shell = CreateShell(shellContext);

        Assert.False(shell.IsVaultUnlocked);
        Assert.Equal("No vault unlocked", shell.VaultContextLabel);
        Assert.Equal("No recovery session", shell.SessionContextLabel);
        Assert.IsType<VaultEntryScreenViewModel>(shell.CurrentScreen);
        Assert.False(shell.LockCommand.CanExecute(null));
    }

    [Fact]
    public void NavigationExposesEveryMvpRouteAndChangesCurrentScreen()
    {
        var shell = CreateShell(new TestVaultLifecycleService());
        AppRoute[] expectedRoutes =
        [
            AppRoute.VaultEntry,
            AppRoute.Dashboard,
            AppRoute.CsvImport,
            AppRoute.Accounts,
            AppRoute.Workflow,
            AppRoute.CredentialsExport,
            AppRoute.Completion,
        ];

        Assert.Equal(expectedRoutes, shell.NavigationItems.Select(item => item.Route));

        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts);

        Assert.Equal(AppRoute.Accounts, shell.CurrentScreen.Route);
        Assert.Equal("Accounts", shell.CurrentScreen.Title);
    }

    [Fact]
    public void NavigationEnforcesVaultAndOverviewPrerequisites()
    {
        var vault = new TestVaultLifecycleService();
        var recoverySession = new TestRecoverySessionService();
        var inventory = new TestAccountInventoryService();
        var localization = CreateLocalization();
        var confirmation = new TestConfirmationDialogService((_, _) => Task.FromResult(false));
        var factory = new AppScreenFactory(
            confirmation,
            vault,
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            recoverySession,
            inventory,
            localization);
        var shell = new ShellViewModel(
            factory,
            vault,
            recoverySession,
            inventory,
            localization);

        Assert.Equal(
            [AppRoute.VaultEntry],
            shell.NavigationItems.Where(item => item.IsEnabled).Select(item => item.Route));
        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport);
        Assert.Equal(AppRoute.VaultEntry, shell.CurrentScreen.Route);

        vault.Unlock("Synthetic vault", string.Empty);

        Assert.True(shell.NavigationItems.Single(item => item.Route == AppRoute.Dashboard).IsEnabled);
        Assert.False(shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport).IsEnabled);

        recoverySession.SetSession(RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Synthetic recovery",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch));

        Assert.True(shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts).IsEnabled);
        Assert.True(shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport).IsEnabled);
        Assert.False(shell.NavigationItems.Single(item => item.Route == AppRoute.Workflow).IsEnabled);
    }

    [Fact]
    public void TerminalSessionKeepsFinalReportAvailableAndDisablesMutationScreens()
    {
        var vault = new TestVaultLifecycleService();
        var recoverySession = new TestRecoverySessionService();
        var inventory = new TestAccountInventoryService();
        var localization = CreateLocalization();
        var factory = new AppScreenFactory(
            new TestConfirmationDialogService((_, _) => Task.FromResult(false)),
            vault,
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            recoverySession,
            inventory,
            localization);
        var shell = new ShellViewModel(factory, vault, recoverySession, inventory, localization);
        vault.Unlock("Synthetic vault", "Synthetic recovery session");
        var archived = RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Synthetic recovery",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch)
            .Archive(DateTimeOffset.UnixEpoch.AddMinutes(1));

        recoverySession.SetSession(archived);

        Assert.True(shell.NavigationItems.Single(item => item.Route == AppRoute.Dashboard).IsEnabled);
        Assert.True(shell.NavigationItems.Single(item => item.Route == AppRoute.Completion).IsEnabled);
        Assert.False(shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts).IsEnabled);
        Assert.False(shell.NavigationItems.Single(item => item.Route == AppRoute.Workflow).IsEnabled);
        Assert.False(shell.NavigationItems.Single(item => item.Route == AppRoute.CredentialsExport).IsEnabled);
        Assert.False(shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport).IsEnabled);
    }

    [Fact]
    public void LanguageChangeRefreshesShellNavigationAndCurrentScreen()
    {
        var localization = CreateLocalization();
        var shell = CreateShell(new TestVaultLifecycleService(), localization);
        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts);

        shell.SelectedLanguage = shell.LanguageOptions.Single(option => option.Code == "de");

        Assert.Equal("de", localization.CurrentLanguageCode);
        Assert.Equal("Kein Tresor entsperrt", shell.VaultContextLabel);
        Assert.Equal("Konten", shell.SelectedNavigation.Label);
        Assert.Equal("Konten", shell.CurrentScreen.Title);
    }

    [Fact]
    public void ShellShowsAbnormalExitAndLocalizedPersistenceOutcome()
    {
        var vault = new TestVaultLifecycleService();
        var recoverySession = new TestRecoverySessionService();
        var inventory = new TestAccountInventoryService();
        var localization = CreateLocalization();
        var persistence = new TestPersistenceStatus();
        var factory = new AppScreenFactory(
            new TestConfirmationDialogService((_, _) => Task.FromResult(false)),
            vault,
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            recoverySession,
            inventory,
            localization);
        var shell = new ShellViewModel(
            factory,
            vault,
            recoverySession,
            inventory,
            localization,
            persistenceStatus: persistence,
            runState: new ApplicationRunState(
                PreviousExitWasAbnormal: true,
                MarkerUnavailable: false));

        Assert.True(shell.HasStartupRecoveryWarning);
        shell.DismissStartupRecoveryCommand.Execute(null);
        Assert.False(shell.HasStartupRecoveryWarning);

        persistence.Publish(
            WorkspacePersistenceState.SaveFailed,
            WorkspacePersistenceFailureCode.IoFailure);
        Assert.True(shell.IsPersistenceStatusVisible);
        Assert.True(shell.IsPersistenceFailure);
        Assert.Equal("!", shell.PersistenceStatusSymbol);
        Assert.Contains("not claimed as saved", shell.PersistenceStatusText, StringComparison.Ordinal);

        shell.SelectedLanguage = shell.LanguageOptions.Single(option => option.Code == "de");
        Assert.Contains("gilt nicht als gespeichert", shell.PersistenceStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobalLockIsAvailableOnlyForUnlockedVaultAndReturnsToVaultEntry()
    {
        var shellContext = new TestVaultLifecycleService();
        var shell = CreateShell(shellContext);
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Dashboard);

        Assert.True(shell.IsVaultUnlocked);
        Assert.True(shell.LockCommand.CanExecute(null));
        Assert.Equal("Synthetic vault", shell.VaultContextLabel);

        var outcome = await shell.LockCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.False(shell.IsVaultUnlocked);
        Assert.Equal(AppRoute.VaultEntry, shell.CurrentScreen.Route);
        Assert.Equal(AppVisualState.Success, shell.CurrentStatus.State);
    }

    [Fact]
    public async Task CompletionCommandCanBeCanceledAndRejectsRepeatedExecution()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new TestCompletionService(async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return RecoveryCompletionReviewResult.Failure(RecoveryCompletionFailureCode.ReadFailed);
        });
        var shellContext = new TestVaultLifecycleService();
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        var viewModel = CreateCompletionViewModel(service, shellContext);

        var firstExecution = viewModel.ReviewCompletionCommand.ExecuteAsync();
        await started.Task;
        var repeatedOutcome = await viewModel.ReviewCompletionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Skipped, repeatedOutcome);
        Assert.True(viewModel.ReviewCompletionCommand.IsRunning);
        Assert.True(viewModel.ReviewCompletionCommand.CanBeCanceled);

        viewModel.ReviewCompletionCommand.Cancel();
        var firstOutcome = await firstExecution;

        Assert.Equal(AsyncCommandOutcome.Canceled, firstOutcome);
        Assert.Equal(1, service.ReviewCalls);
        Assert.False(viewModel.ReviewCompletionCommand.IsRunning);
    }

    [Fact]
    public async Task FailedCompletionCommandUsesCurrentLanguageAndExposesNoSourceMessage()
    {
        const string sourceError = "UNPWN_TEST_SECRET_preflight-failure";
        var service = new TestCompletionService(_ =>
            Task.FromException<RecoveryCompletionReviewResult>(new InvalidOperationException(sourceError)));
        var shellContext = new TestVaultLifecycleService();
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        var localization = CreateLocalization();
        var viewModel = CreateCompletionViewModel(service, shellContext, localization);
        localization.SetLanguage("de");

        var outcome = await viewModel.ReviewCompletionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Failed, outcome);
        Assert.Equal(
            "Die Abschlussaktion konnte nicht ausgeführt werden.",
            viewModel.ReviewCompletionCommand.LastErrorMessage);
        Assert.DoesNotContain(sourceError, viewModel.ReviewCompletionCommand.LastErrorMessage, StringComparison.Ordinal);
        Assert.True(viewModel.ReviewCompletionCommand.HasError);
        Assert.False(viewModel.ReviewCompletionCommand.IsRunning);
    }

    [Fact]
    public async Task ConfirmationNamesExactLocalizedActionAndAffectedItem()
    {
        SensitiveConfirmationRequest? observedRequest = null;
        var confirmation = new TestConfirmationDialogService((request, _) =>
        {
            observedRequest = request;
            return Task.FromResult(false);
        });
        var shellContext = new TestVaultLifecycleService();
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        var localization = CreateLocalization();
        localization.SetLanguage("de");
        var service = new TestCompletionService(_ => Task.FromResult(CleanReview()));
        var viewModel = CreateCompletionViewModel(service, shellContext, localization, confirmation);

        await viewModel.ReviewCompletionCommand.ExecuteAsync();
        var outcome = await viewModel.CompleteCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.NotNull(observedRequest);
        Assert.Equal("Wiederherstellungssitzung abschließen", observedRequest.Action);
        Assert.Equal("Synthetic recovery session", observedRequest.AffectedItem);
        Assert.Equal("SICHERHEITSRELEVANTE AKTION", observedRequest.RiskLabel);
        Assert.Equal(AppVisualState.Normal, viewModel.Status.State);
        Assert.Equal(0, service.CompleteCalls);
    }

    [Fact]
    public void RiskStatesHaveLocalizedTextAndDistinctSymbols()
    {
        var localization = CreateLocalization();
        var states = Enum.GetValues<AppVisualState>()
            .Select(state => VisualStatusViewModel.Create(
                state,
                localization,
                "Screen.Vault.StatusTitle",
                "Screen.Vault.StatusMessage"))
            .ToArray();

        Assert.Equal(states.Length, states.Select(state => state.KindLabel).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(states.Length, states.Select(state => state.Symbol).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(states, state => state.State == AppVisualState.Blocked && state.KindLabel == "Blocked");
        Assert.Contains(states, state => state.State == AppVisualState.Error && state.KindLabel == "Failed");
        Assert.Contains(
            states,
            state => state.State == AppVisualState.UnresolvedRisk && state.KindLabel == "Unresolved risk");
    }

    [Fact]
    public async Task AssistantPrimaryActionAdvancesCanonicalWizardAndOpensItsTarget()
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var vault = new TestVaultLifecycleService();
        vault.Unlock("Synthetic vault", "Synthetic recovery");
        var session = new TestRecoverySessionService();
        session.SetSession(RecoverySessionWorkspace.Create(
                sessionId,
                "Synthetic recovery",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
            [
                DashboardAccount(accountId, "github.com", "change-password"),
            ],
            DateTimeOffset.UnixEpoch.AddMinutes(1)));
        var inventory = new TestAccountInventoryService();
        inventory.SetInventory(AccountInventoryState.Empty(sessionId, DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
            [
                InventoryAccount(accountId, "github.com", "GitHub recovery"),
            ],
            DateTimeOffset.UnixEpoch.AddMinutes(1)));
        var guided = new TestGuidedRecoveryWizardService(
            WizardAt(RecoveryWizardStepId.RecoveryPlan),
            new GuidedRecoveryDecision(
                RecoveryWizardStepId.RecoveryPlan,
                RecoveryWizardStepId.AccountRecovery,
                GuidedRecoveryBlockCode.None,
                accountId,
                "change-password"));
        var shell = CreateGuidedShell(vault, session, inventory, guided);

        var outcome = await shell.GuidedPrimaryCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(1, guided.AdvanceCalls);
        Assert.Equal(RecoveryWizardStepId.AccountRecovery, guided.Current.CurrentStep);
        Assert.Equal(AppRoute.Workflow, shell.CurrentScreen.Route);
        Assert.Contains("GitHub recovery", shell.GuidedTargetText, StringComparison.Ordinal);
        Assert.Contains("Change the password", shell.GuidedTargetText, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualDetailNavigationDoesNotAdvanceTheCanonicalWizard()
    {
        var vault = new TestVaultLifecycleService();
        vault.Unlock("Synthetic vault", "Synthetic recovery");
        var session = new TestRecoverySessionService();
        session.SetSession(RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Synthetic recovery",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch));
        var inventory = new TestAccountInventoryService();
        var guided = new TestGuidedRecoveryWizardService(
            WizardAt(RecoveryWizardStepId.AccountInventory),
            new GuidedRecoveryDecision(
                RecoveryWizardStepId.AccountInventory,
                null,
                GuidedRecoveryBlockCode.AccountsRequired));
        var shell = CreateGuidedShell(vault, session, inventory, guided);

        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts);

        Assert.Equal(AppRoute.Accounts, shell.CurrentScreen.Route);
        Assert.Equal(0, guided.AdvanceCalls);
        Assert.Equal(RecoveryWizardStepId.AccountInventory, guided.Current.CurrentStep);
    }

    [Fact]
    public async Task BlockedAssistantOpensCurrentTaskWithoutAdvancing()
    {
        var vault = new TestVaultLifecycleService();
        vault.Unlock("Synthetic vault", "Synthetic recovery");
        var session = new TestRecoverySessionService();
        session.SetSession(RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Synthetic recovery",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch));
        var guided = new TestGuidedRecoveryWizardService(
            WizardAt(RecoveryWizardStepId.AccountInventory),
            new GuidedRecoveryDecision(
                RecoveryWizardStepId.AccountInventory,
                null,
                GuidedRecoveryBlockCode.AccountsRequired));
        var shell = CreateGuidedShell(vault, session, new TestAccountInventoryService(), guided);

        await shell.GuidedPrimaryCommand.ExecuteAsync();

        Assert.Equal(AppRoute.CsvImport, shell.CurrentScreen.Route);
        Assert.Equal(0, guided.AdvanceCalls);
        Assert.Equal("Open CSV import", shell.GuidedPrimaryActionText);
        Assert.Contains("Import at least one account", shell.GuidedRecommendationText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PausedAssistantRequiresResumeAndKeepsMutationDetailsDisabled()
    {
        var vault = new TestVaultLifecycleService();
        vault.Unlock("Synthetic vault", "Synthetic recovery");
        var session = new TestRecoverySessionService();
        session.SetSession(RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Synthetic recovery",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch)
            .Pause(DateTimeOffset.UnixEpoch.AddMinutes(1)));
        var guided = new TestGuidedRecoveryWizardService(
            WizardAt(RecoveryWizardStepId.RecoveryPlan) with
            {
                Status = RecoveryWizardLifecycleStatus.Paused,
            },
            new GuidedRecoveryDecision(
                RecoveryWizardStepId.RecoveryPlan,
                null,
                GuidedRecoveryBlockCode.Paused));
        var shell = CreateGuidedShell(vault, session, new TestAccountInventoryService(), guided);

        Assert.All(
            shell.NavigationItems.Where(item => item.Route is
                AppRoute.Accounts or AppRoute.Workflow or AppRoute.CredentialsExport or
                AppRoute.Completion or AppRoute.CsvImport),
            item => Assert.False(item.IsEnabled));
        Assert.Equal("Resume recovery", shell.GuidedPrimaryActionText);

        await shell.GuidedPrimaryCommand.ExecuteAsync();

        Assert.Equal(1, session.ResumeCalls);
        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Active, session.CurrentSession?.Status);
        Assert.Equal(0, guided.AdvanceCalls);
    }

    internal static ShellViewModel CreateGuidedShell(
        TestVaultLifecycleService vault,
        TestRecoverySessionService session,
        TestAccountInventoryService inventory,
        IGuidedRecoveryWizardService guided)
    {
        var localization = CreateLocalization();
        var wizard = new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch);
        var factory = new AppScreenFactory(
            new TestConfirmationDialogService((_, _) => Task.FromResult(false)),
            vault,
            wizard,
            session,
            inventory,
            localization);
        return new ShellViewModel(
            factory,
            vault,
            session,
            inventory,
            localization,
            guided);
    }

    internal static RecoveryWizardState WizardAt(RecoveryWizardStepId step) => new(
        Guid.NewGuid(),
        step,
        step,
        RecoveryWizardLifecycleStatus.Active,
        TrustedDeviceDecision.Trusted,
        HasVaultContext: true,
        Revision: 1,
        DateTimeOffset.UnixEpoch);

    private static AccountInventoryEntry InventoryAccount(
        Guid accountId,
        string providerId,
        string accountName) => new(
        accountId,
        providerId,
        accountName,
        null,
        null,
        AccountInventoryPriority.Normal,
        [],
        [],
        DateTimeOffset.UnixEpoch);

    private static RecoveryAccountDashboardEntry DashboardAccount(
        Guid accountId,
        string providerId,
        string actionId) => new(
        accountId,
        providerId,
        AccountCriticality.Important,
        AccountRecoveryStatus.Open,
        RequiredActionsCompleted: 0,
        RequiredActionsTotal: 1,
        CompletedRequiredWeight: 0,
        TotalRequiredWeight: 1,
        BlockedRequiredActions: 0,
        FailedRequiredActions: 0,
        UnresolvedRisks: 0,
        AccessLost: false,
        CredentialsAwaitingExport: 0,
        CredentialsAwaitingDeletion: 0,
        RecommendedActionId: actionId,
        DependencyDepth: 0,
        WaitingForAccountIds: []);

    private static ResourceLocalizationService CreateLocalization() =>
        new(CultureInfo.GetCultureInfo("en"));

    private static CompletionScreenViewModel CreateCompletionViewModel(
        IRecoveryCompletionService completionService,
        TestVaultLifecycleService shellContext,
        ResourceLocalizationService? localization = null,
        IConfirmationDialogService? confirmation = null) =>
        new(
            completionService,
            new TestReportWriter(),
            confirmation ?? new TestConfirmationDialogService((_, _) => Task.FromResult(false)),
            shellContext,
            localization ?? CreateLocalization());

    private static RecoveryCompletionReviewResult CleanReview()
    {
        var sessionId = Guid.NewGuid();
        var reviewedAt = DateTimeOffset.UnixEpoch;
        var preflight = new RecoveryCompletionPreflight(sessionId, 1, 1, reviewedAt, [], 0);
        var report = new RecoveryCompletionReport(
            sessionId, reviewedAt, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        return RecoveryCompletionReviewResult.Success(preflight, report);
    }

    private static ShellViewModel CreateShell(
        TestVaultLifecycleService shellContext,
        ResourceLocalizationService? localization = null)
    {
        localization ??= CreateLocalization();
        var confirmation = new TestConfirmationDialogService((_, _) => Task.FromResult(false));
        return new ShellViewModel(
            new AppScreenFactory(
                confirmation,
                shellContext,
                new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
                new TestRecoverySessionService(),
                localization),
            shellContext,
            localization);
    }

    private sealed class TestConfirmationDialogService(
        Func<SensitiveConfirmationRequest, CancellationToken, Task<bool>> confirm)
        : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken) => confirm(request, cancellationToken);
    }

    internal sealed class TestRecoverySessionService : IRecoverySessionService
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState { get; private set; } = RecoverySessionLoadState.Locked;

        public RecoverySessionWorkspace? CurrentSession { get; private set; }

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public int ResumeCalls { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Locked));

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CurrentSession?.Status != RecoveryWorkspaceLifecycleStatus.Active)
            {
                return Task.FromResult(RecoverySessionOperationResult.Failure(
                    RecoverySessionOperationFailureCode.Conflict));
            }

            CurrentSession = CurrentSession.Pause(CurrentSession.UpdatedAt.AddMinutes(1));
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResumeCalls++;
            if (CurrentSession?.Status != RecoveryWorkspaceLifecycleStatus.Paused)
            {
                return Task.FromResult(RecoverySessionOperationResult.Failure(
                    RecoverySessionOperationFailureCode.Conflict));
            }

            CurrentSession = CurrentSession.Resume(CurrentSession.UpdatedAt.AddMinutes(1));
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Locked));

        public Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Locked));

        public void ClearForLock()
        {
            LoadState = RecoverySessionLoadState.Locked;
            CurrentSession = null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetSession(RecoverySessionWorkspace session)
        {
            CurrentSession = session;
            LoadState = RecoverySessionLoadState.Loaded;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal sealed class TestAccountInventoryService : IAccountInventoryService
    {
        public event EventHandler? InventoryChanged;

        public AccountInventoryLoadState LoadState { get; private set; } = AccountInventoryLoadState.Empty;

        public AccountInventoryState? CurrentInventory { get; private set; }

        public AccountInventoryPlan? CurrentPlan => null;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(
            AccountInventoryUpsertRequest request,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> DecideRoleAsync(
            Guid accountId,
            AccountInventoryRole role,
            AccountRoleDecision decision,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> AddDependencyAsync(
            AccountDependencyRequest request,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> RemoveDependencyAsync(
            Guid accountId,
            Guid dependsOnAccountId,
            AccountDependencyKind kind,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(
            Guid accountId,
            bool dependencyImpactAcknowledged,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> ImportAsync(
            IReadOnlyCollection<ImportAccountCandidate> candidates,
            ImportDuplicateResolution? duplicateResolution,
            CancellationToken cancellationToken) => Unsupported();

        public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

        public void SetInventory(AccountInventoryState inventory)
        {
            CurrentInventory = inventory;
            LoadState = AccountInventoryLoadState.Loaded;
            InventoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearForLock() => InventoryChanged?.Invoke(this, EventArgs.Empty);

        private static Task<AccountInventoryOperationResult> Unsupported() =>
            Task.FromResult(AccountInventoryOperationResult.Failure(
                AccountInventoryFailureCode.Conflict));
    }

    internal sealed class TestGuidedRecoveryWizardService(
        RecoveryWizardState current,
        GuidedRecoveryDecision next) : IGuidedRecoveryWizardService
    {
        public event EventHandler? GuidanceChanged;

        public RecoveryWizardState Current { get; private set; } = current;

        public GuidedRecoveryDecision NextDecision { get; private set; } = next;

        public GuidedRecoveryDecision PreviousDecision => new(
            Current.CurrentStep,
            null,
            GuidedRecoveryBlockCode.UnsupportedStep);

        public int AdvanceCalls { get; private set; }

        public void SetGuidance(
            RecoveryWizardState state,
            GuidedRecoveryDecision decision)
        {
            Current = state;
            NextDecision = decision;
            GuidanceChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<GuidedRecoveryMoveResult> AdvanceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdvanceCalls++;
            if (!NextDecision.CanMove || NextDecision.TargetStep is null)
            {
                return Task.FromResult(GuidedRecoveryMoveResult.Failure(
                    GuidedRecoveryMoveFailureCode.Blocked,
                    NextDecision));
            }

            var result = NextDecision;
            Current = Current with
            {
                CurrentStep = result.TargetStep,
                ResumeStep = result.TargetStep,
                Revision = Current.Revision + 1,
            };
            GuidanceChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(GuidedRecoveryMoveResult.Success(result));
        }

        public Task<GuidedRecoveryMoveResult> GoBackAsync(CancellationToken cancellationToken) =>
            Task.FromResult(GuidedRecoveryMoveResult.Failure(
                GuidedRecoveryMoveFailureCode.Blocked,
                PreviousDecision));

        public Task<GuidedRecoveryMoveResult> BeginCompletionReviewAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(GuidedRecoveryMoveResult.Failure(
                GuidedRecoveryMoveFailureCode.Blocked,
                NextDecision));

        public Task<GuidedRecoveryMoveResult> MarkCompletionReviewReadyAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(GuidedRecoveryMoveResult.Failure(
                GuidedRecoveryMoveFailureCode.Blocked,
                NextDecision));
    }

    private sealed class TestCompletionService(
        Func<CancellationToken, Task<RecoveryCompletionReviewResult>> review)
        : IRecoveryCompletionService
    {
        public int ReviewCalls { get; private set; }

        public int CompleteCalls { get; private set; }

        public Task<RecoveryCompletionReviewResult> ReviewAsync(CancellationToken cancellationToken)
        {
            ReviewCalls++;
            return review(cancellationToken);
        }

        public Task<RecoveryCompletionOperationResult> CompleteAsync(
            RecoveryCompletionPreflight reviewedPreflight,
            bool unresolvedRiskExplicitlyAccepted,
            bool archive,
            CancellationToken cancellationToken)
        {
            CompleteCalls++;
            var completion = new RecoveryCompletionRecord(
                archive ? RecoveryCompletionOutcome.Archived : RecoveryCompletionOutcome.Completed,
                reviewedPreflight.ReviewedAt,
                unresolvedRiskExplicitlyAccepted,
                CleanReview().Report!);
            return Task.FromResult(RecoveryCompletionOperationResult.Success(completion));
        }
    }

    private sealed class TestPersistenceStatus : IWorkspacePersistenceStatus
    {
        public event EventHandler? StatusChanged;

        public WorkspacePersistenceSnapshot Current { get; private set; } =
            WorkspacePersistenceSnapshot.Empty;

        public void Publish(
            WorkspacePersistenceState state,
            WorkspacePersistenceFailureCode failureCode)
        {
            Current = new WorkspacePersistenceSnapshot(
                state,
                failureCode,
                Current.Revision + 1);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestReportWriter : IRecoveryCompletionReportWriter
    {
        public Task<RecoveryCompletionReportWriteResult> WriteAsync(
            RecoveryCompletionReport report,
            string destinationPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecoveryCompletionReportWriteResult.Success);
    }

    internal sealed class TestVaultLifecycleService : IVaultLifecycleService
    {
        public event EventHandler? ContextChanged;

        public event EventHandler? VaultStateChanged;

        public ShellContext Current { get; private set; } = ShellContext.Locked;

        public VaultLifecycleSnapshot Snapshot { get; private set; } = VaultLifecycleSnapshot.Empty;

        public IReadOnlyList<RecentVaultReference> RecentVaults { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<VaultOperationResult> CreateAsync(
            string path,
            string vaultPassword,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public Task<VaultOperationResult> OpenAsync(
            string path,
            string vaultPassword,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public Task<VaultOperationResult> UnlockCurrentAsync(
            string vaultPassword,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public Task<VaultOperationResult> ChangePasswordAsync(
            string currentVaultPassword,
            string newVaultPassword,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public Task LockAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Current = ShellContext.Locked;
            Snapshot = Snapshot with { Status = VaultLifecycleStatus.Locked };
            ContextChanged?.Invoke(this, EventArgs.Empty);
            VaultStateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task RemoveRecentReferenceAsync(
            string path,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<VaultOperationResult> DeleteVaultFileAsync(
            string path,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public void RecordUserActivity(DateTimeOffset occurredAt)
        {
        }

        public Task CheckInactivityAsync(
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void Unlock(string vaultDisplayName, string sessionDisplayName)
        {
            Current = ShellContext.Unlocked(vaultDisplayName, sessionDisplayName);
            Snapshot = new VaultLifecycleSnapshot(
                VaultLifecycleStatus.Unlocked,
                "synthetic.db",
                vaultDisplayName,
                VaultLockReason.None,
                IsInactivityWarningVisible: false,
                InactivityLocksAt: null);
            ContextChanged?.Invoke(this, EventArgs.Empty);
            VaultStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }
}
