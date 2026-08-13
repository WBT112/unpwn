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
        Assert.Equal(StatusPresentation.ScreenInstruction, shell.CurrentScreen.Status.Presentation);
        Assert.Equal(StatusPresentation.GlobalContext, shell.CurrentStatus.Presentation);
        Assert.NotSame(shell.CurrentScreen.Status, shell.CurrentStatus);
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
        Assert.Equal(StatusPresentation.ScreenInstruction, shell.CurrentScreen.Status.Presentation);
        Assert.Equal(StatusPresentation.GlobalContext, shell.CurrentStatus.Presentation);
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
    public async Task ShellKeepsOrphanedBrowserDataVisibleUntilCleanupSucceeds()
    {
        var vault = new TestVaultLifecycleService();
        var recoverySession = new TestRecoverySessionService();
        var inventory = new TestAccountInventoryService();
        var localization = CreateLocalization();
        var browserSessions = new TestBrowserSessionLifecycle();
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
            browserSessions: browserSessions);

        Assert.True(shell.HasBrowserSessionCleanupWarning);
        Assert.Contains("will not be resumed automatically", shell.BrowserSessionCleanupText,
            StringComparison.Ordinal);
        localization.SetLanguage("de");
        Assert.Contains("nicht automatisch fortgesetzt", shell.BrowserSessionCleanupText,
            StringComparison.Ordinal);

        var outcome = await shell.RetryBrowserSessionCleanupCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(1, browserSessions.RetryCalls);
        Assert.False(shell.HasBrowserSessionCleanupWarning);
        Assert.False(shell.RetryBrowserSessionCleanupCommand.CanExecute(null));
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
        Assert.Equal(StatusPresentation.TransientResult, shell.CurrentStatus.Presentation);
    }

    [Fact]
    public async Task UnlockReturnsToCsvImportWhenAccountImportIsStillIncomplete()
    {
        var sessionId = Guid.NewGuid();
        var vault = new TestVaultLifecycleService();
        vault.Unlock("Synthetic vault", "Synthetic recovery");
        var session = new TestRecoverySessionService();
        session.SetSession(RecoverySessionWorkspace.Create(
            sessionId,
            "Synthetic recovery",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch));
        var inventory = new TestAccountInventoryService();
        inventory.SetInventory(AccountInventoryState.Empty(sessionId, DateTimeOffset.UnixEpoch));
        var flow = new TestRecoveryFlowService(
            WizardAt(RecoveryWizardStepId.AccountInventory),
            new NextUserTask(
                RecoveryWizardStepId.AccountInventory,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ImportAccounts,
                NextUserTaskTarget.CsvImport));
        var shell = CreateFlowShell(vault, session, inventory, flow);
        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport);

        await shell.LockCommand.ExecuteAsync();
        session.ClearForLock();
        inventory.ClearForLock();
        vault.Unlock("Synthetic vault", "Synthetic recovery");

        Assert.Equal(AppRoute.VaultEntry, shell.CurrentScreen.Route);

        session.SetSession(RecoverySessionWorkspace.Create(
            sessionId,
            "Synthetic recovery",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch));
        inventory.SetInventory(AccountInventoryState.Empty(sessionId, DateTimeOffset.UnixEpoch));

        Assert.Equal(AppRoute.CsvImport, shell.CurrentScreen.Route);
        Assert.Equal(RecoveryWizardStepId.AccountInventory, flow.Current.CurrentStep);
        Assert.Equal(0, flow.AdvanceCalls);
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
    public void ManualDetailNavigationDoesNotAdvanceTheCanonicalFlow()
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
        var flow = new TestRecoveryFlowService(
            WizardAt(RecoveryWizardStepId.AccountInventory),
            new NextUserTask(
                RecoveryWizardStepId.AccountInventory,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ImportAccounts,
                NextUserTaskTarget.CsvImport));
        var shell = CreateFlowShell(vault, session, inventory, flow);

        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts);

        Assert.Equal(AppRoute.Accounts, shell.CurrentScreen.Route);
        Assert.Equal(0, flow.AdvanceCalls);
        Assert.Equal(RecoveryWizardStepId.AccountInventory, flow.Current.CurrentStep);
    }

    [Fact]
    public void ImportWorkspaceContinuationAdvancesAndOpensCategoryReview()
    {
        var sessionId = Guid.NewGuid();
        var vault = new TestVaultLifecycleService();
        vault.Unlock("Synthetic vault", "Synthetic recovery");
        var session = new TestRecoverySessionService();
        session.SetSession(RecoverySessionWorkspace.Create(
            sessionId,
            "Synthetic recovery",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch));
        var inventory = new TestAccountInventoryService();
        inventory.SetInventory(AccountInventoryState.Empty(sessionId, DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
            [InventoryAccount(Guid.NewGuid(), "example.test", "Synthetic account")],
            DateTimeOffset.UnixEpoch));
        var flow = new TestRecoveryFlowService(
            WizardAt(RecoveryWizardStepId.AccountInventory),
            new NextUserTask(
                RecoveryWizardStepId.AccountInventory,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ReviewAccountCategories,
                NextUserTaskTarget.AccountTriage,
                RecoveryWizardStepId.AccountTriage));
        var shell = CreateFlowShell(vault, session, inventory, flow);
        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport);

        var import = Assert.IsType<CsvImportScreenViewModel>(shell.CurrentScreen);
        import.ContinueToAccountReviewCommand.Execute(null);

        Assert.Equal(AppRoute.Accounts, shell.CurrentScreen.Route);
        Assert.Equal(1, flow.AdvanceCalls);
        Assert.Equal(RecoveryWizardStepId.AccountTriage, flow.Current.CurrentStep);
    }

    [Fact]
    public async Task ImportWorkspaceContinuationContainsUnexpectedAsyncFlowFailure()
    {
        var sessionId = Guid.NewGuid();
        var vault = new TestVaultLifecycleService();
        vault.Unlock("Synthetic vault", "Synthetic recovery");
        var session = new TestRecoverySessionService();
        session.SetSession(RecoverySessionWorkspace.Create(
            sessionId,
            "Synthetic recovery",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch));
        var inventory = new TestAccountInventoryService();
        inventory.SetInventory(AccountInventoryState.Empty(sessionId, DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
            [InventoryAccount(Guid.NewGuid(), "example.test", "Synthetic account")],
            DateTimeOffset.UnixEpoch));
        var flow = new ThrowingRecoveryFlowService(
            WizardAt(RecoveryWizardStepId.AccountInventory),
            new NextUserTask(
                RecoveryWizardStepId.AccountInventory,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ReviewAccountCategories,
                NextUserTaskTarget.AccountTriage,
                RecoveryWizardStepId.AccountTriage));
        var shell = CreateFlowShell(vault, session, inventory, flow);
        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport);
        var failureVisible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ShellViewModel.CurrentStatus) &&
                shell.CurrentStatus.State == AppVisualState.Error)
            {
                failureVisible.TrySetResult();
            }
        };

        var import = Assert.IsType<CsvImportScreenViewModel>(shell.CurrentScreen);
        import.ContinueToAccountReviewCommand.Execute(null);
        await failureVisible.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AppRoute.CsvImport, shell.CurrentScreen.Route);
        Assert.Equal(1, flow.AdvanceCalls);
        Assert.Equal(AppVisualState.Error, shell.CurrentStatus.State);
        Assert.Equal(RecoveryWizardStepId.AccountInventory, flow.Current.CurrentStep);
    }

    [Fact]
    public void CategoryWorkspaceContinuationAdvancesAndReturnsToRecoveryOverview()
    {
        var sessionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var vault = new TestVaultLifecycleService();
        vault.Unlock("Synthetic vault", "Synthetic recovery");
        var session = new TestRecoverySessionService();
        session.SetSession(RecoverySessionWorkspace.Create(
            sessionId,
            "Synthetic recovery",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch));
        var inventory = new TestAccountInventoryService();
        inventory.SetInventory(AccountInventoryState.Empty(sessionId, DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
            [InventoryAccount(accountId, "example.test", "Synthetic account")],
            DateTimeOffset.UnixEpoch));
        var flow = new TestRecoveryFlowService(
            WizardAt(RecoveryWizardStepId.AccountTriage),
            new NextUserTask(
                RecoveryWizardStepId.AccountTriage,
                NextUserTaskState.OptionalWorkMayContinue,
                NextUserTaskCode.ContinueCategoryReviewOrRecovery,
                NextUserTaskTarget.RecoveryOverview,
                RecoveryWizardStepId.RecoveryOverview));
        var shell = CreateFlowShell(vault, session, inventory, flow);
        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts);

        var accounts = Assert.IsType<AccountInventoryScreenViewModel>(shell.CurrentScreen);
        accounts.ContinueRecoveryCommand.Execute(null);

        Assert.Equal(AppRoute.Dashboard, shell.CurrentScreen.Route);
        Assert.Equal(1, flow.AdvanceCalls);
        Assert.Equal(RecoveryWizardStepId.RecoveryOverview, flow.Current.CurrentStep);
    }

    [Fact]
    public void PausedSessionKeepsMutationWorkspacesDisabled()
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
        var flow = new TestRecoveryFlowService(
            WizardAt(RecoveryWizardStepId.RecoveryOverview) with
            {
                Status = RecoveryWizardLifecycleStatus.Paused,
            },
            new NextUserTask(
                RecoveryWizardStepId.RecoveryOverview,
                NextUserTaskState.Blocked,
                NextUserTaskCode.ResumeSession,
                NextUserTaskTarget.RecoveryOverview));
        var shell = CreateFlowShell(vault, session, new TestAccountInventoryService(), flow);

        Assert.All(
            shell.NavigationItems.Where(item => item.Route is
                AppRoute.Accounts or AppRoute.Workflow or AppRoute.CredentialsExport or
                AppRoute.Completion or AppRoute.CsvImport),
            item => Assert.False(item.IsEnabled));
        Assert.Equal(0, flow.AdvanceCalls);
    }

    internal static ShellViewModel CreateFlowShell(
        TestVaultLifecycleService vault,
        TestRecoverySessionService session,
        TestAccountInventoryService inventory,
        IRecoveryFlowService flow)
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
            flow);
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
        AccountRecoveryCategory.Unknown,
        RepositoryAccountClassificationCatalog.CurrentVersion,
        ConfirmedCategory: null,
        CategoryConfirmedRevision: null,
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
        RecommendedActionId: actionId)
        {
            Category = AccountRecoveryCategory.Email,
        };

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

        public AccountRecoveryOrder? CurrentRecoveryOrder => null;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(
            AccountInventoryUpsertRequest request,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> CategorizeAsync(
            Guid accountId,
            AccountRecoveryCategory category,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(
            Guid accountId,
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

        public void ClearForLock()
        {
            LoadState = AccountInventoryLoadState.Locked;
            CurrentInventory = null;
            InventoryChanged?.Invoke(this, EventArgs.Empty);
        }

        private static Task<AccountInventoryOperationResult> Unsupported() =>
            Task.FromResult(AccountInventoryOperationResult.Failure(
                AccountInventoryFailureCode.Conflict));
    }

    internal sealed class TestRecoveryFlowService(
        RecoveryWizardState current,
        NextUserTask next) : IRecoveryFlowService
    {
        public event EventHandler? NextTaskChanged;

        public RecoveryWizardState Current { get; private set; } = current;

        public NextUserTask NextTask { get; private set; } = next;

        public int AdvanceCalls { get; private set; }

        public void SetTask(
            RecoveryWizardState state,
            NextUserTask task)
        {
            Current = state;
            NextTask = task;
            NextTaskChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<RecoveryFlowMoveResult> AdvanceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdvanceCalls++;
            if (!NextTask.RequiresTransition || NextTask.TransitionStep is null)
            {
                return Task.FromResult(RecoveryFlowMoveResult.Failure(
                    RecoveryFlowMoveFailureCode.Blocked,
                    NextTask));
            }

            var result = NextTask;
            Current = Current with
            {
                CurrentStep = result.TransitionStep,
                ResumeStep = result.TransitionStep,
                Revision = Current.Revision + 1,
            };
            NextTaskChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoveryFlowMoveResult.Success(result));
        }

        public Task<RecoveryFlowMoveResult> BeginCompletionReviewAsync(
            CancellationToken cancellationToken) =>
            AdvanceAsync(cancellationToken);

        public Task<RecoveryFlowMoveResult> MarkCompletionReviewReadyAsync(
            CancellationToken cancellationToken) =>
            AdvanceAsync(cancellationToken);
    }

    private sealed class ThrowingRecoveryFlowService(
        RecoveryWizardState current,
        NextUserTask next) : IRecoveryFlowService
    {
        public event EventHandler? NextTaskChanged
        {
            add { }
            remove { }
        }

        public RecoveryWizardState Current { get; } = current;

        public NextUserTask NextTask { get; } = next;

        public int AdvanceCalls { get; private set; }

        public async Task<RecoveryFlowMoveResult> AdvanceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdvanceCalls++;
            await Task.Yield();
            throw new UnauthorizedAccessException("Synthetic platform persistence failure.");
        }

        public Task<RecoveryFlowMoveResult> BeginCompletionReviewAsync(
            CancellationToken cancellationToken) => AdvanceAsync(cancellationToken);

        public Task<RecoveryFlowMoveResult> MarkCompletionReviewReadyAsync(
            CancellationToken cancellationToken) => AdvanceAsync(cancellationToken);
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

    private sealed class TestBrowserSessionLifecycle : IRecoveryBrowserSessionLifecycle
    {
        private readonly RecoveryBrowserOrphanedSession _orphan = new(
            Guid.NewGuid(),
            "/opaque/browser-session");

        public TestBrowserSessionLifecycle()
        {
            Current = new RecoveryBrowserSessionLifecycleSnapshot(
                RecoveryBrowserSessionLifecycleState.OrphanedDataDetected,
                ActiveSession: null,
                [_orphan],
                RecoveryBrowserSessionFailureCode.None);
        }

        public event EventHandler<RecoveryBrowserSessionLifecycleSnapshot>? StateChanged;

        public RecoveryBrowserSessionLifecycleSnapshot Current { get; private set; }

        public int RetryCalls { get; private set; }

        public RecoveryBrowserSessionLifecycleSnapshot InspectStartup() => Current;

        public RecoveryBrowserSessionStartResult Start(Guid accountId) => new(
            null,
            WasReused: false,
            RecoveryBrowserSessionFailureCode.OrphanedDataRequiresCleanup);

        public Task<RecoveryBrowserSessionCleanupResult> EndAsync(
            Guid sessionId,
            IRecoveryBrowserSessionResources resources,
            CancellationToken cancellationToken) => Task.FromResult(
                new RecoveryBrowserSessionCleanupResult(
                    Succeeded: false,
                    RecoveryBrowserSessionFailureCode.SessionNotFound));

        public Task<RecoveryBrowserSessionCleanupResult> RetryOrphanCleanupAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            Assert.Equal(_orphan.SessionId, sessionId);
            RetryCalls++;
            Current = new RecoveryBrowserSessionLifecycleSnapshot(
                RecoveryBrowserSessionLifecycleState.Idle,
                ActiveSession: null,
                [],
                RecoveryBrowserSessionFailureCode.None);
            StateChanged?.Invoke(this, Current);
            return Task.FromResult(new RecoveryBrowserSessionCleanupResult(
                Succeeded: true,
                RecoveryBrowserSessionFailureCode.None));
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
